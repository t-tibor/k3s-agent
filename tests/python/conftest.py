"""Test fixtures. Plays the role of ``KubernetesAgentTestFactory``
(../dotnet/KubernetesAiAgent.Tests/KubernetesAgentTestFactory.cs): sets environment variables *before* any
test loads :class:`~kubernetes_agent.config.AgentOptions`, so tests never reach real OpenRouter or a real
MCP server — the MCP endpoint is a deliberately dead port, exercising the graceful-degradation path.
"""

from __future__ import annotations

import pytest

TEST_MODEL = "test-model"
TEST_API_KEY = "test-key"
TEST_MCP_NAME = "test-mcp"
TEST_MCP_ENDPOINT = "http://127.0.0.1:59999"


@pytest.fixture
def agent_test_env(monkeypatch: pytest.MonkeyPatch) -> None:
    """Sets the same configuration shape ``KubernetesAgentTestFactory`` uses: a valid model connection and
    one MCP server pointed at an unreachable address.
    """
    monkeypatch.setenv("ENVIRONMENT", "Development")
    monkeypatch.setenv("Agent__ModelConnection__Model", TEST_MODEL)
    monkeypatch.setenv("Agent__ModelConnection__ApiKey", TEST_API_KEY)
    monkeypatch.setenv(
        "Agent__McpServers",
        f'[{{"Name":"{TEST_MCP_NAME}","Endpoint":"{TEST_MCP_ENDPOINT}"}}]',
    )
