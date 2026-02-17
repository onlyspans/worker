using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Onlyspans.Worker.Api;

public static partial class Startup
{
    public static IServiceCollection AddHealthz(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                connectionString: configuration.GetConnectionString("Database")!,
                name: "database",
                tags: ["ready"]);

        return services;
    }

    public static WebApplication UseHealthz(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready", new()
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        return app;
    }
}
