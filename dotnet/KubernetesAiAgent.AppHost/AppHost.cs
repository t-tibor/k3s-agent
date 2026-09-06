var builder = DistributedApplication.CreateBuilder(args);

// Secret: the OpenRouter API key for the agent's model connection. Configured here (rather than in the
// agent project's own user secrets) because the AppHost is what owns and injects it into the agent process's
// environment — see the WithEnvironment call below.
var openRouterApiKey = builder.AddParameter("openrouter-api-key", secret: true);

// The Kubernetes AI Agent (KubernetesAiAgent.NetAgent) — also the project the production Docker image is
// built from (dotnet/KubernetesAiAgent.NetAgent/Dockerfile), where it additionally serves the built
// KubernetesAiAgent.WebUI bundle from the same origin (§3.4). The port is pinned (rather than left to
// Aspire's per-run dynamic allocation) so its URL is stable across AppHost restarts; it matches the port
// already pinned in the project's own launchSettings.json for `dotnet run` standalone.
var agent = builder.AddProject<Projects.KubernetesAiAgent_NetAgent>("agent")
    .WithHttpEndpoint(port: 5192)
    .WithEnvironment("Agent__ModelConnection__ApiKey", openRouterApiKey);

// NextChat (the OpenAI-compatible chat UI used with NetAgent) is not wired up here — it's a separate,
// independently-pointed frontend outside the AppHost's default topology. Point NextChat at a running
// NetAgent instance instead if you need it.

// Custom AG-UI frontend (KubernetesAiAgent.WebUI): a Vite/React SPA that speaks the AG-UI protocol. It
// always calls a same-origin relative /agui (src/agent.ts) — in dev, Vite's own dev-server proxy
// (vite.config.ts) forwards that to the Agent's real endpoint; in production, NetAgent instead builds and
// serves this same bundle itself (see the Dockerfile). Either way the browser never calls the Agent
// cross-origin, so the Agent needs no CORS policy. This npm-app resource exists purely for the local
// hot-reload dev loop, not for how the app is deployed.
// WithHttpEndpoint's env: "PORT" is what vite.config.ts reads to bind the port Aspire allocated; the
// VITE_AGENT_URL env var below is what it reads as the dev-server proxy's target (Node-side config, not
// exposed to client-side JS — see vite.config.ts).
builder.AddNpmApp("webui", "../KubernetesAiAgent.WebUI", "dev")
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("VITE_AGENT_URL", agent.GetEndpoint("http"))
    .WithReference(agent)
    .WaitFor(agent);

builder.Build().Run();
