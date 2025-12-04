# NTG Agent - Orchestrator Service

## Project Summary

The **NTG Agent Orchestrator** is the core backend API service that coordinates AI agent interactions, manages conversations, and orchestrates document processing within the NTG Agent platform. Built on .NET 10 and leveraging Microsoft Agent Framework with Semantic Kernel integration, this service handles chat completions, manages AI agent configurations, coordinates with the Knowledge service for RAG (Retrieval-Augmented Generation), and maintains conversation history with intelligent summarization.

The Orchestrator implements distributed authentication using shared cookies and provides comprehensive telemetry through OpenTelemetry integration.

## Project Structure

```
NTG.Agent.Orchestrator/
??? Agents/
?   ??? AgentService.cs                  # Main agent orchestration logic
?   ??? AgentFactory.cs                  # Creates configured agent instances
??? Controllers/
?   ??? AgentsController.cs              # Agent management endpoints
?   ??? DocumentsController.cs           # Document management
?   ??? ConversationsController.cs       # Conversation CRUD
?   ??? SharedConversationsController.cs # Conversation sharing
??? Data/
?   ??? AgentDbContext.cs                # EF Core database context
?   ??? Migrations/                      # Database migrations
??? Models/
?   ??? Agents/                          # Agent entities
?   ??? Chat/                            # Conversation & message entities
?   ??? Documents/                       # Document & folder entities
?   ??? Identity/                        # User models
?   ??? Tags/                            # Tag entities
??? Plugins/
?   ??? KnowledgePlugin.cs               # Semantic search plugin for agents
??? Services/
?   ??? Knowledge/
?       ??? IKnowledgeService.cs
?       ??? KernelMemoryKnowledge.cs     # Knowledge service client
??? appsettings.json
??? Program.cs
??? NTG.Agent.Orchestrator.csproj
```

## Main Components

### Agent Orchestration

- **AgentService** - Core orchestration logic:
  - Streaming chat completions
  - Conversation history management
  - Automatic conversation summarization
  - Document upload and OCR integration
  - Multi-agent workflows

- **AgentFactory** - Creates AI agents with:
  - Configurable LLM providers (GitHub Models, Azure OpenAI, Google Gemini)
  - Custom instructions and tools
  - Knowledge plugin integration

### Data Models

- **Agent** - AI agent configurations (name, instructions, provider settings)
- **Conversation** - Chat sessions (user/session-based)
- **ChatMessage** - Individual messages with roles (User/Assistant/System)
- **Document** - Uploaded files with metadata and folder associations
- **Folder** - Hierarchical document organization
- **Tag** - Role-based content categorization
- **SharedConversation** - Public conversation snapshots

### Services

- **IKnowledgeService** - Interface for knowledge operations
- **KernelMemoryKnowledge** - Client for Kernel Memory service:
  - Document ingestion (files, web pages, text)
  - Semantic search
  - Document export
  - Tag-based filtering

### Plugins

- **KnowledgePlugin** - Provides RAG capabilities:
  - Semantic search over knowledge base
  - Exposed as AI tool for agents
  - Role-based document access

## Design Patterns Used

1. **Factory Pattern** - `AgentFactory` creates configured agent instances
2. **Repository Pattern** - EF Core `AgentDbContext` abstracts data access
3. **Service Layer** - Business logic encapsulated in services
4. **Plugin Architecture** - Extensible agent capabilities via plugins
5. **Strategy Pattern** - Multiple LLM provider implementations
6. **Observer Pattern** - Streaming responses with `IAsyncEnumerable`
7. **CQRS-lite** - Separate read/write operations in controllers

## Key Features

### AI Agent Management

- Configure multiple AI agents with custom instructions
- Support multiple LLM providers (GitHub Models, Azure OpenAI, Gemini)
- Tool integration (Knowledge search, web search via MCP)
- Multi-agent workflows with handoffs

### Conversation Management

- User and anonymous (session-based) conversations
- Automatic conversation naming
- Intelligent conversation summarization (keeps last 5 messages full, summarizes older)
- Conversation sharing with expiration
- Role-based access control

### Document Processing

- Upload files (50MB max), web pages, or text content
- Hierarchical folder organization
- Tag-based categorization with role permissions
- Document download and export
- Integration with Knowledge service for RAG

### Streaming Responses

```csharp
public async IAsyncEnumerable<string> ChatStreamingAsync(Guid? userId, PromptRequestForm request)
{
    var conversation = await ValidateConversation(userId, request);
    var history = await PrepareConversationHistory(userId, conversation);
    var tags = await GetUserTags(userId);
    
    await foreach (var token in InvokePromptStreamingInternalAsync(request, history, tags))
    {
        yield return token;
    }
    
    await SaveMessages(userId, conversation, request.Prompt, fullResponse);
}
```

## Dependencies

### NuGet Packages

- **Microsoft.EntityFrameworkCore.SqlServer** (10.0.0) - Database provider
- **Microsoft.Extensions.AI** - AI abstractions
- **Microsoft.Agents.AI** - Agent Framework
- **Microsoft.KernelMemory.WebClient** - Knowledge service client
- **OpenTelemetry*** - Distributed tracing and metrics

### Project References

- **NTG.Agent.ServiceDefaults** - Shared configuration, telemetry
- **NTG.Agent.Common** - DTOs, constants, utilities

### External Services

- **NTG.Agent.Knowledge** - Kernel Memory service (document ingestion, search)
- **NTG.Agent.MCP.Server** - Model Context Protocol server (web search tool)
- **SQL Server** - Primary database

## Database Schema

### Core Tables

