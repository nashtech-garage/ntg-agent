// app/api/copilotkit/[integrationId]/_ntgAgent.ts

import { AbstractAgent, BaseEvent, EventType, RunAgentInput } from "@ag-ui/client";
import { Observable } from "rxjs";
import { extractObjects } from "./_bufferUtils";
import { conversationStore, createConversation, checkRateLimitStatus, orchestratorUrl } from "./_conversationStore";

// Match the interface expected by the CopilotKit/AG-UI engine structure
interface CustomAgentConfig {
  agentId?: string;
  description?: string;
  threadId?: string;
  cookieHeader?: string; // We bundle our layout cookies safely inside or as an optional addition
}

export class NtgAgent extends AbstractAgent {
  // CopilotKit's handle-run.ts does `agent.agentId = agentId` after cloning,
  // overwriting it with the agents-map key ("dotnet_orchestrator_agent").
  // Store the real backend UUID separately so run() always has the right value.
  private _ntgBackendAgentId: string;
  private cookieHeader: string;

  constructor(config: CustomAgentConfig = {}, fallbackCookie: string = "") {
    const cookie = config.cookieHeader ?? fallbackCookie ?? "";
    const sessionCookieMatch = cookie.match(/ntg_session_id=([^;]+)/);
    const resolvedSessionId = sessionCookieMatch?.[1] || config.threadId || crypto.randomUUID();

    const resolvedAgentId = config.agentId || "";

    super({
      agentId: resolvedAgentId || "ntg_agent",
      description: config.description || "NTG .NET Orchestrator Agent",
      threadId: resolvedSessionId,
    });

    this._ntgBackendAgentId = resolvedAgentId;
    this.cookieHeader = cookie;
  }

  // AbstractAgent.clone() uses Object.create() and only copies its own fields.
  // Override to carry subclass-private fields across clones.
  clone(): this {
    const cloned = super.clone() as this;
    cloned._ntgBackendAgentId = this._ntgBackendAgentId;
    cloned.cookieHeader = this.cookieHeader;
    return cloned;
  }

