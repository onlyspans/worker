using FluentAssertions;
using Grpc.Core;
using NSubstitute;
using Onlyspans.Worker.Api.Clients;
using Targets.Communication;

namespace Onlyspans.Worker.Api.Tests.Clients;

public sealed class TargetsControllerClientTests
{
    [Fact]
    public void ExecuteOnTargetAsync_DelegatesToGeneratedGrpcClient()
    {
        var generatedClient = Substitute.For<TargetsService.TargetsServiceClient>();
        var sut = new TargetsControllerClient(generatedClient);
        using var cts = new CancellationTokenSource();

        var fakeCall = new AsyncDuplexStreamingCall<DeploymentInput, ExecutionResult>(
            Substitute.For<IClientStreamWriter<DeploymentInput>>(),
            Substitute.For<IAsyncStreamReader<ExecutionResult>>(),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        generatedClient.ExecuteOnTarget(
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                cts.Token)
            .Returns(fakeCall);

        var result = sut.ExecuteOnTargetAsync(cts.Token);

        result.Should().NotBeNull();
        generatedClient.Received(1).ExecuteOnTarget(
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            cts.Token);
    }
}
