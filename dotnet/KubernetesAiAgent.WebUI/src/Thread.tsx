import { ComposerPrimitive, MessagePrimitive, ThreadPrimitive } from "@assistant-ui/react";
import { MarkdownTextPrimitive } from "@assistant-ui/react-markdown";
import remarkGfm from "remark-gfm";
import { ToolCall } from "./ToolCall";

// The agent's system prompt (and DeepSeek's own habits) produce markdown — headings, lists, tables, fenced
// code blocks — so the assistant side renders it instead of the raw ** and | characters. remark-gfm adds
// GitHub-flavored tables/task lists/strikethrough, which the model uses freely.
function AssistantText() {
  return <MarkdownTextPrimitive remarkPlugins={[remarkGfm]} className="markdown" />;
}

function UserMessage() {
  return (
    <div className="message message-user">
      <MessagePrimitive.Parts />
    </div>
  );
}

function AssistantMessage() {
  return (
    <div className="message message-assistant">
      <MessagePrimitive.Parts components={{ Text: AssistantText, tools: { Fallback: ToolCall } }} />
    </div>
  );
}

export function Thread() {
  return (
    <ThreadPrimitive.Root className="thread">
      <ThreadPrimitive.Viewport className="thread-viewport">
        <ThreadPrimitive.Empty>
          <div className="thread-empty">
            Ask about the cluster — for example, “list the pods in the default namespace”.
          </div>
        </ThreadPrimitive.Empty>

        <ThreadPrimitive.Messages components={{ UserMessage, AssistantMessage }} />

        <ThreadPrimitive.If running>
          <div className="thread-running">Thinking…</div>
        </ThreadPrimitive.If>
      </ThreadPrimitive.Viewport>

      <ComposerPrimitive.Root className="composer">
        <ComposerPrimitive.Input className="composer-input" placeholder="Ask about your cluster…" autoFocus />
        <ThreadPrimitive.If running={false}>
          <ComposerPrimitive.Send className="composer-button">Send</ComposerPrimitive.Send>
        </ThreadPrimitive.If>
        <ThreadPrimitive.If running>
          <ComposerPrimitive.Cancel className="composer-button">Stop</ComposerPrimitive.Cancel>
        </ThreadPrimitive.If>
      </ComposerPrimitive.Root>
    </ThreadPrimitive.Root>
  );
}
