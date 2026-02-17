using FluentAssertions;
using NSubstitute;
using Onlyspans.Worker.Api.Messages;
using Onlyspans.Worker.Api.Services;
using Wolverine;
using Worker.Communication;

namespace Onlyspans.Worker.Api.Tests.Services;

public sealed class WolverineLogPublisherTests
{
    private readonly IMessageBus _messageBus = Substitute.For<IMessageBus>();
    private readonly WolverineLogPublisher _sut;

    public WolverineLogPublisherTests()
    {
        _sut = new WolverineLogPublisher(_messageBus);
    }

    [Fact]
    public async Task PublishAsync_ValidChunk_PublishesDeploymentLogMessageToMessageBus()
    {
        // Arrange
        var chunk = new LogChunk
        {
            DeploymentId = "deploy-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Level = LogLevel.Info,
            Message = "Test log message"
        };

        // Act
        await _sut.PublishAsync(chunk, CancellationToken.None);

        // Assert
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<DeploymentLogMessage>(m =>
                m.DeploymentId == "deploy-1" &&
                m.Message == "Test log message" &&
                m.LogLevel == "Info"));
    }

    [Fact]
    public async Task PublishAsync_MapsTimestampFromUnixMillisToDateTimeOffset()
    {
        // Arrange
        var expectedTime = new DateTimeOffset(2026, 2, 17, 10, 0, 0, TimeSpan.Zero);
        var chunk = new LogChunk
        {
            DeploymentId = "deploy-2",
            Timestamp = expectedTime.ToUnixTimeMilliseconds(),
            Level = LogLevel.Info,
            Message = "msg"
        };

        // Act
        await _sut.PublishAsync(chunk, CancellationToken.None);

        // Assert
        await _messageBus.Received(1).PublishAsync(
            Arg.Is<DeploymentLogMessage>(m => m.Timestamp == expectedTime));
    }

    [Fact]
    public async Task NoOpLogPublisher_PublishAsync_DoesNotThrowAndDoesNotPublish()
    {
        // Arrange
        var noOp = new NoOpLogPublisher(Microsoft.Extensions.Logging.Abstractions.NullLogger<NoOpLogPublisher>.Instance);
        var chunk = new LogChunk { DeploymentId = "d", Message = "m", Level = LogLevel.Info };

        // Act
        var act = () => noOp.PublishAsync(chunk, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
