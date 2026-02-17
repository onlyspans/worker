using Onlyspans.Worker.Api.Messages;
using Wolverine;
using Worker.Communication;

namespace Onlyspans.Worker.Api.Services;

// Wolverine IMessageBus.PublishAsync signature (v5.16.0):
// ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
public sealed class WolverineLogPublisher(IMessageBus messageBus) : ILogPublisher
{
    public async Task PublishAsync(LogChunk chunk, CancellationToken cancellationToken)
    {
        var message = new DeploymentLogMessage(
            chunk.DeploymentId,
            DateTimeOffset.FromUnixTimeMilliseconds(chunk.Timestamp),
            chunk.Level.ToString(),
            chunk.Message
        );

        await messageBus.PublishAsync(message);
    }
}
