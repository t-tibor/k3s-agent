var builder = DistributedApplication.CreateBuilder(args);

// The Agent's port is pinned (rather than left to Aspire's per-run dynamic allocation) so its URL is stable across
// AppHost restarts.
var agent = builder.AddProject<Projects.KubernetesAiAgent_Agent>("agent")
    .WithHttpEndpoint(port: 5192);

// NextChat (replaces Hollama) calls the Agent server-side from its own Node process rather than from browser
// JavaScript, so BASE_URL is read from a container environment variable instead of being typed into the UI —
// Aspire wires it to the Agent's actual endpoint below, so it's correct on every run with no manual step
// (docs/ARCHITECTURE.md §3). This also means no browser-side cross-origin call is made to the Agent — the
// Agent's CORS policy (which allows any origin) only matters for other, browser-direct frontends.
// BASE_URL must be the bare origin, no "/v1" suffix — NextChat appends "v1/chat/completions" itself.
// OPENAI_API_KEY is a placeholder, not a secret: the Agent doesn't validate it (AgentOptions.RequireApiKey is
// false by default), NextChat just requires some non-empty value to be configured.
// CUSTOM_MODELS restricts the model picker to just this agent's model id instead of NextChat's normal
// OpenAI/Azure/Google model list, since none of those are actually served by this Agent.
builder.AddContainer("nextchat", "yidadaa/chatgpt-next-web", "v2.16.1")
    .WithHttpEndpoint(port: 3000, targetPort: 3000)
    .WithEnvironment("BASE_URL", agent.GetEndpoint("http"))
    .WithEnvironment("OPENAI_API_KEY", "not-required-by-agent")
    .WithEnvironment("HIDE_USER_API_KEY", "1")
    .WithEnvironment("CUSTOM_MODELS", "-all,+kubernetes-agent")
    .WithReference(agent)
    .WaitFor(agent);

// Custom AG-UI frontend (src/webui): a Vite/React SPA that speaks the AG-UI protocol directly to the Agent's
// /agui endpoint from browser JavaScript — no Node-side proxy — so it can render agent messages and every MCP
// tool call as they stream. Unlike NextChat this *does* exercise the Agent's CORS policy (which allows any
// origin). WithHttpEndpoint's env: "PORT" is what vite.config.ts reads to bind the port Aspire allocated.
builder.AddNpmApp("webui", "../webui", "dev")
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("VITE_AGENT_URL", agent.GetEndpoint("http"))
    .WithReference(agent)
    .WaitFor(agent);

builder.Build().Run();
