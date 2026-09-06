"""The agent's single config root — model-connection settings, agent-level behavior, and the MCP servers
to discover tools from. Port of ``Configuration/AgentOptions.cs``, ``ModelConnectionOptions.cs``, and
``McpServerOptions.cs`` (../KubernetesAiAgent.NetAgent/Configuration/) — field names and defaults are kept
identical so ``agentconfig.yaml`` is shared, byte-for-byte, between the .NET and Python agents.

Precedence mirrors ASP.NET Core's configuration chain in ``Program.cs``: ``agentconfig.yaml`` ->
``agentconfig.{ENVIRONMENT}.yaml`` -> environment variables (win). Environment variables use the same
double-underscore nesting the .NET agent and the Aspire AppHost already use
(``Agent__ModelConnection__ApiKey``). One deliberate difference: pydantic-settings has no equivalent of
ASP.NET Core's indexed-array env vars (``Agent__McpServers__0__Name``) — a list-valued env var must be
supplied as a single JSON value instead, e.g.
``Agent__McpServers='[{"Name":"k8s-mcp","Endpoint":"http://localhost:8080/mcp"}]'``.

Four fields that exist on the .NET ``AgentOptions`` are intentionally dropped here: ``MaxToolCalls``,
``RequireApiKey``/``ApiKey``, ``RequestTimeoutSeconds``, and ``MaxConversationMessages``. None of them are
enforced anywhere in the .NET agent's code either (docs/ARCHITECTURE.md notes this) — porting dead
configuration would just carry the debt over into a second implementation.
"""

from __future__ import annotations

import os
from pathlib import Path

from pydantic import BaseModel, Field, field_validator, model_validator
from pydantic_settings import (
    BaseSettings,
    PydanticBaseSettingsSource,
    SettingsConfigDict,
    YamlConfigSettingsSource,
)

from kubernetes_agent import instructions

_AGENT_DIR = Path(__file__).resolve().parent.parent


class McpServerOptions(BaseModel):
    """Connection settings for one MCP server the agent should discover read-only tools from
    (docs/ARCHITECTURE.md §7). Multiple can be configured; each is connected to independently, and one
    being unreachable does not take the others down — see ``agent_factory.py``.
    """

    Name: str = Field(default="")
    """Identifies this server in logs and, if it can't be reached, in the note appended to the agent's
    instructions so it can explain the gap to the user."""

    Endpoint: str | None = None
    """Streamable HTTP endpoint. Blank/missing means this entry is not actually configured — treated the
    same as an unreachable server (graceful degradation, not a startup failure)."""


class ModelConnectionOptions(BaseModel):
    """Connection settings for the OpenAI-compatible chat model provider (docs/ARCHITECTURE.md §6.2).
    Currently OpenRouter, but kept generic (not vendor-named) so the provider stays swappable via
    configuration alone.
    """

    Endpoint: str = "https://openrouter.ai/api/v1"
    """The OpenAI-compatible base URL, e.g. OpenRouter's."""

    Model: str = "deepseek/deepseek-v4-flash-0731"
    """The model slug to use. OpenRouter's catalog changes fairly often — verify against
    https://openrouter.ai/models if this needs to move."""

    ApiKey: str | None = Field(default=None, validate_default=True)
    """Secret. Supply via an environment variable or a Kubernetes Secret — never agentconfig.yaml."""

    @field_validator("Endpoint", "Model")
    @classmethod
    def _required_non_blank(cls, value: str, info) -> str:
        if not value or not value.strip():
            raise ValueError(f"ModelConnection.{info.field_name} is required.")
        return value

    @field_validator("ApiKey")
    @classmethod
    def _api_key_required(cls, value: str | None) -> str:
        if not value or not value.strip():
            raise ValueError("ModelConnection.ApiKey is required.")
        return value


def environment_name() -> str:
    """The ASP.NET Core-style environment name (Development/Production/...), read the same way the .NET
    agent's Aspire host and Kestrel would: ``ENVIRONMENT`` first (what Aspire's Python hosting sets),
    falling back to ``ASPNETCORE_ENVIRONMENT`` for parity when running the two agents side by side.
    """
    return os.environ.get("ENVIRONMENT") or os.environ.get("ASPNETCORE_ENVIRONMENT") or "Production"


