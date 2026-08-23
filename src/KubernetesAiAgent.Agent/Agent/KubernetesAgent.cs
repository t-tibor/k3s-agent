using System.ClientModel;
using KubernetesAiAgent.Agent.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace KubernetesAiAgent.Agent.Agent;

/// <summary>
/// Wires the real Kubernetes agent: an OpenRouter-backed <see cref="IChatClient"/> plus the read-only Kubernetes
/// MCP tools discovered by <see cref="McpClientConnector"/>, and returns the resulting <see cref="AIAgent"/> for
/// <c>MapOpenAIChatCompletions</c> to expose (docs/ARCHITECTURE.md §6, §7). Registered as a DI singleton.
/// </summary>
public sealed class KubernetesAgentFactory(
    IOptions<OpenRouterOptions> openRouterOptions,
    IOptions<AgentOptions> agentOptions,
    McpClientConnector mcpClientConnector)
{
    /// <summary>
    /// Builds the chat client, discovers MCP tools, and returns the agent. Resolve this from
    /// <see cref="WebApplication.Services"/> after <c>Build()</c> — <c>MapOpenAIChatCompletions</c> takes the
    /// returned <see cref="AIAgent"/> handle directly, so nothing here needs to run before <c>Build()</c>.
    /// </summary>
    public async Task<AIAgent> CreateKubernetesAgentAsync(CancellationToken cancellationToken = default)
    {
        var openRouter = openRouterOptions.Value;
        var agentOpts = agentOptions.Value;

        IChatClient chatClient = new ChatClient(
                model: openRouter.Model,
                credential: new ApiKeyCredential(openRouter.ApiKey ?? string.Empty),
                options: new OpenAIClientOptions { Endpoint = new Uri(openRouter.Endpoint) })
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(
                sourceName: "K3s-agent",
                configure: (cfg) => cfg.EnableSensitiveData = true
                )
            .Build();

        var tools = await mcpClientConnector.GetToolsAsync(cancellationToken);

        var agent = chatClient
            .AsAIAgent(
                name: agentOpts.ModelId,
                instructions: agentOpts.SystemPrompt,
                tools: [.. tools]
            )
            .AsBuilder()
            .UseOpenTelemetry()
            .Build();

        return agent;
    }

    /// <summary>
    /// Options for <c>MapOpenAIChatCompletions</c> that honor request-supplied generation settings (temperature,
    /// top_p, max_completion_tokens, frequency_penalty, presence_penalty, seed, stop) instead of the framework's
    /// default of rejecting any request that carries them with a 400. Frontends like NextChat send these on every
    /// request even at their default values, so rejecting them makes the endpoint unusable from such clients.
    /// Request-supplied <c>tools</c>/<c>tool_choice</c>/<c>response_format</c> are still rejected (falling back to
    /// the framework's default behavior for just those fields) — this agent's Kubernetes MCP tools are fixed by
    /// server-side configuration (docs/ARCHITECTURE.md §7), not something a caller should be able to override.
    /// </summary>
    public static readonly OpenAIChatCompletionsMapOptions ChatCompletionsMapOptions = new()
    {
        RunOptionsFactory = request =>
        {
            if (request.Tools is { Count: > 0 }
                || request.ToolChoice is not null
                || request.ResponseFormat is not null)
            {
                return OpenAIChatCompletionsMapOptions.RejectRequestSettings(request);
            }

            return new ChatClientAgentRunOptions(new ChatOptions
            {
                Temperature = request.Temperature,
                TopP = request.TopP,
                MaxOutputTokens = request.MaxOutputTokens,
                FrequencyPenalty = request.FrequencyPenalty,
                PresencePenalty = request.PresencePenalty,
                Seed = request.Seed,
                StopSequences = request.StopSequences?.ToList(),
            });
        },
    };
}
