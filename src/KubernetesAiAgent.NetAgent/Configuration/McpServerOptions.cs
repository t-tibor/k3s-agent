using System.ComponentModel.DataAnnotations;

namespace KubernetesAiAgent.Agent.Configuration;

/// <summary>
/// Connection settings for one MCP server the agent should discover read-only tools from (docs/ARCHITECTURE.md
/// §7). Multiple can be configured (e.g. a Kubernetes one and a Prometheus one); each is connected to
/// independently, and one being unreachable does not take the others down — see
/// <see cref="Agent.KubernetesAgentFactory"/>.
/// </summary>
public sealed class McpServerOptions
{
    /// <summary>
    /// Identifies this server in logs and, if it can't be reached, in the note appended to the agent's
    /// instructions so it can explain the gap to the user.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Streamable HTTP endpoint. Blank/missing means this entry is not actually configured — treated the same as
    /// an unreachable server (graceful degradation, not a startup failure).
    /// </summary>
    public string? Endpoint { get; set; }
}
