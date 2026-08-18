# A2UI and AG-UI — how a surface gets on screen

> Reference · Status: describes the code on `feature/agent-skill`

This is the explainer for the generative-UI pipeline. It assumes you know C# and React and have
never read either spec. After reading it you should be able to open any file named below and know
what you are looking at and why it is there.

The two implementation plans — `docs/A2UI-Implementation-Plan.md` and
`docs/Agent-Skills-Implementation-Plan.md` — record *what was built and in what order*. This
document records *how it works*. Where the plans and the code disagree, the code wins, and the
disagreements are called out in "Corrections to older docs" at the end.

---

## 1. The one-sentence version

**AG-UI is the transport; A2UI is the semantic layer.**

AG-UI is an event protocol for streaming an agent run to a browser: text deltas, reasoning deltas,
tool calls, tool results, run lifecycle. It has no opinion about UI. A2UI (Google's open
Agent-to-UI protocol, spec v0.9) is a JSON description of a UI: *surfaces*, a flat array of
*components* drawn from a *catalog*, a reactive *data model*, and *actions*. It has no opinion about
how those JSON messages reach the browser.

Put them together and the agent can describe a UI in JSON, that JSON rides a normal AG-UI tool-call
event, and a generic renderer paints it. Neither protocol knows about the other; the glue is one
npm package.

| | AG-UI | A2UI |
|---|---|---|
| Answers | "how does a running agent talk to a browser?" | "what does the browser draw?" |
| Unit | an *event* on an SSE stream | an *operation* on a surface |
| Vocabulary | `RUN_STARTED`, `TEXT_MESSAGE_*`, `TOOL_CALL_*`, `ACTIVITY_SNAPSHOT`, `RUN_FINISHED` | `createSurface`, `updateComponents`, `updateDataModel`, `deleteSurface` |
| Owned here by | `AgUiController.cs` (server), `@ag-ui/client` (browser) | `A2uiPrompt.cs` / surface templates (server), `@copilotkit/a2ui-renderer` (browser) |
| If it broke | the chat stops streaming | the chat still streams, surfaces stop rendering |

### The four packages

All four are direct dependencies of `my-copilot-app` (versions from `my-copilot-app/package.json`):

| Package | Version | What it does here |
|---|---|---|
| `@ag-ui/client` | `^0.0.53` | `HttpAgent` — connects to the orchestrator's SSE endpoint and turns the event stream into a message list. Also supplies the `Middleware` base class and the rxjs the middleware pipeline runs on. |
| `@ag-ui/a2ui-middleware` | `^0.0.6` | The glue. Recognises A2UI in the AG-UI stream (two ways — see §4) and re-emits it as `ACTIVITY_SNAPSHOT` events. Also injects the `render_a2ui` tool declaration and converts a user's surface interaction back into a `log_a2ui_event` tool call. |
| `@copilotkit/a2ui-renderer` | `^1.59.5` | The renderer: `A2UIProvider`, `A2UIRenderer`, `basicCatalog`, `createReactComponent`. Holds surface + data-model state and paints components. |
| `@a2ui/web_core` | `^0.9.0` | The protocol primitives: the `Catalog` class, the v0.9 JSON schemas, the data-model binder. The authoritative message shapes live at `node_modules/@a2ui/web_core/src/v0_9/schemas/server_to_client.json`. |

`@copilotkit/react-core/v2` sits above them with `createA2UIMessageRenderer`, which is what actually
binds an `ACTIVITY_SNAPSHOT` to a rendered card.

---

## 2. The pipeline, end to end

Two paths get A2UI into the browser. They differ only in **who writes the component JSON** (§4);
everything downstream of the SSE stream is shared.

