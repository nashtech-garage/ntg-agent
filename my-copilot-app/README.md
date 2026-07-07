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

The frontend acts as a translation layer between CopilotKit's AG-UI event stream and the .NET backend's custom streaming format.

```
Browser
  └─ CopilotKit React UI
       └─ POST /api/copilotkit/{agentUUID}          (Next.js catch-all route)
            └─ NtgAgent.run()                        (AbstractAgent subclass)
                 └─ POST /api/agents/chat            (.NET Orchestrator)
                      └─ IAsyncEnumerable<PromptResponse>  (streaming JSON)
                           └─ AG-UI SSE events → browser
```

> **Note — Option B (future):** The .NET backend can expose a native AG-UI/SSE endpoint using the `Microsoft.Extensions.AgentFramework.AgUI` NuGet package, eliminating this translation layer entirely.

## Project Structure

```
my-copilot-app/
├── app/
│   ├── api/
│   │   ├── agents/
│   │   │   └── route.ts                   # Proxies agent catalog from .NET /api/agents
│   │   └── copilotkit/
│   │       └── [[...integrationId]]/
│   │           ├── route.ts               # CopilotKit runtime endpoint + /threads stub
│   │           ├── _ntgAgent.ts           # Custom AbstractAgent: calls .NET chat endpoint
│   │           ├── _conversationStore.ts  # Creates/caches conversations per session
│   │           └── _bufferUtils.ts        # Parses streaming JSON array from .NET
│   ├── layout.tsx                         # Root layout (fonts only — no CopilotKit wrapper)
│   └── page.tsx                           # Agent selector + CopilotChat component
├── src/
│   └── components/
│       └── AgentSelector.tsx              # Agent switcher dropdown
├── next.config.ts
├── tsconfig.json
└── package.json
```

## Communication Flow

1. **Agent discovery** — On mount, `page.tsx` calls `/api/agents`, which proxies `GET /api/agents` from the .NET orchestrator. The default agent is auto-selected.

2. **CopilotKit initialisation** — `<CopilotKit runtimeUrl="/api/copilotkit/{agentId}" agent="dotnet_orchestrator_agent">` mounts, triggering a `GET /api/copilotkit/{id}/info` discovery call and a `GET /api/copilotkit/{id}/threads` call (returns empty — no thread persistence).

3. **Message send** — CopilotKit POSTs to `/api/copilotkit/{agentId}`. The route handler creates an `NtgAgent` instance (carrying the backend UUID and session cookie), then hands it to `CopilotRuntime`.

4. **NtgAgent.run()** — Before the first message on a session, creates a conversation via `POST /api/conversations`. Then POSTs to `/api/agents/chat` as `multipart/form-data`:

   | Field | Value |
   |---|---|
   | `Prompt` | Last user message text |
   | `ConversationId` | Guid from conversation creation |
   | `SessionId` | UUID from `ntg_session_id` cookie |
   | `AgentId` | Backend agent UUID (from the URL path) |

5. **Streaming** — The .NET backend returns `IAsyncEnumerable<PromptResponse>` serialised as a JSON array `[{"content":"...","contentType":0},...]`. `_bufferUtils.ts` parses chunks incrementally and emits `TEXT_MESSAGE_CONTENT` AG-UI events.

6. **Anonymous rate limiting** — Before each message, `GET /api/conversations/anonymous/rate-limit-status` is checked. If the limit is reached, a local rate-limit message is emitted without calling the backend.

## Key Implementation Notes

### `_ntgAgent.ts` — NtgAgent

Extends `AbstractAgent` from `@ag-ui/client`. Two important design decisions:

- **`_ntgBackendAgentId`** — CopilotKit's `handle-run.ts` explicitly does `agent.agentId = mapKey` after cloning, overwriting it with `"dotnet_orchestrator_agent"`. The real backend UUID is stored in a private field `_ntgBackendAgentId` that CopilotKit never touches.

- **`clone()` override** — `AbstractAgent.clone()` uses `Object.create()` and only copies its own fields. The override copies `_ntgBackendAgentId` and `cookieHeader` so they survive cloning.

### `route.ts` — CopilotRuntime setup

- `NtgAgent` is created directly inside `handleCopilotRequest` (not as an async factory) because `CopilotRuntime.handleServiceAdapter()` calls `Promise.resolve(agents)` — passing a function would resolve to the function itself, yielding an empty agent map.
- The `GET /threads` sub-path returns `{ threads: [] }` to prevent a 405 from `ExperimentalEmptyAdapter`.

## Environment Variables

```bash
# Aspire service binding (auto-set when using AppHost)
services__ntg_agent_orchestrator__https__0="https://localhost:7093"

# Manual override
ORCHESTRATOR_URL="https://localhost:7093"
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
