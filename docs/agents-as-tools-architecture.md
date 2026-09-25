# Agents as Tools Architecture

## Overview

The Agents as Tools feature allows agents to delegate work to specialized sub-agents at runtime. Sub-agents are presented to the LLM as callable functions (tools), alongside built-in functions and MCP tools.

This architecture uses a **single `Agents` table** for both agent kinds, discriminated by the `AgentKind` column. There is no separate table or entity for sub-agents — they reuse the same model, DTOs, controller endpoints, and Blazor UI components as agents.

## Data Model

### `Agents` table

All agents share the same schema. The `AgentKind` column (`int`, `0` = Outer, `1` = Inner) is the discriminator.

```
Agent
├── Id (Guid PK)
├── Name, Description, Instructions
├── ProviderName, ProviderEndpoint, ProviderApiKey, ProviderModelName
├── McpServer (nullable)
├── Mode (AgentMode: Fast=0, Thinking=1) — forced to Fast for Sub-agents
├── AgentKind (AgentKind: Outer=0, Inner=1)
├── IsPublished, IsDefault
├── CreatedAt, UpdatedAt
├── OwnerUserId, UpdatedByUserId (FK → Users)
├── AgentTools (1:N) — built-in + MCP tool assignments
├── SubAgentBindings (1:N) — when this agent owns sub-agent bindings
└── AgentBindings (1:N) — when this agent is used as a Sub-agent
```

### `AgentSubAgents` join table

Links agents to the sub-agents they can call as tools.

```
AgentSubAgent
├── AgentId (FK → Agents, cascade delete)
├── SubAgentId (FK → Agents, restrict delete)
├── IsEnabled (bool)
├── CreatedAt, UpdatedAt
└── Composite PK: (AgentId, SubAgentId)
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
    │  Loads agent config (must be IsPublished && AgentKind.Agent)
    │
    ▼
CreateAgentFromConfigAsync(agentConfig)
    │  Creates AIAgent with provider-specific chat client
    │
    ▼
GetAgentToolsByAgentId(agent)
    │  Collects enabled built-in tools + MCP tools
    │
    ├── agent.AgentKind == AgentKind.Agent?
    │       │
    │       ▼
    │   GetSubAgentToolsAsync(agent)
    │       │  Queries AgentSubAgents for enabled bindings
    │       │  Loads each sub-agent (AgentKind.SubAgent)
    │       │  Creates AIAgent per sub-agent
    │       │  Wraps each via agent.AsAIFunction()
    │       │
    │       ▼
    │   [Sub-agent tools added to tool list]
    │
    ▼
AIAgent with all tools → LLM call
    LLM sees sub-agents as callable functions
    LLM can delegate sub-tasks to sub-agents
```

**Key constraint:** Sub-agents are always `AgentMode.Fast`. They cannot use Thinking mode because they are called synchronously as tools and must return results quickly.

## DTOs

| DTO | Purpose |
|---|---|
| `AgentDetail` | Full agent for CRUD. Contains `AgentKind`, `Mode`, `ToolCount`, and all provider/configuration fields. Used for both Agents and Sub-agents. |
| `AgentListItem` | List view record: `Id`, `Name`, `OwnerEmail`, `UpdatedByEmail`, `UpdatedAt`, `IsDefault`, `IsPublished`, `AgentKind` |
| `AgentListItemDto` | Public/chat list: `Id`, `Name`, `IsDefault`, `Mode` |
| `AgentToolDto` | Tool configuration: `Id`, `AgentId`, `Name`, `Description`, `IsEnabled`, `AgentToolType` |
| `SubAgentBindingDto` | Binding between an agent and sub-agent: `SubAgentId`, `Name`, `Description`, `ProviderModelName`, `IsEnabled` |

There is **no separate sub-agent detail or list-item DTO**. Both agent kinds use the same DTOs.

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
| `GET` | `/{id}/sub-agents` | Get sub-agent bindings for an agent |
| `PUT` | `/{id}/sub-agents` | Update sub-agent bindings for an agent |
| `GET` | `/{agentId}/access` | List the roles granted access to an agent |
| `POST` | `/{agentId}/access` | Grant a role access to an agent |
| `DELETE` | `/{agentId}/access/{roleId}` | Revoke a role's access |
| `GET` | `/roles` | List assignable roles (for the Access tab) |

## Blazor UI Components

### Unified Pages

| Page | Routes | Purpose |
|---|---|---|
| `Home.razor` | `/` | Dashboard with tabs: All / Agents / Sub-agents. Card grid with kind badges |
| `AddAgent.razor` | `/agents/new`, `/agents/duplicate/{sourceAgentId:guid}` | Create/duplicate agent. Shows Agent Kind selector (Outer/Inner) for new agents. Hides Mode for Inner. |
| `AgentDetails.razor` | `/agents/{id:guid}` | Detail view with tabs: Settings / Tools / Knowledge Base / Access |

### Components

| Component | Used by | Purpose |
|---|---|---|
| `AgentSettingsTab.razor` | AgentDetails | Edit provider, description, system prompt, Mode. Hosts `ProviderConfigSection.razor` |
| `ToolManagementTab.razor` | AgentDetails | Three sub-tabs: **MCP & Built-in Tools**, **Sub-Agents** (agents only), **Skills** |
| `SubAgentToolManagementTab.razor` | AgentDetails | Tool management for sub-agents (uses `AgentDetail` parameter) |
| `DocumentsTab.razor` | AgentDetails | Knowledge base: folders, uploads, LightRAG ingestion |
| `AgentAccessTab.razor` | AgentDetails | Grant/revoke role access to the agent |
| `DeleteAgentConfirmationModal.razor` | Home | Confirm delete dialog |

### NavMenu

Agents live at `/`, and the dashboard's All / Agents / Sub-Agents tabs handle filtering
between the two kinds. The other admin areas are siblings, not children of it:

| Entry | Route |
|---|---|
| Agents (dashboard) | `/` |
| Users & Roles | `/users-roles-management` |
| Tags | `/tags-management` |
| Skills | `/skills-management` |
| Token Usage | `/token-usage` |

## Key Design Decisions

1. **Single table, single entity** — No separate sub-agent table/class. The `AgentKind` discriminator keeps the schema simple and avoids code duplication.

2. **Shared DTOs** — `AgentDetail` serves both Agents and Sub-agents. Fields like `IsPublished` and `Mode` are always present but treated as agent-specific in the UI.

3. **Unified API** — No `/inner` sub-routes. Agent kind is a property of the agent, not a separate resource. The `CreateAgent` endpoint reads `AgentKind` from the request body instead of hardcoding it.

4. **Unified UI** — One create page, one list page. The Agent Kind selector on the create form and the filter tabs on the dashboard handle the distinction.

5. **Sub-agents are Fast mode only** — The controller enforces `Mode = AgentMode.Fast` when `AgentKind == SubAgent`, and the UI hides the Mode selector for sub-agents. This is because sub-agents run synchronously as function calls and must not produce streaming reasoning output.

6. **Bindings, not hierarchy** — Sub-agents are not "children" of agents. They are reusable tools that any agent can bind to. The `AgentSubAgents` join table enables many-to-many relationships with per-binding enable/disable.

7. **Sub-agents support document upload** — The knowledge base (documents, folders, LightRAG) is fully available to sub-agents. The `DocumentsController`, `FoldersController`, and `KnowledgeService` operate by `AgentId` with no `AgentKind` restrictions. The Knowledge Base tab appears on the detail page for all agent kinds, and default folders are created for sub-agents at creation time.
