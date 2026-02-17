using Microsoft.Extensions.Diagnostics.HealthChecks;
using Onlyspans.Worker.Api.Configuration;

namespace Onlyspans.Worker.Api.Hosting;

// Custom health check that verifies TCP reachability of the Targets Controller gRPC endpoint
// Pattern: IHealthCheck from https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks
public sealed class TargetsControllerHealthCheck(TargetsControllerOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var uri = new Uri(options.Endpoint);
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 80;

            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port, cancellationToken);

            return HealthCheckResult.Healthy($"Targets Controller reachable at {options.Endpoint}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Targets Controller unreachable at {options.Endpoint}",
                exception: ex);
        }
    }
}
