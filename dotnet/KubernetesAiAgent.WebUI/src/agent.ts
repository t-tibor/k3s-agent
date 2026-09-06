import { HttpAgent } from "@ag-ui/client";

// Always same-origin: in local Aspire dev, Vite's dev-server proxy (vite.config.ts) forwards /agui to the
// agent; in production, NetAgent serves both the built SPA and /agui from the same origin (see
// dotnet/KubernetesAiAgent.NetAgent/Dockerfile). Neither case needs an absolute, environment-specific URL
// or a CORS policy on the agent.
export const agent = new HttpAgent({ url: "/agui" });
