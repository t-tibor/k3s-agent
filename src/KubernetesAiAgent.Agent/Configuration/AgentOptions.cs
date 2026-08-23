using System.ComponentModel.DataAnnotations;

namespace KubernetesAiAgent.Agent.Configuration;

/// <summary>
/// Agent-level behavior settings. See docs/ARCHITECTURE.md §10.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>
    /// The model id this agent answers to on the OpenAI-compatible API (docs/ARCHITECTURE.md §8.1).
    /// </summary>
    [Required]
    public string ModelId { get; set; } = "kubernetes-agent";

    public string SystemPrompt { get; set; } = Agent.AgentInstructions.Default;

    /// <summary>
    /// Upper bound on MCP tool calls per request. Unbounded when null. Not yet enforced — see
    /// docs/ARCHITECTURE.md §10.
    /// </summary>
    public int? MaxToolCalls { get; set; }

    public bool RequireApiKey { get; set; }

    /// <summary>
    /// Secret. Supply via user secrets, an environment variable, or a Kubernetes Secret — never appsettings.json.
    /// </summary>
    public string? ApiKey { get; set; }

    public int? RequestTimeoutSeconds { get; set; }

    public int? MaxConversationMessages { get; set; }

    /// <summary>
    /// Browser origins allowed to call this API directly (CORS), e.g. where Hollama is served from. Empty by
    /// default — no cross-origin browser calls are permitted until this is configured. A single entry of
    /// <c>"*"</c> allows any origin (no credentials are sent cross-origin by this API, so this is safe to use
    /// where the exact origin isn't known ahead of time — e.g. Hollama served from a variable host/port).
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
