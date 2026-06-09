// app/page.tsx
"use client";

import { useState, useRef, useEffect } from "react";
import { extractObjects } from "../src/utils/streamParser";
import AgentSelector, { Agent } from "../src/components/AgentSelector";
import ChatArea, { Message } from "../src/components/ChatArea";
import ChatInput from "../src/components/ChatInput";

export default function Page() {
  const [agentMenuOpen, setAgentMenuOpen] = useState(false);
  const [agents, setAgents] = useState<Agent[]>([]);
  const [selectedAgent, setSelectedAgent] = useState<Agent | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);

  // Initial lookup
  useEffect(() => {
    fetch("/api/agents")
      .then((r) => r.json())
      .then((data: Agent[]) => {
        setAgents(data);
        const def = data.find((a) => a.isDefault) ?? data[0];
        if (def) {
          setSelectedAgent(def);
          setMessages([{ role: "assistant", content: `Xin chào! Tôi là ${def.name}. Tôi có thể giúp gì cho bạn hôm nay?` }]);
        }
      })
      .catch(() => {
        setMessages([{ role: "assistant", content: "Xin chào! Tôi là NTG Assistant. Tôi có thể giúp gì cho bạn hôm nay?" }]);
      });
  }, []);

  // Sync scroll positioning
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  function handleSwitchAgent(agent: Agent) {
    if (agent.id === selectedAgent?.id) {
      setAgentMenuOpen(false);
      return;
    }
    setSelectedAgent(agent);
    setAgentMenuOpen(false);
    setMessages([{ role: "assistant", content: `Xin chào! Tôi là ${agent.name}. Tôi có thể giúp gì cho bạn hôm nay?` }]);
  }

  async function handleSendMessage() {
    const text = input.trim();
    if (!text || loading || !selectedAgent) return;

    setInput("");
    setMessages((prev) => [...prev, { role: "user", content: text }]);
    setLoading(true);
    setMessages((prev) => [...prev, { role: "assistant", content: "" }]);

    try {
      const res = await fetch("/api/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ message: text, agentId: selectedAgent.id }),
      });

      if (!res.ok || !res.body) throw new Error(`HTTP ${res.status}`);

      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        const { objects, remaining } = extractObjects(buffer);
        buffer = remaining;

        for (const chunk of objects) {
          if (chunk.contentType !== 1 && chunk.content) {
            setMessages((prev) => {
              const updated = [...prev];
              updated[updated.length - 1] = {
                role: "assistant",
                content: updated[updated.length - 1].content + chunk.content,
              };
              return updated;
            });
          }
        }
      }
    } catch (err) {
      setMessages((prev) => {
        const updated = [...prev];
        updated[updated.length - 1] = { role: "assistant", content: `⚠️ Error: ${String(err)}` };
        return updated;
      });
    } {
      setLoading(false);
    }
  }

  return (
    <div className="flex flex-col h-screen bg-white dark:bg-gray-900 text-gray-900 dark:text-white">
      {/* Top Navbar */}
      <header className="w-full bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 shadow-sm z-50">
        <div className="flex items-center justify-between px-4 py-3">
          <h1 className="text-xl font-semibold">NTG Agent</h1>
        </div>
      </header>

      <AgentSelector
        agents={agents}
        selectedAgent={selectedAgent}
        isOpen={agentMenuOpen}
        setIsOpen={setAgentMenuOpen}
        onSelect={handleSwitchAgent}
      />

      <ChatArea messages={messages} loading={loading} bottomRef={bottomRef} />

      <ChatInput
        input={input}
        setInput={setInput}
        loading={loading}
        selectedAgent={selectedAgent}
        onSend={handleSendMessage}
      />
    </div>
  );
}