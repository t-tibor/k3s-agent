# Production image for the custom AG-UI frontend (docs/ARCHITECTURE.md §3.4, §17).
#
# VITE_AGENT_URL is a Vite *build-time* env var — it gets baked into the compiled JS bundle, not read at
# container runtime (this frontend calls the agent directly from browser JavaScript, so there is no
# server-side process to hand it a runtime env var to). Leave it unset to keep the code's own fallback
# (http://localhost:5192) baked in, which lines up with the port-forward workflow described in
# k8s/README.md; override with --build-arg only if you have a stable, browser-reachable agent URL at
# build time (e.g. behind an Ingress).
#
# Build from the repo root so the build context can reach package.json/source:
#   docker build -f src/frontend/Dockerfile -t k3s-agent/webui:latest src/frontend

FROM node:22-slim AS build

WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

ARG VITE_AGENT_URL
ENV VITE_AGENT_URL=$VITE_AGENT_URL

RUN npm run build

# nginx-unprivileged already runs as a non-root user (uid 101) and listens on 8080 by default —
# no extra USER/permission wrangling needed to satisfy the "non-root execution" requirement (§17).
FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
