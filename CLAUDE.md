# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository layout

```
global.json                                   # pins the .NET SDK (10.0.400)
KubernetesAiAgent.sln
dotnet/
├── KubernetesAiAgent.AppHost/                # Aspire orchestrator — starts NetAgent + the AG-UI frontend (dev)
├── KubernetesAiAgent.NetAgent/                # ASP.NET Core agent backend (OpenAI chat-completions + AG-UI);
│                                              #   also builds + serves KubernetesAiAgent.WebUI in production
├── KubernetesAiAgent.ServiceDefaults/         # shared Aspire wiring: OpenTelemetry, service discovery, health checks
└── KubernetesAiAgent.WebUI/                   # Vite/React AG-UI frontend
docs/
├── PRD.md                                    # functional description + acceptance criteria
└── ARCHITECTURE.md                           # component design, API contracts, config, security, project layout
python/
└── KubernetesAiAgent.PyAgent/                # Python agent backend on Microsoft Agent Framework (AG-UI only), uv-managed
tests/
├── dotnet/
│   └── KubernetesAiAgent.Tests/              # xUnit tests, references NetAgent
└── python/                                   # pytest suite for PyAgent (discovered via its pyproject.toml testpaths)
k8s/                                          # Kubernetes deployment manifests
```

- `docs/PRD.md` — what the product must do (overview, goals, non-goals, example use cases) and acceptance criteria.
- `docs/ARCHITECTURE.md` — how it's built: component design, API contracts, configuration shape, security/RBAC
  model, project layout, and design principles.

Read both in full before implementing — the summary below is a map of their structure, not a replacement for them.

There are **two parallel agent backends** implementing the same logic: `KubernetesAiAgent.NetAgent` (.NET,
original) and `KubernetesAiAgent.PyAgent` (Python port, uv-managed, under `python/`). The Aspire AppHost
starts NetAgent — see "Which agent runs" below.

## Commands

```
dotnet build                                                                          # build the whole .NET solution
dotnet test tests/dotnet/KubernetesAiAgent.Tests/KubernetesAiAgent.Tests.csproj       # run NetAgent's xUnit tests
dotnet test tests/dotnet/KubernetesAiAgent.Tests/KubernetesAiAgent.Tests.csproj --filter "FullyQualifiedName~<Name>"  # single test
dotnet run --project dotnet/KubernetesAiAgent.AppHost                               # start the Aspire dashboard + NetAgent + webui locally
docker build -f dotnet/KubernetesAiAgent.NetAgent/Dockerfile -t k3s-agent/kubernetes-agent:latest dotnet  # production image: builds + serves the webui too

cd python/KubernetesAiAgent.PyAgent
uv sync                                                                  # install/update the Python virtual environment
uv run pytest                                                            # run PyAgent's pytest suite (from ../../tests/python)
uv run main.py                                                           # run PyAgent standalone (binds $PORT, default 5192)
```

NetAgent exposes `/health`, `/alive`, `GET /v1/models`, `POST /v1/chat/completions`, and `/agui`, and — only
in the production Docker image, where its `.csproj` builds `KubernetesAiAgent.WebUI` at publish time and
serves the result from `wwwroot` — the webui SPA itself. PyAgent exposes only `/health`, `/alive`, and
`/agui` — Microsoft Agent Framework for Python has no server-side OpenAI chat-completions hosting extension
yet, so there's no Python equivalent of `/v1/chat/completions` or `/v1/models`. Both are real Microsoft
Agent Framework agents backed by OpenRouter (DeepSeek V4 Flash) with Kubernetes MCP tools wired in — see
`dotnet/KubernetesAiAgent.NetAgent/Agent/KubernetesAgentFactory.cs` and
`python/KubernetesAiAgent.PyAgent/kubernetes_agent/agent_factory.py`.

Local dev needs an OpenRouter key. For NetAgent standalone:
`dotnet user-secrets set "Agent:ModelConnection:ApiKey" "<key>" --project dotnet/KubernetesAiAgent.NetAgent`.
For PyAgent standalone: `export Agent__ModelConnection__ApiKey="<key>"`. When running via the AppHost
(which starts NetAgent), the key is an AppHost parameter instead:
`dotnet user-secrets set "Parameters:openrouter-api-key" "<key>" --project dotnet/KubernetesAiAgent.AppHost`.

## What this project is

A self-hosted, **read-only** Kubernetes AI assistant. A user asks natural-language questions about a cluster in a
chat UI; the request flows through an agent backend into an LLM that can call read-only Kubernetes MCP
tools to answer with real cluster state.

Component chain (see docs/ARCHITECTURE.md §1 for the full diagram; this reflects what the AppHost actually
runs today — NetAgent):

