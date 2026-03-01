using Onlyspans.Worker.Api.Configuration;
using Onlyspans.Worker.Api.Data;
using Onlyspans.Worker.Api.Hosting;
using Prometheus;

namespace Onlyspans.Worker.Api;

public static partial class Startup
{
    public static IServiceCollection AddHealthz(this IServiceCollection services, IConfiguration configuration)
    {
        var targetsOptions = configuration
            .GetRequiredSection(TargetsControllerOptions.SectionName)
            .Get<TargetsControllerOptions>()!;

        services.AddSingleton(targetsOptions);

        services.AddHealthChecks()
            .AddNpgSql(
                connectionString: configuration.GetConnectionString("Database")!,
                name: "database",
                tags: ["ready"])
            .AddDbContextCheck<WorkerDbContext>(
                name: "ef-migrations",
                tags: ["ready"])
            .AddCheck<TargetsControllerHealthCheck>(
                name: "targets-controller",
                tags: ["ready"]);

        return services;
    }

    public static WebApplication UseHealthz(this WebApplication app)
    {
        // HTTP metrics middleware (prometheus-net.AspNetCore) - must be before routing
        app.UseHttpMetrics();

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready", new()
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        // Prometheus /metrics scrape endpoint
        app.MapMetrics();

        return app;
    }
}
