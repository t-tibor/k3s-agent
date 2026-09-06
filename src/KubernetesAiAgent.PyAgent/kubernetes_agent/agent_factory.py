"""Composes the real Kubernetes agent from :class:`~kubernetes_agent.config.AgentOptions`: an OpenAI-
compatible chat client (docs/ARCHITECTURE.md §6) plus the read-only tools discovered from every configured
MCP server (docs/ARCHITECTURE.md §7). Port of
``../KubernetesAiAgent.NetAgent/Agent/KubernetesAgentFactory.cs``.

Connecting to every configured MCP server is attempted at most once, at startup, exactly like the .NET
factory — there's no separate memoized-connector indirection a hot path would require. Each server is
connected to over Streamable HTTP with a short timeout and degrades gracefully (logs a warning, contributes
no tools) rather than raising if it's unreachable — readiness and error handling must not hard-depend on
this external dependency (docs/ARCHITECTURE.md §12, §16).
"""

from __future__ import annotations

import asyncio
import logging
from contextlib import AsyncExitStack
from dataclasses import dataclass

from agent_framework import Agent, MCPStreamableHTTPTool
from agent_framework.openai import OpenAIChatCompletionClient

from kubernetes_agent.config import AgentOptions, McpServerOptions

logger = logging.getLogger(__name__)

MCP_CONNECT_TIMEOUT_SECONDS = 5.0


@dataclass(frozen=True)
class ConnectedMcp:
    tools: list[MCPStreamableHTTPTool]
    failures: list[tuple[str, str]]


async def _discover_tools(
    servers: list[McpServerOptions], exit_stack: AsyncExitStack
) -> ConnectedMcp:
    """Connects to every configured MCP server independently and aggregates their read-only tools. A
    server that fails (unreachable, blank endpoint, timeout, etc.) doesn't take the others down — it's
    recorded as a failure instead so :func:`build_instructions` can tell the model about the gap.
    Successful connections are entered into ``exit_stack`` so they stay alive (and get cleanly closed) for
    the caller's lifetime, mirroring how the .NET factory keeps its ``McpClient`` instances alive and
    disposes them alongside itself.
    """
    tools: list[MCPStreamableHTTPTool] = []
    failures: list[tuple[str, str]] = []

    for server in servers:
        if not server.Endpoint or not server.Endpoint.strip():
            logger.warning(
                "MCP server %s has no endpoint configured; it will contribute no tools.", server.Name
            )
            failures.append((server.Name, "no endpoint configured"))
            continue

        tool = MCPStreamableHTTPTool(name=server.Name, url=server.Endpoint)
        try:
            await asyncio.wait_for(
                exit_stack.enter_async_context(tool), timeout=MCP_CONNECT_TIMEOUT_SECONDS
            )
            # __aenter__ calls connect(), which (with the default load_tools=True) already loads the
            # server's tool list into `.functions` — no separate list-tools round trip needed.
            tool_count = len(tool.functions)
            tools.append(tool)
            logger.info(
                "Connected to MCP server %s at %s: %d tools discovered and exposed to the agent.",
                server.Name,
                server.Endpoint,
                tool_count,
            )
        except Exception as ex:  # noqa: BLE001 - deliberately broad, matches the .NET catch-all
            logger.warning(
                "Could not connect to MCP server %s at %s; it will contribute no tools.",
                server.Name,
                server.Endpoint,
                exc_info=ex,
            )
            failures.append((server.Name, str(ex)))

    return ConnectedMcp(tools=tools, failures=failures)


def build_instructions(system_prompt: str, failures: list[tuple[str, str]]) -> str:
    """Appends a note listing any MCP servers that couldn't be reached, so the model can honestly tell the
    user it lacks a capability instead of silently having fewer tools than the config implies.
    """
    if not failures:
        return system_prompt

    unavailable = ", ".join(f"{name} ({reason})" for name, reason in failures)
    return (
        f"{system_prompt}\n\n"
        f"Note: the following data sources are currently unavailable and their information cannot be "
        f"retrieved: {unavailable}."
    )


async def create_kubernetes_agent(
    options: AgentOptions, exit_stack: AsyncExitStack
) -> Agent:
    """Builds the chat client, connects to every configured MCP server, and returns the agent. Call this
    once at startup; ``exit_stack`` should be owned by the caller (the app lifespan) and closed on
    shutdown so the MCP connections this creates are cleanly torn down.
    """
    connected = await _discover_tools(options.McpServers, exit_stack)

    chat_client = OpenAIChatCompletionClient(
        model=options.ModelConnection.Model,
        api_key=options.ModelConnection.ApiKey,
        base_url=options.ModelConnection.Endpoint,
    )

    return Agent(
        client=chat_client,
        name=options.ModelId,
        instructions=build_instructions(options.SystemPrompt, connected.failures),
        tools=connected.tools,
    )
