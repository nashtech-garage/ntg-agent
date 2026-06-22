# Agents as Tools Architecture

## Overview

The Agents as Tools feature allows outer (user-facing) agents to delegate work to specialized inner agents at runtime. Inner agents are presented to the LLM as callable functions (tools), alongside built-in functions and MCP tools.

This architecture uses a **single `Agents` table** for both agent kinds, discriminated by the `AgentKind` column. There is no separate table or entity for inner agents — they reuse the same model, DTOs, controller endpoints, and Blazor UI components as outer agents.

## Data Model

### `Agents` table

All agents share the same schema. The `AgentKind` column (`int`, `0` = Outer, `1` = Inner) is the discriminator.

```
Agent
├── Id (Guid PK)
├── Name, Description, Instructions
├── ProviderName, ProviderEndpoint, ProviderApiKey, ProviderModelName
├── McpServer (nullable)
├── Mode (AgentMode: Fast=0, Thinking=1) — forced to Fast for Inner agents
├── AgentKind (AgentKind: Outer=0, Inner=1)
├── IsPublished, IsDefault
├── CreatedAt, UpdatedAt
├── OwnerUserId, UpdatedByUserId (FK → Users)
├── AgentTools (1:N) — built-in + MCP tool assignments
├── InnerAgentBindings (1:N) — when this agent is the Outer agent
└── OuterAgentBindings (1:N) — when this agent is used as an Inner agent
```

### `AgentInnerAgents` join table

Links outer agents to the inner agents they can call as tools.

```
AgentInnerAgent
├── OuterAgentId (FK → Agents, cascade delete)
├── InnerAgentId (FK → Agents, restrict delete)
├── IsEnabled (bool)
├── CreatedAt, UpdatedAt
└── Composite PK: (OuterAgentId, InnerAgentId)
```

## Runtime Flow

```
User Chat Request
    │
    ▼
AgentService (chat streaming)
    │
    ▼
AgentFactory.CreateAgent(agentId)
    │  Loads agent config (must be IsPublished && AgentKind.Outer)
    │
    ▼
CreateAgentFromConfigAsync(agentConfig)
    │  Creates AIAgent with provider-specific chat client
    │
    ▼
GetAgentToolsByAgentId(agent)
    │  Collects enabled built-in tools + MCP tools
    │
    ├── agent.AgentKind == AgentKind.Outer?
    │       │
    │       ▼
    │   GetInnerAgentToolsAsync(outerAgent)
    │       │  Queries AgentInnerAgents for enabled bindings
    │       │  Loads each inner agent (AgentKind.Inner)
    │       │  Creates AIAgent per inner agent
    │       │  Wraps each via agent.AsAIFunction()
    │       │
    │       ▼
    │   [Inner agent tools added to tool list]
    │
    ▼
AIAgent with all tools → LLM call
    LLM sees inner agents as callable functions
    LLM can delegate sub-tasks to inner agents
```

**Key constraint:** Inner agents are always `AgentMode.Fast`. They cannot use Thinking mode because they are called synchronously as tools and must return results quickly.

## DTOs

| DTO | Purpose |
|---|---|
| `AgentDetail` | Full agent for CRUD. Contains `AgentKind`, `Mode`, `ToolCount`, and all provider/configuration fields. Used for both Outer and Inner agents. |
| `AgentListItem` | List view record: `Id`, `Name`, `OwnerEmail`, `UpdatedByEmail`, `UpdatedAt`, `IsDefault`, `IsPublished`, `AgentKind` |
| `AgentListItemDto` | Public/chat list: `Id`, `Name`, `IsDefault`, `Mode` |
| `AgentToolDto` | Tool configuration: `Id`, `AgentId`, `Name`, `Description`, `IsEnabled`, `AgentToolType` |
| `InnerAgentBindingDto` | Binding between outer and inner agent: `InnerAgentId`, `Name`, `Description`, `ProviderModelName`, `IsEnabled` |

There is **no separate `InnerAgentDetail` or `InnerAgentListItem` DTO**. Both agent kinds use the same DTOs.

## API Endpoints

All agent management under `api/agentadmin` (Admin role required).

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/` | List agents. Optional `?agentKind=0\|1` query param |
| `GET` | `/{id}` | Get agent detail |
| `POST` | `/` | Create agent. Reads `AgentKind` from body |
| `PUT` | `/{id}` | Update agent |
| `DELETE` | `/{id}` | Delete agent (removes bindings for inner; checks documents for outer) |
| `PATCH` | `/{id}/publish` | Toggle publish status |
| `GET` | `/{id}/tools` | Get tools (built-in + MCP) |
| `PUT` | `/{id}/tools` | Update tool configuration |
| `POST` | `/{id}/connect` | Connect to MCP server |
| `GET` | `/{id}/inner-agents` | Get inner agent bindings for an outer agent |
| `PUT` | `/{id}/inner-agents` | Update inner agent bindings for an outer agent |

## Blazor UI Components

### Unified Pages

| Page | Routes | Purpose |
|---|---|---|
| `Home.razor` | `/` | Dashboard with tabs: All / Outer / Inner agents. Card grid with kind badges |
| `AddAgent.razor` | `/agents/new`, `/agents/duplicate/{id}`, `/inner-agents/new` | Create/duplicate agent. Shows Agent Kind selector (Outer/Inner) for new agents. Hides Mode for Inner. |
| `AgentDetails.razor` | `/agents/{id}` | Detail view with tabs: Settings / Tools / Knowledge Base |

### Components

| Component | Used by | Purpose |
|---|---|---|
| `AgentSettingsTab.razor` | AgentDetails | Edit provider, description, system prompt, Mode |
| `ToolManagementTab.razor` | AgentDetails | MCP connection, Built-in tools, Inner Agent bindings (dual sub-tab) |
| `InnerAgentToolManagementTab.razor` | AgentDetails | MCP skills binding (uses `AgentDetail` parameter) |
| `DeleteAgentConfirmationModal.razor` | Home | Confirm delete dialog |

### NavMenu

Single "Agent Management" entry pointing to `/`. The dashboard tabs handle filtering between Outer and Inner agents.

## Key Design Decisions

1. **Single table, single entity** — No separate `InnerAgent` table/class. The `AgentKind` discriminator keeps the schema simple and avoids code duplication.

2. **Shared DTOs** — `AgentDetail` serves both Outer and Inner agents. Fields like `IsPublished` and `Mode` are always present but treated as outer-agent-specific in the UI.

3. **Unified API** — No `/inner` sub-routes. Agent kind is a property of the agent, not a separate resource. The `CreateAgent` endpoint reads `AgentKind` from the request body instead of hardcoding it.

4. **Unified UI** — One create page, one list page. The Agent Kind selector on the create form and the filter tabs on the dashboard handle the distinction.

5. **Inner agents are Fast mode only** — The controller enforces `Mode = AgentMode.Fast` when `AgentKind == Inner`, and the UI hides the Mode selector for inner agents. This is because inner agents run synchronously as function calls and must not produce streaming reasoning output.

6. **Bindings, not hierarchy** — Inner agents are not "children" of outer agents. They are reusable tools that any outer agent can bind to. The `AgentInnerAgents` join table enables many-to-many relationships with per-binding enable/disable.

7. **Inner agents support document upload** — The knowledge base (documents, folders, Kernel Memory) is fully available to inner agents. The `DocumentsController`, `FoldersController`, and `KnowledgeService` operate by `AgentId` with no `AgentKind` restrictions. The Knowledge Base tab appears on the detail page for all agent kinds, and default folders are created for inner agents at creation time.