```
 NTG.Agent.Orchestrator (.NET)                        my-copilot-app (Next.js)          browser
 ─────────────────────────────                        ────────────────────────          ───────
                                                    ┌──────────────────────────┐
 AgentService.ChatStreamingAsync                     │ /api/copilotkit/[id]     │
   ├── system msgs: A2uiPrompt.RenderGuide (if       │      route.ts            │
   │   render_a2ui declared) + SkillPrompt catalog   │                          │
   │                                                 │  HttpAgent ──────────────┼──► CopilotChat
   ▼                                                 │      │                   │
 LLM                                                 │      ▼                   │
   │                                                 │  A2UIMiddleware   (inner)│
   ├─ PATH A ─ calls render_a2ui  ────────┐          │      │                   │
   │  (frontend tool: declared, never     │          │      ▼                   │
   │   executed server-side, so it        │          │  StableSurfaceId  (outer)│
   │   surfaces as FunctionCallContent)   │          │  Middleware              │
   │                                      │          └──────────────────────────┘
   └─ PATH B ─ calls render_skill_surface │
      │  (server-side AIFunction)         │
      ▼                                   │
   SurfaceRenderFunction                  │
      ├ SkillRegistry.GetActiveSkillAsset │
      ├ Merge(values into template.data)  │
      ├ RenderableToolCapture ◄─ full ops │
      └ returns one-line receipt to model │
                                          │
 AgUiController (SSE, text/event-stream) ◄┘
   PATH A →  TOOL_CALL_START / TOOL_CALL_ARGS / TOOL_CALL_END        (name: render_a2ui)
   PATH B →  TOOL_CALL_START / ARGS / END  +  TOOL_CALL_RESULT
             where content = {"a2ui_operations":[ … ]}               (name: render_skill_surface)
                    │
                    ▼
   @ag-ui/a2ui-middleware
     PATH A: parses partial TOOL_CALL_ARGS as they stream, emits an ACTIVITY_SNAPSHOT
             as soon as the component array closes, again when `data` closes
     PATH B: tryParseA2UIOperations(TOOL_CALL_RESULT.content) → one ACTIVITY_SNAPSHOT per surface
     both:   activityType "a2ui-surface", messageId `a2ui-surface-<surfaceId>-<callId>`, replace:true
                    │
                    ▼
   StableSurfaceIdMiddleware (route.ts) — drops the `-<callId>` tail for skill surfaces only  (§6)
                    │
                    ▼
   createA2UIMessageRenderer  (src/a2ui/activityRenderer.ts, registered on
     <CopilotKit renderActivityMessages={…}> in app/page.tsx)
        → A2UIProvider holds surface + data-model state
        → paints through src/a2ui/interactiveCatalog.tsx                                      (§5)
                    │
                    │  user types / picks / clicks
                    ▼
   onAction → copilotkit.setProperties({ a2uiAction }) → copilotkit.runAgent()
        → RunAgentInput.forwardedProps.a2uiAction
        → A2UIMiddleware.processUserAction synthesises an assistant `log_a2ui_event`
          tool call + its tool result into the message list
        → POST /api/agui/{agentId}
        → AgUiController.ExtractPrompt turns that tool result into the next turn's prompt   (§5)
```

Two hops in that diagram post-date the drawing in `docs/A2UI-Implementation-Plan.md`: the whole
Path B branch (`render_skill_surface`), and `StableSurfaceIdMiddleware`.

### Where things live

| File | Role |
|---|---|
| `NTG.Agent.Orchestrator/Controllers/AgUiController.cs` | The AG-UI endpoint. Owns the SSE event vocabulary and the thread→conversation map. |
| `NTG.Agent.Orchestrator/Services/Agents/AgentService.cs` | Runs the model, registers tools, prepends the A2UI guide and the skill catalog, converts model output into `PromptResponse` chunks. |
| `NTG.Agent.Orchestrator/Services/Agents/A2uiPrompt.cs` | Path A: the entire A2UI authoring guide the model reads. Also names `render_a2ui` and `log_a2ui_event`. |
| `NTG.Agent.Orchestrator/Services/Skills/SurfaceRenderFunction.cs` | Path B: the `render_skill_surface` tool. Template lookup, value merge, operation emission. |
| `NTG.Agent.Orchestrator/Services/Skills/SurfaceValidator.cs` | Import-time validation of a skill's surface templates. |
| `NTG.Agent.Orchestrator/Services/Skills/A2uiCatalog.cs` | A C# snapshot of `basic_catalog.json` that the validator checks against. |
| `NTG.Agent.Orchestrator/Services/Agents/RenderableToolCapture.cs` | Side channel: server-side tool output destined for the browser, not for the model. |
| `my-copilot-app/app/api/copilotkit/[[...integrationId]]/route.ts` | The bridge. `HttpAgent` + the two middlewares + the feature switches. |
| `my-copilot-app/src/a2ui/activityRenderer.ts` | The **live** renderer (one stable module-scope instance). |
| `my-copilot-app/src/a2ui/interactiveCatalog.tsx` | The component catalog: `basicCatalog` with five components overridden. |
| `my-copilot-app/src/tools/SkillSurfaceTool.tsx` | The **reload** renderer for skill surfaces. |
| `my-copilot-app/src/utils/agentMessages.ts` | Rebuilds persisted tool calls into AG-UI messages on reload. |
| `my-copilot-app/app/globals.css` | `.a2ui-surface` scoped styling ("Ink & Iris"), external `!important` beating the catalog's inline styles. |

