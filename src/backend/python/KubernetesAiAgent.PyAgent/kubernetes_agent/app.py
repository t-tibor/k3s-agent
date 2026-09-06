"""FastAPI application factory. Port of ``../KubernetesAiAgent.NetAgent/Program.cs``, minus the parts that
have no Python-MAF equivalent: ``/v1/chat/completions`` (no server-side OpenAI-compatible hosting exists
in Microsoft Agent Framework for Python — see the package README this project's plan references) and
``/v1/models`` (which exists only to support that endpoint's model-id validation). This agent exposes only
the AG-UI protocol, at ``/agui``, plus the same ``/health``/``/alive`` endpoints.
"""

from __future__ import annotations

import logging
from contextlib import AsyncExitStack, asynccontextmanager

from agent_framework_ag_ui import add_agent_framework_fastapi_endpoint
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from kubernetes_agent.agent_factory import create_kubernetes_agent
from kubernetes_agent.config import environment_name, load_agent_options
from kubernetes_agent.health import register_health_routes
from kubernetes_agent.telemetry import configure_telemetry, instrument_app

logger = logging.getLogger(__name__)


def create_app() -> FastAPI:
    logging.basicConfig(level=logging.INFO)
    configure_telemetry()

    options = load_agent_options()
    environment = environment_name()

    # Owns the (fallible, async) connections to every configured MCP server for the process lifetime,
    # mirroring the .NET factory's IAsyncDisposable — each connected McpClient/MCPStreamableHTTPTool is
    # kept alive so its tools can actually be invoked later, and closed cleanly on shutdown.
    mcp_exit_stack = AsyncExitStack()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        async with mcp_exit_stack:
            agent = await create_kubernetes_agent(options, mcp_exit_stack)

            # AG-UI (https://ag-ui.com) endpoint: event-streamed run/state updates over SSE, matching the
            # .NET agent's app.MapAGUIServer("/agui", kubernetesAgent). Registered here, inside lifespan
            # startup, because building the agent (connecting to MCP servers) is async and must happen
            # before the route can be wired to a real agent instance.
            add_agent_framework_fastapi_endpoint(app, agent, "/agui")

            yield

    app = FastAPI(title="Kubernetes AI Agent (Python)", lifespan=lifespan)

    # No forced HTTPS redirect: in local dev, frontends are pointed at this API's HTTP endpoint to avoid
    # the browser rejecting a dev cert (docs/ARCHITECTURE.md §3.2) — redirecting would break that.

    # The custom AG-UI frontend (src/frontend) calls this API directly from client-side JavaScript, so CORS
    # must allow it. No credentials are sent cross-origin, so allowing any origin is safe — mirrors the
    # .NET agent's FrontendCorsPolicy exactly. allow_credentials=False is required to pair with "*".
    app.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=False,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    register_health_routes(app, options, environment)
    instrument_app(app)

    return app
