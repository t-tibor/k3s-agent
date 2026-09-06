using System.ClientModel;
using KubernetesAiAgent.NetAgent.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;

namespace KubernetesAiAgent.NetAgent.Agent;

/// <summary>
/// Composes the real Kubernetes agent from <see cref="AgentOptions"/>: an OpenAI-compatible <see cref="IChatClient"/>
/// (docs/ARCHITECTURE.md §6) plus the read-only tools discovered from every configured MCP server
/// (docs/ARCHITECTURE.md §7), and returns the resulting <see cref="AIAgent"/> for <c>MapOpenAIChatCompletions</c> to
/// expose. Registered as a DI singleton; resolve and call <see cref="CreateKubernetesAgentAsync"/> exactly once,
/// after <c>Build()</c> — <c>MapOpenAIChatCompletions</c> takes the returned <see cref="AIAgent"/> handle directly,
/// so nothing here needs to run before <c>Build()</c>.
/// </summary>
/// <remarks>
/// Owns the (fallible, async) connections to every configured MCP server directly — connecting is attempted at
/// most once, since this factory is only ever invoked once at startup, so there's no need for the separate
/// memoized-singleton-connector indirection a hot path would require. Each server is connected to over Streamable
/// HTTP with a short timeout and degrades gracefully (logs a warning, contributes no tools) rather than throwing
/// if it's unreachable — readiness and error handling must not hard-depend on this external dependency
/// (docs/ARCHITECTURE.md §12, §16). Any resulting <see cref="McpClient"/> is kept alive (the returned tools need it
/// alive to actually invoke calls later) and disposed alongside this factory (a DI singleton) on shutdown.
/// </remarks>
public sealed class KubernetesAgentFactory(
    ILogger<KubernetesAgentFactory> logger,
    ILoggerFactory loggerFactory,
    IOptions<AgentOptions> agentOptions) : IAsyncDisposable
{
    private static readonly TimeSpan McpConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly List<McpClient> _mcpClients = [];

    /// <summary>
    /// Builds the chat client, connects to every configured MCP server, and returns the agent. Resolve this from
    /// <see cref="WebApplication.Services"/> after <c>Build()</c>.
    /// </summary>
    public async Task<AIAgent> CreateKubernetesAgentAsync(CancellationToken cancellationToken = default)
    {
        var agentOpts = agentOptions.Value;

        var (tools, failures) = await DiscoverToolsAsync(agentOpts.McpServers, cancellationToken);

        IChatClient chatClient = new ChatClient(
                model: agentOpts.ModelConnection.Model,
                credential: new ApiKeyCredential(agentOpts.ModelConnection.ApiKey ?? string.Empty),
                options: new OpenAIClientOptions { Endpoint = new Uri(agentOpts.ModelConnection.Endpoint) })
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(
                sourceName: "K3s-agent",
                configure: (cfg) => cfg.EnableSensitiveData = true
                )
            .UseLogging(loggerFactory)
            .Build();

        var agent = chatClient
            .AsAIAgent(
                name: agentOpts.ModelId,
                instructions: BuildInstructions(agentOpts.SystemPrompt, failures),
                tools: [.. tools]
            )
            .AsBuilder()
            .UseOpenTelemetry()
            .Build();

        return agent;
    }

    /// <summary>
    /// Connects to every configured MCP server independently and aggregates their read-only tools. A server that
    /// fails (unreachable, blank endpoint, etc.) doesn't take the others down — it's recorded as a failure instead
    /// so <see cref="BuildInstructions"/> can tell the model about the gap.
    /// </summary>
    private async Task<(IReadOnlyList<AITool> Tools, IReadOnlyList<(string Name, string Reason)> Failures)>
        DiscoverToolsAsync(IReadOnlyList<McpServerOptions> servers, CancellationToken cancellationToken)
    {
        var tools = new List<AITool>();
        var failures = new List<(string Name, string Reason)>();

        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.Endpoint))
            {
                logger.LogWarning(
                    "MCP server {Name} has no endpoint configured; it will contribute no tools.", server.Name);
                failures.Add((server.Name, "no endpoint configured"));
                continue;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(McpConnectTimeout);

                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(server.Endpoint),
                    TransportMode = HttpTransportMode.StreamableHttp,
                });

                var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
                var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: timeout.Token);

                _mcpClients.Add(mcpClient);
                tools.AddRange(mcpTools.Select(t => (AITool)t));

                logger.LogInformation(
                    "Connected to MCP server {Name} at {Endpoint}: {Total} tools discovered and exposed to the agent.",
                    server.Name, server.Endpoint, mcpTools.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Could not connect to MCP server {Name} at {Endpoint}; it will contribute no tools.",
                    server.Name, server.Endpoint);
                failures.Add((server.Name, ex.Message));
            }
        }

        return (tools, failures);
    }

    /// <summary>
    /// Appends a note listing any MCP servers that couldn't be reached, so the model can honestly tell the user it
    /// lacks a capability instead of silently having fewer tools than the config implies.
    /// </summary>
    private static string BuildInstructions(string systemPrompt, IReadOnlyList<(string Name, string Reason)> failures)
    {
        if (failures.Count == 0)
        {
            return systemPrompt;
        }

        var unavailable = string.Join(", ", failures.Select(f => $"{f.Name} ({f.Reason})"));
        return $"""
            {systemPrompt}

            Note: the following data sources are currently unavailable and their information cannot be retrieved: {unavailable}.
            """;
    }

    /// <summary>
    /// Options for <c>MapOpenAIChatCompletions</c> that honor request-supplied generation settings (temperature,
    /// top_p, max_completion_tokens, frequency_penalty, presence_penalty, seed, stop) instead of the framework's
    /// default of rejecting any request that carries them with a 400. Frontends like NextChat send these on every
    /// request even at their default values, so rejecting them makes the endpoint unusable from such clients.
    /// Request-supplied <c>tools</c>/<c>tool_choice</c>/<c>response_format</c> are still rejected (falling back to
    /// the framework's default behavior for just those fields) — this agent's MCP tools are fixed by server-side
    /// configuration (docs/ARCHITECTURE.md §7), not something a caller should be able to override.
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

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _mcpClients)
        {
            await client.DisposeAsync();
        }
    }
}