  public run(input: RunAgentInput): Observable<BaseEvent> {
    const agentId = this._ntgBackendAgentId;
    const cookieHeader = this.cookieHeader ?? "";

    return new Observable<BaseEvent>((subscriber) => {
      let activeReader: ReadableStreamDefaultReader<Uint8Array> | null = null;
      let isAborted = false;

      (async () => {
        try {
          const sessionCookieMatch = cookieHeader.match(/ntg_session_id=([^;]+)/);
          const sessionId = sessionCookieMatch?.[1] ?? crypto.randomUUID();
          const key = `${sessionId}:${agentId}`;

          let conversationId = conversationStore.get(key);
          if (!conversationId) {
            conversationId = await createConversation(sessionId, cookieHeader);
            conversationStore.set(key, conversationId);
          }

          if (isAborted) return;

          const messages: any[] = (input as any).messages ?? [];
          const lastUser = [...messages].reverse().find((m: any) => m.role === "user");
          const userPrompt =
            typeof lastUser?.content === "string"
              ? lastUser.content
              : (lastUser?.content as any[])
                  ?.filter((p: any) => p.type === "text")
                  .map((p: any) => p.text)
                  .join("") ?? "";

          // Frontend-tool result follow-up: after the browser executes a tool, CopilotKit
          // re-runs the agent with a trailing role:"tool" message. Send the result as a
          // synthetic prompt so the LLM can acknowledge the action.
          const lastNonSystem = [...messages].reverse().find((m: any) => m.role !== "system");
          let prompt = userPrompt;
          if (lastNonSystem?.role === "tool") {
            const toolCallId = lastNonSystem.toolCallId;
            let toolName = "unknown_tool";
            for (const m of messages) {
              const match = m.role === "assistant" && Array.isArray(m.toolCalls)
                ? m.toolCalls.find((t: any) => t.id === toolCallId)
                : undefined;
              if (match) toolName = match.function?.name ?? toolName;
            }
            const resultText = typeof lastNonSystem.content === "string"
              ? lastNonSystem.content
              : JSON.stringify(lastNonSystem.content ?? "");
            prompt = `[The browser tool "${toolName}" was executed and returned: ${resultText}] Briefly confirm to the user what was done. Do not call the tool again.`;
          }

          const canSend = await checkRateLimitStatus(sessionId, cookieHeader);
          if (!canSend && !isAborted) {
            const limitRunId = crypto.randomUUID();
            const limitThreadId = (input as any).threadId ?? crypto.randomUUID();
            const msgId = crypto.randomUUID();
            const now = Date.now();
            subscriber.next({ type: EventType.RUN_STARTED, timestamp: now, threadId: limitThreadId, runId: limitRunId } as any);
            subscriber.next({ type: EventType.STEP_STARTED, timestamp: now, stepName: "chat" } as any);
            subscriber.next({ type: EventType.TEXT_MESSAGE_START, timestamp: now, messageId: msgId, role: "assistant" } as any);
            subscriber.next({ type: EventType.TEXT_MESSAGE_CONTENT, timestamp: now, messageId: msgId, delta: "⚠️ You've reached the message limit for anonymous users. Please sign in to continue." } as any);
            subscriber.next({ type: EventType.TEXT_MESSAGE_END, timestamp: now, messageId: msgId } as any);
            subscriber.next({ type: EventType.STEP_FINISHED, timestamp: now, stepName: "chat" } as any);
            subscriber.next({ type: EventType.RUN_FINISHED, timestamp: now, threadId: limitThreadId, runId: limitRunId } as any);
            subscriber.complete();
            return;
          }

          if (isAborted) return;

          const formData = new FormData();
          formData.append("Prompt", prompt);
          formData.append("ConversationId", conversationId);
          formData.append("SessionId", sessionId);
          formData.append("AgentId", agentId);

          // AG-UI frontend tools: CopilotKit has already converted parameters to JSON Schema.
          // The backend declares these to the LLM but never executes them.
          const tools: any[] = (input as any).tools ?? [];
          if (tools.length > 0) {
            formData.append(
              "FrontendToolsJson",
              JSON.stringify(tools.map((t) => ({
                name: t.name,
                description: t.description,
                parameters: t.parameters,
              })))
            );
          }

          console.log(`[NtgAgent] → POST ${orchestratorUrl}/api/agents/chat`, {
            agentId, sessionId, conversationId, promptLen: prompt.length,
          });

          const response = await fetch(`${orchestratorUrl}/api/agents/chat`, {
            method: "POST",
            body: formData,
            headers: { ...(cookieHeader ? { Cookie: cookieHeader } : {}) },
          });

          if (!response.ok || !response.body) {
            const body = await response.text().catch(() => "(unreadable)");
            console.error(`[NtgAgent] Orchestrator 400 body:`, body);
            throw new Error(`Orchestrator HTTP Error: System returned status ${response.status}`);
          }

          if (isAborted) return;

          const runId = crypto.randomUUID();
          const threadId = (input as any).threadId ?? crypto.randomUUID();
          subscriber.next({ type: EventType.RUN_STARTED, timestamp: Date.now(), threadId, runId } as any);
          subscriber.next({ type: EventType.STEP_STARTED, timestamp: Date.now(), stepName: "chat" } as any);

          // Text messages are opened lazily so a pure tool-call turn doesn't
          // produce an empty assistant message.
          let messageId = crypto.randomUUID();
          let textOpen = false;
          const openText = () => {
            if (!textOpen) {
              subscriber.next({ type: EventType.TEXT_MESSAGE_START, timestamp: Date.now(), messageId, role: "assistant" } as any);
              textOpen = true;
            }
          };
          const closeText = () => {
            if (textOpen) {
              subscriber.next({ type: EventType.TEXT_MESSAGE_END, timestamp: Date.now(), messageId } as any);
              textOpen = false;
              messageId = crypto.randomUUID();
            }
          };

          // contentType: 0 = text, 1 = thinking (not rendered), 2 = frontend tool call
          const emitChunk = (chunk: any) => {
            if (chunk.contentType === 2 && chunk.content) {
              closeText();
              let toolCall: any;
              try {
                toolCall = JSON.parse(chunk.content);
              } catch {
                console.error("[NtgAgent] Unparseable tool call chunk:", chunk.content);
                return;
              }
              const toolCallId = toolCall.callId || crypto.randomUUID();
              const now = Date.now();
              subscriber.next({ type: EventType.TOOL_CALL_START, timestamp: now, toolCallId, toolCallName: toolCall.name } as any);
              subscriber.next({ type: EventType.TOOL_CALL_ARGS, timestamp: now, toolCallId, delta: JSON.stringify(toolCall.arguments ?? {}) } as any);
              subscriber.next({ type: EventType.TOOL_CALL_END, timestamp: now, toolCallId } as any);
            } else if (chunk.contentType !== 1 && chunk.content) {
              openText();
              subscriber.next({ type: EventType.TEXT_MESSAGE_CONTENT, timestamp: Date.now(), messageId, delta: chunk.content } as any);
            }
          };

          activeReader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = "";

          while (!isAborted) {
            const { done, value } = await activeReader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });
            const { objects, remaining } = extractObjects(buffer);
            buffer = remaining;

            for (const chunk of objects) {
              emitChunk(chunk);
            }
          }

          if (isAborted) return;

          if (buffer.trim()) {
            const { objects } = extractObjects(buffer);
            for (const chunk of objects) {
              emitChunk(chunk);
            }
          }

          closeText();
          subscriber.next({ type: EventType.STEP_FINISHED, timestamp: Date.now(), stepName: "chat" } as any);
          subscriber.next({ type: EventType.RUN_FINISHED, timestamp: Date.now(), threadId, runId } as any);
          subscriber.complete();

        } catch (err) {
          console.error(`[NtgAgent Execution Error] crash on backend agent ${agentId}:`, err);
          subscriber.error(err);
        }
      })();

      return () => {
        isAborted = true;
        if (activeReader) {
          activeReader.cancel().catch(() => {});
        }
      };
    });
  }
}