# Architecture: Kubernetes AI Agent with .NET Aspire, Microsoft Agent Framework, and NextChat

> Functional requirements and acceptance criteria live in [PRD.md](PRD.md).
> This document covers how the product is built: components, contracts, configuration, security boundaries, and
> project layout.

## 1. Target Architecture

```text
                                    ┌───────────────────────┐
                                    │       Browser         │
                                    └───────────┬───────────┘
                                                │
                                                │ HTTP/HTTPS
                                                ▼
                              ┌─────────────────────────────┐
                              │         NextChat            │
                              │                             │
                              │ Container managed by Aspire │
                              └──────────────┬──────────────┘
                                             │
                                             │ OpenAI-compatible API
                                             ▼
                              ┌─────────────────────────────┐
                              │     Kubernetes AI Agent     │
                              │                             │
                              │ ASP.NET Core / .NET         │
                              │ Microsoft Agent Framework   │
                              │                             │
                              │ /v1/models                  │
                              │ /v1/chat/completions        │
                              └──────────┬───────────┬──────┘
                                         │           │
                                         │           │ MCP
                                         │           ▼
                                         │    ┌─────────────────────┐
                                         │    │ Kubernetes MCP Server│
                                         │    └──────────┬──────────┘
                                         │               │
                                         │               ▼
                                         │       Kubernetes API
                                         │
                                         │ HTTPS/API
                                         ▼
                              ┌─────────────────────────────┐
                              │         OpenRouter          │
                              │  LLM Model (DeepSeek V4     │
                              │        Flash)               │
                              └─────────────────────────────┘
```

### 1.1 Aspire architecture

The Aspire AppHost is responsible for composing the application resources.

Conceptually:

```text
AppHost
├── NextChat container
├── Kubernetes Agent project
└── optional supporting resources
```

The AppHost should configure service discovery between NextChat and the agent where supported — specifically,
NextChat's `BASE_URL` environment variable should be wired to the agent's actual endpoint by the AppHost, not
typed manually into a UI (see §3.3).

The Kubernetes MCP server is an externally configured dependency from the Aspire application's perspective. It may run inside the same Kubernetes cluster but is not owned by the Aspire application.

---

## 2. Components

### 2.1 Aspire AppHost

#### Responsibilities

- Define the application topology.
- Start the agent backend.
- Start NextChat as a container.
- Configure environment variables and references — including wiring NextChat's `BASE_URL` to the agent's endpoint,
  so no manual configuration step is needed after starting the app (§3.3).
- Provide service discovery where applicable.
- Provide a convenient local development experience.
- Make the resource topology visible in the Aspire dashboard.

#### Requirements

- Use the current stable .NET Aspire version available at implementation time.
- Use Aspire's container resource support for NextChat.
- Do not hard-code secrets in AppHost source code.
- Configuration must work both locally and in Kubernetes.

Example conceptual topology:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var agent = builder.AddProject<Projects.KubernetesAgent>("agent");

var nextChat = builder.AddContainer("nextchat", "yidadaa/chatgpt-next-web")
    .WithEnvironment("BASE_URL", agent.GetEndpoint("http"))
    .WithReference(agent);

builder.Build().Run();
```

The exact APIs and image/tag must be validated against the Aspire version selected for implementation.

---

## 3. NextChat

### 3.1 Role

NextChat (`ChatGPTNextWeb/NextChat`, distributed as the `yidadaa/chatgpt-next-web` container image) is the
user-facing chat application. Unlike Hollama (the original V1 choice — see the note at the end of this section),
NextChat's Node server calls the configured OpenAI-compatible backend itself; the browser only ever talks to
NextChat's own origin.

It must treat the Kubernetes Agent as an OpenAI-compatible model/provider.

Neither NextChat's browser client nor its server process may communicate directly with:

- Kubernetes API
- Kubernetes MCP server
- OpenRouter

The only backend integration required for V1 is:

```text
Browser -> NextChat (server-side proxy) -> Kubernetes Agent
```

Because the call to the Agent happens server-side rather than from browser JavaScript, the Agent's CORS policy
(`Agent:AllowedOrigins`) is not exercised by this default frontend — it remains available, unused, for any future
browser-direct frontend (§20 design principle 5).

### 3.2 Container requirements

NextChat must:

- Run as an Aspire-managed container.
- Be reachable from the developer's browser during local development.
- Be deployable as a Kubernetes workload in the production deployment.

NextChat keeps its own configuration (which backend to call, which model to expose) in container environment
variables rather than browser state, but conversation history is still browser-side (localStorage) — so it still
needs no persistent volume and no database, matching the stateless-frontend requirement Hollama also met (§11).

### 3.3 NextChat configuration

Unlike Hollama, NextChat reads its backend connection from container **environment variables**, not from a UI form
the user fills in on every run:

```text
BASE_URL          the Agent's OpenAI-compatible base URL, no trailing "/v1" — NextChat appends
                   "v1/chat/completions" itself. Must be wired by the AppHost to the Agent's actual endpoint
                   (§2.1), not hard-coded, since the Agent's port can vary across environments.
