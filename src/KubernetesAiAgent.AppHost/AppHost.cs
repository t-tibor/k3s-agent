var builder = DistributedApplication.CreateBuilder(args);

// Secret: the OpenRouter API key for the Python agent's model connection. Configured here (rather than in the
// agent project's own user secrets) because the AppHost is what owns and injects it into the agent process's
// environment — see the WithEnvironment call below.
var openRouterApiKey = builder.AddParameter("openrouter-api-key", secret: true);

// The Kubernetes AI Agent, ported to Python on Microsoft Agent Framework (see
// src/KubernetesAiAgent.PyAgent). WithUv() runs `uv sync` before the app starts so its virtual environment
// stays up to date. The port is pinned (rather than left to Aspire's per-run dynamic allocation) so its URL
// is stable across AppHost restarts, and env: "PORT" is what main.py reads to bind uvicorn.
var agent = builder.AddPythonApp("agent", "../KubernetesAiAgent.PyAgent", "main.py")
    .WithUv()
    .WithHttpEndpoint(port: 5192, env: "PORT")
    .WithEnvironment("ENVIRONMENT", "Development")
    .WithEnvironment("Agent__ModelConnection__ApiKey", openRouterApiKey);

// NextChat (the OpenAI-compatible chat UI used with the .NET agent, KubernetesAiAgent.NetAgent) is not wired
// up here: the Python agent only exposes the AG-UI protocol (no server-side OpenAI chat-completions hosting
// exists yet in Microsoft Agent Framework for Python), so NextChat would have nothing to talk to. Point
// NextChat at a running NetAgent instance instead if you need it.

// Custom AG-UI frontend (src/webui): a Vite/React SPA that speaks the AG-UI protocol directly to the Agent's
// /agui endpoint from browser JavaScript — no Node-side proxy — so it can render agent messages and every MCP
// tool call as they stream. This exercises the Agent's CORS policy (which allows any origin).
// WithHttpEndpoint's env: "PORT" is what vite.config.ts reads to bind the port Aspire allocated.
builder.AddNpmApp("webui", "../webui", "dev")
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("VITE_AGENT_URL", agent.GetEndpoint("http"))
    .WithReference(agent)
    .WaitFor(agent);

builder.Build().Run();
