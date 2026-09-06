"""``/health`` and ``/alive``. Mirrors the behavior documented on
``AgentConfigurationHealthCheck``/``ModelConnectionConfigurationHealthCheck``
(../../KubernetesAiAgent.NetAgent/Health/) and the implicit boot assertion in ``ModelsEndpointTests.cs``.

Configuration-presence checks are exercised two ways here: end-to-end through the real app for the
happy path (:func:`kubernetes_agent.app.create_app` already validates ``AgentOptions`` eagerly, so an
actually-invalid config can never reach a running app — same as .NET's ``ValidateOnStart``), and directly
against :func:`~kubernetes_agent.health.build_health_router` with a hand-built, validation-bypassed
``AgentOptions`` for the unhealthy path, the way it could legitimately occur if a mounted ConfigMap's
value drifted after startup.
"""

from __future__ import annotations

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from kubernetes_agent.app import create_app
from kubernetes_agent.config import AgentOptions, ModelConnectionOptions
from kubernetes_agent.health import build_health_router

pytestmark = pytest.mark.usefixtures("agent_test_env")


def test_alive_is_always_healthy() -> None:
    with TestClient(create_app()) as client:
        response = client.get("/alive")
    assert response.status_code == 200


def test_health_is_healthy_with_valid_configuration() -> None:
    with TestClient(create_app()) as client:
        response = client.get("/health")
    assert response.status_code == 200


def test_health_is_unhealthy_without_api_key() -> None:
    # model_construct bypasses AgentOptions/ModelConnectionOptions validation so a blank ApiKey — which
    # AgentOptions itself would normally reject at construction — can reach the health check directly.
    options = AgentOptions.model_construct(
        ModelId="kubernetes-agent",
        SystemPrompt="",
        ModelConnection=ModelConnectionOptions.model_construct(
            Endpoint="https://openrouter.ai/api/v1", Model="model", ApiKey=None
        ),
        McpServers=[],
    )

    app = FastAPI()
    app.include_router(build_health_router(options))

    with TestClient(app) as client:
        response = client.get("/health")

    assert response.status_code == 503