OPENAI_API_KEY     required to be non-empty by NextChat even when the backend doesn't check it — see
                   Agent:RequireApiKey (§10); use a placeholder, not a real secret, when the Agent doesn't enforce
                   an API key.
HIDE_USER_API_KEY  set so users aren't prompted to supply their own key.
CUSTOM_MODELS      restricts NextChat's model picker to just this agent's model id (`kubernetes-agent`), since
                   NextChat's default list assumes real OpenAI/Azure/Google models this Agent doesn't serve.
```

This means a developer runs the AppHost and gets a working, pre-configured chat UI immediately — no manual
"add server" step, and no risk of the configured URL going stale when the Agent's endpoint changes between runs.

> **Note (V1 frontend history):** V1 originally used Hollama, a minimal backend-less SvelteKit chat UI, for its
> simplicity. It was replaced by NextChat because Hollama has no server-side configuration at all — its connected
> servers are typed into the browser UI and persisted only in that browser's `localStorage`, so the "add the Agent
> as a server" step had to be repeated by hand every time the Agent's endpoint changed across AppHost restarts.
> NextChat's environment-variable-driven `BASE_URL` (above) removes that manual step entirely. Design principle 5
> (§20) — the frontend remains replaceable — is what made this swap low-cost; a future frontend swap should stay
> similarly cheap.

---

## 4. Kubernetes Agent Backend

### 4.1 Technology

The backend must be implemented in:

- C#
- .NET
- ASP.NET Core
- Microsoft Agent Framework

The agent backend is the central application component.

### 4.2 Responsibilities

The backend must:

1. Accept OpenAI-compatible chat requests.
2. Convert incoming messages into Microsoft Agent Framework conversation input.
3. Invoke the configured OpenRouter model.
4. Allow the model to call Kubernetes MCP tools.
5. Return the final agent response through the OpenAI-compatible API.
6. Optionally stream the response.
7. Log agent/tool activity without logging secrets.
8. Expose health/readiness endpoints.
9. Enforce application-level read-only policy.
10. Support configuration through `appsettings.json` and environment variables.

---

## 5. OpenAI-Compatible API

The backend must expose the minimum API required by NextChat (or any other OpenAI-compatible frontend, per §20
design principle 5).

### 5.1 Model discovery

```http
GET /v1/models
```

Example response:

```json
{
  "object": "list",
  "data": [
    {
      "id": "kubernetes-agent",
      "object": "model",
      "created": 0,
      "owned_by": "local"
    }
  ]
}
```

The exact response should follow the OpenAI API shape closely enough for NextChat compatibility.

### 5.2 Chat completions

```http
POST /v1/chat/completions
```

Example request:

```json
{
  "model": "kubernetes-agent",
  "messages": [
    {
      "role": "user",
      "content": "Which pods are currently restarting?"
    }
  ],
  "stream": false
}
```

The backend must return an OpenAI-compatible `chat.completion` response.

### 5.3 Streaming

```json
{
  "stream": true
}
```

is supported using Server-Sent Events in the OpenAI-compatible format, provided by Microsoft Agent Framework's
OpenAI-compatible hosting extension (`Microsoft.Agents.AI.Hosting.OpenAI`, see §6.3) rather than hand-rolled SSE
handling.

> **Known issue (as of `Microsoft.Agents.AI.Hosting.OpenAI` 1.19.0-alpha.260822.1, the latest available prerelease):**
> during tool-call turns, some SSE chunks are emitted with `"choices": []` (an empty array) rather than omitting the
> field or keeping one choice with an empty delta. Real OpenAI only ever sends an empty `choices` array once, as a
> final usage-only chunk — never mid-stream. This was originally found against Hollama's frontend, which does not
> guard against `choices` being empty and throws (`TypeError: can't access property "delta", i.choices[0] is
> undefined`) whenever this happens — in practice on essentially every request that involves an MCP tool call.
> Confirmed via a raw `curl` against `/v1/chat/completions` with `stream: true` — the agent itself calls the right
> tools and produces a correct, MCP-grounded answer; the bug is purely in the hosting extension's SSE chunk shape,
> not in this app's own code (which does not hand-roll the SSE mapping — see §6.3). No newer prerelease exists yet
> to pick up a fix. Whether NextChat's more mature streaming client tolerates this better than Hollama's did is not
> yet verified — re-test against NextChat before assuming it's resolved.

---

## 6. Microsoft Agent Framework

### 6.1 Agent

Create one primary agent:

```text
KubernetesAgent
```

The agent should have a system instruction similar to:

```text
You are a Kubernetes cluster assistant.

