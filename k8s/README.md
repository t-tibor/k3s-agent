# Kubernetes manifests

Deploys `KubernetesAiAgent.NetAgent` — the backend the Aspire AppHost runs, and the image the production
Docker build produces (docs/ARCHITECTURE.md §16) — to the `k3s-agent` namespace. Its container also serves
the built `KubernetesAiAgent.WebUI` frontend (docs/ARCHITECTURE.md §3.4), so there's a single Deployment and
Service for both the UI and the API. Assumes a k3s cluster with no external registry — images are built
locally and imported directly into k3s's containerd. Assumes the Kubernetes MCP server(s) referenced in the
ConfigMap (see [agent-configmap.example.yaml](agent-configmap.example.yaml)) are already deployed elsewhere
in the cluster (per the task this was written for).

Not included: `KubernetesAiAgent.PyAgent` and NextChat — neither is part of this deployment (see
[../CLAUDE.md](../CLAUDE.md)). PyAgent remains runnable standalone (`uv run main.py` from
`python/KubernetesAiAgent.PyAgent`) but isn't AppHost-orchestrated or deployed here.

## 1. Build the image

From the repo root:

```bash
docker build -f dotnet/KubernetesAiAgent.NetAgent/Dockerfile -t k3s-agent/kubernetes-agent:latest dotnet
```

This single image builds the frontend (via the `.csproj`'s publish-time MSBuild target — needs Node.js,
installed inside the build stage) and the .NET backend, and serves both from one container. No build-time
configuration is needed for the frontend — it always calls a same-origin, relative `/agui`.

## 2. Import the image into k3s

k3s's embedded containerd doesn't share Docker's image store, so an image built with `docker build` needs to
be imported explicitly:

```bash
docker save k3s-agent/kubernetes-agent:latest | sudo k3s ctr images import -
```

(Run this on each node that will schedule this pod, or use a shared registry if you have one — the
manifest uses `imagePullPolicy: IfNotPresent`, so nothing will attempt a network pull once the image is
present locally.)

## 3. Apply the manifests

`kustomization.yaml` in this directory pins `agent-deployment.yaml`'s image tag — see "CI-built images"
below if you're pulling from GHCR instead of building locally. For the local/import flow, `kubectl apply -k
k8s/` is equivalent to the individual `apply -f` calls below plus the pinned image, minus the ConfigMap and
`agent-secret.example.yaml` (both deliberately not `kustomization.yaml` resources — see their own
comments), which still need to be created separately either way:

```bash
kubectl apply -f k8s/namespace.yaml

# Real ConfigMap, not the committed template — copy agent-configmap.example.yaml to
# agent-configmap.yaml (gitignored), point McpServers at your real endpoint(s), and apply that. This one
# is managed by hand in the cluster going forward, not redeployed from CI or `kubectl apply -k`.
cp k8s/agent-configmap.example.yaml k8s/agent-configmap.yaml
# edit k8s/agent-configmap.yaml, then:
kubectl apply -f k8s/agent-configmap.yaml

# Real secret, not the committed template — see the comments in agent-secret.example.yaml.
kubectl create secret generic kubernetes-agent-secrets \
  --namespace k3s-agent \
  --from-literal=openrouter-api-key='<your OpenRouter API key>' \
  --from-literal=dashboard-otlp-api-key="$(openssl rand -hex 32)"

kubectl apply -f k8s/agent-deployment.yaml -f k8s/agent-service.yaml
```

`kubectl rollout status deployment/kubernetes-agent -n k3s-agent` to confirm it comes up.

## 4. Reach the UI

No Ingress is included (ClusterIP only, by design for this deployment — see docs/ARCHITECTURE.md §9.2 for
the equivalent network-boundary reasoning). Port-forward the one Service:

```bash
kubectl port-forward -n k3s-agent svc/kubernetes-agent 8080:8080
```

Then open `http://localhost:8080` — the page and its `/agui` calls both come from that same origin, so a
single port-forward is all this needs.

