using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Onlyspans.Worker.Api.Clients;
using Onlyspans.Worker.Api.Data;
using Onlyspans.Worker.Api.Services;

namespace Onlyspans.Worker.Api.Tests.Helpers;

// Pattern: WebApplicationFactory for integration testing with TestContainers PostgreSQL
// NSubstitute is used for mocking (not Moq per anti-pattern guard)
public sealed class WorkerWebApplicationFactory(string connectionString)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace DbContext with TestContainers connection
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<WorkerDbContext>));
            if (dbContextDescriptor is not null)
                services.Remove(dbContextDescriptor);

            services.AddDbContext<WorkerDbContext>(options =>
                options.UseNpgsql(connectionString));

            // Replace ITargetsControllerClient with NSubstitute mock
            var targetClientDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ITargetsControllerClient));
            if (targetClientDescriptor is not null)
                services.Remove(targetClientDescriptor);

            services.AddScoped<ITargetsControllerClient>(
                _ => Substitute.For<ITargetsControllerClient>());

            // Replace ISnapshotDownloader with NSubstitute mock
            var snapshotDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ISnapshotDownloader));
            if (snapshotDescriptor is not null)
                services.Remove(snapshotDescriptor);

            services.AddScoped<ISnapshotDownloader>(
                _ => Substitute.For<ISnapshotDownloader>());
        });
    }
}
