"use client";

// "/" skill picker for the CopilotKit chat input.
//
// The Orchestrator advertises each agent's admin-enabled Agent Skills; typing "/" in the
// chat input opens a popover listing them. Picking one shows a removable chip, and on
// submit the choice travels to the backend as a "/skill:<name> " message prefix — the
// CopilotKit v2 runtime offers no side channel for custom per-run properties, so the
// prefix is the transport; AgUiController strips it and maps it to PromptRequestForm.SkillName.
//
// SkillAwareChatInput is mounted as CopilotChat's `input` SLOT, so it receives the chat's
// internal `value`, `onChange` and `onSubmitMessage` as props and can intercept all three.

import {
  createContext,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import {
  CopilotChatInput,
  type CopilotChatInputProps,
} from "@copilotkit/react-core/v2";

export interface AgentSkill {
  name: string;
  description: string;
}

interface SkillPickerState {
  skills: AgentSkill[];
  selectedSkill: string | null;
  setSelectedSkill: (name: string | null) => void;
}

const SkillPickerContext = createContext<SkillPickerState>({
  skills: [],
  selectedSkill: null,
  setSelectedSkill: () => {},
});

export function SkillPickerProvider({
  agentId,
  children,
}: {
  agentId: string;
  children: ReactNode;
}) {
  const [skills, setSkills] = useState<AgentSkill[]>([]);
  const [selectedSkill, setSelectedSkill] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setSelectedSkill(null);
    setSkills([]);
    fetch(`/api/agents/${agentId}/skills`, { cache: "no-store" })
      .then((r) => (r.ok ? r.json() : []))
      .then((data: AgentSkill[]) => {
        if (!cancelled) setSkills(data);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [agentId]);

  return (
    <SkillPickerContext.Provider value={{ skills, selectedSkill, setSelectedSkill }}>
      {children}
    </SkillPickerContext.Provider>
  );
}

function SkillAwareChatInputBase(props: CopilotChatInputProps) {
  const { skills, selectedSkill, setSelectedSkill } = useContext(SkillPickerContext);

  const value = props.value ?? "";
  const trimmed = value.trimStart();
  const pickerOpen = trimmed.startsWith("/");
  // Both "/exp" and the long form "/skill:exp" filter the list.
  const filter = pickerOpen
    ? trimmed.replace(/^\/(skill:?\s*)?/i, "").toLowerCase()
    : "";
  const matches = skills.filter((s) => s.name.toLowerCase().includes(filter));

  const pick = (name: string) => {
    setSelectedSkill(name);
    props.onChange?.("");
  };

  const handleSubmit = (text: string) => {
    // Enter while the picker is open selects the first match instead of sending.
    if (text.trimStart().startsWith("/")) {
      if (matches.length > 0) pick(matches[0].name);
      return;
    }
    const message = selectedSkill ? `/skill:${selectedSkill} ${text}` : text;
    setSelectedSkill(null);
    props.onSubmitMessage?.(message);
  };

  return (
    <div className="relative">
      {pickerOpen && (
        <div className="absolute bottom-full left-0 right-0 z-50 mb-2 overflow-hidden rounded-lg border border-gray-200 bg-white shadow-lg dark:border-gray-700 dark:bg-gray-800">
          {matches.length === 0 ? (
            <div className="px-3 py-2 text-sm text-gray-500 dark:text-gray-400">
              No skills enabled for this agent.
            </div>
          ) : (
            matches.map((skill) => (
              <button
                key={skill.name}
                type="button"
                onClick={() => pick(skill.name)}
                className="block w-full px-3 py-2 text-left hover:bg-gray-100 dark:hover:bg-gray-700"
              >
                <span className="text-sm font-medium text-gray-900 dark:text-white">
                  /{skill.name}
                </span>
                <span className="block truncate text-xs text-gray-500 dark:text-gray-400">
                  {skill.description}
                </span>
              </button>
            ))
          )}
        </div>
      )}
      {selectedSkill && (
        <div className="mb-1 flex">
          <span className="inline-flex items-center gap-1 rounded-full bg-blue-600 px-2.5 py-0.5 text-xs font-medium text-white">
            /{selectedSkill}
            <button
              type="button"
              onClick={() => setSelectedSkill(null)}
              aria-label="Remove skill"
              className="ml-0.5 leading-none hover:text-blue-200"
            >
              ×
            </button>
          </span>
        </div>
      )}
      <CopilotChatInput {...props} onSubmitMessage={handleSubmit} />
    </div>
  );
}

// The `input` slot type is `typeof CopilotChatInput`, which carries static sub-components
// (SendButton, ToolbarButton, ...). Copy them onto the wrapper so it satisfies the slot type.
export const SkillAwareChatInput = Object.assign(SkillAwareChatInputBase, CopilotChatInput);
