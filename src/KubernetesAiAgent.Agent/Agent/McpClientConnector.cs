using KubernetesAiAgent.Agent.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace KubernetesAiAgent.Agent.Agent;

/// <summary>
/// Owns the (fallible, async) connection to the Kubernetes MCP server and the read-only tools it discovers.
/// Registered as a DI singleton — <see cref="McpClient"/> can't be a plain constructor-injected dependency
/// itself (creating it is async and must degrade gracefully rather than fail container resolution), so this
/// wraps that connection attempt behind a memoized async accessor that <c>KubernetesAgentFactory</c> awaits.
/// </summary>
public sealed class McpClientConnector(
    ILogger<McpClientConnector> logger,
    IOptions<KubernetesMcpOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _client;
    private IReadOnlyList<AITool>? _tools;
    private bool _attempted;

    /// <summary>
    /// Connects to the Kubernetes MCP server over Streamable HTTP with a short timeout and degrades gracefully
    /// (logs a warning, returns no tools) rather than throwing if it's unreachable — readiness and error handling
    /// must not hard-depend on this external dependency (docs/ARCHITECTURE.md §12, §16). The connection is
    /// attempted at most once and memoized; the underlying <see cref="McpClient"/> is kept alive (the returned
    /// tools need it alive to actually invoke calls later) and disposed alongside this connector (a DI singleton)
    /// on shutdown.
    /// </summary>
    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        if (_attempted)
        {
            return _tools ?? [];
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_attempted)
            {
                return _tools ?? [];
            }

            _tools = await ConnectAndDiscoverAsync(options.Value, cancellationToken);
            _attempted = true;
            return _tools;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<AITool>> ConnectAndDiscoverAsync(
        KubernetesMcpOptions mcpOptions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mcpOptions.Endpoint))
        {
            logger.LogWarning("KubernetesMcp:Endpoint is not configured; the agent will have no Kubernetes tools.");
            return [];
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(mcpOptions.Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
            });

            var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
            var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: timeout.Token);

            _client = mcpClient;

            logger.LogInformation(
                "Connected to Kubernetes MCP server at {Endpoint}: {Total} tools discovered and exposed to the agent.",
                mcpOptions.Endpoint, mcpTools.Count);

            return [.. mcpTools.Select(t => t as AITool)];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not connect to the Kubernetes MCP server at {Endpoint}; the agent will have no Kubernetes tools.",
                mcpOptions.Endpoint);
            return [];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}
