using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Palmap.Collector.Health;

internal sealed class PalworldApiHealthCheck(IPalworldApiHealthService palworldHealthService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return await palworldHealthService.IsHealthy(cancellationToken)
            ? HealthCheckResult.Healthy("Palworld REST API is reachable.")
            : HealthCheckResult.Unhealthy("Palworld REST API is unavailable or rejected authentication.");
    }
}
