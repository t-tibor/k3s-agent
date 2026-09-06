using KubernetesAiAgent.Agent.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KubernetesAiAgent.Agent.Health;

/// <summary>
/// Readiness check: model-connection configuration is present. Deliberately does not call the model provider —
/// presence only, not reachability (docs/ARCHITECTURE.md §12). Not tagged "live", so it affects <c>/health</c>
/// (readiness) but not <c>/alive</c> (liveness).
/// </summary>
public sealed class ModelConnectionConfigurationHealthCheck(IOptions<AgentOptions> agentOptions) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connection = agentOptions.Value.ModelConnection;

        var result = string.IsNullOrWhiteSpace(connection.Model) || string.IsNullOrWhiteSpace(connection.ApiKey)
            ? HealthCheckResult.Unhealthy("Agent:ModelConnection:Model and Agent:ModelConnection:ApiKey must both be configured.")
            : HealthCheckResult.Healthy();

        return Task.FromResult(result);
    }
}
