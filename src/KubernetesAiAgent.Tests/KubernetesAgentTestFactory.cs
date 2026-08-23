using Microsoft.AspNetCore.Mvc.Testing;

namespace KubernetesAiAgent.Tests;

/// <summary>
/// Shared test host. Sets dummy OpenRouter credentials via environment variables — read by
/// <c>WebApplicationBuilder.Configuration</c> at the very first step of <c>Program.cs</c>, so they're in place
/// before <c>KubernetesAgentFactory.CreateKubernetesAgentAsync</c> reads configuration, unlike a
/// <c>ConfigureAppConfiguration</c> override (which <see cref="WebApplicationFactory{TEntryPoint}"/> applies too
/// late to affect a <c>ValidateOnStart</c> options bind) — so <c>OpenRouterOptions</c>' required-on-start
/// validation passes without a real key. Also points <c>KubernetesMcp:Endpoint</c> at an address that fails fast,
/// so tests exercise the graceful-degradation path in <c>McpClientConnector</c> rather than depending on a
/// real, reachable MCP server (docs/ARCHITECTURE.md §12, §16).
/// </summary>
public sealed class KubernetesAgentTestFactory : WebApplicationFactory<Program>
{
    public KubernetesAgentTestFactory()
    {
        Environment.SetEnvironmentVariable("OpenRouter__Model", "test-model");
        Environment.SetEnvironmentVariable("OpenRouter__ApiKey", "test-key");
        Environment.SetEnvironmentVariable("KubernetesMcp__Endpoint", "http://127.0.0.1:59999");
    }
}