You have read-only access to the Kubernetes cluster through MCP tools.

Use the available Kubernetes tools whenever the question requires
current cluster state.

Never claim that you inspected a resource unless you actually did so.

Do not invent cluster state.

Distinguish observed facts from hypotheses.

You must not perform or request mutating operations.

When troubleshooting, gather sufficient evidence before drawing conclusions.

When useful, mention which Kubernetes resources or observations
support your conclusion.
```

The final prompt should be stored in configuration or a dedicated prompt file rather than embedded throughout application code.

### 6.2 Model

The model is served through **OpenRouter**, an OpenAI-compatible model marketplace — the agent connects to it the
same way it would connect to any OpenAI-compatible provider (a chat client pointed at OpenRouter's base URL,
authenticated with a Bearer API key).

V1 pins a DeepSeek V4 Flash model, but configuration must not otherwise assume a specific model name — swapping to
a different OpenRouter-hosted model is a configuration change, not a code change.

Example configuration:

```json
{
  "OpenRouter": {
    "Endpoint": "https://openrouter.ai/api/v1",
    "Model": "deepseek/deepseek-v4-flash-0731",
    "ApiKey": "..."
  }
}
```

Environment variables must be supported. Authentication follows OpenRouter's standard mechanism: an
`Authorization: Bearer <ApiKey>` header.

### 6.3 Hosting

The agent is exposed via Microsoft Agent Framework's own OpenAI-compatible hosting extension
(`Microsoft.Agents.AI.Hosting.OpenAI`'s `AddOpenAIChatCompletions`/`MapOpenAIChatCompletions`) rather than a
hand-rolled `/v1/chat/completions` implementation — this is what provides the request/response mapping and the
streaming support described in §5. The route is kept at the flat `/v1/chat/completions` path (via that extension's
path override) rather than its default `/{agentName}/v1/chat/completions` shape, so the documented API surface in
§5 doesn't change based on this implementation choice. `GET /v1/models` remains a small hand-rolled endpoint, since
the hosting extension doesn't provide model discovery.

---

## 7. Kubernetes MCP integration

The agent must connect to the existing Kubernetes MCP server.

### 7.1 Configuration

The MCP server URL must be configurable through application configuration.

Example:

```json
{
  "KubernetesMcp": {
    "Endpoint": "http://kubernetes-mcp.default.svc.cluster.local:8080"
  }
}
```

Environment-variable equivalent:

```text
KubernetesMcp__Endpoint
```

The application must not hard-code the MCP endpoint.

### 7.2 MCP transport

Prefer the MCP transport supported by the existing server, with **Streamable HTTP** preferred where available.

The implementation must use the Microsoft Agent Framework's MCP integration rather than manually implementing MCP protocol handling unless a documented framework limitation requires it.

### 7.3 Tool exposure

All tools exposed to the agent must be read-only.

The agent must not receive mutating Kubernetes tools.

The Kubernetes MCP server this agent connects to exposes only read-only (`get`/`list`/`watch`) tools by design, so the
application does not additionally filter the tool list it receives — RBAC and the MCP server's own tool surface are
the enforcement points (§8), not app-side filtering.

---

## 8. Kubernetes RBAC

Security must be enforced independently of the LLM prompt.

The MCP server must use a dedicated Kubernetes ServiceAccount.

The ServiceAccount must have only the permissions required for read-only inspection.

Allowed verbs should normally be limited to:

```text
get
list
watch
```

Potential resources include:

```text
pods
services
nodes
namespaces
events
configmaps
deployments
replicasets
statefulsets
daemonsets
jobs
cronjobs
ingresses
persistentvolumeclaims
persistentvolumes
```

The exact permissions must match the tools exposed by the existing MCP server.

Do not grant:

```text
create
update
patch
delete
deletecollection
```

unless a future version explicitly requires them.

The agent must never use the user's personal kubeconfig.

---

## 9. Security

### 9.1 Secrets

The following values are secrets and must not be committed to Git:

- OpenRouter API key.
- Agent API key, if enabled.
- Any MCP authentication credentials.

Local development may use:

- environment variables
- user secrets
- local `.env` mechanisms where appropriate

Kubernetes deployment should use:

- Kubernetes Secrets
- preferably Sealed Secrets or an equivalent GitOps-safe secret mechanism.

### 9.2 Network boundaries

Recommended production topology:

```text
Internet/LAN
     │
     ▼
