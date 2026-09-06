import { useState } from "react";
import type { ToolCallMessagePartProps } from "@assistant-ui/react";

const formatJson = (value: unknown): string => {
  if (value === undefined) return "";
  if (typeof value === "string") {
    // MCP tool results arrive as strings; some servers JSON-encode them, so pretty-print when we can.
    try {
      return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
      return value;
    }
  }
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
};

/**
 * Fallback renderer for every tool call the agent makes. The MCP tools are configured server-side
 * (agentconfig.yaml), so the frontend can't know their names ahead of time — one generic card
 * showing name, arguments and result covers all of them.
 */
export function ToolCall({ toolName, args, argsText, result, isError, status }: ToolCallMessagePartProps) {
  const [open, setOpen] = useState(false);
  const running = status.type === "running" || status.type === "requires-action";
  const argsJson = formatJson(Object.keys(args ?? {}).length > 0 ? args : argsText);
  const resultJson = formatJson(result);

  return (
    <div className={`tool-call${isError ? " tool-call-error" : ""}`}>
      <button type="button" className="tool-call-header" onClick={() => setOpen(!open)}>
        <span className={`tool-call-status${running ? " spinning" : ""}`}>{running ? "◐" : isError ? "✕" : "✓"}</span>
        <span className="tool-call-name">{toolName}</span>
        <span className="tool-call-toggle">{open ? "hide" : "details"}</span>
      </button>
      {open && (
        <div className="tool-call-body">
          <div className="tool-call-section-label">Arguments</div>
          <pre>{argsJson || "(none)"}</pre>
          <div className="tool-call-section-label">Result</div>
          <pre>{running ? "…" : resultJson || "(empty)"}</pre>
        </div>
      )}
    </div>
  );
}
