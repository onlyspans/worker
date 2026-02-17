using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Onlyspans.Worker.Api.Configuration;
using Onlyspans.Worker.Api.Data;
using Onlyspans.Worker.Api.Hosting;
using Prometheus;

namespace Onlyspans.Worker.Api;

public static partial class Startup
{
    // Phase 11: Health checks + Prometheus metrics
    // AddKafka API (AspNetCore.HealthChecks.Kafka 9.0.0): AddKafka(Action<ProducerConfig>, string topic, ...)
    // Prometheus API (prometheus-net.AspNetCore 8.2.1): app.UseHttpMetrics() + endpoints.MapMetrics()
    public static IServiceCollection AddHealthz(this IServiceCollection services, IConfiguration configuration)
    {
        var targetsOptions = configuration
            .GetRequiredSection(TargetsControllerOptions.SectionName)
            .Get<TargetsControllerOptions>()!;

        var messagingOptions = configuration
            .GetRequiredSection(MessagingOptions.SectionName)
            .Get<MessagingOptions>();

        services.AddSingleton(targetsOptions);

        var builder = services.AddHealthChecks()
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

        // Kafka health check only when messaging is enabled (degraded, not unhealthy, per anti-pattern guard)
        if (messagingOptions is { Enabled: true })
        {
            builder.AddKafka(
                setup: config => config.BootstrapServers = messagingOptions.Kafka.BootstrapServers,
                topic: messagingOptions.Kafka.Topic,
                name: "kafka",
                failureStatus: HealthStatus.Degraded,
                tags: ["messaging"]);
        }

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
