using KubernetesAiAgent.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace KubernetesAiAgent.Agent.Api;

/// <summary>
/// <c>GET /v1/models</c> — model discovery (docs/ARCHITECTURE.md §5.1).
/// </summary>
public static class ModelsEndpoint
{
    public static IEndpointRouteBuilder MapModelsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/models", (IOptions<AgentOptions> agentOptions) =>
        {
            var response = new ModelsResponse(
                Object: "list",
                Data: [new ModelInfo(agentOptions.Value.ModelId, "model", 0, "local")]);

            return Results.Ok(response);
        });

        return app;
    }
}
