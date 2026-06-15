"use client";

// AgentMode mirrors the backend enum: Fast = 0, Thinking = 1
export const AgentMode = { Fast: 0, Thinking: 1 } as const;

export interface Agent {
  id: string;
  name: string;
  isDefault: boolean;
  mode: number;
}

interface AgentSelectorProps {
  agents: Agent[];
  selectedAgent: Agent | null;
  isOpen: boolean;
  setIsOpen: (open: boolean | ((prev: boolean) => boolean)) => void;
  onSelect: (agent: Agent) => void;
}

function ThinkingBadge() {
  return (
    <span
      title="This agent uses extended thinking — reasoning steps will appear above the reply."
      className="inline-flex items-center gap-0.5 text-[10px] font-medium px-1.5 py-0.5 rounded-full bg-indigo-100 dark:bg-indigo-900/40 text-indigo-600 dark:text-indigo-300 select-none"
    >
      {/* simple brain SVG */}
      <svg className="w-3 h-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
        <path d="M9.5 2A2.5 2.5 0 0 1 12 4.5v15a2.5 2.5 0 0 1-4.96-.46 2.5 2.5 0 0 1-1.52-4.28A3 3 0 0 1 5 9.5a3 3 0 0 1 .5-1.65A2.5 2.5 0 0 1 9.5 2Z" />
        <path d="M14.5 2A2.5 2.5 0 0 0 12 4.5v15a2.5 2.5 0 0 0 4.96-.46 2.5 2.5 0 0 0 1.52-4.28A3 3 0 0 0 19 9.5a3 3 0 0 0-.5-1.65A2.5 2.5 0 0 0 14.5 2Z" />
      </svg>
      Thinking
    </span>
  );
}

export default function AgentSelector({
  agents,
  selectedAgent,
  isOpen,
  setIsOpen,
  onSelect,
}: AgentSelectorProps) {
  const isThinking = selectedAgent?.mode === AgentMode.Thinking;

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
            {isThinking && <ThinkingBadge />}
            <svg className="w-4 h-4 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
            </svg>
          </button>

          {isOpen && agents.length > 0 && (
            <div className="absolute top-full left-0 mt-1 w-64 bg-white dark:bg-gray-900 border border-gray-200 dark:border-gray-700 rounded-xl shadow-lg z-50 overflow-hidden">
              {agents.map((agent) => (
                <button
                  key={agent.id}
                  onClick={() => onSelect(agent)}
                  className={`w-full text-left px-4 py-2.5 text-sm flex items-center gap-2 hover:bg-gray-50 dark:hover:bg-gray-800 transition-colors ${
                    agent.id === selectedAgent?.id ? "bg-blue-50 dark:bg-blue-900/20 text-blue-600 dark:text-blue-400" : ""
                  }`}
                >
                  <span className={`w-2 h-2 rounded-full shrink-0 ${agent.id === selectedAgent?.id ? "bg-blue-500" : "bg-gray-300"}`} />
                  <span className="flex-1">{agent.name}</span>
                  {agent.mode === AgentMode.Thinking && <ThinkingBadge />}
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