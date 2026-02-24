using Google.Protobuf;
using Grpc.Core;
using Onlyspans.Worker.Api.Clients;
using Onlyspans.Worker.Api.Data;
using Targets.Communication;
using Worker.Communication;

using ProtoDeploymentResult = Worker.Communication.DeploymentResult;
using EntityDeploymentResult = Onlyspans.Worker.Api.Data.Entities.DeploymentResult;
using EntityDeploymentLog = Onlyspans.Worker.Api.Data.Entities.DeploymentLog;
using ProtoLogLevel = Worker.Communication.LogLevel;

namespace Onlyspans.Worker.Api.Services;

public sealed class WorkerService(
    ISnapshotDownloader snapshotDownloader,
    ITargetsControllerClient targetsClient,
    ILogPublisher logPublisher,
    WorkerDbContext dbContext,
    ILogger<WorkerService> logger
) : global::Worker.Communication.WorkerService.WorkerServiceBase
{
    private const int ChunkSize = 64 * 1024; // 64 KB

    public override async Task ExecuteDeployment(
        DeploymentPackage request,
        IServerStreamWriter<DeploymentMessage> responseStream,
        ServerCallContext context)
    {
        var startedAt = DateTime.UtcNow;
        var ct = context.CancellationToken;

        // Step 1: Download snapshot
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

            await responseStream.WriteAsync(new DeploymentMessage
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
            }, ct);
            await SaveResultAsync(request.DeploymentId, "failed", startedAt, ex.Message, null, ct);
            return;
        }

        // Step 2: Open BiDi stream to Targets Controller
        string? finalError = null;
        using var streamingCall = targetsClient.ExecuteOnTargetAsync(ct);

        try
        {
            // Send metadata as first message
            await streamingCall.RequestStream.WriteAsync(new DeploymentInput
            {
                Metadata = new DeploymentMetadata
                {
                    DeploymentId = request.DeploymentId,
                    TargetId = request.TargetId,
                    TargetType = request.TargetType,
                    EnvironmentVariables = { request.ResolvedVariables }
                }
            }, ct);

            // Stream snapshot file in chunks
            var buffer = new byte[ChunkSize];
            await using var fileStream = File.OpenRead(snapshotResult.FilePath);

            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer, ct)) > 0)
            {
                var isLast = fileStream.Position >= fileStream.Length;
                await streamingCall.RequestStream.WriteAsync(new DeploymentInput
                {
                    SnapshotChunk = new SnapshotChunk
                    {
                        Data = ByteString.CopyFrom(buffer, 0, bytesRead),
                        IsLast = isLast
                    }
                }, ct);
            }

            // Close request stream — signals TC that all chunks are sent
            await streamingCall.RequestStream.CompleteAsync();

            logger.LogInformation(
                "Snapshot streamed to TC for deployment {DeploymentId} ({Bytes} bytes)",
                request.DeploymentId, snapshotResult.SizeBytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Failed to stream snapshot to TC for deployment {DeploymentId}",
                request.DeploymentId);

            await responseStream.WriteAsync(new DeploymentMessage
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
            }, ct);
            await SaveResultAsync(request.DeploymentId, "failed", startedAt, ex.Message, null, ct);
            return;
        }
        finally
        {
            // Clean up temp file regardless of outcome
            if (File.Exists(snapshotResult.FilePath))
                File.Delete(snapshotResult.FilePath);
        }

        // Step 3: Stream execution results from TC back to Processes
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

                await responseStream.WriteAsync(new DeploymentMessage { Log = logChunk }, ct);
                await logPublisher.PublishAsync(logChunk, ct);

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
                        DeploymentId = request.DeploymentId,
                        CompletedAt = new DateTimeOffset(completedAt).ToUnixTimeMilliseconds(),
                        Summary = "Deployment completed successfully"
                    }
                }
            }, ct);
            await SaveResultAsync(request.DeploymentId, "succeeded", startedAt, null, completedAt, ct);
        }
        else
        {
            await responseStream.WriteAsync(new DeploymentMessage
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
            }, ct);
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
