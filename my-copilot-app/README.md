# NTG Agent — AI Chat Interface

A React chat interface built with Next.js (App Router) and CopilotKit that communicates with a .NET Aspire AI agent backend using the AG-UI protocol.

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | Next.js 16.2.7 (App Router, Turbopack) |
| UI | CopilotKit v1.59.5 (`@copilotkit/react-core` v2) |
| Protocol | AG-UI (`@ag-ui/client`) |
| Styling | Tailwind CSS v4 |
| Language | TypeScript (strict, ES2017 target) |
| Backend | .NET 10 + Aspire Orchestrator |

## Architecture

The frontend bridges CopilotKit's AG-UI event stream straight to the .NET backend's native AG-UI
endpoint — there is no translation layer. `HttpAgent` (from `@ag-ui/client`) is pointed directly at
the Orchestrator; two middlewares wrap it to add A2UI-specific behaviour on top of the raw AG-UI
stream.

```
Browser
  └─ CopilotKit React UI
       └─ POST /api/copilotkit/{agentUUID}          (Next.js catch-all route)
            └─ HttpAgent.run()                       (@ag-ui/client, wrapped in middleware)
                 ├─ StableSurfaceIdMiddleware         (optional — see A2UI_STABLE_SURFACE_IDS below)
                 └─ A2UIMiddleware                    (@ag-ui/a2ui-middleware)
                      └─ POST /api/agui/{agentUUID}   (.NET Orchestrator, AgUiController — native AG-UI/SSE)
                           └─ AG-UI SSE events → browser, already in AG-UI shape
```

> **Note — Option B, done:** the .NET backend exposes a native AG-UI/SSE endpoint
> (`AgUiController`, `POST /api/agui/{agentId}`), which is what `route.ts` talks to. The
> translation layer this note used to describe (`NtgAgent`, `_bufferUtils.ts`, a .NET
> `/api/agents/chat` multipart endpoint) is gone.

## Project Structure

```
my-copilot-app/
├── app/
│   ├── api/
│   │   ├── agents/
│   │   │   └── route.ts                   # Proxies agent catalog from .NET /api/agents
│   │   └── copilotkit/
│   │       └── [[...integrationId]]/
│   │           └── route.ts               # CopilotKit runtime endpoint: HttpAgent → /api/agui,
│   │                                       # A2UIMiddleware + StableSurfaceIdMiddleware, /threads stub
│   ├── layout.tsx                         # Root layout (fonts only — no CopilotKit wrapper)
│   └── page.tsx                           # Agent selector + CopilotChat component
├── src/
│   ├── a2ui/
│   │   ├── interactiveCatalog.tsx         # basicCatalog cloned with five components overridden
│   │   └── activityRenderer.ts            # createA2UIMessageRenderer, stable renderer array
│   ├── components/
│   │   └── AgentSelector.tsx              # Agent switcher dropdown
│   ├── tools/
│   │   ├── WeatherCardTool.tsx            # Hardcoded weather card (get_weather results)
│   │   └── SkillSurfaceTool.tsx           # Renders render_skill_surface, incl. reload rehydration
│   └── utils/
│       └── streamParser.ts                # Streaming/message parsing helpers
├── next.config.ts
├── tsconfig.json
└── package.json
```

## Communication Flow

1. **Agent discovery** — On mount, `page.tsx` calls `/api/agents`, which proxies `GET /api/agents` from the .NET orchestrator. The default agent is auto-selected.

2. **CopilotKit initialisation** — `<CopilotKit runtimeUrl="/api/copilotkit/{agentId}" agent="dotnet_orchestrator_agent">` mounts, triggering a `GET /api/copilotkit/{id}/info` discovery call and a `GET /api/copilotkit/{id}/threads` call (returns empty — no thread persistence).

3. **Message send** — CopilotKit POSTs to `/api/copilotkit/{agentId}`. The route handler builds an `HttpAgent` (`@ag-ui/client`) pointed at the Orchestrator's native AG-UI endpoint, wraps it in `A2UIMiddleware` (and, unless disabled, `StableSurfaceIdMiddleware`), and hands it to `CopilotRuntime`.