NextChat
     │
     ▼
Agent
     │
     ├──► OpenRouter
     │
     └──► Kubernetes MCP
```

The Kubernetes MCP server should be a `ClusterIP` service unless there is a specific reason to expose it externally.

The Kubernetes API should never be exposed to NextChat.

---

## 10. Configuration

Configuration should use strongly typed .NET options.

Required configuration:

```text
OpenRouter:
  Endpoint
  Model
  ApiKey

KubernetesMcp:
  Endpoint

Agent:
  ModelId
  SystemPrompt
  MaxToolCalls (optional)
```

Optional:

```text
Agent:
  RequireApiKey
  ApiKey
  RequestTimeoutSeconds
  MaxConversationMessages
```

The exact authentication properties must follow OpenRouter's Bearer-token authentication mechanism.

---

## 11. Statelessness

The agent backend should be stateless.

NextChat is responsible for maintaining the user-facing conversation (in the browser's local storage).

The agent should receive the relevant conversation messages from each request.

Do not introduce Redis, a database, or another conversation store in V1 unless Microsoft Agent Framework requires it for the selected implementation.

This allows multiple agent replicas:

```text
NextChat
    │
    ├── Agent replica 1
    ├── Agent replica 2
    └── Agent replica 3
```

without sticky sessions.

---

## 12. Health and observability

The backend must expose:

```http
GET /health
GET /alive
```

or equivalent ASP.NET Core health endpoints.

Recommended semantics:

- Liveness: process is running.
- Readiness: application is initialized and required configuration is valid.

Do not make readiness depend on OpenRouter being reachable unless there is a specific reason to do so.

### Logging

Log:

- request ID
- conversation/request correlation ID
- model/deployment name
- agent execution duration
- MCP tool name
- MCP tool execution duration
- errors
- tool-call count

Do NOT log:

- API keys
- authorization headers
- complete sensitive conversation content by default
- Kubernetes Secret values
- credentials returned by tools

Use structured logging.

---

## 13. Error handling

The agent API must convert backend failures into useful OpenAI-compatible HTTP responses.

Examples:

### OpenRouter unavailable

Return an appropriate `5xx` response with a generic error message.

### MCP unavailable

Return an agent error explaining that current Kubernetes information could not be retrieved.

The model must not fabricate cluster information because the MCP server is unavailable.

### Unknown model

Return:

```http
404 Not Found
```

or an OpenAI-compatible model-not-found error.

### Invalid request

Return:

```http
400 Bad Request
```

with useful validation information.

---

## 14. Request flow

For:

> Why is my postgres pod restarting?

The expected flow is:

```text
1. Browser
   |
   v
2. NextChat
   |
   | POST /v1/chat/completions
   v
3. Kubernetes Agent
   |
   v
4. Microsoft Agent Framework
   |
   v
5. OpenRouter (DeepSeek V4 Flash)
   |
   | decides that Kubernetes information is required
   v
6. Kubernetes MCP tool call
   |
   v
7. Kubernetes API
   |
   v
8. MCP result
   |
   v
9. Microsoft Agent Framework
   |
   v
10. OpenRouter
   |
   v
11. Agent API
   |
   v
12. NextChat
```

The agent should be capable of performing multiple tool calls when required.

---

## 15. Aspire local development

The Aspire solution should allow a developer to start the complete local application with a single command.

Expected result:

```text
Aspire Dashboard
│
├── nextchat
│   └── container
│
└── kubernetes-agent
    └── .NET application
