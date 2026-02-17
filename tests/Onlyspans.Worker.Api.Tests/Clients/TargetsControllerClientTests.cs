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
        // Arrange
        var generatedClient = Substitute.For<TargetsService.TargetsServiceClient>();
        var sut = new TargetsControllerClient(generatedClient);

        var request = new TargetExecutionRequest
        {
            DeploymentId = "deploy-1",
            TargetId = "target-1",
            TargetType = "ssh",
            SnapshotPath = "/tmp/snapshot"
        };

        // AsyncServerStreamingCall constructor: (responseStream, responseHeadersAsync, getStatusFunc, getTrailersFunc, disposeAction)
        var fakeCall = new AsyncServerStreamingCall<ExecutionResult>(
            Substitute.For<IAsyncStreamReader<ExecutionResult>>(),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        generatedClient.ExecuteOnTarget(
                Arg.Is<TargetExecutionRequest>(r => r.DeploymentId == "deploy-1"),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(fakeCall);

        // Act
        var result = sut.ExecuteOnTargetAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        generatedClient.Received(1).ExecuteOnTarget(
            Arg.Is<TargetExecutionRequest>(r => r.DeploymentId == "deploy-1"),
            Arg.Any<Metadata>(),
            Arg.Any<DateTime?>(),
            Arg.Any<CancellationToken>());
    }
}
