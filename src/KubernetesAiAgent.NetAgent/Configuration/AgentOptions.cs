using System.ComponentModel.DataAnnotations;

namespace KubernetesAiAgent.NetAgent.Configuration;

/// <summary>
/// The agent's single config root — model-connection settings, agent-level behavior, and the MCP servers to
/// discover tools from — bound from <c>agentconfig.yaml</c> (docs/ARCHITECTURE.md §10). Kept as one object (rather
/// than the three separate sections this replaced) so the whole thing can later be reloaded from a mounted
/// Kubernetes ConfigMap as a unit.
/// </summary>
public sealed class AgentOptions : IValidatableObject
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
    /// Secret. Supply via user secrets, an environment variable, or a Kubernetes Secret — never agentconfig.yaml.
    /// </summary>
    public string? ApiKey { get; set; }

    public int? RequestTimeoutSeconds { get; set; }

    public int? MaxConversationMessages { get; set; }

    /// <summary>
    /// How to reach the chat model provider (docs/ARCHITECTURE.md §6.2).
    /// </summary>
    public ModelConnectionOptions ModelConnection { get; set; } = new();

    /// <summary>
    /// The MCP servers to discover read-only tools from (docs/ARCHITECTURE.md §7). May be empty — the agent then
    /// simply has no tools.
    /// </summary>
    public List<McpServerOptions> McpServers { get; set; } = [];

    /// <summary>
    /// Cascades validation into <see cref="ModelConnection"/> and <see cref="McpServers"/> — plain
    /// <c>Validator.TryValidateObject</c> (what <c>ValidateDataAnnotations()</c> uses) does not recurse into
    /// nested complex properties on its own. These are structural config mistakes and fail startup
    /// (<c>ValidateOnStart</c>); an MCP server being unreachable at runtime is handled separately and does not
    /// fail startup (docs/ARCHITECTURE.md §12, §16).
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var connectionResults = new List<ValidationResult>();
        Validator.TryValidateObject(
            ModelConnection, new ValidationContext(ModelConnection), connectionResults, validateAllProperties: true);
        foreach (var result in connectionResults)
        {
            yield return new ValidationResult(
                result.ErrorMessage,
                result.MemberNames.Select(name => $"{nameof(ModelConnection)}.{name}"));
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < McpServers.Count; i++)
        {
            var server = McpServers[i];
            var member = $"{nameof(McpServers)}[{i}]";

            if (string.IsNullOrWhiteSpace(server.Name))
            {
                yield return new ValidationResult($"{member}.Name is required.", [$"{member}.Name"]);
                continue;
            }

            if (!seenNames.Add(server.Name))
            {
                yield return new ValidationResult(
                    $"McpServers contains more than one entry named \"{server.Name}\"; names must be unique.",
                    [$"{member}.Name"]);
            }
        }
    }
}
