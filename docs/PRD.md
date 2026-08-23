# PRD: Kubernetes AI Agent with .NET Aspire, Microsoft Agent Framework, and NextChat

> Architecture, component design, configuration shape, and project structure live in [ARCHITECTURE.md](ARCHITECTURE.md).
> This document covers what the product must do and how completeness is judged.

## 1. Overview

Build a self-hosted Kubernetes AI assistant that allows a user to ask natural-language questions about a Kubernetes cluster.

The solution will use:

- **.NET Aspire** as the application orchestrator and local development environment.
- **NextChat** as the frontend chat interface, running as a container managed by Aspire.
- **Microsoft Agent Framework** as the backend agent implementation.
- **OpenRouter** as the LLM/model provider.
- An existing **Kubernetes MCP server** as the agent's read-only interface to the Kubernetes cluster.
- **Kubernetes** as the deployment target for the complete solution.

The first version is explicitly **read-only**. The agent must not be capable of mutating Kubernetes resources.

The backend must expose an **OpenAI-compatible chat API** so NextChat can use the custom agent as a model/provider.

---

## 2. Goals

### Primary goals

1. Provide a ChatGPT-like UI for Kubernetes questions using NextChat.
2. Run NextChat as a container resource inside a .NET Aspire application.
3. Implement the Kubernetes agent using Microsoft Agent Framework.
4. Connect the agent to a configurable Kubernetes MCP server.
5. Use OpenRouter as the LLM provider.
6. Expose the agent through an OpenAI-compatible HTTP API.
7. Make the complete solution deployable to Kubernetes.
8. Keep the Kubernetes MCP server and agent internal to the cluster/network.
9. Enforce read-only Kubernetes access through Kubernetes RBAC.
10. Keep all credentials and endpoints configurable through environment variables / configuration and out of source control.

### Secondary goals

1. Make the agent stateless from the backend's perspective where practical.
2. Support streaming responses if supported cleanly by the selected Microsoft Agent Framework API.
3. Provide health/readiness endpoints.
4. Provide structured logging suitable for Aspire and Kubernetes.
5. Make the agent easy to extend with additional MCP servers later.

---

## 3. Non-goals

The initial implementation must NOT:

- Modify Kubernetes resources.
- Create, delete, patch, or update Kubernetes resources.
- Execute arbitrary shell commands in the cluster.
- Expose the Kubernetes API directly to NextChat.
- Expose the Kubernetes MCP server publicly.
- Implement a custom frontend.
- Implement long-term conversation storage in the agent backend.
- Implement autonomous background remediation.
- Implement automatic alert remediation.
- Require OpenAI-hosted models.
- Couple the frontend directly to the Kubernetes MCP server.

Future versions may add controlled write capabilities, additional tools, or persistent agent memory, but these are outside the scope of V1.

---

## 4. Example use cases

The implementation should support questions such as:

### Cluster state

- What nodes are currently in the cluster?
- Which nodes are NotReady?
- What pods are running in namespace X?
- Which deployments have unavailable replicas?

### Troubleshooting

- Why is my PostgreSQL pod restarting?
- Why is this deployment unhealthy?
- What happened to pod X?
- Are there recent warning events in namespace X?

### Resource inspection

- Which pods are using the most CPU?
- Which pods are using the most memory?
- Which workloads have high restart counts?

### Correlation

- Why is application X unavailable?
- Check the deployment, pods and recent events for X and explain what is wrong.

The agent should prefer current MCP data over generic Kubernetes knowledge whenever the question concerns the actual cluster.

---

## 5. Acceptance criteria

The implementation is complete when all of the following are true:

- [ ] An Aspire solution exists containing the agent project and NextChat container.
- [ ] NextChat starts from Aspire.
- [ ] The Kubernetes Agent starts from Aspire.
- [ ] NextChat can be configured to use `kubernetes-agent`.
- [ ] `GET /v1/models` works.
- [ ] `POST /v1/chat/completions` works.
- [ ] The agent uses Microsoft Agent Framework.
- [ ] The agent uses OpenRouter for LLM inference.
- [ ] The MCP server endpoint is configurable through application configuration.
- [ ] The agent can discover/use Kubernetes MCP tools.
- [ ] A user can ask a Kubernetes question through NextChat and receive an answer based on current cluster state.
- [ ] The agent does not require a personal kubeconfig.
- [ ] Kubernetes access is restricted using RBAC.
- [ ] Mutating Kubernetes operations are unavailable to the agent.
- [ ] OpenRouter credentials are not committed to source control.
- [ ] Health and readiness endpoints exist.
- [ ] The application produces structured logs.
- [ ] The agent can be containerized.
- [ ] The agent can be deployed to Kubernetes.
- [ ] NextChat can be deployed to Kubernetes.
- [ ] A README documents local development and deployment.
- [ ] Automated tests cover the critical API and agent integration paths.
