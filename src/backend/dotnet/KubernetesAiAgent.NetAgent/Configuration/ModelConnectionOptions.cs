using System.ComponentModel.DataAnnotations;

namespace KubernetesAiAgent.NetAgent.Configuration;

/// <summary>
/// Connection settings for the OpenAI-compatible chat model provider (docs/ARCHITECTURE.md §6.2). Currently
/// OpenRouter — an OpenAI-compatible model marketplace — but kept generic (not vendor-named) so the provider stays
/// swappable via configuration alone (design principle 5). <see cref="Endpoint"/> and <see cref="ApiKey"/> are
/// exactly what an OpenAI SDK client needs to talk to it.
/// </summary>
public sealed class ModelConnectionOptions
{
    /// <summary>
    /// The OpenAI-compatible base URL, e.g. OpenRouter's. Configurable (rather than hard-coded) so a compatible
    /// gateway or proxy can be substituted without a code change.
    /// </summary>
    [Required]
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// The model slug to use, e.g. "deepseek/deepseek-v4-flash-0731". OpenRouter's catalog changes fairly often —
    /// verify against https://openrouter.ai/models if this needs to move.
    /// </summary>
    [Required]
    public string Model { get; set; } = "deepseek/deepseek-v4-flash-0731";

    /// <summary>
    /// Secret. Supply via user secrets, an environment variable, or a Kubernetes Secret — never agentconfig.yaml.
    /// </summary>
    [Required]
    public string? ApiKey { get; set; }
}
