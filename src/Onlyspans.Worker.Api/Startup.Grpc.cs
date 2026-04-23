using Onlyspans.Worker.Api.Clients;
using Onlyspans.Worker.Api.Configuration;
using Targets.Communication;

namespace Onlyspans.Worker.Api;

public static partial class Startup
{
    public static IServiceCollection AddGrpcServices(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddGrpc();

        
        if (environment.IsDevelopment())
        {
            services.AddGrpcReflection();
        }

        
        
        var targetsOptions = configuration
            .GetRequiredSection(TargetsControllerOptions.SectionName)
            .Get<TargetsControllerOptions>()!;

        services.AddGrpcClient<TargetsService.TargetsServiceClient>(options =>
        {
            options.Address = new Uri(targetsOptions.Endpoint);
        });

        services.AddScoped<ITargetsControllerClient, TargetsControllerClient>();

        return services;
    }

    public static WebApplication UseGrpcServices(this WebApplication app)
    {
        
        app.MapGrpcService<Services.WorkerService>();

        
        if (app.Environment.IsDevelopment())
        {
            app.MapGrpcReflectionService();
        }

        return app;
    }
}
