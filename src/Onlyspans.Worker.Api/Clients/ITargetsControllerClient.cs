using Grpc.Core;
using Targets.Communication;

namespace Onlyspans.Worker.Api.Clients;

public interface ITargetsControllerClient
{
    AsyncDuplexStreamingCall<DeploymentInput, ExecutionResult> ExecuteOnTargetAsync(
        CancellationToken cancellationToken);
}