---

## 3. A2UI itself: surfaces, operations, components, data

A **surface** is one addressable piece of UI, identified by a `surfaceId`. Everything else is an
operation against a surface. There are four; three matter here.

The authoritative shapes are in
`my-copilot-app/node_modules/@a2ui/web_core/src/v0_9/schemas/server_to_client.json`. Every message
carries `"version": "v0.9"` and exactly one operation key — the schema is a `oneOf` with
`additionalProperties: false`, so a message with two operations in it is invalid.

```jsonc
// 1. Declare the surface and the vocabulary it will be drawn from.
{ "version": "v0.9",
  "createSurface": {
    "surfaceId": "trip-planner",
    "catalogId": "https://a2ui.org/specification/v0_9/basic_catalog.json"  // required
  } }

// 2. Give it components. Sent as often as you like; replaces the tree.
{ "version": "v0.9",
  "updateComponents": {
    "surfaceId": "trip-planner",
    "components": [ /* minItems: 1, one of them MUST have id "root" */ ] } }

// 3. Write into the reactive data model. `path` defaults to "/" (the whole model).
//    Omitting `value` deletes the key at `path`.
{ "version": "v0.9",
  "updateDataModel": { "surfaceId": "trip-planner", "path": "/form", "value": { "destination": "" } } }
```

(The fourth, `deleteSurface`, is in the schema and understood by the middleware, but nothing in this
repo emits it.)

**Components are flat, not nested.** Each is `{ "id": "...", "component": "TypeName", ...props }`
and children are referenced *by id* — `"children": ["a","b"]` for many, `"child": "a"` for one. A
flat array is what makes streaming possible: the middleware can parse a partially-received array and
emit a surface before the model has finished writing.

**The data model is the interactive half.** It is a JSON document per surface, addressed by JSON
Pointer paths. A component prop is *either* a literal *or* a binding object `{ "path": "/form/name" }`.
This is the single rule everything else hangs off, and `A2uiPrompt.cs` states it in capitals:

> Every editable input (TextField, CheckBox, Slider, DateTimeInput, ChoicePicker) binds through the
> SAME prop — always `value` — to a data-model path, AND you MUST seed that path in the `data`
> argument. **An input whose value is a literal (or missing) is FROZEN**: the user cannot type or
> toggle it, and nothing you put in `data` will pre-fill it.

