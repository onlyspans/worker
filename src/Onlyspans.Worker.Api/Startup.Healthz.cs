using Onlyspans.Worker.Api.Configuration;
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
            .AddCheck<TargetsControllerHealthCheck>(
                name: "targets-controller",
                tags: ["ready"]);

        return services;
    }

    public static WebApplication UseHealthz(this WebApplication app)
    {
        
        app.UseHttpMetrics();

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready", new()
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        
        app.MapMetrics();

        return app;
    }
}
