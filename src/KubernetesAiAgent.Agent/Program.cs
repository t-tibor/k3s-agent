using KubernetesAiAgent.Agent.Agent;
using KubernetesAiAgent.Agent.Api;
using KubernetesAiAgent.Agent.Configuration;
using KubernetesAiAgent.Agent.Health;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<OpenRouterOptions>()
    .Bind(builder.Configuration.GetSection(OpenRouterOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<KubernetesMcpOptions>(builder.Configuration.GetSection(KubernetesMcpOptions.SectionName));

builder.Services.AddSingleton<McpClientConnector>();
builder.Services.AddSingleton<KubernetesAgentFactory>();

builder.Services.AddHealthChecks()
    .AddCheck<AgentConfigurationHealthCheck>("agent-configuration")
    .AddCheck<OpenRouterConfigurationHealthCheck>("openrouter-configuration");

// Hollama (and any other browser-based frontend) calls this API directly from client-side JavaScript rather than
// through a server-side proxy, so the calling origin must be granted CORS access explicitly
// (docs/ARCHITECTURE.md §3.2). No origins are allowed unless configured via Agent:AllowedOrigins.
var allowedOrigins = builder.Configuration
    .GetSection($"{AgentOptions.SectionName}:{nameof(AgentOptions.AllowedOrigins)}")
    .Get<string[]>() ?? [];

builder.Services.AddCors(corsOptions => corsOptions.AddPolicy(FrontendCorsPolicy, policy =>
{
    if (allowedOrigins.Contains("*"))
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    }
    else if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }
}));

var app = builder.Build();

app.MapDefaultEndpoints();

// Builds the OpenRouter-backed chat client, connects to the Kubernetes MCP server for read-only tools, and
// returns the agent (docs/ARCHITECTURE.md §6, §7) — MapOpenAIChatCompletions below takes the returned handle
// directly, so this can run after Build() now that KubernetesAgentFactory is a resolvable DI singleton.
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