- `Agents` - AI agent configurations
- `Conversations` - Chat sessions
- `ChatMessages` - Individual messages
- `SharedConversations` - Public conversation snapshots
- `Documents` - Uploaded documents
- `Folders` - Document organization
- `Tags` - Content categories
- `TagRoles` - Role-based tag access
- `DocumentTags` - Document-tag relationships
- `UserPreferences` - User/session preferences
- `AspNetUsers`, `AspNetRoles` - Identity tables (external)

## How to Run

### Prerequisites

- .NET 10 SDK
- SQL Server
- Running instance of NTG.Agent.Knowledge service

### Setup

1. **Configure Database**

Update connection string in `appsettings.Development.json` or user secrets:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=NTGAgent;Trusted_Connection=True;TrustServerCertificate=true"
```

2. **Configure Knowledge Service**

Set the API key in user secrets:

```bash
dotnet user-secrets set "KernelMemory:ApiKey" "your-api-key"
```

The service endpoint is discovered via .NET Aspire service discovery.

3. **Apply Migrations**

```bash
dotnet ef database update
```

This creates:
- All required tables
- Default agent
- Root and default folders
- Public tag with anonymous role access

4. **Run the Service**

```bash
# Standalone
dotnet run

# Via Aspire (recommended)
cd ../NTG.Agent.AppHost
dotnet run
```

5. **Verify**

Navigate to `https://localhost:5002/health` (or configured port)

## Configuration

### OpenTelemetry

```json
{
  "OTEL_EXPORTER_OTLP_ENDPOINT": "http://localhost:4317"
}
```

Traces and metrics include:
- HTTP requests
- Database queries
- AI agent interactions
- Custom business metrics

### Data Protection

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("../key/"))
    .SetApplicationName("NTGAgent");
```

### Authentication

Uses shared cookie authentication compatible with Admin and WebClient projects.

## API Endpoints

### Agent Management

- `GET /api/agents` - List all agents
- `GET /api/agents/{id}` - Get agent by ID
- `POST /api/agents` - Create agent
- `PUT /api/agents/{id}` - Update agent
- `DELETE /api/agents/{id}` - Delete agent

### Chat

- `POST /api/agents/chat` - Stream chat completions (multipart/form-data)

### Conversations

- `GET /api/conversations` - List user conversations
- `GET /api/conversations/{id}` - Get conversation with messages
- `POST /api/conversations` - Create conversation
- `DELETE /api/conversations/{id}` - Delete conversation

### Shared Conversations

- `POST /api/sharedconversations` - Share a conversation
- `GET /api/sharedconversations/public/{shareId}` - Get public snapshot
- `GET /api/sharedconversations/mine` - List user's shared conversations
- `PUT /api/sharedconversations/{id}/expiration` - Update expiration
- `DELETE /api/sharedconversations/{id}` - Delete shared conversation

### Documents

- `GET /api/documents/{agentId}` - List documents
- `POST /api/documents/upload/{agentId}` - Upload files
- `POST /api/documents/import-webpage/{agentId}` - Import webpage
- `POST /api/documents/upload-text/{agentId}` - Upload text
- `GET /api/documents/download/{agentId}/{id}` - Download document
- `DELETE /api/documents/{id}/{agentId}` - Delete document

### Folders & Tags

- `GET /api/folders/{agentId}` - List folders
- `POST /api/folders` - Create folder
- `GET /api/tags` - List tags
- `POST /api/tags` - Create tag

## Development

### Adding a New LLM Provider

1. Update `AgentFactory.CreateAgent()` to support the new provider
2. Add provider configuration in `Agent` model
3. Update admin UI to configure new provider

### Adding a New Plugin

```csharp
public class MyPlugin
{
    [Description("Does something useful")]
    public async Task<string> MyFunction([Description("input")]string input)
    {
        // Implementation
    }
    
    public AITool AsAITool()
    {
        return AIFunctionFactory.Create(this.MyFunction);
    }
}

// In AgentService
var chatOptions = new ChatOptions
{
    Tools = [new MyPlugin().AsAITool()]
};
```

### Database Migrations

```bash
# Add migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update

# Rollback
dotnet ef database update PreviousMigrationName
```

## Testing

### Unit Tests

Run tests in `NTG.Agent.Orchestrator.Tests`:

```bash
dotnet test
```

Coverage includes:
- KernelMemoryKnowledge service
- Validation logic
- Business rules

## Deployment

### Production Build

```bash
dotnet publish -c Release -o ./publish
```

### Environment Variables

```bash
# Database
ConnectionStrings__DefaultConnection="Server=prod-server;Database=NTGAgent;..."

# OpenTelemetry
OTEL_EXPORTER_OTLP_ENDPOINT="https://otel-collector:4317"

# Knowledge Service
services__ntg-agent-knowledge__https__0="https://knowledge-service:443"
KernelMemory__ApiKey="production-api-key"

# Environment
ASPNETCORE_ENVIRONMENT=Production
```

### Pre-deployment Checklist

- ? Update database connection string
- ? Configure Knowledge service endpoint
- ? Set production API keys
- ? Enable HTTPS
- ? Configure OpenTelemetry exporter
- ? Review authorization policies
- ? Run database migrations

## Monitoring

### Metrics

Custom metrics tracked:
- `agent_interactions_total` - Counter
- `agent_response_time_seconds` - Histogram

### Traces

Distributed tracing includes:
- HTTP requests
- Database operations
- AI agent calls
- Knowledge service calls

### Health Checks

- `/health` - Overall health
- `/alive` - Liveness probe

## Additional Resources

- [Main Solution README](../README.md)
- [Microsoft Agent Framework](https://github.com/microsoft/agents)
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel)
- [Kernel Memory](https://github.com/microsoft/kernel-memory)
