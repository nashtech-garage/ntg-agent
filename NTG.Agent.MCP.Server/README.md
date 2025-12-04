# NTG Agent - MCP Server

## Project Summary

The **NTG Agent MCP (Model Context Protocol) Server** is a specialized service that provides AI tools and capabilities through the Model Context Protocol standard. Built on .NET 10 and leveraging the ModelContextProtocol.AspNetCore SDK, this server exposes tools that can be dynamically discovered and integrated into AI agents, enabling capabilities like web search, data retrieval, and custom business logic.

The MCP Server demonstrates extensibility patterns for AI agents and serves as an example of how to create reusable AI tools that can be shared across different AI systems following the MCP standard.

## Project Summary

The Model Context Protocol (MCP) is an open standard that enables AI applications to securely connect to data sources and tools. This server implements MCP to provide:
- **Tool Discovery** - Agents can query available tools
- **Tool Execution** - Agents can invoke tools with parameters
- **HTTP Transport** - RESTful API for easy integration

## Project Structure

```
NTG.Agent.MCP.Server/
??? McpTools/
?   ??? MonkeyTools.cs                   # Example MCP tools
??? Services/
?   ??? MonkeyService.cs                 # Business logic service
??? appsettings.json
??? Program.cs
??? NTG.Agent.MCP.Server.csproj
```

## Main Components

### MCP Tools

**MonkeyTools** - Example tool implementation:
- `GetMonkeys()` - Returns a list of monkeys from external API
- `GetMonkey(name)` - Returns details for a specific monkey

These demonstrate how to:
- Annotate methods with `[McpServerTool]`
- Add descriptions for tool discovery
- Handle async operations
- Return serialized data

### Services

