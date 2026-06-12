"use client";

import { useFrontendTool } from "@copilotkit/react-core/v2";
import { z } from "zod";

// AG-UI frontend tool: the LLM calls change_background, the handler runs in the
// browser. Must be rendered inside the <CopilotKit> provider.
export default function ChangeBackgroundTool({ onChange }: { onChange: (color: string) => void }) {
  useFrontendTool(
    {
      name: "change_background",
      description:
        "Change the page background. Use when the user asks to change the background, theme color, or page color.",
      parameters: z.object({
        color: z
          .string()
          .describe("A valid CSS background value, e.g. 'darkblue', '#1e3a8a', or a CSS gradient"),
      }),
      handler: async ({ color }) => {
        onChange(color);
        return `Background changed to ${color}`;
      },
      render: ({ status, args }) => (
        <div className="my-2 inline-flex items-center gap-2 rounded-full border border-red-300 dark:border-red-700 bg-red-100 dark:bg-red-900 px-3 py-1.5 text-sm font-medium text-red-900 dark:text-red-100">
          <span aria-hidden className="text-base">🎨</span>
          {String(status) === "complete"
            ? `Background set to ${args?.color ?? "new color"}`
            : "Changing background…"}
        </div>
            ),
    },
    [onChange]
  );
  return null;
}
