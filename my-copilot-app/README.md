# NTG Agent - AI Agent Interface

An interactive, responsive chat interface built with Next.js (App Router) and TypeScript. It allows users to communicate with multiple AI agents powered by a custom .NET Core Kernel Memory Orchestrator backend.

## Features

- **Multi-Agent Orchestration**: Fetches dynamic agent profiles on mount, automatically matching the default configuration set on the backend, and provides a switchable menu to change agent personas seamlessly.
- **Custom Streaming Engine**: Consumes server response streams directly via a custom extraction engine designed to isolate, safely decode, and reconstruct structured token fragments (`PromptChunk`) sequentially without interface freezing.
- **Session & Conversation State Persistence**: Transparently negotiates cookies (`ntg_session_id`) and maps local storage caches to synchronize ongoing conversation histories matching backend storage identifiers.
- **Responsive Layout**: Native mobile layout adaptations using a toggleable side drawer and state configuration wrappers.

## Technical Architecture

The frontend maps to the following component structure and Next.js backend proxy routers:

├── app/
│   ├── api/
│   │   ├── agents/
│   │   │   └── route.ts         # Proxies agent catalogs from the orchestrator service
│   │   └── chat/
│   │       └── route.ts         # Handles session setups and routes data payloads to chat handlers
│   ├── layout.tsx               # Primary application wrapper injecting Geist fonts
│   ├── page.tsx                 # Main client application handling state loops and token streaming
│   └── globals.css              # Global style declarations and utilities

Currently due to the agent have to stream answer from both chatClient and new React based UI. The token of each agent can exhaust quickly 

### Communication Flow
1. **Agent Retrieval**: The client targets `/api/agents` to fetch metadata definitions. The handler calls the .NET orchestrator endpoint `${ORCHESTRATOR_URL}/api/agents` to fetch live configs.
2. **Chat & Payload Delivery**: User entries are forwarded to `/api/chat` containing specific `agentId` mappings. 
3. **Internal Processing**: The route handler checks for a tracking cookie (`ntg_session_id`). If unassigned, it calls the internal service API (`/api/conversations`) to establish a unique backend persistence layer before creating an ingestion mapping with a `FormData` envelope (`Prompt`, `ConversationId`, `SessionId`, `AgentId`) targeting the core engine.
4. **Buffer Transformation**: Token packages stream back through raw chunks typed explicitly as `PromptChunk` boundaries containing content categories (`content`, `contentType`).

## Environment Configurations

The Next.js backend checks for orchestration paths using the following runtime variables (evaluated in priority sequence):

```bash
# Service Binding Mapping (Default Aspire Profile)
services__ntg_agent_orchestrator__https__0="https://localhost:7093"

# Direct Manual Target Alternative
ORCHESTRATOR_URL="https://localhost:7093"
Note: In development environments, certificate verification checks are relaxed via NODE_TLS_REJECT_UNAUTHORIZED = "0" to facilitate smooth communication over HTTPS loops.

Getting Started
Prerequisites
Node.js (v18.x or later)

Core .NET Backend service initialized and listening on your configured ORCHESTRATOR_URL

Installation
Install project dependencies:

Bash
npm install
# or
yarn install
# or
pnpm install
Run the local development server:

Bash
npm run dev
# or
yarn dev
# or
pnpm dev
Open http://localhost:3000 inside your browser to view the application interface.
or use dotnet run --project NTG.Agent.AppHost to run