using KubernetesAiAgent.Agent.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KubernetesAiAgent.Agent.Health;

/// <summary>
/// Readiness check: OpenRouter configuration is present. Deliberately does not call OpenRouter — presence only,
/// not reachability (docs/ARCHITECTURE.md §12). Not tagged "live", so it affects <c>/health</c> (readiness) but
/// not <c>/alive</c> (liveness).
/// </summary>
public sealed class OpenRouterConfigurationHealthCheck(IOptions<OpenRouterOptions> openRouterOptions) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var options = openRouterOptions.Value;

        var result = string.IsNullOrWhiteSpace(options.Model) || string.IsNullOrWhiteSpace(options.ApiKey)
            ? HealthCheckResult.Unhealthy("OpenRouter:Model and OpenRouter:ApiKey must both be configured.")
            : HealthCheckResult.Healthy();

        return Task.FromResult(result);
    }
}