4. **HttpAgent.run()** — Posts the AG-UI `RunAgentInput` (thread id, messages, declared tools) as JSON straight to `POST /api/agui/{agentId}` on the .NET Orchestrator (`AgUiController`). The controller maps `threadId` to a conversation itself, creating one on first use — the frontend never calls `/api/conversations` before a message.

5. **Streaming** — `AgUiController` streams native AG-UI SSE events (`TEXT_MESSAGE_CONTENT`, `TOOL_CALL_*`, `ACTIVITY_SNAPSHOT`, …) directly — no intermediate JSON array and no client-side buffer parser. `A2UIMiddleware` further rewrites any `render_a2ui`/`render_skill_surface` tool result into `ACTIVITY_SNAPSHOT` events for the A2UI renderer.

6. **Anonymous rate limiting** — Enforced server-side: `AgentService` throws `AnonymousRateLimitExceededException` when an anonymous session is over its limit, and `AgUiController` catches it mid-run and emits an error event instead of a normal completion. There is no separate pre-flight rate-limit check on the frontend anymore.

## Key Implementation Notes

### `route.ts` — CopilotRuntime setup

- The `HttpAgent` (`@ag-ui/client`) is built directly inside `handleCopilotRequest` (not as an async factory) because `CopilotRuntime.handleServiceAdapter()` calls `Promise.resolve(agents)` — passing a function would resolve to the function itself, yielding an empty agent map.
- The backend agent UUID lives in the request URL (`/api/agui/{integrationId}`), baked straight into the `HttpAgent`'s own `url`. Unlike the old translation-layer design, nothing needs to survive CopilotKit's `agent.agentId = mapKey` overwrite in `handle-run.ts` — the id CopilotKit sees (`dotnet_orchestrator_agent`) is just a routing key.
- Middleware registration order matters: `use()` pushes onto a list `AbstractAgent` folds with `reduceRight`, so the middleware registered *first* wraps the rest and sees what they emit. `StableSurfaceIdMiddleware` (see `A2UI_STABLE_SURFACE_IDS` below) is registered before `A2UIMiddleware` so it can see the `ACTIVITY_SNAPSHOT` events the latter invents.
- The `GET /threads` sub-path returns `{ threads: [] }` to prevent a 405 from `ExperimentalEmptyAdapter`.

## Environment Variables

```bash
# Aspire service binding (auto-set when using AppHost)
services__ntg_agent_orchestrator__https__0="https://localhost:7093"

# Manual override
ORCHESTRATOR_URL="https://localhost:7093"

# Freeform A2UI. Unset (or anything other than 0/false/off) declares `render_a2ui`, letting the
# model hand-author a surface; the orchestrator then also prepends its A2uiPrompt.RenderGuide.
# Set to 0 for a deployment whose agents render only through Agent Skills' render_skill_surface —
# skill surfaces and A2UI rendering keep working either way.
A2UI_FREEFORM_TOOL="0"

# Stable skill-surface ids. Unset (or anything other than 0/false/off) makes a re-rendered
# render_skill_surface surface replace its existing chat card in place instead of stacking a new
# one (StableSurfaceIdMiddleware). Scoped to skill surfaces only — freeform render_a2ui always
# stacks, since its surface ids are model-invented and reused across unrelated requests. Set to 0
# to restore the pre-d534812 stacking behaviour for skill surfaces too.
A2UI_STABLE_SURFACE_IDS="0"
```

TLS verification is relaxed in development (`NODE_TLS_REJECT_UNAUTHORIZED=0`) to allow self-signed certificates over HTTPS.

## Getting Started

### Prerequisites

- Node.js 18+
- .NET backend running on the configured `ORCHESTRATOR_URL`

### Run standalone

```bash
npm install
npm run dev
```

Open [http://localhost:3000](http://localhost:3000).

### Run via .NET Aspire AppHost

```bash
dotnet run --project NTG.Agent.AppHost
```

The AppHost starts all services including the Next.js dev server.

## WSL2 Development Note

Turbopack on `/mnt/d/` (NTFS) does not receive inotify file-change events — hot reload does not work. After editing source files, restart the dev server manually:

```bash
# Kill the server, clear Turbopack cache, restart
rm -rf .next && npm run dev
```
