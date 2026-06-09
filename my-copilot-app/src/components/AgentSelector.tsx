"use client";

export interface Agent {
  id: string;
  name: string;
  isDefault: boolean;
  mode: string;
}

interface AgentSelectorProps {
  agents: Agent[];
  selectedAgent: Agent | null;
  isOpen: boolean;
  setIsOpen: (open: boolean | ((prev: boolean) => boolean)) => void;
  onSelect: (agent: Agent) => void;
}

export default function AgentSelector({
  agents,
  selectedAgent,
  isOpen,
  setIsOpen,
  onSelect,
}: AgentSelectorProps) {
  return (
    <div className="border-b border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-800/50 px-4 py-2">
      <div className="max-w-3xl mx-auto flex items-center gap-2">
        <span className="text-xs text-gray-500 dark:text-gray-400">Agent:</span>
        <div className="relative">
          <button
            onClick={() => setIsOpen((o) => !o)}
            className="flex items-center gap-2 px-3 py-1.5 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-900 text-sm hover:bg-gray-50 dark:hover:bg-gray-800 transition-colors"
          >
            <span className="w-2 h-2 rounded-full bg-green-500" />
            <span>{selectedAgent?.name ?? "Loading..."}</span>
            <svg className="w-4 h-4 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
            </svg>
          </button>

          {isOpen && agents.length > 0 && (
            <div className="absolute top-full left-0 mt-1 w-56 bg-white dark:bg-gray-900 border border-gray-200 dark:border-gray-700 rounded-xl shadow-lg z-50 overflow-hidden">
              {agents.map((agent) => (
                <button
                  key={agent.id}
                  onClick={() => onSelect(agent)}
                  className={`w-full text-left px-4 py-2.5 text-sm flex items-center gap-2 hover:bg-gray-50 dark:hover:bg-gray-800 transition-colors ${
                    agent.id === selectedAgent?.id ? "bg-blue-50 dark:bg-blue-900/20 text-blue-600 dark:text-blue-400" : ""
                  }`}
                >
                  <span className={`w-2 h-2 rounded-full ${agent.id === selectedAgent?.id ? "bg-blue-500" : "bg-gray-300"}`} />
                  <span className="flex-1">{agent.name}</span>
                  {agent.isDefault && (
                    <span className="text-xs text-gray-400 dark:text-gray-500">default</span>
                  )}
                </button>
              ))}
            </div>
          )}
        </div>

        {isOpen && (
          <div className="fixed inset-0 z-40" onClick={() => setIsOpen(false)} />
        )}
      </div>
    </div>
  );
}