"use client";

import { useState, useEffect, useCallback } from "react";
import { CopilotChat, useAgent, useCopilotKit } from "@copilotkit/react-core/v2";
import "@copilotkit/react-core/v2/styles.css"; // Ensure styles are imported

import AgentSelector, { Agent } from "./AgentSelector";
import AppHeader from "./AppHeader";
import ConversationSidebar from "./ConversationSidebar";
import { SkillPickerProvider, SkillAwareChatInput } from "./SkillPicker";
import FrontendTools from "../tools";
import { useAuth } from "../auth/AuthProvider";
import { AGENT_ID } from "../constants";
import { ChatMessageListItem, toAgentMessages } from "../utils/agentMessages";

export interface ChatWorkspaceProps {
  agents: Agent[];
  selectedAgent: Agent;
  agentMenuOpen: boolean;
  setAgentMenuOpen: (open: boolean | ((prev: boolean) => boolean)) => void;
  onSwitchAgent: (agent: Agent) => void;
  backgroundColor: string | null;
  setBackgroundColor: (color: string | null) => void;
}

export default function ChatWorkspace({
  agents,
  selectedAgent,
  agentMenuOpen,
  setAgentMenuOpen,
  onSwitchAgent,
  backgroundColor,
  setBackgroundColor,
}: ChatWorkspaceProps) {
  const { user } = useAuth();
  const { copilotkit } = useCopilotKit();
  const { agent } = useAgent({ agentId: AGENT_ID });

  // We drive the agent's threadId imperatively rather than via CopilotChat's `threadId`
  // prop. Passing the prop makes CopilotChat "connect" on every thread switch, which
  // calls agent.setMessages([]) and wipes any history we hydrate. Without the prop the
  // agent still sends `agent.threadId` on each run, so we control the conversation while
  // keeping loaded messages on screen. `activeSessionId` mirrors agent.threadId for the
  // sidebar highlight.
  const [activeSessionId, setActiveSessionId] = useState<string>(() => crypto.randomUUID());
  const [reloadSignal, setReloadSignal] = useState(0);

  // Keep the agent's threadId pinned to the active session id. Resolved via
  // copilotkit.getAgent() (a method result, not a hook value) so we can assign the
  // property without tripping react-hooks/immutability. Re-runs when the agent registers
  // or the active session changes — so a brand-new chat persists under a known id and
  // switching conversations routes follow-up turns to the selected conversation.
  useEffect(() => {
    const a = copilotkit.getAgent(AGENT_ID);
    if (a) a.threadId = activeSessionId;
  }, [copilotkit, agent, activeSessionId]);

  // When a run finishes a new conversation may have been persisted — refresh the list.
  useEffect(() => {
    if (!agent) return;
    const sub = agent.subscribe({
      onRunFinalized: () => setReloadSignal((s) => s + 1),
    });
    return () => sub.unsubscribe();
  }, [agent]);

  const handleSelectConversation = useCallback(
    async (sessionId: string | null, conversationId: string) => {
      try {
        const res = await fetch(`/api/conversations/${conversationId}/messages`, {
          cache: "no-store",
        });
        if (!res.ok) return;
        const items: ChatMessageListItem[] = await res.json();
        const tid = sessionId ?? crypto.randomUUID();
        agent.setMessages(toAgentMessages(items));
        setActiveSessionId(tid); // pin effect sets agent.threadId
      } catch (err) {
        console.error("Failed to load conversation", err);
      }
    },
    [agent]
  );

  const handleNewChat = useCallback(() => {
    agent.setMessages([]);
    setActiveSessionId(crypto.randomUUID()); // pin effect sets agent.threadId
  }, [agent]);

  return (
    <>
      <FrontendTools onChangeBackground={setBackgroundColor} />
      <div
        className="flex flex-col h-screen bg-white dark:bg-gray-900 text-gray-900 dark:text-white"
        style={backgroundColor ? { background: backgroundColor } : undefined}
      >
        <AppHeader />

        {/* Agent selector */}
        <AgentSelector
          agents={agents}
          selectedAgent={selectedAgent}
          isOpen={agentMenuOpen}
          setIsOpen={setAgentMenuOpen}
          onSelect={onSwitchAgent}
        />

        {/* Sidebar (logged-in only) + Chat */}
        <div className="flex flex-1 overflow-hidden">
          {user && (
            <ConversationSidebar
              activeSessionId={activeSessionId}
              reloadSignal={reloadSignal}
              onSelect={handleSelectConversation}
              onNewChat={handleNewChat}
            />
          )}

          <div className="flex-1 overflow-hidden p-4">
            <div className="mx-auto h-full w-full max-w-4xl">
              <SkillPickerProvider agentId={selectedAgent.id}>
                <CopilotChat
                  agentId={AGENT_ID}
                  className="h-full w-full rounded-xl border border-gray-200 dark:border-gray-700 shadow-sm"
                  input={SkillAwareChatInput}
                  labels={{
                    modalHeaderTitle: selectedAgent.name,
                    welcomeMessageText: `Xin chào! Tôi là ${selectedAgent.name}. Tôi có thể giúp gì cho bạn hôm nay?`,
                    chatInputPlaceholder: `Nhắn tin với ${selectedAgent.name}, hoặc gõ / để dùng skill…`,
                  }}
                />
              </SkillPickerProvider>
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
