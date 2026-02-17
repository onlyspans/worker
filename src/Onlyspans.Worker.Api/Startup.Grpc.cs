namespace Onlyspans.Worker.Api;

public static partial class Startup
{
    public static IServiceCollection AddGrpcServices(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddGrpc();

        // Enable gRPC reflection in development for testing with grpcurl
        if (environment.IsDevelopment())
        {
            services.AddGrpcReflection();
        }

        // TODO: Phase 5 - Register gRPC clients for Targets Controller
        // services.AddGrpcClient<TargetsService.TargetsServiceClient>(options =>
        // {
        //     options.Address = new Uri(targetsOptions.Endpoint);
        // });

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
