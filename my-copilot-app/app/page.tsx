// app/page.tsx
"use client";

import { useState, useRef, useEffect } from "react";
import { CopilotKit } from "@copilotkit/react-core";
import { CopilotChat } from "@copilotkit/react-core/v2";
import "@copilotkit/react-core/v2/styles.css"; // Ensure styles are imported
// import "@copilotkit/react-ui/styles.css"; // UI styles are still needed for the chat components

import AgentSelector, { Agent } from "../src/components/AgentSelector";
import FrontendTools from "../src/tools";
// import ChatArea, { Message } from "../src/components/ChatArea";

export default function Page() {
  const [agentMenuOpen, setAgentMenuOpen] = useState(false);
  const [agents, setAgents] = useState<Agent[]>([]);
  const [selectedAgent, setSelectedAgent] = useState<Agent | null>(null);
  const [backgroundColor, setBackgroundColor] = useState<string | null>(null);

  // 1. Keep your exact automatic lookup feature untouched!
  useEffect(() => {
    fetch("/api/agents")
      .then((r) => r.json())
      .then((data: Agent[]) => {
        setAgents(data);
        const def = data.find((a) => a.isDefault) ?? data[0];
        if (def) {
          setSelectedAgent(def);
        }
      })
      .catch((err) => {
        console.error("Failed to load agents automatically:", err);
      });
  }, []);

  function handleSwitchAgent(agent: Agent) {
    if (agent.id === selectedAgent?.id) {
      setAgentMenuOpen(false);
      return;
    }
    setSelectedAgent(agent);
    setAgentMenuOpen(false);
  }

  // Prevent rendering CopilotKit until the agent ID is automatically fetched
  if (!selectedAgent) {
    return (
      <div className="flex h-screen items-center justify-center bg-white dark:bg-gray-900">
        <div className="text-sm text-gray-500 animate-pulse">Initializing Agent Connection...</div>
      </div>
    );
  }

  return (
    // 2. Pass the automatically discovered agent ID directly into the runtime string!
    // Adding a 'key' makes sure CopilotKit resets cleanly if you swap agents via the menu.
    <CopilotKit 
      key={selectedAgent.id}
      runtimeUrl={`/api/copilotkit/${selectedAgent.id}`}
      agent="dotnet_orchestrator_agent"
    >
      <FrontendTools onChangeBackground={setBackgroundColor} />
      <div
        className="flex flex-col h-screen bg-white dark:bg-gray-900 text-gray-900 dark:text-white"
        style={backgroundColor ? { background: backgroundColor } : undefined}
      >
        {/* Top Navbar */}
        <header className="w-full bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 shadow-sm z-50">
          <div className="flex items-center justify-between px-4 py-3">
            <h1 className="text-xl font-semibold">NTG Agent</h1>
          </div>
        </header>

        {/* Your original selector works exactly the same */}
        <AgentSelector
          agents={agents}
          selectedAgent={selectedAgent}
          isOpen={agentMenuOpen}
          setIsOpen={setAgentMenuOpen}
          onSelect={handleSwitchAgent}
        />

        {/* Swap out your old custom chat loops with CopilotChat */}
        <div className="flex-1 overflow-hidden max-w-4xl w-full mx-auto p-4">
          <CopilotChat
            agentId="dotnet_orchestrator_agent"
            className="h-full w-full rounded-xl border border-gray-200 dark:border-gray-700 shadow-sm"
            labels={{
              modalHeaderTitle: selectedAgent.name,
              welcomeMessageText: `Xin chào! Tôi là ${selectedAgent.name}. Tôi có thể giúp gì cho bạn hôm nay?`,
              chatInputPlaceholder: `Nhắn tin với ${selectedAgent.name}…`,
            }}
          />
        </div>
      </div>
    </CopilotKit>
  );
}