## 5. Observability (Aspire dashboard)

`agent-deployment.yaml` also runs the Aspire dashboard as a sidecar in the same Pod (not deployed as its
own resource). `kubernetes-agent` exports OpenTelemetry traces, logs, and metrics to it over `localhost`,
and proxies the dashboard UI itself through its own `/dashboard` path (see
`dotnet/KubernetesAiAgent.NetAgent/Program.cs`) — no extra Service or port-forward needed beyond the one
above. Open `http://localhost:8080/dashboard`.

The dashboard's login page asks for a browser token, auto-generated fresh on every pod (re)start — never
stored as a Secret. Retrieve the current one from the sidecar's own logs:

```bash
kubectl logs -n k3s-agent deployment/kubernetes-agent -c aspire-dashboard | grep -i "login?t="
```

**Storage and retention**: the dashboard keeps everything in memory only — there's no volume for it, and a
pod restart (or `kubectl rollout restart`) discards all previously collected telemetry. It's a live
debugging view, not a durable telemetry store; point `OTEL_EXPORTER_OTLP_ENDPOINT` at a real backend
(Tempo/Loki/Prometheus, an OTel Collector, Azure Monitor, etc.) instead if you need telemetry to survive a
restart or be queryable over a longer window. Within that in-memory window, how much it holds before
evicting the oldest entries is controlled by `Dashboard:TelemetryLimits:*` env vars on the
`aspire-dashboard` container (`agent-deployment.yaml`) — set there to 2,000 log entries, 2,000 traces, and
10,000 metric data points (defaults are 10,000 / 10,000 / 50,000), which is what lets the sidecar's memory
limit be 256Mi instead of the ~512Mi+ the defaults would need. Raise both the limits and the memory
together if you want more history.

The dashboard UI's own auth is set to `Dashboard:Frontend:AuthMode=Unsecured` — anyone who can reach
`agent-service.yaml` (ClusterIP-only, so today that means `kubectl port-forward` or in-cluster access) can
open `/dashboard` with no login. Switch it back to the default `BrowserToken` (or `OpenIdConnect`) before
putting anything in front of that Service that widens who can reach it, e.g. an Ingress.

## Updating

- **Config change** (model, MCP endpoints): edit your cluster's `k8s/agent-configmap.yaml` (not the
  `.example.yaml` template), `kubectl apply` it, then `kubectl rollout restart
  deployment/kubernetes-agent -n k3s-agent` — ConfigMap edits don't trigger a rollout by themselves.
- **Code change**: rebuild the image, re-import it (step 2), then `kubectl rollout restart
  deployment/kubernetes-agent -n k3s-agent` (same tag won't otherwise be re-pulled with `IfNotPresent`).
- **Secret rotation**: `kubectl delete secret kubernetes-agent-secrets -n k3s-agent` and re-create it (step
  3), then roll out the agent deployment.

For a GitOps-safe secret workflow instead of the imperative `kubectl create secret` above, see
[agent-secret.example.yaml](agent-secret.example.yaml) and consider Sealed Secrets (docs/ARCHITECTURE.md
§9.1).

## CI-built images (GHCR)

`.github/workflows/netagent-image.yml` builds the `linux/arm64` NetAgent image on every push to `master`
(and on demand), pushes it to `ghcr.io/<owner>/<repo>/netagent`, then runs `kustomize edit set image` in
this directory and commits the retagged `kustomization.yaml` back to the branch — so this directory always
reflects the latest image the pipeline pushed, independent of whatever's checked out locally. To deploy
that image instead of a locally-built one:

```bash
kubectl apply -k k8s/
```

This still assumes the namespace, ConfigMap, and secret from step 3 above are already in place — the
ConfigMap in particular is never touched by CI or by `kubectl apply -k` (it's managed by hand in the
cluster; see step 3). Only the image tag is automated. Pods already running the previous tag need `kubectl
rollout restart deployment/kubernetes-agent -n k3s-agent` to pick up the new one, same as any other
code-change update.
