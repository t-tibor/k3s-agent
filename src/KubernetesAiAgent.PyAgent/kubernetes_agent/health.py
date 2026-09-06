"""Readiness/liveness endpoints. Port of ``Health/AgentConfigurationHealthCheck.cs``,
``Health/ModelConnectionConfigurationHealthCheck.cs``, and the health-check registration in
``../../KubernetesAiAgent.ServiceDefaults/Extensions.cs`` (``MapDefaultEndpoints``).

Deliberately checks configuration *presence*, not connectivity — it does not call OpenRouter or any MCP
server (docs/ARCHITECTURE.md §12). Registered only when running under the Development environment, exactly
like the .NET ``MapDefaultEndpoints`` gate.
"""

from __future__ import annotations

from fastapi import APIRouter, FastAPI
from fastapi.responses import JSONResponse

from kubernetes_agent.config import AgentOptions


def _agent_configuration_check(options: AgentOptions) -> str | None:
    """Mirrors AgentConfigurationHealthCheck: the agent has the minimum configuration required to serve
    requests. Returns None when healthy, or the failure reason.
    """
    if not options.ModelId or not options.ModelId.strip():
        return "Agent:ModelId is not configured."
    return None


def _model_connection_configuration_check(options: AgentOptions) -> str | None:
    """Mirrors ModelConnectionConfigurationHealthCheck: model-connection configuration is present."""
    connection = options.ModelConnection
    if not connection.Model or not connection.Model.strip() or not connection.ApiKey or not connection.ApiKey.strip():
        return (
            "Agent:ModelConnection:Model and Agent:ModelConnection:ApiKey must both be configured."
        )
    return None


def build_health_router(options: AgentOptions) -> APIRouter:
    router = APIRouter()

    @router.get("/alive")
    async def alive() -> JSONResponse:
        # The "self" liveness check in ServiceDefaults: always healthy once the process is up.
        return JSONResponse({"status": "Healthy"})

    @router.get("/health")
    async def health() -> JSONResponse:
        failures = [
            reason
            for reason in (
                _agent_configuration_check(options),
                _model_connection_configuration_check(options),
            )
            if reason is not None
        ]
        if failures:
            return JSONResponse({"status": "Unhealthy", "reasons": failures}, status_code=503)
        return JSONResponse({"status": "Healthy"})

    return router


def register_health_routes(app: FastAPI, options: AgentOptions, environment: str) -> None:
    if environment != "Development":
        return
    app.include_router(build_health_router(options))
