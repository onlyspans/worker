using Worker.Communication;

namespace Onlyspans.Worker.Api.Services;

public interface ILogPublisher
{
    Task PublishAsync(LogChunk chunk, CancellationToken cancellationToken);
}
