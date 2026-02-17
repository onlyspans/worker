using Onlyspans.Worker.Api.Configuration;
using Onlyspans.Worker.Api.Services;

namespace Onlyspans.Worker.Api;

public static class MessagingStartup
{
    /// <summary>
    /// Registers messaging services.
    /// Currently registers NoOpLogPublisher as a placeholder.
    /// Phase 6 will replace this with Wolverine + Kafka (WolverineFx, Wolverine.Kafka).
    /// </summary>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetRequiredSection(MessagingOptions.SectionName)
            .Get<MessagingOptions>();

        if (options is { Enabled: true })
        {
            // TODO Phase 6: Replace with Wolverine/Kafka publisher
            // builder.UseWolverine(opts => {
            //     opts.UseKafka(options.Kafka.BootstrapServers)
            //         .ConfigureProducers(p => p.DefaultTopic = options.Kafka.Topic);
            // });
        }

        // Register NoOpLogPublisher for both enabled and disabled states until Phase 6
        services.AddSingleton<ILogPublisher, NoOpLogPublisher>();

        return services;
    }
}
