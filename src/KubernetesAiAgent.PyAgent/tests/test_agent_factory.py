"""Boot-with-unreachable-MCP graceful degradation. Python equivalent of the implicit assertion in
``ModelsEndpointTests.cs`` (../../KubernetesAiAgent.Tests/Api/) that the .NET agent starts successfully
with a dead MCP endpoint configured.
"""

from __future__ import annotations

from contextlib import AsyncExitStack

import pytest

from kubernetes_agent.config import load_agent_options
from kubernetes_agent.agent_factory import create_kubernetes_agent

pytestmark = pytest.mark.usefixtures("agent_test_env")


async def test_agent_builds_despite_unreachable_mcp_server() -> None:
    options = load_agent_options()

    async with AsyncExitStack() as exit_stack:
        agent = await create_kubernetes_agent(options, exit_stack)

        assert agent.name == options.ModelId
        # The dead MCP server contributes no tools rather than failing startup.
        instructions = agent.default_options["instructions"]
        assert "test-mcp" in instructions
        assert "currently unavailable" in instructions
