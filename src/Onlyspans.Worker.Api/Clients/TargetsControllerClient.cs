using Grpc.Core;
using Targets.Communication;

namespace Onlyspans.Worker.Api.Clients;

public sealed class TargetsControllerClient : ITargetsControllerClient
{
    private readonly TargetsService.TargetsServiceClient _client;

    public TargetsControllerClient(TargetsService.TargetsServiceClient client)
    {
        _client = client;
    }

    public AsyncDuplexStreamingCall<DeploymentInput, ExecutionResult> ExecuteOnTargetAsync(
        CancellationToken cancellationToken)
    {
        return _client.ExecuteOnTarget(cancellationToken: cancellationToken);
    }
}
