using System.ComponentModel.DataAnnotations;

namespace KubernetesAiAgent.Agent.Configuration;

/// <summary>
/// OpenRouter connection settings (docs/ARCHITECTURE.md §6.2). OpenRouter is an OpenAI-compatible model
/// marketplace; <see cref="Endpoint"/> and <see cref="ApiKey"/> are exactly what an OpenAI SDK client needs to
/// talk to it.
/// </summary>
public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    /// <summary>
    /// OpenRouter's OpenAI-compatible base URL. Configurable (rather than hard-coded) so a compatible gateway or
    /// proxy can be substituted without a code change.
    /// </summary>
    [Required]
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// The OpenRouter model slug to use, e.g. "deepseek/deepseek-v4-flash-0731". OpenRouter's catalog changes
    /// fairly often — verify against https://openrouter.ai/models if this needs to move.
    /// </summary>
    [Required]
    public string Model { get; set; } = "deepseek/deepseek-v4-flash-0731";

    /// <summary>
    /// Secret. Supply via user secrets, an environment variable, or a Kubernetes Secret — never appsettings.json.
    /// </summary>
    [Required]
    public string? ApiKey { get; set; }
}
