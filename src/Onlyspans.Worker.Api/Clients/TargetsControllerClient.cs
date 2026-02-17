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

    public AsyncServerStreamingCall<ExecutionResult> ExecuteOnTargetAsync(
        TargetExecutionRequest request,
        CancellationToken cancellationToken)
    {
        return _client.ExecuteOnTarget(request, cancellationToken: cancellationToken);
    }
}
