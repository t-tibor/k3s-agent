"""OpenTelemetry wiring — the Python stand-in for the OTLP-exporter half of
``KubernetesAiAgent.ServiceDefaults/Extensions.cs``. Aspire injects ``OTEL_EXPORTER_OTLP_ENDPOINT`` (and
friends) into the process environment; when it isn't set (e.g. running ``uv run main.py`` standalone) this
is a no-op, mirroring the same guard in the .NET ServiceDefaults
(../../KubernetesAiAgent.ServiceDefaults/Extensions.cs#L84).

Service discovery and the HTTP-client resilience handler from ``AddServiceDefaults`` have no equivalent
here — the agent doesn't call other Aspire-managed HTTP services (only the model provider and MCP servers,
both externally configured), so there's nothing to wire.
"""

from __future__ import annotations

import logging
import os

logger = logging.getLogger(__name__)

_HEALTH_PATHS = ("/health", "/alive")


def configure_telemetry() -> None:
    """No-ops unless ``OTEL_EXPORTER_OTLP_ENDPOINT`` is set. When it is, configures the OTLP exporters
    (agent_framework's own configure_otel_providers reads the standard OTEL_EXPORTER_OTLP_* env vars
    Aspire sets) and instruments FastAPI + httpx, excluding health-check paths from tracing — matching the
    .NET tracing filter.
    """
    if not os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT", "").strip():
        return

    from agent_framework.observability import configure_otel_providers
    from opentelemetry.instrumentation.httpx import HTTPXClientInstrumentor

    configure_otel_providers()
    HTTPXClientInstrumentor().instrument()
    logger.info("OpenTelemetry OTLP export enabled.")


def instrument_app(app) -> None:  # noqa: ANN001 - avoids a hard FastAPI import at module load time
    """Instruments the FastAPI app for tracing. Split from :func:`configure_telemetry` because it needs
    the app instance, which only exists once :func:`kubernetes_agent.app.create_app` has built it.
    """
    if not os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT", "").strip():
        return

    from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor

    FastAPIInstrumentor.instrument_app(
        app, excluded_urls=",".join(path.lstrip("/") for path in _HEALTH_PATHS)
    )
