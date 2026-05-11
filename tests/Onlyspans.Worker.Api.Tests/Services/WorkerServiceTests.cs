using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Onlyspans.Worker.Api.Clients;
using Targets.Communication;
using Worker.Communication;

using SUTWorkerService = Onlyspans.Worker.Api.Services.WorkerService;
using TCCommandType = Targets.Communication.CommandType;
using TCDeploymentInput = Targets.Communication.DeploymentInput;
using TCResultType = Targets.Communication.ResultType;
using WorkerArtifactChunk = Worker.Communication.ArtifactChunk;
using WorkerCommandType = Worker.Communication.CommandType;
using WorkerErrorType = Worker.Communication.ErrorType;
using WorkerStepCommand = Worker.Communication.StepCommand;

namespace Onlyspans.Worker.Api.Tests.Services;

public sealed class WorkerServiceTests
{
    [Fact]
    public async Task ExecuteStep_WhenFirstMessageIsNotMetadata_ReturnsInvalidStepPackage()
    {
        var targetsClient = Substitute.For<ITargetsControllerClient>();
        var sut = CreateSut(targetsClient);
        var response = new TestServerStreamWriter<StepExecutionMessage>();

        await sut.ExecuteStep(
            new TestAsyncStreamReader<StepExecutionInput>([
                ChunkInput("artifact", isLast: true)
            ]),
            response,
            new TestServerCallContext());

        response.Writes.Should().ContainSingle();
        response.Writes.Single().Result.Error.ErrorType.Should().Be(WorkerErrorType.InvalidStepPackage);
        targetsClient.DidNotReceive().ExecuteOnTargetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStep_WhenCommandIsMissing_ReturnsInvalidStepPackage()
    {
        var targetsClient = Substitute.For<ITargetsControllerClient>();
        var sut = CreateSut(targetsClient);
        var response = new TestServerStreamWriter<StepExecutionMessage>();

        await sut.ExecuteStep(
            new TestAsyncStreamReader<StepExecutionInput>([
                MetadataInput(CreateMetadataWithoutCommand())
            ]),
            response,
            new TestServerCallContext());

        response.Writes.Should().ContainSingle();
        response.Writes.Single().Result.Error.ErrorType.Should().Be(WorkerErrorType.InvalidStepPackage);
        targetsClient.DidNotReceive().ExecuteOnTargetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStep_WhenCommandTypeIsUnspecified_ReturnsInvalidStepPackage()
    {
        var targetsClient = Substitute.For<ITargetsControllerClient>();
        var sut = CreateSut(targetsClient);
        var response = new TestServerStreamWriter<StepExecutionMessage>();

        await sut.ExecuteStep(
            new TestAsyncStreamReader<StepExecutionInput>([
                MetadataInput(CreateMetadata(new WorkerStepCommand
                {
                    Type = WorkerCommandType.Unspecified,
                    InlineScript = "echo ok"
                }))
            ]),
            response,
            new TestServerCallContext());

        response.Writes.Should().ContainSingle();
        response.Writes.Single().Result.Error.ErrorType.Should().Be(WorkerErrorType.InvalidStepPackage);
        targetsClient.DidNotReceive().ExecuteOnTargetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStep_WhenCommandSourceIsMissing_ReturnsInvalidStepPackage()
    {
        var targetsClient = Substitute.For<ITargetsControllerClient>();
        var sut = CreateSut(targetsClient);
        var response = new TestServerStreamWriter<StepExecutionMessage>();

        await sut.ExecuteStep(
            new TestAsyncStreamReader<StepExecutionInput>([
                MetadataInput(CreateMetadata(new WorkerStepCommand
                {
                    Type = WorkerCommandType.Shell
                }))
            ]),
            response,
            new TestServerCallContext());

        response.Writes.Should().ContainSingle();
        response.Writes.Single().Result.Error.ErrorType.Should().Be(WorkerErrorType.InvalidStepPackage);
        targetsClient.DidNotReceive().ExecuteOnTargetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStep_ForwardsArtifactChunksToTargetsController()
    {
        var (targetCall, targetRequestStream) = CreateTargetCall(SuccessResult());
        var targetsClient = Substitute.For<ITargetsControllerClient>();
        targetsClient.ExecuteOnTargetAsync(Arg.Any<CancellationToken>()).Returns(targetCall);
        var sut = CreateSut(targetsClient);
        var response = new TestServerStreamWriter<StepExecutionMessage>();

        await sut.ExecuteStep(
            new TestAsyncStreamReader<StepExecutionInput>([
                MetadataInput(CreateMetadata()),
                ChunkInput("part-1", isLast: false),
                ChunkInput("part-2", isLast: true)
            ]),
            response,
            new TestServerCallContext());

        targetRequestStream.Writes.Should().HaveCount(3);
        targetRequestStream.Writes[1].ArtifactChunk.Data.ToStringUtf8().Should().Be("part-1");
        targetRequestStream.Writes[1].ArtifactChunk.IsLast.Should().BeFalse();
        targetRequestStream.Writes[2].ArtifactChunk.Data.ToStringUtf8().Should().Be("part-2");
        targetRequestStream.Writes[2].ArtifactChunk.IsLast.Should().BeTrue();
        targetRequestStream.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteStep_ForwardsCommandToTargetsController()
    {
        var (targetCall, targetRequestStream) = CreateTargetCall(SuccessResult());
        var targetsClient = Substitute.For<ITargetsControllerClient>();
        targetsClient.ExecuteOnTargetAsync(Arg.Any<CancellationToken>()).Returns(targetCall);
        var sut = CreateSut(targetsClient);
        var response = new TestServerStreamWriter<StepExecutionMessage>();

        await sut.ExecuteStep(
            new TestAsyncStreamReader<StepExecutionInput>([
                MetadataInput(CreateMetadata(new WorkerStepCommand
                {
                    Type = WorkerCommandType.Shell,
                    TimeoutSeconds = 45,
                    WorkingDirectory = "/app",
                    ScriptPath = "deploy.sh"
                }))
            ]),
            response,
            new TestServerCallContext());

        var forwardedMetadata = targetRequestStream.Writes.Single().Metadata;
        forwardedMetadata.DeploymentId.Should().Be("deploy-1");
        forwardedMetadata.StepId.Should().Be("step-1");
        forwardedMetadata.StepName.Should().Be("Build");
        forwardedMetadata.TargetId.Should().Be("target-1");
        forwardedMetadata.EnvironmentVariables.Should().ContainKey("ENV").WhoseValue.Should().Be("prod");
        forwardedMetadata.Command.Type.Should().Be(TCCommandType.Shell);
        forwardedMetadata.Command.TimeoutSeconds.Should().Be(45);
        forwardedMetadata.Command.WorkingDirectory.Should().Be("/app");
        forwardedMetadata.Command.ScriptPath.Should().Be("deploy.sh");
    }

    [Fact]
    public async Task ExecuteStep_WhenTargetSucceeds_ReturnsSuccessResultAndStreamsLogs()
    {
        var (targetCall, _) = CreateTargetCall(
            new ExecutionResult
            {
                Type = TCResultType.Log,
                Timestamp = 123,
                Message = "running"
            },
            SuccessResult("done"));
        var targetsClient = Substitute.For<ITargetsControllerClient>();
        targetsClient.ExecuteOnTargetAsync(Arg.Any<CancellationToken>()).Returns(targetCall);
        var sut = CreateSut(targetsClient);
        var response = new TestServerStreamWriter<StepExecutionMessage>();

        await sut.ExecuteStep(
            new TestAsyncStreamReader<StepExecutionInput>([
                MetadataInput(CreateMetadata())
            ]),
            response,
            new TestServerCallContext());

        response.Writes.Where(m => m.MessageCase == StepExecutionMessage.MessageOneofCase.Result)
            .Should().ContainSingle();
        response.Writes[0].Log.DeploymentId.Should().Be("deploy-1");
        response.Writes[0].Log.StepId.Should().Be("step-1");
        response.Writes[0].Log.Message.Should().Be("running");
        response.Writes.Last().Result.Success.Summary.Should().Be("done");
        response.Writes.Last().Result.Success.DeploymentId.Should().Be("deploy-1");
        response.Writes.Last().Result.Success.StepId.Should().Be("step-1");
    }

    [Fact]
    public async Task ExecuteStep_WhenTargetErrors_ReturnsErrorResult()
    {
        var (targetCall, _) = CreateTargetCall(new ExecutionResult
        {
            Type = TCResultType.Error,
            Timestamp = 123,
            Message = "target failed"
        });
        var targetsClient = Substitute.For<ITargetsControllerClient>();
        targetsClient.ExecuteOnTargetAsync(Arg.Any<CancellationToken>()).Returns(targetCall);
        var sut = CreateSut(targetsClient);
        var response = new TestServerStreamWriter<StepExecutionMessage>();

        await sut.ExecuteStep(
            new TestAsyncStreamReader<StepExecutionInput>([
                MetadataInput(CreateMetadata())
            ]),
            response,
            new TestServerCallContext());

        response.Writes.Where(m => m.MessageCase == StepExecutionMessage.MessageOneofCase.Result)
            .Should().ContainSingle();
        response.Writes.Last().Result.Error.ErrorType.Should().Be(WorkerErrorType.TargetExecutionFailed);
        response.Writes.Last().Result.Error.DeploymentId.Should().Be("deploy-1");
        response.Writes.Last().Result.Error.StepId.Should().Be("step-1");
        response.Writes.Last().Result.Error.Message.Should().Be("target failed");
    }

    private static SUTWorkerService CreateSut(ITargetsControllerClient targetsClient)
    {
        return new SUTWorkerService(targetsClient, NullLogger<SUTWorkerService>.Instance);
    }

    private static (AsyncDuplexStreamingCall<TCDeploymentInput, ExecutionResult> Call,
        TestClientStreamWriter<TCDeploymentInput> RequestStream) CreateTargetCall(
        params ExecutionResult[] responses)
    {
        var requestStream = new TestClientStreamWriter<TCDeploymentInput>();
        var call = new AsyncDuplexStreamingCall<TCDeploymentInput, ExecutionResult>(
            requestStream,
            new TestAsyncStreamReader<ExecutionResult>(responses),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        return (call, requestStream);
    }

    private static ExecutionResult SuccessResult(string message = "Step completed successfully")
    {
        return new ExecutionResult
        {
            Type = TCResultType.Success,
            Timestamp = 456,
            Message = message
        };
    }

    private static StepExecutionInput MetadataInput(StepExecutionMetadata metadata)
    {
        return new StepExecutionInput { Metadata = metadata };
    }

    private static StepExecutionInput ChunkInput(string content, bool isLast)
    {
        return new StepExecutionInput
        {
            ArtifactChunk = new WorkerArtifactChunk
            {
                Data = ByteString.CopyFromUtf8(content),
                IsLast = isLast
            }
        };
    }

    private static StepExecutionMetadata CreateMetadata(WorkerStepCommand? command = null)
    {
        var metadata = new StepExecutionMetadata
        {
            DeploymentId = "deploy-1",
            ProcessId = "process-1",
            StepId = "step-1",
            StepName = "Build",
            StepOrder = 1,
            ProjectId = "project-1",
            EnvironmentId = "env-1",
            TargetId = "target-1"
        };

        metadata.Command = command ?? new WorkerStepCommand
        {
            Type = WorkerCommandType.Shell,
            TimeoutSeconds = 30,
            WorkingDirectory = "/app",
            InlineScript = "echo ok"
        };

        metadata.ResolvedVariables.Add("ENV", "prod");
        return metadata;
    }

    private static StepExecutionMetadata CreateMetadataWithoutCommand()
    {
        var metadata = CreateMetadata();
        metadata.Command = null;
        return metadata;
    }

    private sealed class TestAsyncStreamReader<T>(IEnumerable<T> messages) : IAsyncStreamReader<T>
    {
        private readonly IEnumerator<T> _enumerator = messages.GetEnumerator();

        public T Current { get; private set; } = default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!_enumerator.MoveNext())
            {
                return Task.FromResult(false);
            }

            Current = _enumerator.Current;
            return Task.FromResult(true);
        }
    }

    private sealed class TestClientStreamWriter<T> : IClientStreamWriter<T>
    {
        public List<T> Writes { get; } = [];
        public bool Completed { get; private set; }
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Writes.Add(message);
            return Task.CompletedTask;
        }

        public Task CompleteAsync()
        {
            Completed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestServerStreamWriter<T> : IServerStreamWriter<T>
    {
        public List<T> Writes { get; } = [];
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Writes.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "ExecuteStep";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:0";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(5);
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } =
            new(string.Empty, new Dictionary<string, List<AuthProperty>>());
        protected override IDictionary<object, object> UserStateCore { get; } =
            new Dictionary<object, object>();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
        {
            return Task.CompletedTask;
        }

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        {
            throw new NotSupportedException();
        }
    }
}