```
Browser -> webui (AG-UI) -> Kubernetes AI Agent (NetAgent) -> Microsoft Agent Framework -> OpenRouter (DeepSeek V4 Flash)
                                    |
                                    +--> Kubernetes MCP Server (read-only) -> Kubernetes API
```

In production (the k8s/ Docker image), NetAgent also serves the webui's built static files from the same
origin/pod — there's no separate webui process there. In local Aspire dev, the webui still runs as its own
Vite dev server resource for hot reload, but its own dev-server proxy forwards `/agui` to the agent
server-side, so the browser still only ever talks to one origin either way.

- **webui** (`dotnet/KubernetesAiAgent.WebUI`) — Vite/React SPA speaking the AG-UI protocol. It always calls
  a same-origin relative `/agui` (`src/agent.ts`) and renders agent messages and MCP tool calls as they
  stream. In local Aspire dev, `VITE_AGENT_URL` is wired by the AppHost to the agent's endpoint
  (`agent.GetEndpoint("http")`) and consumed only by `vite.config.ts`'s dev-server proxy (Node-side, not
  exposed to client JS) to forward `/agui` there; in the production image, NetAgent builds and serves this
  same bundle directly (see `dotnet/KubernetesAiAgent.NetAgent/Dockerfile` and its `.csproj`'s
  `PublishWebUI` target). Neither path needs a CORS policy on the agent.
- **NextChat** (`yidadaa/chatgpt-next-web`) — the OpenAI-compatible chat UI used with NetAgent. Not started
  by the AppHost or deployed by `k8s/` — it's a separate, independently-pointed frontend. To use it, point
  it at a running NetAgent instance.
- **Kubernetes AI Agent** — two implementations of the same logic:
  - `KubernetesAiAgent.NetAgent` — ASP.NET Core + Microsoft Agent Framework. `GET /v1/models` is
    hand-rolled; `POST /v1/chat/completions` is provided by `Microsoft.Agents.AI.Hosting.OpenAI`'s
    `MapOpenAIChatCompletions`; `/agui` by `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`'s
    `MapAGUIServer`. This is bleeding-edge, date-versioned prerelease software; re-verify exact method
    signatures against whatever version is installed if things don't compile. It's the backend the AppHost
    starts and `k8s/` deploys.
  - `KubernetesAiAgent.PyAgent` (`python/KubernetesAiAgent.PyAgent`) — FastAPI + Microsoft Agent Framework
    for Python, managed with `uv`. `/agui` is provided by
    `agent_framework_ag_ui.add_agent_framework_fastapi_endpoint`. Also prerelease software — verify against
    the installed package versions (`uv run python -c "import agent_framework"` etc.) rather than trusting
    stale docs. Runnable standalone (`uv run main.py`) and tested (`uv run pytest`), but not
    AppHost-orchestrated or deployed by `k8s/` — kept for future development.
  - Config shape is intentionally identical between the two (`agentconfig.yaml` /
    `agentconfig.{Environment}.yaml`, `Agent__...` env var nesting) so the same values apply to either.
    One difference: PyAgent's `Agent__McpServers` env var must be a single JSON array (pydantic-settings
    has no equivalent of ASP.NET Core's indexed `Agent__McpServers__0__Name` env vars) — see
    `python/KubernetesAiAgent.PyAgent/README.md`.
- **OpenRouter** — the LLM provider, an OpenAI-compatible model marketplace. Configured (not hard-coded) via
  `Agent:ModelConnection:Endpoint` / `Model` / `ApiKey` (NetAgent) or `Agent__ModelConnection__*` (PyAgent);
  both pin `deepseek/deepseek-v4-flash-0731` by default, but neither agent assumes a specific model name.
  Re-verify the model slug at openrouter.ai/models if it drifts — OpenRouter's catalog changes often.
- **Kubernetes MCP server** — an *existing*, externally-configured dependency (not owned by this solution), reached
  over Streamable HTTP. Only `get`/`list`/`watch` verbs; exposes tools for pods, services, nodes, namespaces,
  events, configmaps, deployments, replicasets, statefulsets, daemonsets, jobs, cronjobs, ingresses, PVCs/PVs. Its
  endpoint(s) are configuration-driven (`Agent:McpServers` / `Agent__McpServers`). Each agent connects with a
  short timeout (5s) and degrades gracefully (zero tools, not a crash) if a server is unreachable at startup
  — see `KubernetesAgentFactory.cs` / `agent_factory.py`. The MCP server exposes only read-only (`get`/`list`/
  `watch`) tools by design, so neither agent additionally filters the tool list app-side — RBAC on the MCP
  server's ServiceAccount is the enforcement point (docs/ARCHITECTURE.md §8).
