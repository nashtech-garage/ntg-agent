// A2UI (Agent-to-UI) renderer.
//
// CopilotKit's official A2UI renderer turns AG-UI "activity" messages (emitted by
// @ag-ui/a2ui-middleware from the agent's render_a2ui tool call) into native React
// UI, using the A2UI v0.9 basic catalog (Text, Card, Column, Button, TextField, …).
//
// Register the exported renderer via <CopilotKit renderActivityMessages={[a2uiActivityRenderer]}>.
// Created once at module scope so the renderer identity is stable across re-renders.
import { createA2UIMessageRenderer, a2uiDefaultTheme } from "@copilotkit/react-core/v2";
import { interactiveCatalog } from "./interactiveCatalog";

export const a2uiActivityRenderer = createA2UIMessageRenderer({
  theme: a2uiDefaultTheme,
  // Basic catalog with local-state-backed TextField/CheckBox so inputs are always editable.
  catalog: interactiveCatalog,
});

// Stable array reference — CopilotKit requires `renderActivityMessages` to keep the
// same identity across renders, so build it once here rather than inline in JSX.
export const a2uiActivityRenderers = [a2uiActivityRenderer];
