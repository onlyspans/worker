using Grpc.Core;
using Onlyspans.Worker.Api.Clients;
using Onlyspans.Worker.Api.Data;
using Targets.Communication;
using Worker.Communication;

using ProtoDeploymentResult = Worker.Communication.DeploymentResult;
using EntityDeploymentResult = Onlyspans.Worker.Api.Data.Entities.DeploymentResult;
using EntityDeploymentLog = Onlyspans.Worker.Api.Data.Entities.DeploymentLog;
using ProtoLogLevel = Worker.Communication.LogLevel;
using WorkerDeploymentInput = Worker.Communication.DeploymentInput;
using WorkerDeploymentMetadata = Worker.Communication.DeploymentMetadata;
using WorkerSnapshotChunk = Worker.Communication.SnapshotChunk;
using TCDeploymentInput = Targets.Communication.DeploymentInput;
using TCDeploymentMetadata = Targets.Communication.DeploymentMetadata;
using TCSnapshotChunk = Targets.Communication.SnapshotChunk;

namespace Onlyspans.Worker.Api.Services;

public sealed class WorkerService(
    ITargetsControllerClient targetsClient,
    WorkerDbContext dbContext,
    ILogger<WorkerService> logger
) : global::Worker.Communication.WorkerService.WorkerServiceBase
{
    public override async Task ExecuteDeployment(
        IAsyncStreamReader<WorkerDeploymentInput> requestStream,
        IServerStreamWriter<DeploymentMessage> responseStream,
        ServerCallContext context)
    {
        var startedAt = DateTime.UtcNow;
        var ct = context.CancellationToken;

        // Step 1: Read first message — must be metadata
        if (!await requestStream.MoveNext(ct) ||
            requestStream.Current.InputCase != WorkerDeploymentInput.InputOneofCase.Metadata)
        {
            await responseStream.WriteAsync(new DeploymentMessage
            {
                Result = new ProtoDeploymentResult
                {
                    Error = new ProtoDeploymentResult.Types.Error
                    {
                        DeploymentId = "",
                        ErrorType = ErrorType.Internal,
                        Message = "First message must be DeploymentMetadata"
                    }
                }
            }, ct);
            return;
        }

        var meta = requestStream.Current.Metadata;
        string? finalError = null;

        // Step 2: Open BiDi stream to Targets Controller and forward metadata + chunks
        using var streamingCall = targetsClient.ExecuteOnTargetAsync(ct);
        try
        {
            await streamingCall.RequestStream.WriteAsync(new TCDeploymentInput
            {
                Metadata = new TCDeploymentMetadata
                {
                    DeploymentId = meta.DeploymentId,
                    TargetId = meta.TargetId,
                    TargetType = meta.TargetType,
                    EnvironmentVariables = { meta.ResolvedVariables }
                }
            }, ct);

            await foreach (var msg in requestStream.ReadAllAsync(ct))
            {
                if (msg.InputCase == WorkerDeploymentInput.InputOneofCase.SnapshotChunk)
                {
                    await streamingCall.RequestStream.WriteAsync(new TCDeploymentInput
                    {
                        SnapshotChunk = new TCSnapshotChunk
                        {
                            Data = msg.SnapshotChunk.Data,
                            IsLast = msg.SnapshotChunk.IsLast
                        }
                    }, ct);
                }
            }

            await streamingCall.RequestStream.CompleteAsync();

            logger.LogInformation(
                "Snapshot forwarded to TC for deployment {DeploymentId}",
                meta.DeploymentId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Failed to stream snapshot to TC for deployment {DeploymentId}",
                meta.DeploymentId);

            await responseStream.WriteAsync(new DeploymentMessage
            {
                Result = new ProtoDeploymentResult
                {
                    Error = new ProtoDeploymentResult.Types.Error
                    {
                        DeploymentId = meta.DeploymentId,
                        ErrorType = ErrorType.Internal,
                        Message = ex.Message
                    }
                }
            }, ct);
            await SaveResultAsync(meta.DeploymentId, "failed", startedAt, ex.Message, null, ct);
            return;
        }

        // Step 3: Stream execution results from TC back to Processes
        try
        {
            await foreach (var executionResult in streamingCall.ResponseStream.ReadAllAsync(ct))
            {
                var logChunk = new LogChunk
                {
                    DeploymentId = meta.DeploymentId,
                    Timestamp = executionResult.Timestamp,
                    Level = MapResultTypeToLogLevel(executionResult.Type),
                    Message = executionResult.Message,
                    Source = "target-controller"
                };

                await responseStream.WriteAsync(new DeploymentMessage { Log = logChunk }, ct);

                dbContext.DeploymentLogs.Add(new EntityDeploymentLog
                {
                    Id = Guid.NewGuid(),
                    DeploymentId = meta.DeploymentId,
                    Timestamp = DateTime.UtcNow,
                    LogLevel = logChunk.Level.ToString(),
                    Message = logChunk.Message,
                    Source = logChunk.HasSource ? logChunk.Source : null
                });

                if (executionResult.Type == ResultType.Error)
                    finalError = executionResult.Message;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Target execution stream failed for deployment {DeploymentId}",
                meta.DeploymentId);
            finalError = ex.Message;
        }

        // Step 4: Persist and send final result
        await dbContext.SaveChangesAsync(ct);

        var completedAt = DateTime.UtcNow;
        if (finalError is null)
        {
            await responseStream.WriteAsync(new DeploymentMessage
            {
                Result = new ProtoDeploymentResult
                {
                    Success = new ProtoDeploymentResult.Types.Success
                    {
                        DeploymentId = meta.DeploymentId,
                        CompletedAt = new DateTimeOffset(completedAt).ToUnixTimeMilliseconds(),
                        Summary = "Deployment completed successfully"
                    }
                }
            }, ct);
            await SaveResultAsync(meta.DeploymentId, "succeeded", startedAt, null, completedAt, ct);
        }
        else
        {
            await responseStream.WriteAsync(new DeploymentMessage
            {
                Result = new ProtoDeploymentResult
                {
                    Error = new ProtoDeploymentResult.Types.Error
                    {
                        DeploymentId = meta.DeploymentId,
                        ErrorType = ErrorType.TargetExecutionFailed,
                        Message = finalError
                    }
                }
            }, ct);
            await SaveResultAsync(meta.DeploymentId, "failed", startedAt, finalError, completedAt, ct);
        }
    }

    private static ProtoLogLevel MapResultTypeToLogLevel(ResultType type) => type switch
    {
        ResultType.Error => ProtoLogLevel.Error,
        ResultType.Log => ProtoLogLevel.Info,
        ResultType.Progress => ProtoLogLevel.Info,
        ResultType.Success => ProtoLogLevel.Info,
        _ => ProtoLogLevel.Unspecified
    };

    private async Task SaveResultAsync(
        string deploymentId,
        string status,
        DateTime startedAt,
        string? errorMessage,
        DateTime? completedAt,
        CancellationToken ct)
    {
        dbContext.DeploymentResults.Add(new EntityDeploymentResult
        {
            DeploymentId = deploymentId,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ErrorMessage = errorMessage
        });
        await dbContext.SaveChangesAsync(ct);
    }
}
