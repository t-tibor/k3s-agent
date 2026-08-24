using KubernetesAiAgent.Agent.Agent;
using KubernetesAiAgent.Agent.Api;
using KubernetesAiAgent.Agent.Configuration;
using KubernetesAiAgent.Agent.Health;

const string FrontendCorsPolicy = "Frontend";

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

builder.Services.AddHealthChecks()
    .AddCheck<AgentConfigurationHealthCheck>("agent-configuration")
    .AddCheck<ModelConnectionConfigurationHealthCheck>("model-connection-configuration");

// Hollama (and any other browser-based frontend) calls this API directly from client-side JavaScript rather than
// through a server-side proxy (docs/ARCHITECTURE.md §3.2). No credentials are sent cross-origin by this API, so
// allowing any origin is safe.
builder.Services.AddCors(corsOptions => corsOptions.AddPolicy(FrontendCorsPolicy, policy =>
{
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
}));

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

// No forced HTTPS redirect: in local dev, Hollama is pointed at this API's HTTP endpoint to avoid the browser
// rejecting the ASP.NET Core dev-cert (docs/ARCHITECTURE.md §3.2) — redirecting would break that.

app.UseCors(FrontendCorsPolicy);

app.MapModelsEndpoint();
app.MapOpenAIChatCompletions(kubernetesAgent, path: "/v1/chat/completions", KubernetesAgentFactory.ChatCompletionsMapOptions);

app.Run();

// Exposed so WebApplicationFactory<Program> can host this app in-process for integration tests.
public partial class Program;