```

The Aspire dashboard should show:

- resource status
- logs
- endpoints
- environment/resource relationships

The local configuration must allow the MCP server endpoint and OpenRouter credentials to be supplied without committing secrets.

If the Kubernetes MCP server is running in a remote/local Kubernetes cluster, the developer must be able to configure its accessible endpoint.

---

## 16. Kubernetes deployment

The application must eventually support deployment to Kubernetes.

Preferred production topology:

```text
Namespace: ai-agent

nextchat
├── Deployment
└── Service

kubernetes-agent
├── Deployment
├── Service
└── Secret reference

kubernetes-mcp
└── existing service
```

NextChat needs no `PersistentVolumeClaim` — conversation history is browser-side, and its own configuration comes
from environment variables/its Deployment spec, not from persisted server-side state.

The agent should communicate with the MCP server using its internal Kubernetes DNS name.

Example:

```text
http://kubernetes-mcp.ai.svc.cluster.local:8080
```

The exact namespace/service name must remain configurable.

---

## 17. Containerization

The agent must have a production-ready multi-stage Dockerfile.

Requirements:

- .NET SDK image for build.
- ASP.NET runtime image for execution.
- Non-root execution where practical.
- Minimal runtime image.
- No secrets baked into the image.
- Configurable listening port.
- Health endpoint available to Kubernetes probes.

NextChat should use a pinned image version rather than an unqualified `latest` tag in production.

---

## 18. Testing

### 18.1 Unit tests

Test:

- configuration validation
- request validation
- model listing
- OpenAI response mapping
- error mapping
- prompt/configuration handling

### 18.2 Integration tests

Test:

1. Agent can connect to the configured MCP server.
2. Agent can retrieve Kubernetes information.
3. Agent can call OpenRouter.
4. Agent can answer a simple cluster-state question.
5. Agent handles MCP failure.
6. Agent handles OpenRouter failure.

### 18.3 API compatibility tests

Verify NextChat can:

- discover `kubernetes-agent`
- send a chat completion request
- receive a normal response
- receive a streamed response if implemented

Use automated HTTP tests for the OpenAI-compatible endpoints.

---

## 19. Suggested project structure

```text
src/
│
├── KubernetesAiAgent.AppHost/
│   └── Program.cs
│
├── KubernetesAiAgent.Agent/
│   ├── Program.cs
│   ├── Agent/
│   │   ├── KubernetesAgent.cs           # chat completions come from Microsoft.Agents.AI.Hosting.OpenAI, see §6.3
│   │   └── AgentInstructions.cs
│   ├── Api/
│   │   ├── ModelsEndpoint.cs
│   │   └── OpenAiModels.cs
│   ├── Configuration/
│   │   ├── OpenRouterOptions.cs
│   │   ├── KubernetesMcpOptions.cs
│   │   └── AgentOptions.cs
│   ├── Health/
│   └── Dockerfile
│
├── KubernetesAiAgent.Tests/
│   ├── Api/
│   ├── Agent/
│   └── Integration/
│
├── appsettings.json
├── appsettings.Development.json
├── .gitignore
└── README.md
k8s/
```

The exact structure may be adjusted during implementation.

---

## 20. Design principles

1. **The agent is the only component that talks to the MCP server.**
2. **The MCP server is the only component that talks to Kubernetes on behalf of the agent.**
3. **RBAC is the security boundary, not the system prompt.**
4. **The agent API is OpenAI-compatible, but internally it is not required to use OpenAI models.**
5. **NextChat remains replaceable.**
6. **OpenRouter remains replaceable through the agent's model abstraction** (any OpenAI-compatible provider can be substituted).
7. **The MCP endpoint remains configuration-driven.**
8. **The backend should remain stateless where possible.**
9. **No secrets are committed to source control.**
10. **Read-only access is the default and the V1/V2 safety boundary.**

---

## 21. Future extensions

Potential future versions may add:

- Prometheus MCP integration.
- Grafana MCP integration.
- Argo CD integration.
- GitHub integration.
- Loki/log aggregation integration.
- Multi-agent architecture.
- Kubernetes resource diagrams.
- Incident investigation workflows.
- Automatic root-cause analysis.
- Persistent investigation sessions.
- Human approval before write operations.
- Controlled remediation actions.
- Slack/Teams integration.
- Alert-driven investigations.
- Scheduled cluster health reports.

A future write-capable agent should be implemented as a separate security boundary rather than simply expanding the read-only agent's RBAC permissions.
