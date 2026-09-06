# Production image for KubernetesAiAgent.PyAgent (docs/ARCHITECTURE.md §17 — the containerization
# requirements were written for the .NET agent but apply equally here: multi-stage, non-root, minimal
# runtime image, no secrets baked in, configurable port, health surface reachable by k8s probes).
#
# Build from the repo root so the build context can reach uv.lock/pyproject.toml/app code:
#   docker build -f src/backend/python/KubernetesAiAgent.PyAgent/Dockerfile -t k3s-agent/kubernetes-agent:latest \
#     src/backend/python/KubernetesAiAgent.PyAgent

FROM python:3.12-slim AS build

# uv manages the venv the same way `uv sync` does locally (CLAUDE.md "Commands").
COPY --from=ghcr.io/astral-sh/uv:0.9.5 /uv /uvx /usr/local/bin/

WORKDIR /app

ENV UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_PYTHON_DOWNLOADS=never

# Install dependencies first (cached layer, independent of app code changes).
COPY pyproject.toml uv.lock ./
RUN uv sync --locked --no-install-project --no-dev

# Now add the application source and install the project itself.
COPY main.py agentconfig.yaml README.md ./
COPY kubernetes_agent ./kubernetes_agent
RUN uv sync --locked --no-dev

FROM python:3.12-slim AS runtime

RUN groupadd --gid 1000 appuser && \
    useradd --uid 1000 --gid appuser --shell /usr/sbin/nologin --no-create-home appuser

WORKDIR /app
COPY --from=build /app /app

ENV PATH="/app/.venv/bin:$PATH" \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PORT=5192

# Agent__ModelConnection__ApiKey (and any other secret) must be injected at deploy time — never baked
# into the image (docs/ARCHITECTURE.md §9.1, §20 design principle 9).

USER appuser
EXPOSE 5192

ENTRYPOINT ["python", "main.py"]
