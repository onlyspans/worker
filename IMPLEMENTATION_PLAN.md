# Worker Service - Implementation Plan

**Created**: 2026-02-15
**Architecture Reference**: https://github.com/onlyspans/issues/blob/nikitakaralius/platform-arch/arch/target-arch.md
**ADR References**: Issues #1, #10, #12, #14, #15 (onlyspans/issues)
**Reference Implementation**: Variables service (C# .NET)

---

## Phase 0: Documentation Discovery ✅

### Sources Verified
- ✅ Architecture document read and analyzed
- ✅ ADRs from GitHub Issues reviewed
- ✅ Reference implementations examined (Variables, Events)
- ✅ Current scaffolding structure documented

### Key Findings
- **Testing Stack**: XUnit v3, TestContainers, FluentAssertions, Moq
- **Language**: Proceed with C# .NET 10.0 (current scaffolding)
- **Coding Standards**: Use LINQ method syntax (not query syntax)
- **Worker Role**: Stateless execution layer between Processes and Targets Controller
- **Security**: No direct access to Variables service (secrets pre-resolved)

### Allowed APIs List
```csharp
// gRPC
Grpc.AspNetCore 2.64.0+
Grpc.Tools (for proto compilation)

// Testing
xunit 3.0.0+
xunit.runner.visualstudio 3.0.0+
Testcontainers.PostgreSql 4.2.0
Microsoft.AspNetCore.Mvc.Testing 10.0.0
FluentAssertions 7.0.0
NSubstitute (latest)

// Data Access
Npgsql.EntityFrameworkCore.PostgreSQL
Microsoft.EntityFrameworkCore.Tools

// Messaging
Wolverine (for message handling and log streaming)

// AWS
AWSSDK.S3 (for snapshot downloads)

// Configuration
Strongly.Options (source generator)

// Logging
Serilog.AspNetCore
Serilog.Sinks.Console
Serilog.Formatting.Compact
```

### Anti-Patterns to Avoid
- ❌ LINQ query syntax (use method syntax instead)
- ❌ In-memory database for integration tests
- ❌ Direct gRPC calls to Variables service
- ❌ Storing secrets in Worker (receive pre-resolved from Processes)
- ❌ Stateful deployment tracking (use Processes for state)
- ❌ Implementing rollback logic (Processes decides, Worker executes)

---

## Phase 1: Proto Definitions & Service Contracts

### What to Implement

**Copy proto patterns from Variables service** and adapt for Worker's domain.

**Reference**: `/Users/arthurminiakhmetov/developer-platform/variables/src/Onlyspans.Variables.Api/gRPC/Protos/variables.proto` (lines 22-48 for error handling pattern)

#### Tasks

1. **Create `worker.proto`** at `src/Onlyspans.Worker.Api/Protos/worker.proto`
   - Copy `oneof result { Success/Error }` pattern from Variables proto
   - Define `DeploymentPackage` message (received from Processes)
   - Define `LogChunk` message (streamed to Processes)
   - Define `WorkerService` with `ExecuteDeployment` RPC

2. **Create `targets.proto`** at `src/Onlyspans.Worker.Api/Protos/targets.proto`
   - Define client contract for Targets Controller
   - Define `TargetExecutionRequest` message
   - Define `ExecutionResult` streaming response

3. **Update `.csproj`** to compile new protos
   - Add `<Protobuf Include="Protos/worker.proto" GrpcServices="Server" />`
   - Add `<Protobuf Include="Protos/targets.proto" GrpcServices="Client" />`

4. **Remove placeholder `greet.proto`** and `GreeterService.cs`

### Documentation References

- **Error handling pattern**: Variables `variables.proto` lines 22-35 (oneof result)
- **Streaming pattern**: Architecture doc "Поток 2: Доставка релиза" (Step 5-10)
- **Message structure**: Architecture doc "Worker receives snapshot + secrets"

### Verification Checklist

```bash
# Proto compilation succeeds
dotnet build

# Generated C# files exist
ls obj/Debug/net10.0/Protos/

# Grep for expected types
grep -r "DeploymentPackage" src/Onlyspans.Worker.Api/obj/
grep -r "WorkerService" src/Onlyspans.Worker.Api/obj/

# No greet.proto references remain
! grep -r "Greeter" src/
```

### Anti-Pattern Guards

- ❌ Do NOT add proto fields for raw secrets (only pre-resolved values)
- ❌ Do NOT create synchronous ExecuteDeployment (must return stream)
- ❌ Do NOT invent proto options without checking Variables proto first

---

## Phase 2: Database Setup & Migrations

### What to Implement

**Copy database setup pattern from Variables service** for worker-logs persistence.

**Reference**: Variables `IMPLEMENTATION_PLAN.md` Phase 2 (MigrationHostedService pattern)

#### Tasks

1. **Create DbContext** at `src/Onlyspans.Worker.Api/Data/WorkerDbContext.cs`
   - Copy entity configuration pattern from Variables
   - Define `DeploymentLog` entity (deployment_id, timestamp, log_level, message)
   - Define `DeploymentResult` entity (deployment_id, status, started_at, completed_at)

2. **Add EF Core packages** to `.csproj`
   ```bash
   dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
   dotnet add package Microsoft.EntityFrameworkCore.Tools
   dotnet add package Microsoft.EntityFrameworkCore.Design
   ```

3. **Create initial migration**
   ```bash
   dotnet ef migrations add InitialCreate -o Data/Migrations
   ```

4. **Create `MigrationHostedService.cs`** at `src/Onlyspans.Worker.Api/Hosting/MigrationHostedService.cs`
   - Copy from Variables IMPLEMENTATION_PLAN.md Phase 2
   - Auto-migrate on startup (development + production)

5. **Register in Program.cs**
   ```csharp
   builder.Services.AddDbContext<WorkerDbContext>(options =>
       options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
   builder.Services.AddHostedService<MigrationHostedService>();
   ```

### Documentation References

- **MigrationHostedService pattern**: Variables IMPLEMENTATION_PLAN.md Phase 2
- **DbContext pattern**: Variables service codebase
- **Connection string config**: Variables `appsettings.json`

### Verification Checklist

```bash
# Migration files created
ls src/Onlyspans.Worker.Api/Data/Migrations/

# Build succeeds
dotnet build

# TestContainers can apply migrations (test in Phase 7)
# Verify in integration tests that migrations run automatically
```

### Anti-Pattern Guards

- ❌ Do NOT use in-memory database for testing (use TestContainers)
- ❌ Do NOT create separate migration service (use HostedService pattern)
- ❌ Do NOT skip migration on startup (auto-migrate always enabled)

---

## Phase 3: Configuration & Startup Modularity

### What to Implement

**Copy modular startup pattern from Variables service** for clean separation of concerns.

**Reference**: Variables IMPLEMENTATION_PLAN.md Phase 3 (Startup.*.cs files)

#### Tasks

1. **Create modular Startup files** at `src/Onlyspans.Worker.Api/`
   - `Startup.cs` - Main orchestrator
   - `Startup.Db.cs` - DbContext + migrations registration
   - `Startup.Grpc.cs` - gRPC server + client registration
   - `Startup.Messaging.cs` - Wolverine messaging (with feature flag)
   - `Startup.Logging.cs` - Serilog configuration
   - `Startup.Healthz.cs` - Health check endpoints

2. **Add configuration packages**
   ```bash
   dotnet add package Strongly.Options
   dotnet add package Serilog.AspNetCore
   dotnet add package Serilog.Sinks.Console
   dotnet add package Serilog.Formatting.Compact
   ```

3. **Create configuration classes** at `src/Onlyspans.Worker.Api/Configuration/`
   - `DatabaseOptions.cs` (connection string)
   - `TargetsControllerOptions.cs` (gRPC endpoint)
   - `ProcessesOptions.cs` (gRPC endpoint)
   - `MessagingOptions.cs` (Wolverine configuration, Kafka broker, topic name, enabled flag)
   - `S3Options.cs` (bucket name, region, credentials)

4. **Update `appsettings.json`**
   ```json
   {
     "ConnectionStrings": {
       "Database": "Host=localhost;Database=worker;Username=worker;Password=dev"
     },
     "TargetsController": {
       "Endpoint": "http://localhost:5001"
     },
     "Processes": {
       "Endpoint": "http://localhost:5002"
     },
     "Messaging": {
       "Enabled": false,
       "Kafka": {
         "BootstrapServers": "localhost:9092",
         "Topic": "worker-logs"
       }
     },
     "S3": {
       "BucketName": "onlyspans-snapshots",
       "Region": "us-east-1"
     }
   }
   ```

5. **Refactor `Program.cs`** to use modular startup
   ```csharp
   var builder = WebApplication.CreateBuilder(args);

   builder.AddLogging();  // Startup.Logging.cs
   builder.AddDatabase(); // Startup.Db.cs
   builder.AddGrpcServices(); // Startup.Grpc.cs
   builder.AddMessaging(); // Startup.Messaging.cs (Wolverine with feature flag)

   var app = builder.Build();
   app.MapHealthChecks(); // Startup.Healthz.cs
   app.MapGrpcServices();
   app.Run();
   ```

### Documentation References

- **Modular startup pattern**: Variables IMPLEMENTATION_PLAN.md Phase 3
- **Strongly.Options usage**: Variables service configuration
- **Feature flag pattern**: Events service CLAUDE.md (KAFKA_ENABLED)

### Verification Checklist

```bash
# Build succeeds with new configuration
dotnet build

# Configuration binds correctly (test with unit test)
dotnet test --filter "ConfigurationTests"

# Serilog outputs structured JSON
dotnet run | grep -q '"@t"'

# Feature flag disables messaging
MESSAGING_ENABLED=false dotnet run
! grep -q "Wolverine" <logs>
```

### Anti-Pattern Guards

- ❌ Do NOT hardcode service endpoints (use configuration)
- ❌ Do NOT require messaging for local development (feature flag)
- ❌ Do NOT use appsettings.*.json for secrets (use environment variables)
- ❌ Do NOT use LINQ query syntax (use method syntax: .Where().Select() not from...where...select)

---

## Phase 4: S3 Snapshot Download Service

### What to Implement

**Create service for downloading deployment snapshots from S3** (no existing pattern to copy, implement from scratch with AWS SDK best practices).

**Reference**: AWS SDK documentation + Architecture doc Step 6 "worker → S3 (snapshots)"

#### Tasks

1. **Add AWS SDK package**
   ```bash
   dotnet add package AWSSDK.S3
   ```

2. **Create `ISnapshotDownloader` interface** at `src/Onlyspans.Worker.Api/Services/ISnapshotDownloader.cs`
   ```csharp
   public interface ISnapshotDownloader
   {
       Task<DownloadSnapshotResult> DownloadAsync(
           string snapshotKey,
           CancellationToken cancellationToken);
   }
   ```

3. **Create `S3SnapshotDownloader` implementation** at `src/Onlyspans.Worker.Api/Services/S3SnapshotDownloader.cs`
   - Use `AmazonS3Client` with configured region
   - Download snapshot to temporary file
   - Return file path + metadata
   - Handle S3 errors (NoSuchKey, AccessDenied, etc.)
   - Log download progress for large snapshots

4. **Register in `Startup.Grpc.cs`**
   ```csharp
   services.AddSingleton<IAmazonS3>(sp =>
       new AmazonS3Client(s3Options.Region));
   services.AddScoped<ISnapshotDownloader, S3SnapshotDownloader>();
   ```

### Documentation References

- **Architecture flow**: Architecture doc "Поток 2: Доставка релиза" Step 6
- **AWS SDK patterns**: https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/s3-apis-intro.html

### Verification Checklist

```bash
# Build succeeds
dotnet build

# Interface exists
grep -r "ISnapshotDownloader" src/

# Unit tests pass (mock S3 client with Moq)
dotnet test --filter "S3SnapshotDownloaderTests"

# Integration test with LocalStack (optional, defer to Phase 7)
```

### Anti-Pattern Guards

- ❌ Do NOT download entire snapshot into memory (stream to file)
- ❌ Do NOT expose S3 credentials in logs
- ❌ Do NOT retry indefinitely (max 3 retries with exponential backoff)

---

## Phase 5: Targets Controller gRPC Client

### What to Implement

**Create gRPC client for calling Targets Controller** to execute deployments.

**Reference**: Variables service gRPC client pattern (if exists), otherwise standard Grpc.Net.Client usage

#### Tasks

1. **Add gRPC client package** (if not already present)
   ```bash
   dotnet add package Grpc.Net.Client
   dotnet add package Grpc.Net.ClientFactory
   ```

2. **Create `ITargetsControllerClient` interface** at `src/Onlyspans.Worker.Api/Clients/ITargetsControllerClient.cs`
   ```csharp
   public interface ITargetsControllerClient
   {
       IAsyncStreamReader<ExecutionResult> ExecuteOnTargetAsync(
           TargetExecutionRequest request,
           CancellationToken cancellationToken);
   }
   ```

3. **Create `TargetsControllerClient` wrapper** at `src/Onlyspans.Worker.Api/Clients/TargetsControllerClient.cs`
   - Wrap generated `TargetsService.TargetsServiceClient`
   - Add error handling for connection failures
   - Add logging for all outbound calls

4. **Register in `Startup.Grpc.cs`**
   ```csharp
   services.AddGrpcClient<TargetsService.TargetsServiceClient>(options =>
   {
       options.Address = new Uri(targetsOptions.Endpoint);
   });
   services.AddScoped<ITargetsControllerClient, TargetsControllerClient>();
   ```

### Documentation References

- **Architecture flow**: Architecture doc "Worker → Targets Controller" (Step 7-8)
- **gRPC client factory**: https://learn.microsoft.com/en-us/aspnet/core/grpc/clientfactory
- **Streaming pattern**: Variables proto error handling (adapt for streaming)

### Verification Checklist

```bash
# Build succeeds
dotnet build

# Client registered in DI
grep -r "AddGrpcClient" src/

# Unit tests with mocked client pass
dotnet test --filter "TargetsControllerClientTests"
```

### Anti-Pattern Guards

- ❌ Do NOT create client per request (use client factory)
- ❌ Do NOT ignore stream completion (await stream until done)
- ❌ Do NOT swallow connection errors (propagate to caller)

---

## Phase 6: Wolverine Message Publishing Service

### What to Implement

**Configure Wolverine for message publishing** to stream logs to Kafka with feature flag support.

**Reference**: Wolverine documentation - https://wolverine.netlify.app/

#### Tasks

1. **Add Wolverine packages**
   ```bash
   dotnet add package WolverineFx
   dotnet add package Wolverine.Kafka
   ```

2. **Create `ILogPublisher` interface** at `src/Onlyspans.Worker.Api/Services/ILogPublisher.cs`
   ```csharp
   public interface ILogPublisher
   {
       Task PublishAsync(LogChunk chunk, CancellationToken cancellationToken);
   }
   ```

3. **Create log message type** at `src/Onlyspans.Worker.Api/Messages/DeploymentLogMessage.cs`
   ```csharp
   public record DeploymentLogMessage(
       string DeploymentId,
       DateTimeOffset Timestamp,
       string LogLevel,
       string Message
   );
   ```

4. **Configure Wolverine in `Startup.Messaging.cs`**
   ```csharp
   public static void AddMessaging(this WebApplicationBuilder builder)
   {
       var messagingOptions = builder.Configuration
           .GetSection("Messaging")
           .Get<MessagingOptions>();

       if (messagingOptions?.Enabled == true)
       {
           builder.Host.UseWolverine(opts =>
           {
               // Configure Kafka transport
               opts.UseKafka(messagingOptions.Kafka.BootstrapServers)
                   .ConfigureClient(client =>
                   {
                       // Kafka client configuration
                       client.ClientId = "worker-service";
                   });

               // Publish DeploymentLogMessage to Kafka topic
               opts.PublishMessage<DeploymentLogMessage>()
                   .ToKafkaTopic(messagingOptions.Kafka.Topic);
           });

           builder.Services.AddScoped<ILogPublisher, WolverineLogPublisher>();
       }
       else
       {
           builder.Services.AddScoped<ILogPublisher, NoOpLogPublisher>();
       }
   }
   ```

5. **Create `WolverineLogPublisher` implementation** at `src/Onlyspans.Worker.Api/Services/WolverineLogPublisher.cs`
   ```csharp
   public class WolverineLogPublisher(IMessageBus messageBus) : ILogPublisher
   {
       public async Task PublishAsync(LogChunk chunk, CancellationToken ct)
       {
           var message = new DeploymentLogMessage(
               chunk.DeploymentId,
               DateTimeOffset.UtcNow,
               chunk.Level.ToString(),
               chunk.Message
           );

           await messageBus.PublishAsync(message, ct);
       }
   }
   ```

6. **Create `NoOpLogPublisher`** for when messaging is disabled
   ```csharp
   public class NoOpLogPublisher : ILogPublisher
   {
       public Task PublishAsync(LogChunk chunk, CancellationToken ct)
           => Task.CompletedTask;
   }
   ```

### Documentation References

- **Wolverine docs**: https://wolverine.netlify.app/guide/messaging/
- **Wolverine + Kafka**: https://wolverine.netlify.app/guide/messaging/transports/kafka.html
- **Feature flag pattern**: Events CLAUDE.md "KAFKA_ENABLED" section
- **Log streaming flow**: Architecture doc Step 7 "worker → Kafka"

### Verification Checklist

```bash
# Build succeeds
dotnet build

# Feature flag works (messaging disabled)
MESSAGING__ENABLED=false dotnet test --filter "LogPublisherTests"
# Should use NoOpLogPublisher

# With messaging enabled, Wolverine configures Kafka
MESSAGING__ENABLED=true dotnet run
# Check logs for "Wolverine" and "Kafka"

# Integration test with Kafka (defer to Phase 9)
```

### Anti-Pattern Guards

- ❌ Do NOT require messaging for local dev (use NoOpLogPublisher by default)
- ❌ Do NOT use Confluent.Kafka directly (use Wolverine abstraction)
- ❌ Do NOT lose logs on messaging failure (also log to database)
- ❌ Do NOT use LINQ query syntax (use method syntax)

---

## Phase 7: Worker Service Implementation

### What to Implement

**Implement main `WorkerService` gRPC service** that orchestrates deployment execution.

**Reference**: Current `GreeterService.cs` structure + Variables service handler pattern

#### Tasks

1. **Create `WorkerService.cs`** at `src/Onlyspans.Worker.Api/Services/WorkerService.cs`
   ```csharp
   public class WorkerService(
       ISnapshotDownloader snapshotDownloader,
       ITargetsControllerClient targetsClient,
       ILogPublisher logPublisher,
       WorkerDbContext dbContext,
       ILogger<WorkerService> logger
   ) : WorkerServiceProto.WorkerServiceProtoBase
   {
       public override async Task ExecuteDeployment(
           DeploymentPackage request,
           IServerStreamWriter<LogChunk> responseStream,
           ServerCallContext context)
       {
           // 1. Download snapshot from S3
           // 2. Execute on target via Targets Controller
           // 3. Stream logs to both: Kafka + responseStream
           // 4. Save result to worker-logs DB
       }
   }
   ```

2. **Implement deployment flow**
   - Download snapshot using `ISnapshotDownloader`
   - Parse deployment steps from snapshot
   - Call `ITargetsControllerClient.ExecuteOnTargetAsync`
   - For each `ExecutionResult` from target:
     - Convert to `LogChunk`
     - Send to `responseStream.WriteAsync()`
     - Send to `logPublisher.PublishAsync()`
     - Save to `dbContext.DeploymentLogs.Add()`
   - Save final result to `dbContext.DeploymentResults.Add()`

3. **Add graceful shutdown** (copy from Events CLAUDE.md)
   - Respect `context.CancellationToken`
   - Wolverine handles message flushing automatically
   - Complete in-flight deployments within 30-second timeout

4. **Register in `Startup.Grpc.cs`**
   ```csharp
   app.MapGrpcService<WorkerService>();
   ```

### Documentation References

- **Service structure**: `GreeterService.cs` lines 1-15
- **Graceful shutdown**: Events CLAUDE.md "Graceful Shutdown" section
- **Deployment flow**: Architecture doc "Поток 2: Доставка релиза"

### Verification Checklist

```bash
# Build succeeds
dotnet build

# Service registered
grep -r "MapGrpcService<WorkerService>" src/

# Integration test with mocked dependencies passes
dotnet test --filter "WorkerServiceTests"

# End-to-end test with real Targets Controller (manual, defer to Phase 9)
```

### Anti-Pattern Guards

- ❌ Do NOT block stream on database writes (async all the way)
- ❌ Do NOT retry failed deployments (Processes handles retries)
- ❌ Do NOT store snapshot content in database (only metadata)

---

## Phase 8: Test Project Setup

### What to Implement

**Create comprehensive test project** with XUnit v3, TestContainers, and integration test infrastructure.

**Reference**: XUnit v3 documentation + TestContainers patterns

#### Tasks

1. **Create test project**
   ```bash
   cd /Users/arthurminiakhmetov/developer-platform/worker
   dotnet new xunit -n Onlyspans.Worker.Api.Tests -f net10.0
   mkdir -p tests/Onlyspans.Worker.Api.Tests
   mv Onlyspans.Worker.Api.Tests/* tests/Onlyspans.Worker.Api.Tests/
   rmdir Onlyspans.Worker.Api.Tests
   ```

2. **Add test packages**
   ```bash
   cd tests/Onlyspans.Worker.Api.Tests
   dotnet add package xunit
   dotnet add package xunit.runner.visualstudio
   dotnet add package Testcontainers.PostgreSql --version 4.2.0
   dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 10.0.0
   dotnet add package FluentAssertions --version 7.0.0
   dotnet add package NSubstitute
   dotnet add reference ../../src/Onlyspans.Worker.Api/Onlyspans.Worker.Api.csproj
   ```

3. **Create test directory structure**
   ```
   tests/Onlyspans.Worker.Api.Tests/
   ├── Services/
   │   ├── WorkerServiceTests.cs            # Main service tests
   │   ├── S3SnapshotDownloaderTests.cs     # S3 download tests
   │   └── WolverineLogPublisherTests.cs    # Wolverine publisher tests
   ├── Clients/
   │   └── TargetsControllerClientTests.cs  # gRPC client tests
   ├── Integration/
   │   ├── DeploymentFlowTests.cs           # End-to-end flow tests
   │   └── DatabaseTests.cs                 # EF Core + migrations tests
   └── Helpers/
       ├── TestContainerFixture.cs          # PostgreSQL container setup
       └── WorkerWebApplicationFactory.cs   # API test factory
   ```

4. **Create `TestContainerFixture.cs`** (copy from Variables pattern)
   ```csharp
   public class TestContainerFixture : IAsyncLifetime
   {
       private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
           .WithImage("postgres:17-alpine")
           .Build();

       public string ConnectionString => _postgres.GetConnectionString();

       public async Task InitializeAsync() => await _postgres.StartAsync();
       public async Task DisposeAsync() => await _postgres.DisposeAsync();
   }
   ```

5. **Create `WorkerWebApplicationFactory.cs`**
   ```csharp
   public class WorkerWebApplicationFactory : WebApplicationFactory<Program>
   {
       protected override void ConfigureWebHost(IWebHostBuilder builder)
       {
           builder.ConfigureServices(services =>
           {
               // Replace real DbContext with TestContainers connection
               // Mock ITargetsControllerClient, ILogPublisher with NSubstitute
           });
       }
   }
   ```

### Documentation References

- **XUnit v3**: https://xunit.net/docs/getting-started/v3/cmdline
- **NSubstitute**: https://nsubstitute.github.io/
- **TestContainers pattern**: https://dotnet.testcontainers.org/
- **Integration test pattern**: Events CLAUDE.md "Integration Tests" section

### Verification Checklist

```bash
# Test project builds
dotnet build tests/Onlyspans.Worker.Api.Tests/

# Tests discovered
dotnet test --list-tests

# TestContainers starts PostgreSQL
dotnet test --filter "DatabaseTests"
# Check logs for "postgres:17-alpine"

# Sample test passes
dotnet test --filter "WorkerServiceTests.Constructor_ShouldInjectDependencies"
```

### Anti-Pattern Guards

- ❌ Do NOT use Moq (use NSubstitute for mocking)
- ❌ Do NOT use in-memory database (use TestContainers.PostgreSql)
- ❌ Do NOT test against real S3/messaging (mock in unit tests, LocalStack/Testcontainers in integration)
- ❌ Do NOT use LINQ query syntax in tests (use method syntax)

---

## Phase 9: Critical Feature Tests

### What to Implement

**Write tests for critical deployment execution paths** and edge cases.

**Reference**: Variables IMPLEMENTATION_PLAN.md test examples

#### Tasks

1. **Unit Tests** (with NSubstitute mocks)

   **WorkerServiceTests.cs**:
   - ✅ `ExecuteDeployment_SuccessfulFlow_ReturnsLogsAndSavesResult`
   - ✅ `ExecuteDeployment_S3DownloadFails_ReturnsErrorLog`
   - ✅ `ExecuteDeployment_TargetExecutionFails_SavesFailureResult`
   - ✅ `ExecuteDeployment_CancellationRequested_StopsGracefully`

   **S3SnapshotDownloaderTests.cs**:
   - ✅ `DownloadAsync_ValidKey_ReturnsSnapshotPath`
   - ✅ `DownloadAsync_NoSuchKey_ThrowsSnapshotNotFoundException`
   - ✅ `DownloadAsync_LargeSnapshot_LogsProgress`

   **WolverineLogPublisherTests.cs**:
   - ✅ `PublishAsync_MessagingEnabled_PublishesToMessageBus`
   - ✅ `PublishAsync_MessagingDisabled_NoOpPublisher`
   - ✅ `PublishAsync_MessageFormat_CorrectlyMapsLogChunk`

2. **Integration Tests** (with TestContainers)

   **DeploymentFlowTests.cs**:
   - ✅ `EndToEnd_DeploymentPackage_LogsSavedToDatabase`
   - ✅ `EndToEnd_ParallelDeployments_NoConflicts`
   - ✅ `EndToEnd_LongRunningDeployment_StreamsLogsInRealTime`

   **DatabaseTests.cs**:
   - ✅ `Migrations_ApplySuccessfully`
   - ✅ `DeploymentLog_SaveAndQuery_WorksCorrectly`
   - ✅ `DeploymentResult_UniqueConstraint_EnforcedOnDeploymentId`

3. **Create test data builders** at `tests/Helpers/`
   - `DeploymentPackageBuilder.cs` - Fluent builder for test data
   - `LogChunkBuilder.cs` - Generate test log chunks

### Documentation References

- **Test naming pattern**: Standard XUnit v3 conventions
- **FluentAssertions usage**: https://fluentassertions.com/
- **NSubstitute patterns**: https://nsubstitute.github.io/help/getting-started/

### Verification Checklist

```bash
# All tests pass
dotnet test

# Code coverage > 80% (aspirational)
dotnet test --collect:"XPlat Code Coverage"

# No tests marked as [Fact(Skip = "...")]
! grep -r "Skip =" tests/

# TestContainers cleanup (no dangling containers)
docker ps -a | grep -q postgres:17-alpine
# Should be empty after test run
```

### Anti-Pattern Guards

- ❌ Do NOT skip integration tests in CI (they must pass)
- ❌ Do NOT use `Thread.Sleep` for timing (use `Task.Delay` with cancellation)
- ❌ Do NOT test internal implementation details (test public APIs only)

---

## Phase 10: Docker & Compose Configuration

### What to Implement

**Update Docker and Compose files** for multi-service local development environment.

**Reference**: Current `Dockerfile` + Architecture doc deployment patterns

#### Tasks

1. **Update `compose.yaml`**
   ```yaml
   services:
     worker:
       build:
         context: .
         dockerfile: src/Onlyspans.Worker.Api/Dockerfile
       environment:
         ConnectionStrings__Database: "Host=postgres;Database=worker;Username=worker;Password=dev"
         TargetsController__Endpoint: "http://targets-controller:5001"
         Processes__Endpoint: "http://processes:5002"
         Kafka__Enabled: "true"
         Kafka__BootstrapServers: "kafka:9092"
         Kafka__Topic: "worker-logs"
         S3__BucketName: "onlyspans-snapshots"
         S3__Endpoint: "http://localstack:4566"  # For local dev
       ports:
         - "5003:8080"
       depends_on:
         postgres:
           condition: service_healthy
         kafka:
           condition: service_started

     postgres:
       image: postgres:17-alpine
       environment:
         POSTGRES_DB: worker
         POSTGRES_USER: worker
         POSTGRES_PASSWORD: dev
       ports:
         - "5432:5432"
       healthcheck:
         test: ["CMD-SHELL", "pg_isready -U worker"]
         interval: 5s
         timeout: 5s
         retries: 5

     kafka:
       image: confluentinc/cp-kafka:7.6.0
       environment:
         KAFKA_BROKER_ID: 1
         KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
         KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092
         KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
       depends_on:
         - zookeeper
       ports:
         - "9092:9092"

     zookeeper:
       image: confluentinc/cp-zookeeper:7.6.0
       environment:
         ZOOKEEPER_CLIENT_PORT: 2181

     localstack:
       image: localstack/localstack:3.0
       environment:
         SERVICES: s3
         DEFAULT_REGION: us-east-1
       ports:
         - "4566:4566"
   ```

2. **Verify Dockerfile** (should already be correct from scaffold)
   - Multi-stage build (base → build → publish → final)
   - Non-root user
   - Health check endpoint

3. **Create `.env.example`**
   ```env
   # Database
   POSTGRES_DB=worker
   POSTGRES_USER=worker
   POSTGRES_PASSWORD=dev

   # Kafka (set to false for minimal deployment)
   KAFKA_ENABLED=true

   # S3 (use LocalStack for dev)
   S3_ENDPOINT=http://localhost:4566
   AWS_ACCESS_KEY_ID=test
   AWS_SECRET_ACCESS_KEY=test
   ```

### Documentation References

- **Compose pattern**: Architecture doc "Docker Compose (Development)" section
- **Feature flag pattern**: Events CLAUDE.md "Two Deployment Modes"
- **Current Dockerfile**: `src/Onlyspans.Worker.Api/Dockerfile`

### Verification Checklist

```bash
# Compose starts all services
docker compose up -d

# Worker service healthy
curl http://localhost:5003/healthz
# Should return 200 OK

# PostgreSQL accessible
docker compose exec postgres psql -U worker -c "\dt"
# Should show migrated tables

# Kafka topic created
docker compose exec kafka kafka-topics --list --bootstrap-server localhost:9092
# Should show "worker-logs"

# LocalStack S3 accessible
aws --endpoint-url=http://localhost:4566 s3 ls
# Should connect successfully

# Cleanup
docker compose down -v
```

### Anti-Pattern Guards

- ❌ Do NOT expose PostgreSQL password in repository (use .env)
- ❌ Do NOT require all services for development (support minimal mode)
- ❌ Do NOT use latest tags for dependencies (pin versions)

---

## Phase 11: Health Checks & Observability

### What to Implement

**Add health check endpoints and Prometheus metrics** for production readiness.

**Reference**: Architecture doc "Observability" section

#### Tasks

1. **Add health check packages**
   ```bash
   dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
   dotnet add package AspNetCore.HealthChecks.NpgSql
   dotnet add package AspNetCore.HealthChecks.Kafka
   ```

2. **Implement health checks in `Startup.Healthz.cs`**
   ```csharp
   services.AddHealthChecks()
       .AddNpgSql(dbConnectionString, name: "postgres")
       .AddDbContextCheck<WorkerDbContext>(name: "ef-migrations")
       .AddCheck<TargetsControllerHealthCheck>("targets-controller")
       .AddCheck<KafkaHealthCheck>("kafka", failureStatus: HealthStatus.Degraded);
   ```

3. **Create `TargetsControllerHealthCheck.cs`**
   - Ping Targets Controller gRPC endpoint
   - Mark unhealthy if unreachable

4. **Create `KafkaHealthCheck.cs`**
   - Check Kafka producer connectivity
   - Mark degraded (not unhealthy) if Kafka disabled

5. **Add Prometheus metrics** (optional, use prometheus-net)
   ```bash
   dotnet add package prometheus-net.AspNetCore
   ```

   **Metrics to export**:
   - `worker_deployments_total{status="success|failure"}`
   - `worker_deployment_duration_seconds{project="X"}`
   - `worker_active_deployments`
   - `worker_snapshot_download_bytes`

6. **Map endpoints in `Startup.Healthz.cs`**
   ```csharp
   app.MapHealthChecks("/healthz");
   app.MapHealthChecks("/healthz/ready");
   app.MapMetrics();  // /metrics for Prometheus
   ```

### Documentation References

- **Health checks**: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks
- **Prometheus pattern**: Architecture doc mentions `/metrics` endpoint

### Verification Checklist

```bash
# Health endpoint responds
curl http://localhost:5003/healthz
# Should return "Healthy"

# Readiness endpoint responds
curl http://localhost:5003/healthz/ready
# Should return 200 only when dependencies ready

# Metrics endpoint responds
curl http://localhost:5003/metrics
# Should return Prometheus format

# Test unhealthy state (stop PostgreSQL)
docker compose stop postgres
sleep 5
curl http://localhost:5003/healthz
# Should return "Unhealthy"
```

### Anti-Pattern Guards

- ❌ Do NOT mark service unhealthy if Kafka disabled (degraded is OK)
- ❌ Do NOT expose sensitive data in health check responses
- ❌ Do NOT make health checks expensive (cache results if needed)

---

## Final Phase: Integration Verification

### What to Verify

**End-to-end verification** that Worker service meets architecture requirements.

#### Verification Checklist

**Architecture Compliance**:
- ✅ Worker does NOT call Variables service directly (security boundary)
- ✅ Worker is stateless (no in-memory deployment state between requests)
- ✅ Worker supports horizontal scaling (no singleton services)
- ✅ Worker streams logs to both Kafka and gRPC response
- ✅ Worker saves results to worker-logs database

**gRPC Contracts**:
```bash
# Verify proto compilation
grep -r "WorkerService" src/Onlyspans.Worker.Api/obj/
# Should show generated C# files

# Verify client registration
grep -r "TargetsService.TargetsServiceClient" src/
# Should show gRPC client factory registration
```

**Database Schema**:
```bash
# Connect to PostgreSQL
docker compose exec postgres psql -U worker

# Verify tables exist
\dt
# Should show: deployment_logs, deployment_results

# Verify migrations applied
SELECT version FROM __EFMigrationsHistory;
# Should show migration versions
```

**Feature Flags**:
```bash
# Minimal deployment (no Kafka)
KAFKA_ENABLED=false docker compose up worker postgres

# Full deployment (with Kafka)
KAFKA_ENABLED=true docker compose up
```

**Integration with Other Services** (Manual - requires Processes + Targets Controller running):
1. Start Processes service
2. Start Targets Controller service
3. Start Worker service
4. Send `DeploymentPackage` via Processes → Worker
5. Verify logs appear in Kafka topic
6. Verify results saved to worker-logs DB
7. Verify gRPC stream returns log chunks to Processes

**Test Coverage**:
```bash
# Run all tests
dotnet test --collect:"XPlat Code Coverage"

# Check coverage report
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator \
  -reports:tests/*/TestResults/*/coverage.cobertura.xml \
  -targetdir:coverage-report \
  -reporttypes:Html

# Verify critical paths covered:
# - WorkerService.ExecuteDeployment
# - S3SnapshotDownloader.DownloadAsync
# - TargetsControllerClient.ExecuteOnTargetAsync
# - KafkaLogPublisher.PublishAsync
```

**Anti-Patterns Check**:
```bash
# No Variables service dependency
! grep -r "IVariablesClient" src/

# No Moq references (use NSubstitute)
! grep -r "Moq" tests/

# No Confluent.Kafka direct usage (use Wolverine)
! grep -r "Confluent.Kafka" src/

# No in-memory database in tests
! grep -r "UseInMemoryDatabase" tests/

# No hardcoded secrets
! grep -r "password.*=.*\".*\"" src/appsettings.json

# No LINQ query syntax (use method syntax)
! grep -r "from .* in .* where .* select" src/
```

**Performance Baseline** (optional):
- Deployment execution < 5 seconds overhead (excluding target execution time)
- Log streaming latency < 100ms
- Snapshot download > 10 MB/s

**Documentation**:
- ✅ README.md updated with setup instructions
- ✅ IMPLEMENTATION_PLAN.md completed (this file)
- ✅ Proto files documented with comments
- ✅ Configuration options documented in appsettings.json comments

---

## Summary

**Total Phases**: 11 implementation phases + 1 verification phase

**Estimated Effort**:
- Phase 1-3 (Contracts, DB, Config): ~2-3 days
- Phase 4-7 (Core Services): ~3-4 days
- Phase 8-9 (Tests): ~2-3 days
- Phase 10-11 (Docker, Observability): ~1-2 days
- **Total**: ~8-12 days for single developer

**Dependencies**:
- External: Processes service proto definition (for upstream contract)
- External: Targets Controller proto definition (for downstream contract)
- Internal: All phases sequential (each builds on previous)

**Risk Areas**:
1. **Language mismatch**: Architecture specifies Go, scaffolding is C#. Decision made to proceed with C# but may need revisiting.
2. **Missing proto definitions**: Processes and Targets Controller contracts not yet defined. Phase 1 may be blocked until these are available.
3. **Integration complexity**: Full end-to-end testing requires all services running (Processes, Worker, Targets Controller, Target Agents).

**Next Steps**:
1. Confirm C# vs Go decision with team
2. Obtain or design Processes service proto
3. Obtain or design Targets Controller proto
4. Begin Phase 1 implementation

---

**Plan Author**: Claude Code (Orchestrator)
**Plan Version**: 1.0
**Last Updated**: 2026-02-15
