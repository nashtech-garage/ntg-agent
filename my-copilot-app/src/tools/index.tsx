"use client";

import ChangeBackgroundTool from "./ChangeBackgroundTool";

export interface FrontendToolsProps {
  onChangeBackground: (color: string) => void;
}

// Registers every AG-UI frontend tool with CopilotKit. Mount this once inside
// the <CopilotKit> provider; add new tools by creating a component in this
// folder and rendering it here.
export default function FrontendTools({ onChangeBackground }: FrontendToolsProps) {
  return (
    <>
      <ChangeBackgroundTool onChange={onChangeBackground} />
    </>
  );
}
