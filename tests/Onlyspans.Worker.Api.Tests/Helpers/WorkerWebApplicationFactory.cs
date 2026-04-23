using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Onlyspans.Worker.Api.Clients;
using Onlyspans.Worker.Api.Data;
using Onlyspans.Worker.Api.Services;

namespace Onlyspans.Worker.Api.Tests.Helpers;

public sealed class WorkerWebApplicationFactory(string connectionString)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<WorkerDbContext>));
            if (dbContextDescriptor is not null)
                services.Remove(dbContextDescriptor);

            services.AddDbContext<WorkerDbContext>(options =>
                options.UseNpgsql(connectionString));

            
            var targetClientDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ITargetsControllerClient));
            if (targetClientDescriptor is not null)
                services.Remove(targetClientDescriptor);

            services.AddScoped<ITargetsControllerClient>(
                _ => Substitute.For<ITargetsControllerClient>());

            
            var snapshotDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ISnapshotDownloader));
            if (snapshotDescriptor is not null)
                services.Remove(snapshotDescriptor);

            services.AddScoped<ISnapshotDownloader>(
                _ => Substitute.For<ISnapshotDownloader>());
        });
    }
}
