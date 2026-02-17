using Grpc.Core;
using Onlyspans.Worker.Api.Clients;
using Onlyspans.Worker.Api.Data;
using Targets.Communication;
using Worker.Communication;

// Disambiguate proto DeploymentResult from EF entity DeploymentResult
using ProtoDeploymentResult = Worker.Communication.DeploymentResult;
using EntityDeploymentResult = Onlyspans.Worker.Api.Data.Entities.DeploymentResult;
using EntityDeploymentLog = Onlyspans.Worker.Api.Data.Entities.DeploymentLog;
// Disambiguate proto LogLevel from Microsoft.Extensions.Logging.LogLevel
using ProtoLogLevel = Worker.Communication.LogLevel;

namespace Onlyspans.Worker.Api.Services;

// Base class generated from worker.proto: Worker.Communication.WorkerService.WorkerServiceBase
// ExecuteDeployment signature: Task ExecuteDeployment(DeploymentPackage, IServerStreamWriter<DeploymentMessage>, ServerCallContext)
public sealed class WorkerService(
    ISnapshotDownloader snapshotDownloader,
    ITargetsControllerClient targetsClient,
    ILogPublisher logPublisher,
    WorkerDbContext dbContext,
    ILogger<WorkerService> logger
) : global::Worker.Communication.WorkerService.WorkerServiceBase
{
    public override async Task ExecuteDeployment(
        DeploymentPackage request,
        IServerStreamWriter<DeploymentMessage> responseStream,
        ServerCallContext context)
    {
        var startedAt = DateTime.UtcNow;
        var ct = context.CancellationToken;

        // Step 1: Download snapshot from S3
        DownloadSnapshotResult snapshotResult;
        try
        {
            snapshotResult = await snapshotDownloader.DownloadAsync(request.SnapshotKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to download snapshot {SnapshotKey} for deployment {DeploymentId}",
                request.SnapshotKey, request.DeploymentId);

            var errorMsg = new DeploymentMessage
            {
                Result = new ProtoDeploymentResult
                {
                    Error = new ProtoDeploymentResult.Types.Error
                    {
                        DeploymentId = request.DeploymentId,
                        ErrorType = ErrorType.SnapshotDownloadFailed,
                        Message = ex.Message
                    }
                }
            };
            await responseStream.WriteAsync(errorMsg, ct);
            await SaveResultAsync(request.DeploymentId, "failed", startedAt, ex.Message, null, ct);
            return;
        }

        // Step 2: Build request for Targets Controller
        var targetRequest = new TargetExecutionRequest
        {
            DeploymentId = request.DeploymentId,
            TargetId = request.TargetId,
            TargetType = request.TargetType,
            SnapshotPath = snapshotResult.FilePath
        };
        foreach (var kv in request.ResolvedVariables)
            targetRequest.EnvironmentVariables[kv.Key] = kv.Value;

        // Step 3: Stream execution results from Targets Controller
        string? finalError = null;
        using var streamingCall = targetsClient.ExecuteOnTargetAsync(targetRequest, ct);

        try
        {
            await foreach (var executionResult in streamingCall.ResponseStream.ReadAllAsync(ct))
            {
                var logChunk = new LogChunk
                {
                    DeploymentId = request.DeploymentId,
                    Timestamp = executionResult.Timestamp,
                    Level = MapResultTypeToLogLevel(executionResult.Type),
                    Message = executionResult.Message,
                    Source = "target-controller"
                };

                // Stream log to gRPC response
                await responseStream.WriteAsync(new DeploymentMessage { Log = logChunk }, ct);

                // Publish to Kafka via Wolverine (or NoOp when disabled)
                await logPublisher.PublishAsync(logChunk, ct);

                // Persist log entry to database
                dbContext.DeploymentLogs.Add(new EntityDeploymentLog
                {
                    Id = Guid.NewGuid(),
                    DeploymentId = request.DeploymentId,
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
                request.DeploymentId);
            finalError = ex.Message;
        }

        // Step 4: Save logs and send final result
        await dbContext.SaveChangesAsync(ct);

        var completedAt = DateTime.UtcNow;
        if (finalError is null)
        {
            var successMsg = new DeploymentMessage
            {
                Result = new ProtoDeploymentResult
                {
                    Success = new ProtoDeploymentResult.Types.Success
                    {
                        DeploymentId = request.DeploymentId,
                        CompletedAt = new DateTimeOffset(completedAt).ToUnixTimeMilliseconds(),
                        Summary = "Deployment completed successfully"
                    }
                }
            };
            await responseStream.WriteAsync(successMsg, ct);
            await SaveResultAsync(request.DeploymentId, "succeeded", startedAt, null, completedAt, ct);
        }
        else
        {
            var errorMsg = new DeploymentMessage
            {
                Result = new ProtoDeploymentResult
                {
                    Error = new ProtoDeploymentResult.Types.Error
                    {
                        DeploymentId = request.DeploymentId,
                        ErrorType = ErrorType.TargetExecutionFailed,
                        Message = finalError
                    }
                }
            };
            await responseStream.WriteAsync(errorMsg, ct);
            await SaveResultAsync(request.DeploymentId, "failed", startedAt, finalError, completedAt, ct);
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
