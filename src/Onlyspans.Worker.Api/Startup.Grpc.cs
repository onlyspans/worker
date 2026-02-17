using Onlyspans.Worker.Api.Clients;
using Onlyspans.Worker.Api.Configuration;
using Targets.Communication;

namespace Onlyspans.Worker.Api;

public static partial class Startup
{
    public static IServiceCollection AddGrpcServices(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddGrpc();

        // Enable gRPC reflection in development for testing with grpcurl
        if (environment.IsDevelopment())
        {
            services.AddGrpcReflection();
        }

        // Phase 5: Register gRPC client for Targets Controller
        // Pattern: https://learn.microsoft.com/en-us/aspnet/core/grpc/clientfactory
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
        // TODO: Phase 7 - Map WorkerService
        // app.MapGrpcService<Services.WorkerService>();

        // Enable gRPC reflection in development for testing with grpcurl
        if (app.Environment.IsDevelopment())
        {
            app.MapGrpcReflectionService();
        }

        return app;
    }
}
