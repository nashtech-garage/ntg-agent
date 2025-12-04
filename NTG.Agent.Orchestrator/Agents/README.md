# Agent Service Layer

## Project Summary

The **Agent Service Layer** is the core orchestration component within the NTG.Agent.Orchestrator that manages AI agent interactions, conversation flows, and intelligent prompt processing. This layer implements sophisticated conversation management including streaming responses, conversation history summarization, multi-agent workflows, and RAG (Retrieval-Augmented Generation) integration.

The service coordinates between AI agents, the knowledge base, conversation storage, and user requests to deliver context-aware, intelligent responses while maintaining conversation state and implementing performance optimizations like automatic summarization.

## Project Structure

```
NTG.Agent.Orchestrator/Agents/
??? AgentService.cs                      # Core orchestration logic
??? AgentFactory.cs                      # Agent instance creation and configuration
??? IAgentFactory.cs                     # Factory interface
```

## Main Components

### AgentService

The central orchestration service that coordinates all AI agent interactions.

**Key Responsibilities:**
- **Streaming Chat Execution** - Processes user prompts and streams AI responses
- **Conversation Management** - Validates and manages conversation context
- **History Management** - Maintains conversation history with automatic summarization
- **Message Persistence** - Saves user and assistant messages to database
- **Multi-Agent Workflows** - Supports agent handoffs and orchestration
- **RAG Integration** - Connects agents to knowledge base for context-aware responses

**Key Methods:**

```csharp
public async IAsyncEnumerable<string> ChatStreamingAsync(Guid? userId, PromptRequestForm promptRequest)
```
Main entry point for chat interactions. Validates conversation, prepares history, invokes agent, and streams responses.

```csharp
private async Task<List<PChatMessage>> PrepareConversationHistory(Guid? userId, Conversation conversation)
```
Optimizes conversation history by keeping last 5 messages full and summarizing older messages.

```csharp
private async Task<List<string>> GetUserTags(Guid? userId)
```
Retrieves role-based tags for document access control in RAG.

```csharp
private async IAsyncEnumerable<string> InvokePromptStreamingInternalAsync(...)
```
Invokes AI agent with prepared context, tools, and streaming configuration.

### AgentFactory (IAgentFactory)

Creates and configures AI agent instances based on database configuration.

**Key Responsibilities:**
- **Multi-Provider Support** - GitHub Models, Azure OpenAI, Google Gemini
- **Agent Configuration** - Loads agent settings from database
- **Tool Integration** - Attaches enabled tools to agents
- **MCP Tool Discovery** - Connects to MCP servers for dynamic tool loading
- **Telemetry Integration** - OpenTelemetry instrumentation

**Supported Agent Providers:**

```csharp
"GitHubModel"   ? CreateOpenAIAgentAsync()
"GoogleGemini"  ? CreateOpenAIAgentAsync()
"AzureOpenAI"   ? CreateAzureOpenAIAgentAsync()
```

**Tool Types:**
1. **Built-in Tools** - Statically defined (e.g., DateTimeTools)
2. **MCP Tools** - Dynamically loaded from Model Context Protocol servers
3. **Knowledge Tools** - RAG search via KnowledgePlugin

**Key Methods:**

```csharp
public async Task<AIAgent> CreateAgent(Guid agentId)
```
Creates a fully configured agent with tools based on database configuration.

```csharp
public async Task<AIAgent> CreateBasicAgent(string instructions)
```
Creates a simple agent without tools (used for summarization, naming).

```csharp
public async Task<IEnumerable<AITool>> GetMcpToolsAsync(string endpoint)
```
Connects to MCP server and retrieves available tools.

## Design Patterns Used

### 1. **Factory Pattern** (AgentFactory)
- **Purpose:** Encapsulates agent creation logic
- **Benefit:** Supports multiple LLM providers without changing client code
- **Implementation:** Different factory methods for each provider

```csharp
public async Task<AIAgent> CreateAgent(Guid agentId)
{
    var agentConfig = await _agentDbContext.Agents.FirstOrDefaultAsync(...);
    return agentConfig.ProviderName switch
    {
        "GitHubModel" => await CreateOpenAIAgentAsync(agentConfig),
        "AzureOpenAI" => await CreateAzureOpenAIAgentAsync(agentConfig),
        _ => throw new NotSupportedException(...)
    };
}
```

### 2. **Strategy Pattern** (Prompt Building)
- **Purpose:** Different prompt strategies based on context
- **Benefit:** Flexible prompt construction for different scenarios
- **Implementation:** `BuildTextOnlyPrompt()` vs `BuildOcrPromptAsync()`