- **Aspire AppHost** — orchestrates local dev: starts the NetAgent project (`AddProject<Projects.KubernetesAiAgent_NetAgent>`)
  and the webui npm app, wires service discovery, surfaces logs/endpoints/topology in the Aspire dashboard.
  Does not start PyAgent or NextChat.

## Non-negotiable design principles (docs/ARCHITECTURE.md §20)

1. The agent is the *only* component that talks to the MCP server; the MCP server is the *only* component that
   talks to Kubernetes.
2. **RBAC is the security boundary, not the system prompt.** Read-only enforcement must hold even if prompt/tool
   filtering is bypassed — the MCP server's ServiceAccount should physically lack `create`/`update`/`patch`/
   `delete`/`deletecollection` verbs (docs/ARCHITECTURE.md §8).
3. No mutating Kubernetes operations, ever, in this version — enforced at the RBAC layer (the MCP server's
   ServiceAccount lacks mutating verbs) and by the MCP server exposing only read-only tools in the first place; not
   re-enforced in agent logic.
4. The agent backend should be stateless — the frontend owns conversation history (client-side); each request
   carries the messages the agent needs. Don't introduce Redis/a DB for conversation state unless Agent
   Framework strictly requires it.
5. OpenRouter, the frontend, and the MCP endpoint(s) are all meant to stay swappable/configuration-driven —
   don't hard-code endpoints, model names, or image tags into source.
6. No secrets in source control (OpenRouter API key, agent API key, MCP auth). Local dev uses env vars / user
   secrets; Kubernetes deployment uses Secrets (preferably Sealed Secrets or equivalent).

## API contract specifics (docs/ARCHITECTURE.md §5)

Applies to NetAgent; PyAgent doesn't expose this surface (see "What this project is" above).

- `GET /v1/models` must list a model with `id: "kubernetes-agent"`. NextChat's `CUSTOM_MODELS` env var (set by the
  AppHost, when NextChat is wired up) restricts its model picker to just this id regardless of what this endpoint
  returns; whether NextChat additionally calls `GET /v1/models` for validation wasn't verified — keep the
  endpoint correct either way.
- `POST /v1/chat/completions` accepts standard OpenAI chat message arrays and returns an OpenAI-compatible
  `chat.completion` shape; `stream: true` is real SSE streaming, handled by the framework's hosting extension
  (docs/ARCHITECTURE.md §5.3, §6.3) — no hand-rolled request/response mapping for chat completions.
- Error mapping matters for the frontend's UX: OpenRouter-unavailable -> generic 5xx; MCP-unavailable -> degrades
  to zero tools rather than failing the request (never let the model fabricate cluster state); invalid request ->
  400 with validation detail (docs/ARCHITECTURE.md §13). Model-id validation on chat completions is handled by the
  framework, not hand-rolled 404s.

## Logging constraints (docs/ARCHITECTURE.md §12)

Log request/correlation IDs, model/deployment name, agent execution duration, MCP tool name + duration, tool-call
count, and errors. Never log API keys, authorization headers, full sensitive conversation content by default,
Kubernetes Secret values, or credentials returned by tools.

## Testing expectations (docs/ARCHITECTURE.md §18)

- **NetAgent** unit tests (`tests/dotnet/KubernetesAiAgent.Tests`): configuration validation (`AgentOptionsTests`,
  `ModelConnectionOptionsTests`), model listing (`ModelsEndpointTests`), all without a live MCP server. Tests
  use `KubernetesAgentTestFactory`, not a bare `WebApplicationFactory<Program>` — it injects dummy OpenRouter
  credentials and an unreachable MCP endpoint via environment variables (set before the host builds, since a
  `ConfigureAppConfiguration` override applies too late for the agent's pre-`Build()` configuration reads) so
  tests never hit real OpenRouter or a real MCP server, and exercise the graceful-degradation path instead.
- **PyAgent** unit tests (`tests/python`, `uv run pytest`): the same shape —
  `test_config.py` mirrors the .NET config-validation tests, `test_agent_factory.py` mirrors the
  boot-with-unreachable-MCP behavior, `test_health.py` covers `/health`/`/alive`. `conftest.py` plays the
  role of `KubernetesAgentTestFactory`, setting the same dummy credentials / dead MCP endpoint via
  environment variables before each test loads config.
- Integration tests: real MCP connectivity, calling OpenRouter, answering a simple cluster-state question
  end-to-end — not automated yet for either agent; this needs a real API key and a real MCP server (manual
  verification, per docs/ARCHITECTURE.md §18.2).
