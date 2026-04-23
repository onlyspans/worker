using Microsoft.EntityFrameworkCore;
using Onlyspans.Worker.Api.Data.Entities;

namespace Onlyspans.Worker.Api.Data;

public class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
{
    public DbSet<DeploymentLog> DeploymentLogs => Set<DeploymentLog>();
    public DbSet<DeploymentResult> DeploymentResults => Set<DeploymentResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkerDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        
        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());
    }
}