The reason is mechanical, not stylistic: A2UI inputs are controlled components whose `setValue` is a
no-op unless the value came from a `{ path }` binding (`@a2ui/web_core`'s generic binder). An unbound
input renders, looks fine, and ignores the user. This is why the validator refuses templates with
unseeded bindings (§7), and why the catalog overrides keep local React state as a backstop (§5).

**The catalog** is the component vocabulary. We use the stock v0.9 `basic_catalog`, which has 18
components: `Text`, `Image`, `Icon`, `Divider`, `AudioPlayer`, `Video`, `Column`, `Row`, `List`,
`Card`, `Tabs`, `Modal`, `Button`, `TextField`, `CheckBox`, `Slider`, `DateTimeInput`,
`ChoicePicker`. The catalog schemas are strict — Zod `.strict()` on the component APIs and
`unevaluatedProperties: false` in the JSON schema — so an invented prop is a hard error, not a
silently ignored one. That strictness is load-bearing in §5.

Note the two audiences differ. `SurfaceValidator` accepts all 18 (via `A2uiCatalog.cs`), but
`A2uiPrompt.RenderGuide` lists only 14 to the model — no `Tabs`, `Modal`, `AudioPlayer` or `Video`.
So `Tabs` is in practice a Path B component: the travel and ticket skill templates use it, freeform
`render_a2ui` never will unless the guide is extended.

---

## 4. Path A vs Path B — the most important distinction

Both paths end in the same `ACTIVITY_SNAPSHOT` and the same renderer. They differ in who authors the
component JSON.

**Path A — `render_a2ui`.** The middleware injects a `render_a2ui` tool declaration into the run's
tool list. It is a *frontend tool*: `AgUiRunRequest.Tools` → `FrontendToolDeclaration` → declared to
the LLM but never executed server-side, so the model's call comes back as a `FunctionCallContent`
that `AgentService` forwards verbatim as `TOOL_CALL_*` events. The model writes the whole component
array itself, every time. To do that it needs the component catalog — which is why
`A2uiPrompt.RenderGuide` exists (§8).

**Path B — `render_skill_surface`.** A real server-side `AIFunction`
(`SurfaceRenderFunction`). The model names a skill and a surface template and supplies *values*; the
orchestrator loads the hand-authored template from the skill package, merges the values into its data
model, and emits the operations itself. The model never sees a component.

| | Path A — `render_a2ui` | Path B — `render_skill_surface` |
|---|---|---|
| Who writes the component JSON | LLM, every turn | Us, once, as a template |
| Layout consistency | varies per run | identical every run |
| Malformed JSON | whole surface fails | not possible |
| Bindings + `data` seeding | model must get both right | baked into the template |
| Streaming cost | large `TOOL_CALL_ARGS` | model emits a few values |

(That table is lifted from `docs/Agent-Skills-Implementation-Plan.md` §"A server-side tool can render
A2UI directly", where it was the finding that motivated Path B.)

**Why both exist.** Path A is the general capability: any agent, any request, no preparation —
"build me a card with the trade-offs side by side". It is also the fragile one, because it puts a
schema-strict JSON document in the model's hands. Path B is the reliable one, but only for UI
somebody wrote down in advance; it cannot answer a request nobody anticipated. The same coexistence
as the hardcoded weather card (`src/tools/WeatherCardTool.tsx`), which predates both and is kept as
the before/after comparison.

The rule the model is given, from `SkillPrompt.BuildCatalog`: *when a skill names a surface, always
use `render_skill_surface`; only use `render_a2ui` to build a surface by hand when no skill applies.*

**How the middleware tells them apart.** It does not need to. Path A is recognised by tool *name*
(`a2uiToolNames`, default `["render_a2ui"]`) and parsed out of streaming arguments. Path B is
recognised by tool *result shape*: the middleware runs `tryParseA2UIOperations` over every
`TOOL_CALL_RESULT` and acts on anything shaped `{"a2ui_operations": [...]}`. That is why Path B
needed **no frontend changes at all** to start rendering — it reuses `createA2UIMessageRenderer` and
`interactiveCatalog` unchanged.

**Two switches** in `route.ts` control the paths, both on by default, both `0|false|off` to disable:

- `A2UI_FREEFORM_TOOL` → `injectA2UITool`. Turning it off removes the `render_a2ui` declaration,
  which in turn stops `AgentService` prepending ~130 lines of authoring guide on every turn — dead
  weight for an agent whose bound skill owns the surface. Path B is unaffected.
- `A2UI_STABLE_SURFACE_IDS` → whether `StableSurfaceIdMiddleware` is installed (§6).

Both are per-deployment rather than per-agent because the route knows the agent id and nothing else:
skill bindings are server state, the only endpoint exposing them is Admin-only, and asking would add
a round trip to every chat turn.

### Path B's split audience

`SurfaceRenderFunction` is deliberately *not* built on `CapturingAIFunction` (the wrapper the weather
card uses). From its own docblock:

> That wrapper returns the inner result to the model unchanged, which would dump the whole surface
> JSON back into context on every render — hundreds of lines the model has no use for. Here the two
> audiences are split: the browser gets the full operations through `RenderableToolCapture`, and the
> model gets a one-line receipt.

The receipt is literally `"Rendered the '<surface>' surface. Do not describe it — the user can see
it. Stop now and wait for them to submit it."` Meanwhile `DrainRenderableToolCalls` turns the capture
into a synthetic `TOOL_CALL_*` + `TOOL_CALL_RESULT` pair on the SSE stream, and the result content is
the operations payload. Same tool call, two completely different payloads to two consumers.

Two details of the operation emission are worth knowing, both from comments in that file:

- **Never write the data model at `/`.** Writing the root *replaces* the entire data model, and the
  model holds more than the template seeds — the catalog overrides mirror every input the user
  touches to `/__inputs/<componentId>`. Replacing the root on a re-render therefore discards the
  user's own answers while the inputs on screen still show them: a surface that looks correct and has
  quietly lost its data. So the tool emits one `updateDataModel` per top-level branch instead. On a
  first render that is identical to a root write; on every render after it leaves untouched every
  branch we do not own.
- **The merge is kind-preserving.** A model-supplied value may overwrite a seeded path but may not
  change its *kind*. Swapping an object for a scalar removes every path beneath it, so the inputs
  bound there render frozen — the exact failure the validator exists to prevent, reintroduced at run
  time past the point validation reaches. A mismatch returns a readable error to the model instead of
  rendering: a frozen input looks like a working form that ignores the user, which is far worse than
  a message the model can act on and retry.

---

## 5. Interactivity: the action round trip

A2UI's interaction model is one-way per turn. The surface collects state in its data model; a
`Button` carries an `action: { event: { name, context } }`; clicking it dispatches that event with
the named context paths resolved. Getting that event back to the agent is AG-UI's job:

```
Button click
  → context.dispatchAction({ event: { name, context } })
  → A2UIProvider onAction  (identical code in activityRenderer's provider and SkillSurfaceTool)
  → copilotkit.setProperties({ ...properties, a2uiAction: message })
  → copilotkit.runAgent()                      // a new run, carrying forwardedProps
  → A2UIMiddleware.processUserAction() reads forwardedProps.a2uiAction.userAction and
    appends two synthetic messages: assistant{ toolCalls:[log_a2ui_event] } + tool{ result }
  → POST /api/agui/{agentId}
  → AgUiController.ExtractPrompt() sees the last message is role "tool" for log_a2ui_event
  → builds the next turn's prompt from it
  → the property is deleted in a finally block, so the next turn does not replay it
```

Note that the surface is **not** re-rendered by the round trip. The agent's reply either renders the
next surface or updates this one.

### Why a submission is not an approval

`AgUiController.ExtractPrompt` special-cases `log_a2ui_event` and it is worth understanding why,
because the failure was subtle. The generic tool-result wording was written for human-in-the-loop
tools — *"if approved, briefly confirm what changed; do not call the tool again"*. Applied to a
surface submission it does the opposite of what is needed: it asks for a text-only confirmation and
discourages rendering the next step. And because that text lands in the **user** turn — the most
recent, highest-salience position — it outranks any system message telling the model to continue a
multi-step flow.

Observed: step 2 of the travel skill survived (a search submission has no "approval" reading) while
step 3 did not — *"the user picked option 2"* maps exactly onto *"if approved, briefly confirm what
changed"*, so the model confirmed in prose and stopped, one surface short of finishing. Sending the
identical text as an ordinary user message rendered the final surface correctly, which is what
isolated the cause to the prompt rather than to the model or the skill.

The surface-specific wording instead says: read the submitted values, re-read the skill's
instructions, and carry out the next step it defines *including rendering the next surface*.

### Why `interactiveCatalog.tsx` overrides five components

The catalog is `basicCatalog` cloned with five components swapped: **`TextField`, `CheckBox`,
`ChoicePicker`, `Button`, `Tabs`**. Every override exists to work around the same structural fact —
the data model is the *only* channel between a component and anything else — in one of two
directions.

**Direction 1 (user → agent): the inputs and the Button.** Restating the problem from the file's
header comment: A2UI inputs only write to the data model when bound to a `{ path }`, and a Button
only sends the paths its action context names. So an imperfect binding — a missing one, or an input
path and a button path that do not match — silently loses the user's answer. The fix is
belt-and-braces:

- `TextField` / `CheckBox` / `ChoicePicker` keep **local React state**, so they are always editable
  regardless of binding, and on every change write the value **twice**: through `setValue` (the bound
  path, when there is one) *and* to a deterministic fallback path `/__inputs/<componentId>`.
- `Button` dispatches with the correct A2UI payload shape `{ event: { name, context } }`, where
  `context` carries the resolved named values **plus the complete data model** under `formData`.

So the user's answers reach the agent even when every binding is wrong. `A2uiPrompt.RenderGuide`
tells the model how to read them: check the named values first, then `formData.<bound path>`, then
`formData.__inputs` keyed by component id.

**Direction 2 (agent → user): `Tabs`.** The stock `Tabs` keeps its selected index in plain React
state, so the agent can neither read it nor change it — which makes a tabbed multi-step wizard
impossible. `InteractiveTabs` binds that index to the data model over a *convention path*,
`/__tabs/<componentId>`. The reason it is a convention path and not a declared prop is the single
best protocol passage in the frontend:

> `TabsApi.schema` is `.strict()` and the catalog JSON schemas are `unevaluatedProperties: false`, so
> a `value`/`selectedIndex` prop would be rejected as an unknown property unless we forked the schema
> — and a forked schema drifts from the one the agent is prompted with. An override, by contrast, may
> read and write the data model at ANY path (`dataContext.subscribeDynamicValue` / `dataModel.get` /
> `.set`), with no schema involvement at all. So the channel lives in the data model, out of band,
> and costs nothing when unused: a surface that never mentions `/__tabs` behaves exactly as stock.

That is the general lever worth remembering: **the schema constrains what the agent may author; it
does not constrain what a renderer override may do with the data model.** Anything you need that the
catalog has no prop for goes in a `/__`-prefixed convention path.

The two directions are kept deliberately separate:

- `/__tabs` is **agent → user** (intent). The agent writes it; the component re-syncs only when a
  genuinely *new* value arrives on the path, compared against the last value that arrived rather than
  against what is on screen — otherwise a user clicking back to tab 0 would be dragged to tab 1 by
  every subsequent re-render. The user's own clicks never write back to it.
- `/__inputs` is **user → agent** (state). `Tabs` reports where the user is through the same
  `captureValue` every other input uses, so the current tab rides along in `formData`. It mirrors
  `{ index, title }` rather than a bare index, because an index is meaningless to a model reading
  `formData` ("tiers: 2" — two of what?) while the title is the label the user actually saw.

One more piece of user-facing pragmatism lives in `InteractiveButton`: a step button in a tabbed flow
reads as "next", not "submit", so the tab advances **locally on click**, before the agent has
answered. Waiting for the round trip means ten to twenty seconds sitting on a form the user has
finished with, with no signal that the click registered. The agent's own later `/__tabs` write lands
on the tab we already moved to and is a no-op. The `advanceTab` key that drives this is stripped from
the payload rather than sent to the agent as if it were an answer.

---

## 6. Card identity: why one surface is one card

This is the part that is easy to get wrong and hard to debug.

The chain of identity is:

```
ACTIVITY_SNAPSHOT.messageId
  → @ag-ui/client resolves the event by messageId: a hit REPLACES that message in place,
    a miss APPENDS a new one
  → CopilotKit renders each activity message with key={message.id}
  → an unchanged key keeps the same React element, the same A2UIProvider, the same surface store
```

So the message id *is* the card, and it is also the store. A changed id is a new card with an empty
data model.

The middleware keys its snapshots `a2ui-surface-<surfaceId>-<callId>`. The call id in that key is the
entire reason a second render of one surface used to open a second card, stacking down the
transcript, with the user's answers stranded in the first one's store. The middleware does have a
call-id-free form of the id, but cannot reach it on this path: it keys by `outerCallId ?? toolCallId`,
and because `render_skill_surface` is neither an A2UI tool name nor `log_a2ui_event`, the middleware
classifies it as the "outer" call — so `outerCallId` is always set and the id is never bare.

`StableSurfaceIdMiddleware` in `route.ts` fixes this by renaming the event to
`a2ui-surface-<surfaceId>`, which is what `react-core`'s own A2UI renderer documents as the model:
"one stable messageId … rendered in place". Three details of its design are deliberate:

- **It reads the surface id out of the operations**, not by stripping the last dash-delimited segment
  of the message id — surface ids are authored names like `trip-planner`, so stripping would eat half
  the id. It then rebuilds the prefix and requires an exact match; that match is the only proof that
  the remainder really is a call id.
- **Skill surfaces only.** Collapsing two renders that share a surface id is right when the id was
  *authored* — a skill's template owns its surface id and A2UI treats that id as the surface's
  identity, so re-rendering `trip-planner` is by definition the same surface. Ids in freeform
  `render_a2ui` are invented by the model turn by turn and get reused across unrelated requests,
  where collapsing would silently rewrite a card further up the transcript. It distinguishes them by
  tracking `TOOL_CALL_START` events in the same stream and renaming only when the call id belongs to
  a `render_skill_surface` call.
- **Registration order matters.** `agent.use()` pushes onto a list that `AbstractAgent` folds with
  `reduceRight`, so the middleware registered **first** wraps the rest and sees what they emit.
  `StableSurfaceIdMiddleware` is registered *before* `A2UIMiddleware` precisely so it can see the
  `ACTIVITY_SNAPSHOT`s that `A2UIMiddleware` invents; registered after, it would only ever see the
  raw orchestrator stream, where those events do not exist yet.

It also swallows its own errors: it sits in front of every event of every chat, so a malformed event
must cost one un-renamed surface, not the run.

Set `A2UI_STABLE_SURFACE_IDS=off` and the stack behaves exactly as it did before — one card per
render. The switch exists because this changes *where* a re-rendered surface appears in the
transcript (in place, rather than appended), which is a judgement call about the conversation rather
than an uncontroversial bug fix.

---

## 7. Live vs reload: two renderers for one surface

There are two renderers because there are two ways a surface reaches the screen, and only one of them
involves the event stream.

| | Live run | Page reload |
|---|---|---|
| Source | the SSE event stream | `GET /api/conversations/{id}/messages`, mapped by `src/utils/agentMessages.ts` |
| A2UI arrives as | `ACTIVITY_SNAPSHOT` (invented by the middleware) | a persisted assistant tool call + tool result |
| Painted by | `createA2UIMessageRenderer` (`src/a2ui/activityRenderer.ts`) | `useRenderTool` (`src/tools/SkillSurfaceTool.tsx`) |
| Applies to | Path A **and** Path B | Path B only |

The middleware's parsing runs over the event stream **only**. On reload, `agentMessages.ts` rebuilds
the persisted tool call and its result but there is no activity message — so without a renderer for
the tool itself the surface vanished and the raw operations JSON (often ~100 KB) fell through to
CopilotKit's default tool-call card.

`SkillSurfaceTool.tsx` replays the stored operations through the very same A2UI pieces the live path
uses — `A2UIProvider`, `interactiveCatalog`, `A2UIRenderer`, the same `onAction` bridge — so a
reloaded conversation shows the real, interactive surface again, not a screenshot of one. Its
complexity is almost entirely about *standing down* so nothing renders twice:

- During a live run the middleware has already turned this exact result into an activity the other
  renderer is painting, so the tool renderer suppresses itself when it finds an activity message
  whose id ends in `-<toolCallId>`.
- When the id has been stabilised (§6), it suppresses per surface instead: an activity keyed
  `a2ui-surface-<surfaceId>` serves every render of that surface.
- On reload, rendering one surface three times leaves three persisted tool calls carrying the same
  surface id. Only the **last** call per surface paints, so reload agrees with the live view —
  content-wise exactly (the last render is the current state), position-wise at the last render
  rather than the first.
- If the operations cannot be parsed or the provider rejects them, it draws a small summary card
  ("ask the assistant to show it again") and *never* the raw JSON.

Freeform `render_a2ui` surfaces are still not rehydrated: nothing persists them, because the
declaration-only frontend-tool path never produces a server-side tool result to store.

---

## 8. What is checked, and where

Path A and Path B fail in different places, so they are defended in different places.

**Path A — at prompt time.** `A2uiPrompt.RenderGuide` is the whole defence: a ~130-line authoring
guide injected as a leading system message by `AgentService`, but only when `render_a2ui` is among
the run's declared frontend tools. Its opening docblock explains why it exists at all:

> The CopilotKit/AG-UI A2UI middleware declares the `render_a2ui` tool to the model and turns its
> streamed arguments into UI on the client, but the component catalog the model needs lives in the
> AG-UI `context` channel which this backend does not forward.

That is exact. `A2UIMiddleware` puts its usage guidance on `RunAgentInput.context` and its flag on
`forwardedProps`; `AgUiRunRequest` binds only `threadId`, `runId`, `messages` and `tools`, so both are
dropped unread. Of `injectA2UITool`'s three effects, **only the tool declaration in `tools` survives
the trip to this backend.** Everything the model actually knows about A2UI comes from
`A2uiPrompt.cs`.

Besides the component reference, the guide carries a design section — Text `variant` for hierarchy,
`Row justify: "spaceBetween"` for label/value pairs, one primary Button, Divider between real sections
— because with colour, spacing and fonts owned by `globals.css`, *structure* is the only thing the
model controls, and it is the difference between a surface that looks designed and one that looks
thrown together.

**Path B — at import time and at render time.** `SurfaceValidator` runs when a skill package is
uploaded, so a malformed template is a rejected upload with a line-item error list rather than a
blank card during a demo:

> Every check here corresponds to a way a surface can silently fail to render: an unresolvable child
> reference draws nothing, an unknown property is dropped by the catalog, and — the subtle one — an
> input bound to a data path that was never seeded renders frozen, because A2UI inputs are controlled
> components whose setter is a no-op until the path exists.

It returns **all** failures rather than the first, on the theory that an author fixing one error per
upload round-trip will start disabling checks instead. It checks against `A2uiCatalog.cs`, a C#
snapshot of `basic_catalog.json` (the orchestrator has no access to the frontend's `node_modules`);
`A2uiCatalogDriftTests` re-derives that table from the real schema when the file is present and fails
on divergence, so bumping the npm package surfaces as a test failure rather than a mystery import
rejection. Render-time checks are the kind-preserving merge described in §4.

---

## 9. Things that will surprise you

- **`weight` is in the schema and does nothing.** `CatalogComponentCommon.weight` (a flex-grow
  analogue for direct children of a Row or Column) is present in `basic_catalog.json` and in the Zod
  APIs, so a template using it validates — but nothing in `@copilotkit/a2ui-renderer` reads it, and
  there is no `flexGrow` anywhere in the rendering path. Setting it has no visual effect.
- **The middleware's bundled A2UI prompt is never injected.** `@ag-ui/a2ui-middleware` exports an
  `A2UI_PROMPT` constant containing a component reference with wrong property names. It is exported
  and nothing inside the package references it. The text the middleware *does* inject when
  `injectA2UITool` is on is a different constant (`RENDER_A2UI_TOOL_GUIDELINES`), and that one rides
  the `context` channel this backend drops. Neither competes with `A2uiPrompt.RenderGuide`.
- **A surface never "submits" like a form.** There is no form POST; a click is a new agent run
  carrying a synthetic tool call. If a surface appears to do nothing on click, look at
  `forwardedProps` and `ExtractPrompt`, not at the renderer.
- **`updateDataModel` with no `path` means the whole model**, and writing it replaces everything —
  including `/__inputs`, i.e. the user's answers (§4).
- **A skill's instructions do not survive the turn.** A loaded skill body is never persisted into
  conversation history, so on the turn after a surface submission the model holds only the tier-1
  catalog. `SkillPrompt.BuildCatalog` therefore states in bold that the model must call `load_skill`
  again before continuing — without it, the model narrated raw data-model paths back to the user and
  skipped the flow's final surface.
- **`render_a2ui` is recognised regardless of who declared it.** The middleware matches on tool name
  (`a2uiToolNames`), not on having injected the declaration itself — so the declaration could be moved
  server-side without touching the frontend.

---

## 10. References

Upstream specs:

- A2UI — What is A2UI: https://a2ui.org/introduction/what-is-a2ui/
- A2UI v0.9 specification: https://a2ui.org/specification/v0.9-a2ui/
- Google Developers Blog — Introducing A2UI: https://developers.googleblog.com/introducing-a2ui-an-open-project-for-agent-driven-interfaces/
- CopilotKit — Build with Google's A2UI + AG-UI: https://www.copilotkit.ai/blog/build-with-googles-new-a2ui-spec-agent-user-interfaces-with-a2ui-ag-ui
- Agent Skills specification: https://agentskills.io/specification

In-repo schemas (the authority when a doc and a schema disagree):

- `my-copilot-app/node_modules/@a2ui/web_core/src/v0_9/schemas/server_to_client.json` — the four operations
- `my-copilot-app/node_modules/@a2ui/web_core/src/v0_9/schemas/basic_catalog.json` — every component and prop

Sibling documents:

- `docs/A2UI-Implementation-Plan.md` — how Path A was built
- `docs/Agent-Skills-Implementation-Plan.md` — how skills and Path B were built
- `docs/agents-as-tools-architecture.md` — the surrounding agent/tool model

### Claims that have expired

These were all true at some point and are written down somewhere. If you meet one in an older
document, a commit message or a code comment, this is the current position:

| You may read that… | …but today |
|---|---|
| a re-rendered surface opens a new card; multi-step flows stack | `StableSurfaceIdMiddleware` collapses skill surfaces onto one card (§6). On by default; `A2UI_STABLE_SURFACE_IDS=off` restores stacking, which is still a legitimate wizard-trail presentation |
| `interactiveCatalog` overrides four components | Five — `Tabs` was added (§5) |
| A2UI surfaces are not replayed on reload | Skill surfaces are (`SkillSurfaceTool.tsx`, §7). Freeform `render_a2ui` surfaces still are not |
| the middleware's own A2UI prompt competes with ours | It is never injected on this path (§9) |
| a well-formed `values` payload of the wrong shape can still freeze an input | The merge is kind-preserving and refuses with a readable error instead (§4) |
| `render_a2ui` is declared on every run of every agent | `A2UI_FREEFORM_TOOL` turns the declaration — and with it the ~130-line guide — off per deployment (§4). A per-agent switch is still open work |
