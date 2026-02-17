using Onlyspans.Worker.Api.Configuration;
using Onlyspans.Worker.Api.Messages;
using Onlyspans.Worker.Api.Services;
using Wolverine;
using Wolverine.Kafka;

namespace Onlyspans.Worker.Api;

public static class MessagingStartup
{
    // Phase 6: Wolverine + Kafka integration with feature flag
    // Wolverine UseKafka API: opts.UseKafka(bootstrapServers) — WolverineFx.Kafka 5.16.0
    // Publish routing: opts.PublishMessage<T>().ToKafkaTopic(topicName)
    public static WebApplicationBuilder AddMessaging(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration
            .GetRequiredSection(MessagingOptions.SectionName)
            .Get<MessagingOptions>();

        if (options is { Enabled: true })
        {
            builder.Host.UseWolverine(opts =>
            {
                opts.UseKafka(options.Kafka.BootstrapServers)
                    .ConfigureProducers(p => p.ClientId = "worker-service");

                opts.PublishMessage<DeploymentLogMessage>()
                    .ToKafkaTopic(options.Kafka.Topic);
            });

            builder.Services.AddScoped<ILogPublisher, WolverineLogPublisher>();
        }
        else
        {
            builder.Services.AddSingleton<ILogPublisher, NoOpLogPublisher>();
        }

        return builder;
    }
}
