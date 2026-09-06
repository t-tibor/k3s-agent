# Production image for KubernetesAiAgent.NetAgent (docs/ARCHITECTURE.md §17). Single image: the .csproj's
# PublishWebUI MSBuild target (see KubernetesAiAgent.NetAgent.csproj) builds the sibling
# KubernetesAiAgent.WebUI (Vite/React) project as part of `dotnet publish` and folds its output into
# wwwroot, so this one container serves both the SPA and the /agui API from one origin — no separate
# webui image/Deployment, no CORS needed in production (docs/ARCHITECTURE.md §3.4).
#
# Build with `dotnet/` (this file's parent directory) as context, since the .csproj also needs its sibling
# KubernetesAiAgent.ServiceDefaults project directory:
#   docker build -f dotnet/KubernetesAiAgent.NetAgent/Dockerfile -t k3s-agent/kubernetes-agent:latest dotnet

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# The SDK image has no Node.js — install it the same way Microsoft's own ASP.NET Core + React template
# Dockerfile does, since there's no official combined SDK+Node image.
RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY KubernetesAiAgent.ServiceDefaults/ KubernetesAiAgent.ServiceDefaults/
COPY KubernetesAiAgent.WebUI/ KubernetesAiAgent.WebUI/
COPY KubernetesAiAgent.NetAgent/ KubernetesAiAgent.NetAgent/

RUN dotnet restore KubernetesAiAgent.NetAgent/KubernetesAiAgent.NetAgent.csproj

# Triggers the .csproj's PublishWebUI target (npm ci + npm run build in ../KubernetesAiAgent.WebUI), which
# copies the built SPA into wwwroot alongside the published API.
RUN dotnet publish KubernetesAiAgent.NetAgent/KubernetesAiAgent.NetAgent.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

# Agent__ModelConnection__ApiKey (and any other secret) must be injected at deploy time — never baked into
# the image (docs/ARCHITECTURE.md §9.1, §20 design principle 9).

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# mcr.microsoft.com/dotnet/aspnet defines (but doesn't default to) a non-root "app" user via the $APP_UID
# build arg — this image runs as root unless told otherwise. `docker run --rm --entrypoint id <image>`
# confirms the actual UID if k8s/agent-deployment.yaml's securityContext.runAsUser is ever pinned instead
# of just runAsNonRoot.
USER $APP_UID

ENTRYPOINT ["dotnet", "KubernetesAiAgent.NetAgent.dll"]
