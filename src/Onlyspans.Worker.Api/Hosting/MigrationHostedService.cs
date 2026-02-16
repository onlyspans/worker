using Microsoft.EntityFrameworkCore;
using Onlyspans.Worker.Api.Data;

namespace Onlyspans.Worker.Api.Hosting;

public sealed class MigrationHostedService(
    IServiceProvider serviceProvider,
    ILogger<MigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();

            logger.LogInformation("Starting database migration...");
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database migration completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during database migration: {Message}", ex.Message);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
