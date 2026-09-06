import { AssistantRuntimeProvider } from "@assistant-ui/react";
import { useAgUiRuntime } from "@assistant-ui/react-ag-ui";
import { agent } from "./agent";
import { Thread } from "./Thread";

export function App() {
  const runtime = useAgUiRuntime({
    agent,
    onError: (error) => console.error("AG-UI run failed", error),
  });

  return (
    <AssistantRuntimeProvider runtime={runtime}>
      <div className="app">
        <header className="app-header">
          <h1>Kubernetes Agent</h1>
          <p>Read-only cluster assistant. Tool calls are shown as they happen.</p>
        </header>
        <Thread />
      </div>
    </AssistantRuntimeProvider>
  );
}
