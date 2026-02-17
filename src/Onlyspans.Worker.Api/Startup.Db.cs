using Microsoft.EntityFrameworkCore;
using Onlyspans.Worker.Api.Configuration;
using Onlyspans.Worker.Api.Data;
using Onlyspans.Worker.Api.Hosting;

namespace Onlyspans.Worker.Api;

public static partial class Startup
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database");

        services.AddDbContextPool<WorkerDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddHostedService<MigrationHostedService>();

        return services;
    }
}
