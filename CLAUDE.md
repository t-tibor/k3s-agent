# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository layout

```
global.json                                   # pins the .NET SDK (10.0.400)
KubernetesAiAgent.sln
docs/
├── PRD.md                                    # functional description + acceptance criteria
└── ARCHITECTURE.md                           # component design, API contracts, config, security, project layout
src/
├── KubernetesAiAgent.AppHost/                # Aspire orchestrator — starts Agent + NextChat container
├── KubernetesAiAgent.Agent/                  # ASP.NET Core agent backend (the component being built)
├── KubernetesAiAgent.ServiceDefaults/        # shared Aspire wiring: OpenTelemetry, service discovery, health checks
└── KubernetesAiAgent.Tests/                  # xUnit tests, references Agent
k8s/                                          # Kubernetes deployment manifests (placeholder, not yet populated)
```

- `docs/PRD.md` — what the product must do (overview, goals, non-goals, example use cases) and acceptance criteria.
- `docs/ARCHITECTURE.md` — how it's built: component design, API contracts, configuration shape, security/RBAC
  model, project layout, and design principles.

Read both in full before implementing — the summary below is a map of their structure, not a replacement for them.

## Commands

```
dotnet build                                                          # build the whole solution
dotnet test src/KubernetesAiAgent.Tests/KubernetesAiAgent.Tests.csproj # run all tests
dotnet test src/KubernetesAiAgent.Tests/KubernetesAiAgent.Tests.csproj --filter "FullyQualifiedName~<Name>"  # run a single test
dotnet run --project src/KubernetesAiAgent.AppHost                    # start the Aspire dashboard + Agent + NextChat locally
```

The Agent exposes `/health`, `/alive`, `GET /v1/models`, and `POST /v1/chat/completions`. Chat completions are a
real Microsoft Agent Framework agent backed by OpenRouter (DeepSeek V4 Flash) with Kubernetes MCP tools wired in —
see `src/KubernetesAiAgent.Agent/Agent/KubernetesAgent.cs`. Local dev needs an OpenRouter key:
`dotnet user-secrets set "OpenRouter:ApiKey" "<key>" --project src/KubernetesAiAgent.Agent`.

## What this project is

A self-hosted, **read-only** Kubernetes AI assistant. A user asks natural-language questions about a cluster in a
chat UI; the request flows through an OpenAI-compatible backend into an LLM that can call read-only Kubernetes MCP
tools to answer with real cluster state.

Component chain (see docs/ARCHITECTURE.md §1 for the full diagram):

```
Browser -> NextChat -> Kubernetes AI Agent (ASP.NET Core) -> Microsoft Agent Framework -> OpenRouter (DeepSeek V4 Flash)
                                    |
                                    +--> Kubernetes MCP Server (read-only) -> Kubernetes API
```

- **NextChat** (`yidadaa/chatgpt-next-web`, replaces the original V1 Hollama frontend) — chat UI run as a container
  resource inside .NET Aspire (pinned image tag, see `src/KubernetesAiAgent.AppHost/AppHost.cs`). Its Node server
  calls the Agent's OpenAI-compatible API server-side (the browser never calls the Agent directly), so the Agent's
  CORS policy isn't exercised by this frontend. Configuration is via container environment variables the AppHost
  sets, not a browser UI form — `BASE_URL` is wired to the Agent's actual endpoint (`agent.GetEndpoint("http")`),
  so there's no manual "add server" step to repeat on every run, unlike Hollama.
- **Kubernetes AI Agent** — the component being built. ASP.NET Core + Microsoft Agent Framework. `GET /v1/models`
  is hand-rolled; `POST /v1/chat/completions` is provided by `Microsoft.Agents.AI.Hosting.OpenAI`'s
  `MapOpenAIChatCompletions` (mapped at that flat path rather than its default `/{agentName}/v1/chat/completions`)
  — see `Agent/KubernetesAgent.cs`. This is bleeding-edge, date-versioned prerelease software; re-verify exact
  method signatures against whatever version is installed if things don't compile.
- **OpenRouter** — the LLM provider, an OpenAI-compatible model marketplace. Configured (not hard-coded) via
  `OpenRouter:Endpoint` / `OpenRouter:Model` / `OpenRouter:ApiKey`; V1 pins `deepseek/deepseek-v4-flash-0731` but
  the agent doesn't assume a specific model name. Re-verify the model slug at openrouter.ai/models if it drifts —
  OpenRouter's catalog changes often.