class AgentOptions(BaseSettings):
    """Bound from ``agentconfig.yaml`` (docs/ARCHITECTURE.md §10). Kept as one object so the whole thing
    can later be reloaded from a mounted Kubernetes ConfigMap as a unit.
    """

    model_config = SettingsConfigDict(
        env_prefix="Agent__",
        env_nested_delimiter="__",
        case_sensitive=False,
        extra="ignore",
    )

    ModelId: str = Field(default="kubernetes-agent")
    """The model id this agent answers to (docs/ARCHITECTURE.md §8.1). Not exposed over an OpenAI-
    compatible /v1/models endpoint here (PyAgent is AG-UI only), but still identifies the AG-UI agent."""

    SystemPrompt: str = Field(default_factory=lambda: instructions.DEFAULT)

    ModelConnection: ModelConnectionOptions = Field(default_factory=ModelConnectionOptions)
    """How to reach the chat model provider (docs/ARCHITECTURE.md §6.2)."""

    McpServers: list[McpServerOptions] = Field(default_factory=list)
    """The MCP servers to discover read-only tools from (docs/ARCHITECTURE.md §7). May be empty — the
    agent then simply has no tools."""

    @field_validator("ModelId")
    @classmethod
    def _model_id_required(cls, value: str) -> str:
        if not value or not value.strip():
            raise ValueError("ModelId is required.")
        return value

    @model_validator(mode="after")
    def _mcp_servers_valid(self) -> "AgentOptions":
        """Cascades the same structural checks as the .NET ``AgentOptions.Validate``: every MCP server
        needs a non-blank ``Name``, and names must be unique (case-insensitively). These are structural
        config mistakes and fail startup; an MCP server being unreachable at runtime is handled
        separately in ``agent_factory.py`` and does not fail startup (docs/ARCHITECTURE.md §12, §16).
        """
        seen: set[str] = set()
        for i, server in enumerate(self.McpServers):
            if not server.Name or not server.Name.strip():
                raise ValueError(f"McpServers[{i}].Name is required.")
            key = server.Name.casefold()
            if key in seen:
                raise ValueError(
                    f'McpServers contains more than one entry named "{server.Name}"; names must be unique.'
                )
            seen.add(key)
        return self

    @classmethod
    def settings_customise_sources(
        cls,
        settings_cls: type[BaseSettings],
        init_settings: PydanticBaseSettingsSource,
        env_settings: PydanticBaseSettingsSource,
        dotenv_settings: PydanticBaseSettingsSource,
        file_secret_settings: PydanticBaseSettingsSource,
    ) -> tuple[PydanticBaseSettingsSource, ...]:
        environment = environment_name()
        sources: list[PydanticBaseSettingsSource] = [init_settings, env_settings, dotenv_settings]

        # Mirrors ASP.NET Core's AddYamlFile(..., optional: true): a missing file contributes nothing
        # rather than failing. YamlConfigSettingsSource's yaml_config_section, unlike AddYamlFile, raises
        # KeyError outright when the file doesn't exist (rather than treating it as empty), so each file's
        # presence is checked here before the source is added.
        for candidate in (
            _AGENT_DIR / f"agentconfig.{environment}.yaml",
            _AGENT_DIR / "agentconfig.yaml",
        ):
            if candidate.is_file():
                sources.append(
                    YamlConfigSettingsSource(
                        settings_cls, yaml_file=str(candidate), yaml_config_section="Agent"
                    )
                )

        sources.append(file_secret_settings)
        return tuple(sources)


def load_agent_options() -> AgentOptions:
    """Loads and validates :class:`AgentOptions`. Raises ``pydantic.ValidationError`` on any structural
    config mistake — call this eagerly at startup, the equivalent of ASP.NET Core's ``ValidateOnStart``.
    """
    return AgentOptions()
