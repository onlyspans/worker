using Grpc.Core;
using Targets.Communication;

namespace Onlyspans.Worker.Api.Clients;

public interface ITargetsControllerClient
{
    AsyncServerStreamingCall<ExecutionResult> ExecuteOnTargetAsync(
        TargetExecutionRequest request,
        CancellationToken cancellationToken);
}
