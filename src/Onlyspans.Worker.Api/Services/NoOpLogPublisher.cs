using Worker.Communication;

namespace Onlyspans.Worker.Api.Services;

/// <summary>
/// No-op implementation of ILogPublisher used as a placeholder until
/// Wolverine/Kafka integration is added in Phase 6.
/// </summary>
public sealed class NoOpLogPublisher : ILogPublisher
{
    private readonly ILogger<NoOpLogPublisher> _logger;

    public NoOpLogPublisher(ILogger<NoOpLogPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(LogChunk chunk, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "NoOpLogPublisher: discarding log chunk for deployment {DeploymentId} at {Timestamp}",
            chunk.DeploymentId,
            chunk.Timestamp);

        return Task.CompletedTask;
    }
}
