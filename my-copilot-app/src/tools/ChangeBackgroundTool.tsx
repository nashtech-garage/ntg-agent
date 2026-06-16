"use client";

import { useState } from "react";
import { useHumanInTheLoop } from "@copilotkit/react-core/v2";
import { z } from "zod";

type Status = "inProgress" | "executing" | "complete";

function readableTextColor(hex: string): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.6 ? "#000000" : "#ffffff";
}

function Swatch({ color }: { color: string }) {
  return (
    <span
      aria-hidden
      className="inline-block h-5 w-5 rounded-full border border-black/10 dark:border-white/20"
      style={{ background: color }}
    />
  );
}

function ApprovalCard({
  status,
  args,
  result,
  respond,
  onChange,
}: {
  status: Status;
  args: Partial<{ color: string }>;
  result?: string;
  respond?: (result: unknown) => Promise<void>;
  onChange: (color: string) => void;
}) {
  const suggested = args.color ?? "#1e3a8a";
  // <input type="color"> only accepts 6-digit hex; the suggested color may be a
  // named CSS color or gradient, so fall back to a sane default for the picker.
  const isHex = /^#[0-9a-fA-F]{6}$/.test(suggested);
  const [pickerColor, setPickerColor] = useState(isHex ? suggested : "#1e3a8a");

  if (status === "inProgress") {
    return (
      <div className="my-2 inline-flex items-center gap-2 rounded-lg border border-gray-300 dark:border-gray-700 bg-gray-100 dark:bg-gray-800 px-3 py-1.5 text-sm text-gray-700 dark:text-gray-200">
        <span aria-hidden>🎨</span>
        Proposing a color…
      </div>
    );
  }

  const canAct = status === "executing" && !!respond;

  if (canAct) {
    const approve = (color: string, note?: string) => {
      onChange(color);
      respond!(note ? `approved ${color} (${note})` : `approved ${color}`);
    };
    const reject = () => respond!("denied");

    return (
      <div className="my-2 w-full max-w-sm overflow-hidden rounded-xl border border-gray-300 dark:border-gray-600 bg-gray-50 dark:bg-gray-800 text-sm text-gray-900 dark:text-gray-100">
        <div className="flex items-center gap-2 border-b border-gray-200 dark:border-gray-700 px-3 py-2 font-medium">
          <span aria-hidden>🎨</span>
          Change background color?
        </div>

        <div className="flex flex-col divide-y divide-gray-200 dark:divide-gray-700">
          {/* Suggested option */}
          <div className="flex items-center justify-between gap-3 px-3 py-2.5">
            <div className="flex items-center gap-2 min-w-0">
              <Swatch color={suggested} />
              <div className="min-w-0">
                <div className="text-xs uppercase tracking-wide text-gray-500 dark:text-gray-400">
                  Suggested
                </div>
                <code className="block truncate rounded bg-black/5 dark:bg-white/10 px-1 py-0.5">
                  {suggested}
                </code>
              </div>
            </div>
            <button
              onClick={() => approve(suggested)}
              className="shrink-0 rounded-full bg-blue-600 px-3 py-1 text-white hover:bg-blue-700"
            >
              Use this
            </button>
          </div>

          {/* Custom option */}
          <div className="flex items-center justify-between gap-3 px-3 py-2.5">
            <div className="flex items-center gap-2 min-w-0">
              <input
                type="color"
                value={pickerColor}
                onChange={(e) => setPickerColor(e.target.value)}
                className="h-7 w-10 shrink-0 cursor-pointer rounded border border-gray-300 dark:border-gray-500 bg-transparent"
                aria-label="Pick a custom color"
              />
              <div className="min-w-0">
                <div className="text-xs uppercase tracking-wide text-gray-500 dark:text-gray-400">
                  Or pick your own
                </div>
                <code className="block truncate rounded bg-black/5 dark:bg-white/10 px-1 py-0.5">
                  {pickerColor}
                </code>
              </div>
            </div>
            <button
              onClick={() => approve(pickerColor, `overriding suggested ${suggested}`)}
              className="shrink-0 rounded-full border border-black/10 dark:border-white/20 px-3 py-1 transition-opacity hover:opacity-90"
              style={{ backgroundColor: pickerColor, color: readableTextColor(pickerColor) }}
            >
              Use this color
            </button>
          </div>
        </div>

        <div className="border-t border-gray-200 dark:border-gray-700 px-3 py-2">
          <button
            onClick={reject}
            className="rounded-full border border-red-300 dark:border-red-700 px-3 py-1 text-red-700 dark:text-red-300 hover:bg-red-50 dark:hover:bg-red-950"
          >
            Reject suggestion
          </button>
        </div>
      </div>
    );
  }

  // status === "complete"
  const approvedMatch = /^approved (\S+)/.exec(result ?? "");
  const appliedColor = approvedMatch?.[1];

  return (
    <div className="my-2 inline-flex items-center gap-2 rounded-full border border-green-300 dark:border-green-700 bg-green-100 dark:bg-green-900 px-3 py-1.5 text-sm font-medium text-green-900 dark:text-green-100">
      {appliedColor ? (
        <>
          <Swatch color={appliedColor} />
          Background set to {appliedColor}
        </>
      ) : (
        <>
          <span aria-hidden>🚫</span>
          Kept the previous background
        </>
      )}
    </div>
  );
}

// AG-UI human-in-the-loop tool: the LLM proposes a background color, the chat
// shows a preview the user can approve, override with their own color, or
// reject. Must be rendered inside the <CopilotKit> provider.
export default function ChangeBackgroundTool({ onChange }: { onChange: (color: string) => void }) {
  useHumanInTheLoop(
    {
      name: "change_background",
      description:
        "Propose a background color change for the user to preview and approve. Call this " +
        "whenever the user wants to change the background or asks for a color recommendation " +
        "— propose your best suggestion via this tool rather than describing it in plain text. " +
        "The user may approve your suggestion, override it with a different color, or reject it; " +
        "do not assume any color was applied until you see the response.",
      parameters: z.object({
        color: z
          .string()
          .describe("A valid CSS background value, e.g. 'darkblue', '#1e3a8a', or a CSS gradient"),
      }),
      render: (props) => <ApprovalCard {...props} onChange={onChange} />,
    },
    [onChange]
  );
  return null;
}
