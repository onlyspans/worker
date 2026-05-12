using Grpc.Core;
using Onlyspans.Worker.Api.Clients;
using Targets.Communication;
using Worker.Communication;

using ProtoLogLevel = Worker.Communication.LogLevel;
using ProtoStepExecutionResult = Worker.Communication.StepExecutionResult;
using TCArtifactChunk = Targets.Communication.ArtifactChunk;
using TCCommandType = Targets.Communication.CommandType;
using TCDeploymentInput = Targets.Communication.DeploymentInput;
using TCResultType = Targets.Communication.ResultType;
using TCStepCommand = Targets.Communication.StepCommand;
using TCStepTargetMetadata = Targets.Communication.StepTargetMetadata;
using WorkerCommandType = Worker.Communication.CommandType;
using WorkerStepCommand = Worker.Communication.StepCommand;
using WorkerStepExecutionInput = Worker.Communication.StepExecutionInput;

namespace Onlyspans.Worker.Api.Services;

public sealed class WorkerService(
    ITargetsControllerClient targetsClient,
    ILogger<WorkerService> logger
) : global::Worker.Communication.WorkerService.WorkerServiceBase
{
    public override async Task ExecuteStep(
        IAsyncStreamReader<WorkerStepExecutionInput> requestStream,
        IServerStreamWriter<StepExecutionMessage> responseStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;

        if (!await requestStream.MoveNext(ct) ||
            requestStream.Current.InputCase != WorkerStepExecutionInput.InputOneofCase.Metadata)
        {
            await WriteErrorAsync(
                responseStream,
                "",
                "",
                ErrorType.InvalidStepPackage,
                "First message must be StepExecutionMetadata",
                ct);
            return;
        }

        var metadata = requestStream.Current.Metadata;
        var validationError = ValidateMetadata(metadata);
        if (validationError is not null)
        {
            await WriteErrorAsync(
                responseStream,
                metadata.DeploymentId,
                metadata.StepId,
                ErrorType.InvalidStepPackage,
                validationError,
                ct);
            return;
        }

        string? finalError = null;
        string? successSummary = null;

        using var streamingCall = targetsClient.ExecuteOnTargetAsync(ct);

        try
        {
            await streamingCall.RequestStream.WriteAsync(new TCDeploymentInput
            {
                Metadata = new TCStepTargetMetadata
                {
                    DeploymentId = metadata.DeploymentId,
                    StepId = metadata.StepId,
                    StepName = metadata.StepName,
                    TargetId = metadata.TargetId,
                    Command = MapCommand(metadata.Command),
                    EnvironmentVariables = { metadata.ResolvedVariables }
                }
            }, ct);

            await foreach (var input in requestStream.ReadAllAsync(ct))
            {
                if (input.InputCase != WorkerStepExecutionInput.InputOneofCase.ArtifactChunk)
                {
                    await streamingCall.RequestStream.CompleteAsync();
                    await WriteErrorAsync(
                        responseStream,
                        metadata.DeploymentId,
                        metadata.StepId,
                        ErrorType.InvalidStepPackage,
                        "Messages after StepExecutionMetadata must be ArtifactChunk",
                        ct);
                    return;
                }

                await streamingCall.RequestStream.WriteAsync(new TCDeploymentInput
                {
                    ArtifactChunk = new TCArtifactChunk
                    {
                        Data = input.ArtifactChunk.Data,
                        IsLast = input.ArtifactChunk.IsLast
                    }
                }, ct);
            }

            await streamingCall.RequestStream.CompleteAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Failed to stream step package to target controller for deployment {DeploymentId}, step {StepId}",
                metadata.DeploymentId,
                metadata.StepId);

            await WriteErrorAsync(
                responseStream,
                metadata.DeploymentId,
                metadata.StepId,
                ErrorType.ArtifactTransferFailed,
                ex.Message,
                ct);
            return;
        }

        try
        {
            await foreach (var targetMessage in streamingCall.ResponseStream.ReadAllAsync(ct))
            {
                switch (targetMessage.Type)
                {
                    case TCResultType.Error:
                        finalError = targetMessage.Message;
                        await WriteLogAsync(
                            responseStream,
                            metadata,
                            targetMessage,
                            MapResultTypeToLogLevel(targetMessage.Type),
                            ct);
                        break;
                    case TCResultType.Success:
                        successSummary = string.IsNullOrWhiteSpace(targetMessage.Message)
                            ? "Step completed successfully"
                            : targetMessage.Message;
                        break;
                    default:
                        await WriteLogAsync(
                            responseStream,
                            metadata,
                            targetMessage,
                            MapResultTypeToLogLevel(targetMessage.Type),
                            ct);
                        break;
                }

                if (finalError is not null)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Target execution stream failed for deployment {DeploymentId}, step {StepId}",
                metadata.DeploymentId,
                metadata.StepId);
            finalError = ex.Message;
        }

        if (finalError is null)
        {
            await responseStream.WriteAsync(new StepExecutionMessage
            {
                Result = new ProtoStepExecutionResult
                {
                    Success = new ProtoStepExecutionResult.Types.Success
                    {
                        DeploymentId = metadata.DeploymentId,
                        StepId = metadata.StepId,
                        CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Summary = successSummary ?? "Step completed successfully"
                    }
                }
            }, ct);
            return;
        }

        await WriteErrorAsync(
            responseStream,
            metadata.DeploymentId,
            metadata.StepId,
            ErrorType.TargetExecutionFailed,
            finalError,
            ct);
    }

    private static string? ValidateMetadata(StepExecutionMetadata metadata)
    {
        if (metadata.Command is null)
        {
            return "Step command is required";
        }

        if (metadata.Command.Type == WorkerCommandType.Unspecified)
        {
            return "Step command type is required";
        }

        return metadata.Command.SourceCase switch
        {
            WorkerStepCommand.SourceOneofCase.InlineScript => null,
            WorkerStepCommand.SourceOneofCase.ScriptPath => null,
            WorkerStepCommand.SourceOneofCase.None => "Step command source is required",
            _ => "Step command source must be inline_script or script_path"
        };
    }

    private static TCStepCommand MapCommand(WorkerStepCommand command)
    {
        var mapped = new TCStepCommand
        {
            Type = MapCommandType(command.Type),
            TimeoutSeconds = command.TimeoutSeconds,
            WorkingDirectory = command.WorkingDirectory
        };

        switch (command.SourceCase)
        {
            case WorkerStepCommand.SourceOneofCase.InlineScript:
                mapped.InlineScript = command.InlineScript;
                break;
            case WorkerStepCommand.SourceOneofCase.ScriptPath:
                mapped.ScriptPath = command.ScriptPath;
                break;
        }

        return mapped;
    }

    private static TCCommandType MapCommandType(WorkerCommandType type) => type switch
    {
        WorkerCommandType.Shell => TCCommandType.Shell,
        WorkerCommandType.Kubernetes => TCCommandType.Kubernetes,
        WorkerCommandType.Helm => TCCommandType.Helm,
        _ => TCCommandType.Unspecified
    };

    private static ProtoLogLevel MapResultTypeToLogLevel(TCResultType type) => type switch
    {
        TCResultType.Error => ProtoLogLevel.Error,
        TCResultType.Log => ProtoLogLevel.Info,
        TCResultType.Progress => ProtoLogLevel.Info,
        TCResultType.Success => ProtoLogLevel.Info,
        _ => ProtoLogLevel.Unspecified
    };

    private static async Task WriteLogAsync(
        IServerStreamWriter<StepExecutionMessage> responseStream,
        StepExecutionMetadata metadata,
        ExecutionResult targetMessage,
        ProtoLogLevel level,
        CancellationToken ct)
    {
        await responseStream.WriteAsync(new StepExecutionMessage
        {
            Log = new LogChunk
            {
                DeploymentId = metadata.DeploymentId,
                StepId = metadata.StepId,
                Timestamp = targetMessage.Timestamp,
                Level = level,
                Message = targetMessage.Message,
                Source = "target-controller"
            }
        }, ct);
    }

    private static async Task WriteErrorAsync(
        IServerStreamWriter<StepExecutionMessage> responseStream,
        string deploymentId,
        string stepId,
        ErrorType errorType,
        string message,
        CancellationToken ct)
    {
        await responseStream.WriteAsync(new StepExecutionMessage
        {
            Result = new ProtoStepExecutionResult
            {
                Error = new ProtoStepExecutionResult.Types.Error
                {
                    DeploymentId = deploymentId,
                    StepId = stepId,
                    ErrorType = errorType,
                    Message = message
                }
            }
        }, ct);
    }
}
