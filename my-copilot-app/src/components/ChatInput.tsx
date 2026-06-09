"use client";

import { Agent } from "./AgentSelector";

interface ChatInputProps {
  input: string;
  setInput: (value: string) => void;
  loading: boolean;
  selectedAgent: Agent | null;
  onSend: () => void;
}

export default function ChatInput({
  input,
  setInput,
  loading,
  selectedAgent,
  onSend,
}: ChatInputProps) {
  return (
    <div className="border-t border-gray-200 dark:border-gray-700 px-4 py-3 bg-white dark:bg-gray-800">
      <div className="flex gap-2 max-w-3xl mx-auto">
        <input
          className="flex-1 px-4 py-2 rounded-xl border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-900 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          placeholder={selectedAgent ? `Nhắn tin với ${selectedAgent.name}…` : "Nhập tin nhắn…"}
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={e => e.key === "Enter" && !e.shiftKey && onSend()}
          disabled={loading || !selectedAgent}
        />
        <button
          onClick={onSend}
          disabled={loading || !input.trim() || !selectedAgent}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white rounded-xl text-sm transition-colors"
        >
          {loading ? "..." : "Gửi"}
        </button>
      </div>
    </div>
  );
}