- **Kubernetes MCP server** — an *existing*, externally-configured dependency (not owned by this solution), reached
  over Streamable HTTP. Only `get`/`list`/`watch` verbs; exposes tools for pods, services, nodes, namespaces,
  events, configmaps, deployments, replicasets, statefulsets, daemonsets, jobs, cronjobs, ingresses, PVCs/PVs. Its
  endpoint is configuration-driven (`KubernetesMcp:Endpoint`). The agent connects to it with a short timeout and
  degrades gracefully (zero tools, not a crash) if it's unreachable at startup — see
  `KubernetesAgentExtensions.DiscoverReadOnlyMcpToolsAsync`. The MCP server exposes only read-only (`get`/`list`/
  `watch`) tools by design, so the agent does not additionally filter the tool list app-side — RBAC on the MCP
  server's ServiceAccount is the enforcement point (docs/ARCHITECTURE.md §8).
- **Aspire AppHost** — orchestrates local dev: starts the Agent project and the NextChat container, wires service
  discovery, surfaces logs/endpoints/topology in the Aspire dashboard.

## Non-negotiable design principles (docs/ARCHITECTURE.md §20)

1. The agent is the *only* component that talks to the MCP server; the MCP server is the *only* component that
   talks to Kubernetes.
2. **RBAC is the security boundary, not the system prompt.** Read-only enforcement must hold even if prompt/tool
   filtering is bypassed — the MCP server's ServiceAccount should physically lack `create`/`update`/`patch`/
   `delete`/`deletecollection` verbs (docs/ARCHITECTURE.md §8).
3. No mutating Kubernetes operations, ever, in this version — enforced at the RBAC layer (the MCP server's
   ServiceAccount lacks mutating verbs) and by the MCP server exposing only read-only tools in the first place; not
   re-enforced in agent logic.
4. The agent backend should be stateless — NextChat owns conversation history (client-side); each request carries
   the messages the agent needs. Don't introduce Redis/a DB for conversation state unless Agent Framework strictly
   requires it.
5. OpenRouter, NextChat, and the MCP endpoint are all meant to stay swappable/configuration-driven — don't hard-code
   endpoints, model names, or image tags into source.
6. No secrets in source control (OpenRouter API key, agent API key, MCP auth). Local dev uses env vars / user
   secrets; Kubernetes deployment uses Secrets (preferably Sealed Secrets or equivalent).

## API contract specifics (docs/ARCHITECTURE.md §5)

- `GET /v1/models` must list a model with `id: "kubernetes-agent"`. NextChat's `CUSTOM_MODELS` env var (set by the
  AppHost) restricts its model picker to just this id regardless of what this endpoint returns; whether NextChat
  additionally calls `GET /v1/models` for validation wasn't verified — keep the endpoint correct either way.
- `POST /v1/chat/completions` accepts standard OpenAI chat message arrays and returns an OpenAI-compatible
  `chat.completion` shape; `stream: true` is real SSE streaming, handled by the framework's hosting extension
  (docs/ARCHITECTURE.md §5.3, §6.3) — no hand-rolled request/response mapping for chat completions anymore.
- Error mapping matters for the frontend's UX: OpenRouter-unavailable -> generic 5xx; MCP-unavailable -> degrades
  to zero tools rather than failing the request (never let the model fabricate cluster state); invalid request ->
  400 with validation detail (docs/ARCHITECTURE.md §13). Model-id validation on chat completions is handled by the
  framework, not hand-rolled 404s.

## Logging constraints (docs/ARCHITECTURE.md §12)

Log request/correlation IDs, model/deployment name, agent execution duration, MCP tool name + duration, tool-call
count, and errors. Never log API keys, authorization headers, full sensitive conversation content by default,
Kubernetes Secret values, or credentials returned by tools.

## Testing expectations (docs/ARCHITECTURE.md §18)

- Unit tests: configuration validation (`AgentOptionsTests`, `OpenRouterOptionsTests`), model listing
  (`ModelsEndpointTests`), all without a live MCP server. Tests use `KubernetesAgentTestFactory`, not a bare
  `WebApplicationFactory<Program>` — it injects dummy
  OpenRouter credentials and an unreachable `KubernetesMcp:Endpoint` via environment variables (set before the
  host builds, since a `ConfigureAppConfiguration` override applies too late for the agent's pre-`Build()`
  configuration reads) so tests never hit real OpenRouter or a real MCP server, and exercise the
  graceful-degradation path instead.
- Integration tests: real MCP connectivity, calling OpenRouter, answering a simple cluster-state question
  end-to-end — not automated yet; this needs a real API key and a real MCP server (manual verification, per
  docs/ARCHITECTURE.md §18.2).
