import { HttpAgent } from "@ag-ui/client";

// Wired by the Aspire AppHost to the agent project's actual endpoint; the fallback matches the
// port pinned in the agent's launchSettings.json so `npm run dev` works standalone.
const agentUrl = (import.meta.env.VITE_AGENT_URL ?? "http://localhost:5192").replace(/\/+$/, "");

export const agent = new HttpAgent({ url: `${agentUrl}/agui` });