```csharp
private static string BuildPromptAsync(PromptRequest<UploadItemForm> promptRequest, List<string> ocrDocuments)
{
    if (ocrDocuments.Count != 0)
        return BuildOcrPromptAsync(promptRequest.Prompt, ocrDocuments);
    
    return BuildTextOnlyPrompt(promptRequest.Prompt);
}
```

### 3. **Repository Pattern** (via AgentDbContext)
- **Purpose:** Data access abstraction
- **Benefit:** Separates data access from business logic
- **Implementation:** Entity Framework Core DbContext

```csharp
var conversation = await _agentDbContext.Conversations
    .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);
```

### 4. **Service Layer Pattern**
- **Purpose:** Encapsulates business logic
- **Benefit:** Centralized orchestration logic
- **Implementation:** AgentService coordinates all components

### 5. **Streaming Pattern** (IAsyncEnumerable)
- **Purpose:** Stream responses incrementally
- **Benefit:** Better UX with real-time feedback
- **Implementation:** `async IAsyncEnumerable<string>`

```csharp
public async IAsyncEnumerable<string> ChatStreamingAsync(...)
{
    await foreach (var item in InvokePromptStreamingInternalAsync(...))
    {
        agentMessageSb.Append(item);
        yield return item;
    }
}
```

### 6. **Builder Pattern** (Agent Configuration)
- **Purpose:** Fluent configuration of agents
- **Benefit:** Clean, readable agent setup
- **Implementation:** `AsBuilder()` chains in AgentFactory

```csharp
var chatClient = openAiClient.GetChatClient(model)
    .AsIChatClient()
    .AsBuilder()
    .UseFunctionInvocation()
    .UseOpenTelemetry(...)
    .Build();
```

### 7. **Adapter Pattern** (MCP Tool Integration)
- **Purpose:** Adapts external MCP tools to AITool interface
- **Benefit:** Seamless integration of remote tools
- **Implementation:** `GetMcpToolsAsync()` converts MCP tools

### 8. **Template Method Pattern** (Conversation Flow)
- **Purpose:** Defines conversation processing skeleton
- **Benefit:** Consistent flow with customizable steps
- **Implementation:** `ChatStreamingAsync()` defines the flow

```csharp
public async IAsyncEnumerable<string> ChatStreamingAsync(...)
{
    // 1. Validate
    var conversation = await ValidateConversation(...);
    // 2. Prepare
    var history = await PrepareConversationHistory(...);
    var tags = await GetUserTags(...);
    // 3. Execute
    await foreach (var item in InvokePromptStreamingInternalAsync(...))
        yield return item;
    // 4. Save
    await SaveMessages(...);
}
```

### 9. **Dependency Injection Pattern**
- **Purpose:** Loose coupling between components
- **Benefit:** Testability and maintainability
- **Implementation:** Constructor injection

```csharp
public AgentService(
    IAgentFactory agentFactory,
    AgentDbContext agentDbContext,
    IKnowledgeService knowledgeService)
{
    _agentFactory = agentFactory;
    _agentDbContext = agentDbContext;
    _knowledgeService = knowledgeService;
}
```

### 10. **Observer Pattern** (Event Streaming in Workflows)
- **Purpose:** React to workflow events
- **Benefit:** Asynchronous event handling
- **Implementation:** `WatchStreamAsync()` in multi-agent workflows

```csharp
await foreach (WorkflowEvent evt in run.WatchStreamAsync().ConfigureAwait(false))
{
    if (evt is AgentRunUpdateEvent e)
        yield return e.Data?.ToString() ?? string.Empty;
}
```

## Key Features

### 1. Conversation History Optimization

**Problem:** Long conversations consume excessive tokens and slow down responses.

**Solution:** Automatic summarization
- Keeps last **5 messages** in full detail
- Summarizes older messages into a system message
- Updates summary incrementally

```csharp
private const int MAX_LATEST_MESSAGE_TO_KEEP_FULL = 5;

private async Task<List<PChatMessage>> PrepareConversationHistory(...)
{
    if (historyMessages.Count <= MAX_LATEST_MESSAGE_TO_KEEP_FULL) 
        return historyMessages;

    var toSummarize = historyMessages.Take(historyMessages.Count - MAX_LATEST_MESSAGE_TO_KEEP_FULL).ToList();
    var summary = await SummarizeMessagesAsync(toSummarize);
    
    // Create or update summary message
    summaryMsg.Content = $"Summary of earlier conversation: {summary}";
    
    return [summaryMsg, ...lastFiveMessages];
}
```

### 2. Multi-Agent Workflows

Supports agent orchestration with handoffs:

```csharp
private async IAsyncEnumerable<string> TestOrchestratorInvokePromptStreamingInternalAsync(...)
{
    var triageAgent = await _agentFactory.CreateAgent(promptRequest.AgentId);
    var csharpAgent = await _agentFactory.CreateAgent(new Guid("684604F0-...")); 
    var javaAgent = await _agentFactory.CreateAgent(new Guid("25ACDA2A-..."));
    
    var workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
        .WithHandoffs(triageAgent, [csharpAgent, javaAgent])
        .Build();
    
    StreamingRun run = await InProcessExecution.StreamAsync(workflow, chatHistory);
    // Stream workflow events
}
```

**Use Case:** Route questions to specialized agents (C# expert, Java expert, etc.)

### 3. RAG Integration

Connects agents to knowledge base:

```csharp
AITool memorySearch = new KnowledgePlugin(_knowledgeService, tags, promptRequest.AgentId).AsAITool();

var chatOptions = new ChatOptions
{
    Tools = [memorySearch]
};
```

**Flow:**
1. User asks question
2. Agent detects need for knowledge
3. Calls KnowledgePlugin.SearchAsync()
4. Retrieves relevant documents
5. Synthesizes answer with citations

### 4. Automatic Conversation Naming

New conversations automatically get descriptive names:

```csharp
private async Task SaveMessages(...)
{
    if (conversation.Name == "New Conversation")
    {
        conversation.Name = await GenerateConversationName(userPrompt);
        _agentDbContext.Conversations.Update(conversation);
    }
}

private async Task<string> GenerateConversationName(string question)
{
    var agent = await _agentFactory.CreateBasicAgent(
        "Generate a short, descriptive conversation name (? 5 words).");
    var results = await agent.RunAsync(question);
    return results.Text;
}
```

### 5. Anonymous & Authenticated Support

Handles both logged-in users and anonymous sessions:

```csharp
private async Task<Conversation> ValidateConversation(Guid? userId, PromptRequestForm promptRequest)
{
    if (userId.HasValue)
    {
        conversation = await _agentDbContext.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);
    }
    else
    {
        if (!Guid.TryParse(promptRequest.SessionId, out var sessionId))
            throw new InvalidOperationException("A valid Session ID is required...");

        conversation = await _agentDbContext.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.SessionId == sessionId);
    }
}
```

### 6. Role-Based Document Access

Tag-based security for knowledge base:

```csharp
private async Task<List<string>> GetUserTags(Guid? userId)
{
    if (userId is not null)
    {
        var roleIds = await _agentDbContext.UserRoles
            .Where(c => c.UserId == userId)
            .Select(c => c.RoleId)
            .ToListAsync();

        return await _agentDbContext.TagRoles
            .Where(c => roleIds.Contains(c.RoleId))
            .Select(c => c.TagId.ToString())
            .ToListAsync();
    }
    else
    {
        // Anonymous users get "Anonymous" role tags
        var anonymousRoleId = new Guid(Constants.AnonymousRoleId);
        return await _agentDbContext.TagRoles
            .Where(c => c.RoleId == anonymousRoleId)
            .Select(c => c.TagId.ToString())
            .ToListAsync();
    }
}
```

## Internal Dependencies

### NuGet Packages
- **Microsoft.Agents.AI** - Agent Framework core
- **Microsoft.Agents.AI.Workflows** - Multi-agent orchestration
- **Microsoft.Extensions.AI** - AI abstractions
- **Microsoft.EntityFrameworkCore** - Database access
- **OpenAI** - OpenAI client
- **Azure.AI.OpenAI** - Azure OpenAI client
- **ModelContextProtocol.Client** - MCP client

### Project References
- **NTG.Agent.Common** - DTOs (PromptRequest, PromptResponse)
- **NTG.Agent.AITools.SimpleTools** - Built-in tools (DateTimeTools)

### Database Tables Used
- `Conversations` - Chat sessions
- `ChatMessages` - Individual messages
- `Agents` - Agent configurations
- `AgentTools` - Agent-tool associations
- `UserRoles` - User role mappings
- `TagRoles` - Tag-role access control

## External Dependencies

### Services
- **IKnowledgeService** - Semantic search and document retrieval
- **AgentDbContext** - Database access
- **IConfiguration** - Application configuration

### LLM Providers
- **GitHub Models** - Free tier LLM access
- **Azure OpenAI** - Enterprise LLM service
- **Google Gemini** - Google's LLM

### External Systems
- **MCP Servers** - Model Context Protocol tool servers
- **Knowledge Service** - Kernel Memory for RAG

## Configuration

### Agent Configuration (Database)

Agents are configured in the `Agents` table:

```sql
Agent {
    Id: Guid,
    Name: string,
    Instructions: string,
    ProviderName: "GitHubModel" | "AzureOpenAI" | "GoogleGemini",
    ProviderEndpoint: string,
    ProviderApiKey: string,
    ProviderModelName: string,
    McpServer: string (optional),
    IsPublished: bool,
    IsDefault: bool
}
```

### Tool Configuration

Tools are enabled per agent in `AgentTools` table:

```sql
AgentTools {
    AgentId: Guid,
    Name: string,
    Description: string,
    AgentToolType: BuiltIn | MCP,
    McpServer: string,
    IsEnabled: bool
}
```

### Constants

```csharp
private const int MAX_LATEST_MESSAGE_TO_KEEP_FULL = 5;
private Guid DefaultAgentId = new Guid("31CF1546-E9C9-4D95-A8E5-3C7C7570FEC5");
```

## Usage Examples

### Basic Chat Flow

```csharp
// Inject AgentService
public class ChatController
{
    private readonly AgentService _agentService;
    
    public async IAsyncEnumerable<PromptResponse> Chat(PromptRequestForm request)
    {
        var userId = User.GetUserId();
        
        await foreach (var chunk in _agentService.ChatStreamingAsync(userId, request))
        {
            yield return new PromptResponse(chunk, null, null);
        }
    }
}
```

### Creating a Custom Agent

```csharp
// Via AgentFactory
var agent = await _agentFactory.CreateAgent(agentId);

// Or basic agent for utility tasks
var summaryAgent = await _agentFactory.CreateBasicAgent(
    "Summarize the following text concisely.");
var result = await summaryAgent.RunAsync(longText);
```

### Adding Custom Tools

```csharp
public async Task<List<AITool>> GetAvailableTools(Models.Agents.Agent agent)
{
    var tools = new List<AITool>
    {
        AIFunctionFactory.Create(DateTimeTools.GetCurrentDateTime),
        AIFunctionFactory.Create(MyCustomTools.MyCustomFunction),
        // Add more tools
    };
    
    if (!string.IsNullOrEmpty(agent.McpServer))
    {
        var mcpTools = await GetMcpToolsAsync(agent.McpServer);
        tools.AddRange(mcpTools);
    }
    
    return tools;
}
```

## Performance Optimizations

### 1. Conversation History Summarization
- Reduces token usage by 60-80% for long conversations
- Keeps last 5 messages full for context
- Incrementally updates summary

### 2. Streaming Responses
- Immediate user feedback
- Reduced perceived latency
- Better UX for long responses

### 3. Lazy Loading
- Conversation messages loaded on-demand
- Tools loaded only for enabled agents
- MCP tools cached per connection

### 4. Parallel Processing
- MCP tool discovery in parallel
- Multiple document processing
- Concurrent agent invocations in workflows

## Testing Considerations

### Unit Testing

Mock dependencies:

```csharp
[Test]
public async Task ChatStreamingAsync_ValidRequest_ReturnsStream()
{
    var mockFactory = new Mock<IAgentFactory>();
    var mockDbContext = new Mock<AgentDbContext>();
    var mockKnowledge = new Mock<IKnowledgeService>();
    
    var service = new AgentService(
        mockFactory.Object, 
        mockDbContext.Object, 
        mockKnowledge.Object);
    
    // Test streaming
}
```

### Integration Testing

Test with real database and LLM:

```csharp
[Test]
public async Task ChatStreamingAsync_RealAgent_CompletesSuccessfully()
{
    // Use test database
    // Use test LLM provider
    // Verify conversation saved
    // Verify messages correct
}
```

## Troubleshooting

### Common Issues

**Agent not found:**
- Verify `AgentId` exists in database
- Check `IsPublished` is true

**Tools not working:**
- Verify tools enabled in `AgentTools` table
- Check MCP server is running and accessible
- Verify tool names match exactly

**Conversation not found:**
- For authenticated: Check `UserId` matches
- For anonymous: Check `SessionId` is provided and valid

**Slow responses:**
- Check conversation length (auto-summarization should help)
- Verify LLM provider latency
- Check network connectivity to MCP servers

## Future Enhancements

- [ ] Conversation branching (multiple paths)
- [ ] Message editing and regeneration
- [ ] Tool execution caching
- [ ] Advanced prompt templates
- [ ] Multi-modal support (images, audio)
- [ ] Conversation export/import
- [ ] Agent performance metrics
- [ ] A/B testing different agent configurations

## Additional Resources

- [Microsoft Agent Framework Documentation](https://github.com/microsoft/agents)
- [Main Orchestrator README](../README.md)
- [Solution README](../../README.md)
