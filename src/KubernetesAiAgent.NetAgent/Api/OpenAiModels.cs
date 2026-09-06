using System.Text.Json.Serialization;

namespace KubernetesAiAgent.Agent.Api;

// Wire-format DTOs for the parts of the OpenAI API this backend hand-rolls (docs/ARCHITECTURE.md §5.1). Chat
// completions are handled by Microsoft.Agents.AI.Hosting.OpenAI (docs/ARCHITECTURE.md §6.3), which owns its own
// wire-format types — only model discovery is hand-rolled here.

public sealed record ModelInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("owned_by")] string OwnedBy);

public sealed record ModelsResponse(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("data")] List<ModelInfo> Data);
