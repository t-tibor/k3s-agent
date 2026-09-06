using KubernetesAiAgent.NetAgent.Agent;
using KubernetesAiAgent.NetAgent.Api;
using KubernetesAiAgent.NetAgent.Configuration;
using KubernetesAiAgent.NetAgent.Health;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Agent config lives in its own YAML file rather than appsettings.json — easier to hand-author as a Kubernetes
// ConfigMap and mount/reload independently of the rest of ASP.NET Core's config. optional: true on both matters
// for tests: KubernetesAgentTestFactory supplies everything required purely via environment variables, so a
// missing file in the test output directory must not crash the host — ValidateOnStart is what catches genuinely
// missing required values. reloadOnChange: true is what lets a future ConfigMap volume mount propagate without a
// restart.
builder.Configuration
    .AddYamlFile("agentconfig.yaml", optional: true, reloadOnChange: true)
    .AddYamlFile($"agentconfig.{builder.Environment.EnvironmentName}.yaml", optional: true, reloadOnChange: true);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<KubernetesAgentFactory>();
builder.Services.AddAGUIServer();

builder.Services.AddHealthChecks()
    .AddCheck<AgentConfigurationHealthCheck>("agent-configuration")
    .AddCheck<ModelConnectionConfigurationHealthCheck>("model-connection-configuration");

var app = builder.Build();

app.MapDefaultEndpoints();

// Builds the chat client, connects to every configured MCP server for read-only tools, and returns the agent
// (docs/ARCHITECTURE.md §6, §7) — MapOpenAIChatCompletions below takes the returned handle directly, so this can
// run after Build() now that KubernetesAgentFactory is a resolvable DI singleton.
var kubernetesAgent = await app.Services.GetRequiredService<KubernetesAgentFactory>().CreateKubernetesAgentAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// No forced HTTPS redirect: in local dev, frontends are pointed at this API's HTTP endpoint to avoid the browser
// rejecting the ASP.NET Core dev-cert (docs/ARCHITECTURE.md §3.2) — redirecting would break that.

app.MapModelsEndpoint();
app.MapOpenAIChatCompletions(kubernetesAgent, path: "/v1/chat/completions", KubernetesAgentFactory.ChatCompletionsMapOptions);

// AG-UI (https://ag-ui.com) endpoint for frontends that speak the protocol natively (event-streamed run/state
// updates) rather than OpenAI chat completions — exposes the same underlying AIAgent, just via a different
// wire protocol. Purely additive: doesn't change /v1/chat/completions or its NextChat integration.
app.MapAGUIServer("/agui", kubernetesAgent);

// Serves the built KubernetesAiAgent.WebUI bundle (wwwroot, produced by that project's publish-time MSBuild
// target — see KubernetesAiAgent.NetAgent.csproj) so the SPA and /agui share one origin/pod in production.
// wwwroot doesn't exist for a plain `dotnet run`/local Aspire dev (which uses the Vite dev server instead —
// see AppHost.cs), so these routes just 404 harmlessly there.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

// Exposed so WebApplicationFactory<Program> can host this app in-process for integration tests.
public partial class Program;
