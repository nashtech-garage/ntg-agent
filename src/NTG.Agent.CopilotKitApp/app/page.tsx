// app/page.tsx
"use client";

import { useState, useEffect } from "react";
import { CopilotKit } from "@copilotkit/react-core";

import { Agent } from "../src/components/AgentSelector";
import ChatWorkspace from "../src/components/ChatWorkspace";
import { AGENT_ID } from "../src/constants";
import { a2uiActivityRenderers } from "../src/a2ui/activityRenderer";

export default function Page() {
  const [agentMenuOpen, setAgentMenuOpen] = useState(false);
  const [agents, setAgents] = useState<Agent[]>([]);
  const [selectedAgent, setSelectedAgent] = useState<Agent | null>(null);
  const [backgroundColor, setBackgroundColor] = useState<string | null>(null);

  // Automatically discover the available agents and pick the default.
  useEffect(() => {
    let cancelled = false;

    async function loadAgents() {
      const maxAttempts = 5;
      for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        try {
          const r = await fetch("/api/agents");
          if (!r.ok) throw new Error(`HTTP ${r.status}`);
          const data: Agent[] = await r.json();
          if (cancelled) return;
          setAgents(data);
          const def = data.find((a) => a.isDefault) ?? data[0];
          if (def) {
            setSelectedAgent(def);
          }
          return;
        } catch (err) {
          if (cancelled) return;
          if (attempt === maxAttempts) {
            console.error("Failed to load agents automatically:", err);
          } else {
            await new Promise((resolve) => setTimeout(resolve, 1000 * attempt));
          }
        }
      }
    }

    loadAgents();
    return () => {
      cancelled = true;
    };
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
    // Pass the automatically discovered agent ID directly into the runtime string.
    // 'key' makes sure CopilotKit resets cleanly when swapping agents via the menu.
    <CopilotKit
      key={selectedAgent.id}
      runtimeUrl={`/api/copilotkit/${selectedAgent.id}`}
      agent={AGENT_ID}
      renderActivityMessages={a2uiActivityRenderers}
    >
      <ChatWorkspace
        agents={agents}
        selectedAgent={selectedAgent}
        agentMenuOpen={agentMenuOpen}
        setAgentMenuOpen={setAgentMenuOpen}
        onSwitchAgent={handleSwitchAgent}
        backgroundColor={backgroundColor}
        setBackgroundColor={setBackgroundColor}
      />
    </CopilotKit>
  );
}
