import type { Message } from "@copilotkit/react-core/v2";

// Shape returned by GET /api/conversations/{id}/messages (ASP.NET camelCase JSON).
export interface ChatMessageListItem {
  id: string;
  content: string;
  role: string; // "user" | "assistant" | "system" | "tool"
}

// Legacy guard: tool-result follow-up turns used to be persisted as a synthetic "user"
// message. The backend no longer saves these, but older conversations may still contain one.
const SYNTHETIC_TOOL_PROMPT_PREFIX = "[The user responded to the";

// A renderable tool call persisted by the backend (Tool-role message content is an array of these).
interface PersistedToolCall {
  callId: string;
  name: string;
  arguments: string; // JSON string of the tool arguments
  result: string; // raw tool result text (the tool's JSON output)
}

/** Maps persisted chat messages into AG-UI messages for hydrating CopilotChat. */
export function toAgentMessages(items: ChatMessageListItem[]): Message[] {
  const out: Message[] = [];
  for (const m of items) {
    if (m.role === "user") {
      if (m.content.trimStart().startsWith(SYNTHETIC_TOOL_PROMPT_PREFIX)) continue;
      out.push({ id: m.id, role: "user", content: m.content });
    } else if (m.role === "assistant") {
      out.push({ id: m.id, role: "assistant", content: m.content });
    } else if (m.role === "tool") {
      // Rebuild renderable tool calls (e.g. get_weather) as an assistant tool-call + a tool result
      // message, so useRenderTool re-renders the card exactly as it did during the live run.
      let calls: PersistedToolCall[] = [];
      try {
        const parsed = JSON.parse(m.content);
        if (Array.isArray(parsed)) calls = parsed as PersistedToolCall[];
      } catch {
        calls = [];
      }
      for (const c of calls) {
        if (!c.callId || !c.name) continue;
        out.push({
          id: `${c.callId}-call`,
          role: "assistant",
          toolCalls: [
            { id: c.callId, type: "function", function: { name: c.name, arguments: c.arguments ?? "{}" } },
          ],
        } as Message);
        out.push({
          id: `${c.callId}-result`,
          role: "tool",
          toolCallId: c.callId,
          content: c.result ?? "",
        } as Message);
      }
    }
  }
  return out;
}
