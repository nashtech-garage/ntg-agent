# A2UI (Agent-to-UI) Implementation

> Status: Implemented · Branch: `feature/AG-UI-CopilotKit`

## Context

The project already shipped a "generative UI" capability, but it was **per-tool hardcoded
React** (e.g. `my-copilot-app/src/tools/WeatherCardTool.tsx` matches the `get_weather` tool by
name via `useRenderTool` and renders a bespoke weather card). This change adds **A2UI** —
Google's open declarative Agent-to-UI protocol ([a2ui.org](https://a2ui.org), spec v0.9) — so
the **LLM composes arbitrary UIs on the fly** from a component catalog, rendered by a **generic**
renderer. A2UI is the *semantic* layer (the LLM emits a JSON description of surfaces, components,
a reactive data model, and actions); **AG-UI** (already in use) is the *transport*.

**Goals (met):** adopt CopilotKit's official A2UI renderer; support display **and** interactivity
(inputs + a submit action that round-trips to the agent); keep the hardcoded weather card alongside
as a before/after comparison.

The official A2UI stack was already present transitively (now pinned as direct deps):
`@copilotkit/a2ui-renderer` (basic catalog + `createA2UIMessageRenderer`), `@ag-ui/a2ui-middleware`
(injects the `render_a2ui` tool, converts its tool-call args into `ACTIVITY_SNAPSHOT` messages,
injects `log_a2ui_event` for interactions), `@ag-ui/client`, and `@a2ui/web_core` (catalog +
data-model primitives). The app is already on the CopilotKit **v2** API.

## Architecture (as built)

```
LLM (in .NET Orchestrator)
  │  calls render_a2ui({ surfaceId, components:[...], data:{...} })   ← declaration-only frontend tool
  ▼  injected by the middleware; backend forwards it via AgUiRunRequest.Tools → FrontendToolDeclaration
AgUiController  → streams standard AG-UI TOOL_CALL_START / TOOL_CALL_ARGS / TOOL_CALL_END (SSE)
  ▼
@ag-ui/client HttpAgent (Next.js bridge)
  ▼
@ag-ui/a2ui-middleware  → intercepts render_a2ui, emits ACTIVITY_SNAPSHOT (activityType "a2ui-surface")
  ▼
createA2UIMessageRenderer (registered via the renderActivityMessages prop on <CopilotKit>)
  → maintains surface + data-model state, renders our interactiveCatalog
  │  user edits a field / clicks the submit button
  ▼
Button dispatches the action (incl. the full data model) → middleware injects log_a2ui_event
  → next agent turn; the agent reads the answers and responds
```

The weather card path (`get_weather` → `CapturingAIFunction` → `RenderableToolCapture` →
`TOOL_CALL_RESULT` → `WeatherCardTool`) is untouched and runs side-by-side.

