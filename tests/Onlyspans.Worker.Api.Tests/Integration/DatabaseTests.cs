using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Onlyspans.Worker.Api.Data;
using Onlyspans.Worker.Api.Data.Entities;
using Onlyspans.Worker.Api.Tests.Helpers;

namespace Onlyspans.Worker.Api.Tests.Integration;

[Collection("Database")]
public sealed class DatabaseTests(TestContainerFixture fixture) : IClassFixture<TestContainerFixture>
{
    private WorkerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        return new WorkerDbContext(options);
    }

    [Fact]
    public async Task Migrations_ApplySuccessfully()
    {
        
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        
        var deploymentLogCount = await context.DeploymentLogs.CountAsync();
        var deploymentResultCount = await context.DeploymentResults.CountAsync();

        deploymentLogCount.Should().Be(0);
        deploymentResultCount.Should().Be(0);
    }

    [Fact]
    public async Task DeploymentLog_CanSaveAndQuery()
    {
        
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var log = new DeploymentLog
        {
            Id = Guid.NewGuid(),
            DeploymentId = "test-deploy-1",
            Timestamp = DateTime.UtcNow,
            LogLevel = "Info",
            Message = "Test log entry",
            Source = "worker"
        };

        
        context.DeploymentLogs.Add(log);
        await context.SaveChangesAsync();

        
        var saved = await context.DeploymentLogs
            .Where(l => l.DeploymentId == "test-deploy-1")
            .FirstOrDefaultAsync();

        saved.Should().NotBeNull();
        saved!.Message.Should().Be("Test log entry");
        saved.Source.Should().Be("worker");
    }

    [Fact]
    public async Task DeploymentResult_CanSaveAndQuery()
    {
        
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var result = new DeploymentResult
        {
            DeploymentId = "test-deploy-2",
            Status = "succeeded",
            StartedAt = DateTime.UtcNow.AddSeconds(-5),
            CompletedAt = DateTime.UtcNow
        };

        
        context.DeploymentResults.Add(result);
        await context.SaveChangesAsync();

        
        var saved = await context.DeploymentResults
            .Where(r => r.DeploymentId == "test-deploy-2")
            .FirstOrDefaultAsync();

        saved.Should().NotBeNull();
        saved!.Status.Should().Be("succeeded");
        saved.CompletedAt.Should().NotBeNull();
    }
}