**MonkeyService** - Business logic layer:
- Fetches data from external API (https://www.montemagno.com/monkeys.json)
- Caches results for efficiency
- Provides searchable data

### MCP Server Configuration

The Program.cs configures the MCP server with:
```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()           // Enable HTTP-based communication
    .WithToolsFromAssembly()       // Auto-discover tools
    .AddAiTool();                  // Add AI-specific tools (web search)
```

## Design Patterns Used

1. **Service Layer Pattern** - Business logic in services, tools in presentation
2. **Dependency Injection** - Services injected into tools
3. **Factory Pattern** - HTTP client factory for resilient connections
4. **Singleton Pattern** - MonkeyService for data caching
5. **Adapter Pattern** - MCP tools adapt business services to MCP protocol

## Features

### Built-in Tools

**Monkey Information Tools:**
- Get list of all monkeys with details
- Search for specific monkey by name
- Returns JSON-formatted data

**AI Search Tools** (via NTG.Agent.AITools.SearchOnlineTool):
- Web search capabilities
- Integrated via `.AddAiTool()` extension

### Tool Discovery

Agents can query the server to discover available tools:

```http
GET /mcp/tools
```

Response includes:
- Tool names
- Descriptions
- Parameter schemas
- Return types

### Tool Execution

Agents can invoke tools with parameters:

```http
POST /mcp/execute
{
  "tool": "GetMonkey",
  "parameters": {
    "name": "Baboon"
  }
}
```

## Dependencies

### NuGet Packages

- **ModelContextProtocol.AspNetCore** (0.4.0-preview.3) - MCP server implementation
- Standard .NET 10 web packages

### Project References

- **NTG.Agent.AITools.SearchOnlineTool** - Web search capabilities
- **NTG.Agent.ServiceDefaults** - Shared configuration and telemetry

## How to Run

### Prerequisites

- .NET 10 SDK
- Internet connection (for external API calls)
- Google Custom Search API credentials (for search tools)

### Setup

1. **Configure Google Search (Optional)**

For web search functionality:

```bash
dotnet user-secrets set "Google:ApiKey" "your-google-api-key"
dotnet user-secrets set "Google:SearchEngineId" "your-search-engine-id"
```

Get credentials from: https://developers.google.com/custom-search/docs/tutorial/creatingcse

2. **Run the Service**

```bash
# Standalone
dotnet run

# Via Aspire (recommended)
cd ../NTG.Agent.AppHost
dotnet run
```

3. **Verify**

Navigate to `https://localhost:5136` (or configured port)

The MCP server is now running and ready to accept tool requests.

## Integration with Agents

### From Admin Portal

In the NTG.Agent.Admin portal:

1. Navigate to **Agent Management**
2. Select an agent
3. Go to **Tools** tab
4. Enter MCP Server endpoint: `http://localhost:5136`
5. Click **Connect**
6. Select which tools to enable
7. Save configuration

### From Code

```csharp
// In agent configuration
var mcpTools = await agentClient.ConnectToMcpServerAsync(
    agentId, 
    "http://localhost:5136"
);

// Enable selected tools
await agentClient.UpdateAgentToolsAsync(agentId, mcpTools);
```

## Creating Custom Tools

### 1. Create a Tool Class

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public sealed class MyCustomTools
{
    private readonly IMyService _myService;

    public MyCustomTools(IMyService myService)
    {
        _myService = myService;
    }

    [McpServerTool]
    [Description("Performs a custom operation")]
    public async Task<string> MyCustomTool(
        [Description("Input parameter")] string input)
    {
        var result = await _myService.ProcessAsync(input);
        return JsonSerializer.Serialize(result);
    }
}
```

### 2. Register Services

```csharp
// In Program.cs
builder.Services.AddScoped<IMyService, MyService>();
```

### 3. Tools Auto-Discovery

The `.WithToolsFromAssembly()` method automatically discovers all classes marked with `[McpServerToolType]` and methods marked with `[McpServerTool]`.

## API Endpoints

### MCP Protocol Endpoints

- `GET /mcp/tools` - List available tools
- `POST /mcp/execute` - Execute a tool
- `GET /health` - Health check (from ServiceDefaults)
- `GET /alive` - Liveness probe (from ServiceDefaults)

### Tool-Specific Endpoints

Tools are invoked through the MCP protocol, not directly via HTTP routes.

## Development

### Project Commands

```bash
# Restore
dotnet restore

# Build
dotnet build

# Run
dotnet run

# Test
cd ../tests/NTG.Agent.MCP.Server.Tests
dotnet test
```

### Testing

The project includes comprehensive unit tests for:

**MonkeyService Tests:**
- Successful API calls
- Failed API calls
- Monkey search by name
- Null handling

**MonkeyTools Tests:**
- Tool method serialization
- Parameter passing
- Null result handling
- Constructor validation

Run tests:
```bash
dotnet test ../tests/NTG.Agent.MCP.Server.Tests
```

### Adding New Data Sources

```csharp
public class WeatherService
{
    private readonly HttpClient _httpClient;

    public WeatherService(IHttpClientFactory factory)
    {
        _httpClient = factory.CreateClient();
    }

    public async Task<Weather> GetWeatherAsync(string city)
    {
        // Fetch weather data
    }
}

[McpServerToolType]
public class WeatherTools
{
    private readonly WeatherService _service;

    public WeatherTools(WeatherService service)
    {
        _service = service;
    }

    [McpServerTool]
    [Description("Get current weather for a city")]
    public async Task<string> GetWeather(
        [Description("City name")] string city)
    {
        var weather = await _service.GetWeatherAsync(city);
        return JsonSerializer.Serialize(weather);
    }
}
```

## Configuration

### MCP Server Options

```json
{
  "McpServer": {
    "EnableToolDiscovery": true,
    "MaxConcurrentExecutions": 10,
    "Timeout": "00:01:00"
  }
}
```

### HTTP Client Configuration

```csharp
builder.Services.AddHttpClient("MonkeyAPI", client =>
{
    client.BaseAddress = new Uri("https://www.montemagno.com");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler(); // Retry, circuit breaker, etc.
```

## Deployment

### Production Build

```bash
dotnet publish -c Release -o ./publish
```

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY ./publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "NTG.Agent.MCP.Server.dll"]
```

### Environment Variables

```bash
# Google Search (Optional)
Google__ApiKey="your-api-key"
Google__SearchEngineId="your-search-id"

# OpenTelemetry
OTEL_EXPORTER_OTLP_ENDPOINT="https://otel-collector:4317"

# Environment
ASPNETCORE_ENVIRONMENT=Production
```

### Pre-deployment Checklist

- ? Configure production API endpoints
- ? Set API keys using secrets management
- ? Enable HTTPS
- ? Configure CORS if needed
- ? Set appropriate timeouts
- ? Enable monitoring and logging
- ? Test tool discovery and execution

## Monitoring

### Telemetry

The service includes OpenTelemetry integration through ServiceDefaults:
- HTTP request tracing
- Tool execution metrics
- Error tracking
- Performance monitoring

### Health Checks

- `/health` - Overall service health
- `/alive` - Liveness check

## Troubleshooting

### Common Issues

**Tools not discovered:**
- Verify classes have `[McpServerToolType]` attribute
- Verify methods have `[McpServerTool]` attribute
- Check service registration in DI container

**Tool execution fails:**
- Check parameter types match
- Verify service dependencies are registered
- Review logs for exceptions

**External API failures:**
- Check network connectivity
- Verify API endpoints are accessible
- Review HTTP client resilience configuration

## Model Context Protocol (MCP) Standard

MCP is an open protocol that enables:
- **Standardized Tool Discovery** - Agents can find tools without hardcoding
- **Cross-Platform Integration** - Works with various AI frameworks
- **Secure Execution** - Tools run in controlled environments
- **Extensibility** - Easy to add new capabilities

Learn more: https://modelcontextprotocol.io

## Example Use Cases

### In AI Conversations

**User:** "Tell me about Baboons"

**Agent** (with MonkeyTools):
1. Discovers GetMonkey tool
2. Executes: `GetMonkey("Baboon")`
3. Receives JSON data
4. Formats response: "The Baboon is found in..."

### Web Search Integration

**User:** "Search for latest AI news"

**Agent** (with SearchOnlineTool):
1. Discovers web search tool
2. Executes search query
3. Returns relevant results
4. Summarizes findings

## Additional Resources

- [Main Solution README](../README.md)
- [Model Context Protocol Documentation](https://modelcontextprotocol.io)
- [Google Custom Search API](https://developers.google.com/custom-search)
- [.NET Service Defaults](https://aka.ms/dotnet/aspire/service-defaults)