`render_a2ui` arguments (from `@ag-ui/a2ui-middleware`'s injected tool):
`{ surfaceId: string, components: A2UIComponent[], data?: object }`. The middleware wraps these
into the `createSurface` / `updateComponents` / `updateDataModel` ops the renderer consumes.

## Frontend (`my-copilot-app`)

- **`app/api/copilotkit/[[...integrationId]]/route.ts`** — apply the middleware to the bridge agent:
  `agentInstance.use(new A2UIMiddleware({ injectA2UITool: true }))`.
- **`src/a2ui/activityRenderer.ts`** (new) — builds the renderer once and exports a **stable array**
  (`createA2UIMessageRenderer` requires a stable `renderActivityMessages` reference):
  `createA2UIMessageRenderer({ theme: a2uiDefaultTheme, catalog: interactiveCatalog })`.
- **`src/a2ui/interactiveCatalog.tsx`** (new) — the official `basicCatalog` cloned with four
  components overridden (see "Interactivity" below). Registered as the renderer's `catalog`.
- **`app/page.tsx`** — register on the provider: `<CopilotKit renderActivityMessages={a2uiActivityRenderers}>`.
  (The `useRenderActivityMessage()` hook is a *consumer*, not a registrar — the prop is the way.)
- **`app/globals.css`** — scoped `.a2ui-surface` "Ink & Iris" styling (see "Design").
- **`package.json`** — pinned `@a2ui/web_core`, `@ag-ui/a2ui-middleware`, `@ag-ui/client`,
  `@copilotkit/a2ui-renderer`.

### Interactivity & the action round-trip (`interactiveCatalog.tsx`)

The basic catalog's inputs are controlled components whose `setValue` is a **no-op unless the
value is bound to a data-model `{ path }`** (`@a2ui/web_core` generic-binder), and a Button only
sends the paths its action context names. So imperfect LLM bindings caused two bugs: frozen inputs
and an empty submit payload. The catalog overrides fix both, independent of binding quality:

- **`TextField` / `CheckBox` / `ChoicePicker`** — keep local React state (always editable) and on
  every change write the value through `setValue` (bound path, when present) **and** to a fallback
  path `/__inputs/<componentId>` so it is always captured. `ChoicePicker` is the correct component
  for multi-select (stores a selections array).
- **`Button`** — dispatches via `context.dispatchAction(...)` using the required A2UI payload shape
  **`{ event: { name, context } }`**, where `context` includes the resolved named values **plus the
  complete data model** under `formData`. So the user's answers always reach the agent even if a
  context path was wrong/unwritten.

### Design — "Ink & Iris" (`app/globals.css`)

Generated surfaces are styled via scoped `.a2ui-surface` rules (external `!important` beats the
catalog's inline styles). Direction: a calm neutral card with an iris/indigo-violet accent
(`#5b5bd6`, not the default `#007bff`), tactile inputs with an iris focus ring, dark-mode variant
(`prefers-color-scheme`), reduced-motion support, and a faint iris-tinted ambient shadow as the
signature. (Follows the `frontend-design` skill.)

## Backend (`NTG.Agent.Orchestrator`)

The middleware ships the A2UI usage guide via the AG-UI **`context`** channel, which this backend
does not forward — so the model otherwise wouldn't know the component catalog. We inject our own
guide instead:

- **`Services/Agents/A2uiPrompt.cs`** (new) — `RenderToolName = "render_a2ui"` and `RenderGuide`,
  a focused A2UI v0.9 authoring guide (component catalog, binding rules, an interactive-form
  example, and how to read the user's answer from `formData`).
- **`Services/Agents/AgentService.cs`** — when the request's frontend tools include `render_a2ui`,
  insert `A2uiPrompt.RenderGuide` as a leading system message.

The `render_a2ui` tool itself flows through the existing frontend-tool path with no other change:
`AgUiRunRequest.Tools` → `FrontendToolDeclaration` → declared to the LLM → call surfaces as
`FunctionCallContent` → streamed as `TOOL_CALL_*`. No DB schema change, no new SSE event types, and
`RenderableToolCapture`/`CapturingAIFunction` stay dedicated to the weather card.

## Known limitations / not done

- **Reload rehydration** — A2UI surfaces render live but are not persisted/replayed on conversation
  reload (the weather card is). Future work: persist the `render_a2ui` call and replay it.
- **Binding consistency** still depends partly on the model; the Button's `formData` payload is the
  safety net that makes the round-trip work regardless.
- `deleteSurface` and streaming partial-update "heal" are not specifically exercised.

## Verification

- Builds: `dotnet build NTG.Agent.Orchestrator` and `npm run build` (in `my-copilot-app`) both pass.
- Contract check: middleware emits `activityType: "a2ui-surface"`, which `createA2UIMessageRenderer`
  binds to — confirmed identical.
- Manual e2e (run AppHost + the agent must have `render_a2ui` available):
  1. "build a small card with a heading and a button" → a styled surface renders.
  2. "make me a signup form with a name field and a subscribe checkbox" → type + toggle work.
  3. "ask my opinion with a 3-option multiple choice" → pick + submit; the agent receives the
     selection (in the action context / `formData`) and confirms it.
  4. The hardcoded weather card still works unchanged (comparison intact).

## References

- A2UI — What is A2UI: https://a2ui.org/introduction/what-is-a2ui/
- A2UI v0.9 spec: https://a2ui.org/specification/v0.9-a2ui/
- Google Developers Blog — Introducing A2UI: https://developers.googleblog.com/introducing-a2ui-an-open-project-for-agent-driven-interfaces/
- CopilotKit — Build with Google's A2UI + AG-UI: https://www.copilotkit.ai/blog/build-with-googles-new-a2ui-spec-agent-user-interfaces-with-a2ui-ag-ui
