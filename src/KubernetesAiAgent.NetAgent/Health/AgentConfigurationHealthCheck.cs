using KubernetesAiAgent.Agent.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KubernetesAiAgent.Agent.Health;

/// <summary>
/// Readiness check: the agent has the minimum configuration required to serve requests. Deliberately does not
/// depend on the model provider or any MCP server being reachable (docs/ARCHITECTURE.md §12). Not tagged
/// "live", so it affects <c>/health</c> (readiness) but not <c>/alive</c> (liveness).
/// </summary>
public sealed class AgentConfigurationHealthCheck(IOptions<AgentOptions> agentOptions) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = string.IsNullOrWhiteSpace(agentOptions.Value.ModelId)
            ? HealthCheckResult.Unhealthy("Agent:ModelId is not configured.")
            : HealthCheckResult.Healthy();

        return Task.FromResult(result);
    }
}
