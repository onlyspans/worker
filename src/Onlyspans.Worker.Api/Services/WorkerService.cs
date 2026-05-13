using Grpc.Core;
using Onlyspans.Worker.Api.Clients;
using Targets.Communication;
using Worker.Communication;

using ProtoLogLevel = Worker.Communication.LogLevel;
using ProtoStepExecutionResult = Worker.Communication.StepExecutionResult;
using TCArtifactChunk = Targets.Communication.ArtifactChunk;
using TCCommandType = Targets.Communication.CommandType;
using TCDeploymentInput = Targets.Communication.DeploymentInput;
using TCLogLevel = Targets.Communication.LogLevel;
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
                metadata.ExecutionId,
                metadata.DeploymentId,
                metadata.StepId,
                ErrorType.InvalidStepPackage,
                validationError,
                ct);
            return;
        }

        using var streamingCall = targetsClient.ExecuteOnTargetAsync(ct);
        using var uploadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var responseTask = ReadTargetResponsesAsync(
            streamingCall.ResponseStream,
            responseStream,
            metadata,
            uploadCts,
            ct);

        Exception? uploadError = null;
        var uploadCompleted = false;

        try
        {
            uploadCts.Token.ThrowIfCancellationRequested();
            await streamingCall.RequestStream.WriteAsync(new TCDeploymentInput
            {
                Metadata = new TCStepTargetMetadata
                {
                    ExecutionId = metadata.ExecutionId,
                    DeploymentId = metadata.DeploymentId,
                    StepId = metadata.StepId,
                    StepName = metadata.StepName,
                    TargetId = metadata.TargetId,
                    Command = MapCommand(metadata.Command),
                    EnvironmentVariables = { metadata.ResolvedVariables }
                }
            });

            await foreach (var input in requestStream.ReadAllAsync(uploadCts.Token))
            {
                if (input.InputCase != WorkerStepExecutionInput.InputOneofCase.ArtifactChunk)
                {
                    uploadCts.Cancel();
                    await streamingCall.RequestStream.CompleteAsync();
                    await WriteErrorAsync(
                        responseStream,
                        metadata.ExecutionId,
                        metadata.DeploymentId,
                        metadata.StepId,
                        ErrorType.InvalidStepPackage,
                        "Messages after StepExecutionMetadata must be ArtifactChunk",
                        ct);
                    return;
                }

                uploadCts.Token.ThrowIfCancellationRequested();
                await streamingCall.RequestStream.WriteAsync(new TCDeploymentInput
                {
                    ArtifactChunk = new TCArtifactChunk
                    {
                        Data = input.ArtifactChunk.Data,
                        IsLast = input.ArtifactChunk.IsLast
                    }
                });
            }

            await streamingCall.RequestStream.CompleteAsync();
            uploadCompleted = true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            uploadError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            uploadError = ex;
            logger.LogError(
                ex,
                "Failed to stream step package to target controller for execution {ExecutionId}, deployment {DeploymentId}, step {StepId}",
                metadata.ExecutionId,
                metadata.DeploymentId,
                metadata.StepId);
        }

        TargetExecutionOutcome outcome;
        try
        {
            outcome = await responseTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Target execution stream failed for execution {ExecutionId}, deployment {DeploymentId}, step {StepId}",
                metadata.ExecutionId,
                metadata.DeploymentId,
                metadata.StepId);
            outcome = TargetExecutionOutcome.Failed(ex.Message);
        }

        if (outcome.ErrorMessage is not null)
        {
            await WriteErrorAsync(
                responseStream,
                metadata.ExecutionId,
                metadata.DeploymentId,
                metadata.StepId,
                ErrorType.TargetExecutionFailed,
                outcome.ErrorMessage,
                ct);
            return;
        }

        if (uploadError is not null)
        {
            await WriteErrorAsync(
                responseStream,
                metadata.ExecutionId,
                metadata.DeploymentId,
                metadata.StepId,
                ErrorType.ArtifactTransferFailed,
                uploadError.Message,
                ct);
            return;
        }

        if (uploadCompleted || outcome.SuccessSummary is not null)
        {
            await responseStream.WriteAsync(new StepExecutionMessage
            {
                Result = new ProtoStepExecutionResult
                {
                    Success = new ProtoStepExecutionResult.Types.Success
                    {
                        ExecutionId = metadata.ExecutionId,
                        DeploymentId = metadata.DeploymentId,
                        StepId = metadata.StepId,
                        CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Summary = outcome.SuccessSummary ?? "Step completed successfully"
                    }
                }
            }, ct);
            return;
        }

        await WriteErrorAsync(
            responseStream,
            metadata.ExecutionId,
            metadata.DeploymentId,
            metadata.StepId,
            ErrorType.TargetExecutionFailed,
            "Target execution did not complete",
            ct);
    }

    private static string? ValidateMetadata(StepExecutionMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.ExecutionId))
        {
            return "Step execution_id is required";
        }

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

    private static ProtoLogLevel MapLogLevel(ExecutionResult targetMessage)
    {
        if (targetMessage.HasLogLevel)
        {
            return targetMessage.LogLevel switch
            {
                TCLogLevel.Debug => ProtoLogLevel.Debug,
                TCLogLevel.Info => ProtoLogLevel.Info,
                TCLogLevel.Warning => ProtoLogLevel.Warning,
                TCLogLevel.Error => ProtoLogLevel.Error,
                _ => ProtoLogLevel.Unspecified
            };
        }

        return MapResultTypeToLogLevel(targetMessage.Type);
    }

    private static ProtoLogLevel MapResultTypeToLogLevel(TCResultType type) => type switch
    {
        TCResultType.Error => ProtoLogLevel.Error,
        TCResultType.Log => ProtoLogLevel.Info,
        TCResultType.Progress => ProtoLogLevel.Info,
        TCResultType.Success => ProtoLogLevel.Info,
        _ => ProtoLogLevel.Unspecified
    };

    private static async Task<TargetExecutionOutcome> ReadTargetResponsesAsync(
        IAsyncStreamReader<ExecutionResult> targetResponses,
        IServerStreamWriter<StepExecutionMessage> responseStream,
        StepExecutionMetadata metadata,
        CancellationTokenSource uploadCts,
        CancellationToken ct)
    {
        string? successSummary = null;

        await foreach (var targetMessage in targetResponses.ReadAllAsync(ct))
        {
            switch (targetMessage.Type)
            {
                case TCResultType.Error:
                    await WriteLogAsync(
                        responseStream,
                        metadata,
                        targetMessage,
                        MapLogLevel(targetMessage),
                        ct);
                    uploadCts.Cancel();
                    return TargetExecutionOutcome.Failed(targetMessage.Message);

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
                        MapLogLevel(targetMessage),
                        ct);
                    break;
            }
        }

        return TargetExecutionOutcome.Succeeded(successSummary);
    }

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
                ExecutionId = metadata.ExecutionId,
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
        string executionId,
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
                    ExecutionId = executionId,
                    DeploymentId = deploymentId,
                    StepId = stepId,
                    ErrorType = errorType,
                    Message = message
                }
            }
        }, ct);
    }

    private sealed record TargetExecutionOutcome(string? SuccessSummary, string? ErrorMessage)
    {
        public static TargetExecutionOutcome Succeeded(string? summary) => new(summary, null);
        public static TargetExecutionOutcome Failed(string message) => new(null, message);
    }
}
