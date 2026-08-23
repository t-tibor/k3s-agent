namespace KubernetesAiAgent.Agent.Configuration;

/// <summary>
/// Connection settings for the existing, externally-managed Kubernetes MCP server. See docs/ARCHITECTURE.md §7.
/// Not yet consumed — MCP integration lands in a follow-up pass.
/// </summary>
public sealed class KubernetesMcpOptions
{
    public const string SectionName = "KubernetesMcp";

    public string? Endpoint { get; set; }
}
