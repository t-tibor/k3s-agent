"""Config validation. Mirrors ``AgentOptionsTests.cs`` and ``ModelConnectionOptionsTests.cs``
(../dotnet/KubernetesAiAgent.Tests/Configuration/).
"""

from __future__ import annotations

import pytest
from pydantic import ValidationError

from kubernetes_agent.config import AgentOptions, McpServerOptions, ModelConnectionOptions


def _valid_connection() -> ModelConnectionOptions:
    return ModelConnectionOptions(
        Endpoint="https://openrouter.ai/api/v1", Model="deepseek/deepseek-v4-flash-0731", ApiKey="key"
    )


def test_valid_options_pass() -> None:
    options = AgentOptions(
        ModelId="kubernetes-agent",
        ModelConnection=_valid_connection(),
        McpServers=[McpServerOptions(Name="k8s-mcp", Endpoint="http://localhost:8080/mcp")],
    )
    assert options.ModelId == "kubernetes-agent"


def test_blank_model_id_fails() -> None:
    with pytest.raises(ValidationError, match="ModelId is required"):
        AgentOptions(ModelId="", ModelConnection=_valid_connection())


def test_null_api_key_fails() -> None:
    with pytest.raises(ValidationError, match="ApiKey is required"):
        AgentOptions(
            ModelId="kubernetes-agent",
            ModelConnection=ModelConnectionOptions(
                Endpoint="https://openrouter.ai/api/v1", Model="model", ApiKey=None
            ),
        )


def test_mcp_server_blank_name_fails() -> None:
    with pytest.raises(ValidationError, match="Name is required"):
        AgentOptions(
            ModelId="kubernetes-agent",
            ModelConnection=_valid_connection(),
            McpServers=[McpServerOptions(Name="", Endpoint="http://localhost:8080/mcp")],
        )


def test_duplicate_mcp_server_names_fail() -> None:
    with pytest.raises(ValidationError, match="more than one entry"):
        AgentOptions(
            ModelId="kubernetes-agent",
            ModelConnection=_valid_connection(),
            McpServers=[
                McpServerOptions(Name="k8s-mcp", Endpoint="http://localhost:8080/mcp"),
                # Differs only in case — must still be rejected as a duplicate.
                McpServerOptions(Name="K8S-MCP", Endpoint="http://localhost:8081/mcp"),
            ],
        )


def test_model_connection_defaults_fail_without_api_key() -> None:
    with pytest.raises(ValidationError, match="ApiKey is required"):
        ModelConnectionOptions()


def test_model_connection_with_api_key_passes() -> None:
    connection = ModelConnectionOptions(ApiKey="key")
    assert connection.ApiKey == "key"


def test_model_connection_blank_model_fails() -> None:
    with pytest.raises(ValidationError, match="Model is required"):
        ModelConnectionOptions(Model="", ApiKey="key")


def test_model_connection_blank_endpoint_fails() -> None:
    with pytest.raises(ValidationError, match="Endpoint is required"):
        ModelConnectionOptions(Endpoint="", ApiKey="key")
