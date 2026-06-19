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

/** Maps persisted chat messages into AG-UI messages for hydrating CopilotChat. */
export function toAgentMessages(items: ChatMessageListItem[]): Message[] {
  const out: Message[] = [];
  for (const m of items) {
    if (m.role === "user") {
      if (m.content.trimStart().startsWith(SYNTHETIC_TOOL_PROMPT_PREFIX)) continue;
      out.push({ id: m.id, role: "user", content: m.content });
    } else if (m.role === "assistant") {
      out.push({ id: m.id, role: "assistant", content: m.content });
    }
  }
  return out;
}
