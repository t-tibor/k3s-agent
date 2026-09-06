# KubernetesAiAgent.PyAgent

Python port of the Kubernetes AI Agent backend (`KubernetesAiAgent.NetAgent`), built on
[Microsoft Agent Framework](https://github.com/microsoft/agent-framework) for Python and managed with
[uv](https://docs.astral.sh/uv/).

See the repository root [CLAUDE.md](../../CLAUDE.md) and [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md)
for the overall system design; this file covers only what's specific to the Python agent.

## Why AG-UI only

This agent exposes the [AG-UI protocol](https://ag-ui.com) at `/agui`, plus `/health` and `/alive`. Unlike
the .NET agent, it does **not** expose `/v1/chat/completions` or `/v1/models`: Microsoft Agent Framework
for Python currently has no server-side hosting extension equivalent to .NET's
`Microsoft.Agents.AI.Hosting.OpenAI` (`MapOpenAIChatCompletions`). Only the AG-UI FastAPI integration
(`agent_framework_ag_ui.add_agent_framework_fastapi_endpoint`) exists. As a result:

- The custom AG-UI frontend (`dotnet/KubernetesAiAgent.WebUI`) works against this agent — it already speaks
  AG-UI natively.
- NextChat (the OpenAI-compatible chat UI used with the .NET agent) does **not** — it only speaks
  chat-completions. Point NextChat at a running `KubernetesAiAgent.NetAgent` instance instead.

## Configuration

Same shape and env-var spelling as the .NET agent, loaded in the same order: `agentconfig.yaml` ->
`agentconfig.{ENVIRONMENT}.yaml` -> environment variables (win). See `kubernetes_agent/config.py`.

- `ENVIRONMENT` (or `ASPNETCORE_ENVIRONMENT` as a fallback) selects the environment-specific YAML file and
  gates `/health`/`/alive` registration, the same way the .NET agent's ASP.NET Core environment does.
- `Agent__ModelConnection__Endpoint` / `Agent__ModelConnection__Model` / `Agent__ModelConnection__ApiKey`
  — the OpenAI-compatible model provider (OpenRouter by default). `ApiKey` is required and must never be
  committed to `agentconfig.yaml`.
- `Agent__McpServers` — the MCP servers to discover read-only tools from. **Difference from .NET**:
  pydantic-settings has no equivalent of ASP.NET Core's indexed array env vars
  (`Agent__McpServers__0__Name`). Supply the whole list as one JSON value instead:
  ```bash
  export Agent__McpServers='[{"Name":"k8s-mcp","Endpoint":"http://localhost:8080/mcp"}]'
  ```
  In `agentconfig*.yaml` files, use normal YAML list syntax (see `agentconfig.Development.yaml`).

Four fields on the .NET `AgentOptions` are intentionally **not** ported: `MaxToolCalls`,
`RequireApiKey`/`ApiKey`, `RequestTimeoutSeconds`, `MaxConversationMessages`. None of them are enforced
anywhere in the .NET agent's code either — they're dead configuration, and porting dead configuration would
just carry the debt into a second implementation.

## Running standalone

```bash
uv sync
export Agent__ModelConnection__ApiKey="<your OpenRouter key>"
uv run main.py
```

Binds to `http://0.0.0.0:$PORT` (default `5192`, matching the .NET agent's pinned dev port and the
webui's dev fallback URL).

## Tests

```bash
uv run pytest
```

Mirrors `KubernetesAiAgent.Tests`: config validation (`tests/python/test_config.py`), boot with an
unreachable MCP server (`tests/python/test_agent_factory.py`), and the health endpoints
(`tests/python/test_health.py`). Tests live in the repo's top-level `tests/python/` directory rather than
under this project (see `pyproject.toml`'s `testpaths`), so the same suite sits alongside the .NET tests
under `tests/`. Tests never reach a real OpenRouter endpoint or a real MCP server — see
`tests/python/conftest.py`.

## Not orchestrated by the Aspire AppHost

The AppHost starts `KubernetesAiAgent.NetAgent`, not this agent (see the repository root CLAUDE.md "Which
agent runs") — run this project standalone (above) if you need it. It's kept buildable and tested for future
development, but isn't wired into local Aspire dev or the `k8s/` deployment.
