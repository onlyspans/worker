using Serilog;

namespace Onlyspans.Worker.Api;

public static partial class Startup
{
    public static IHostBuilder AddSerilog(this IHostBuilder hostBuilder)
    {
        hostBuilder.UseSerilog((context, configuration) =>
        {
            configuration.ReadFrom.Configuration(context.Configuration);
        });

        return hostBuilder;
    }
}
