# NTG Agent - AppHost (.NET Aspire Orchestrator)

## Project Summary

The **NTG Agent AppHost** is the .NET Aspire orchestration project that serves as the centralized entry point for running and managing all microservices in the NTG Agent solution. Built on .NET Aspire, this project handles service orchestration, dependency management, service discovery configuration, and provides a unified development dashboard for monitoring all services, traces, metrics, and logs in real-time.

Running this project starts the entire distributed application with proper service dependencies, health monitoring, and observability out-of-the-box.

## Project Structure

```
NTG.Agent.AppHost/
??? Program.cs                           # Service orchestration configuration
??? appsettings.json                     # Aspire configuration
??? NTG.Agent.AppHost.csproj             # Aspire host project
```

## Main Components

### Service Orchestration

The AppHost configures and starts all services with proper dependencies:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// MCP Server - Provides AI tools via Model Context Protocol
var mcpServer = builder.AddProject<Projects.NTG_Agent_MCP_Server>("ntg-agent-mcp-server");

// Knowledge Service - Document ingestion and semantic search
var knowledge = builder.AddProject<Projects.NTG_Agent_Knowledge>("ntg-agent-knowledge");

// Orchestrator - Main backend API
var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator")
    .WithExternalHttpEndpoints()           // Allow external access
    .WithReference(mcpServer)              // Depends on MCP Server
    .WithReference(knowledge);             // Depends on Knowledge

// Web Client - End-user interface
builder.AddProject<Projects.NTG_Agent_WebClient>("ntg-agent-webclient")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)           // Depends on Orchestrator
    .WaitFor(orchestrator);                // Waits for Orchestrator to be ready

// Admin Portal - Administrative interface
builder.AddProject<Projects.NTG_Agent_Admin>("ntg-agent-admin")
    .WithExternalHttpEndpoints()
    .WithReference(orchestrator)
    .WaitFor(orchestrator);

builder.Build().Run();
```

## Service Dependencies

### Dependency Graph

```
???????????????????
?   MCP Server    ?
???????????????????
         ?
???????????????????     ???????????????????
?   Knowledge     ?     ?   Orchestrator  ???????
???????????????????     ???????????????????     ?
         ?                       ?              ?
         ?????????????????????????              ?
                     ?                          ?
         ?????????????????????????              ?
         ?                       ?              ?
    ????????????          ????????????         ?
    ? WebClient?          ?  Admin   ?         ?
    ????????????          ????????????         ?
                                                ?
                              (References/WaitFor)
```

## Design Patterns Used

1. **Orchestrator Pattern** - Centralized service coordination
2. **Service Registry Pattern** - Automatic service discovery
3. **Gateway Pattern** - External endpoint management
4. **Dependency Injection** - Service resolution via references
5. **Health Check Pattern** - Service readiness verification

## Features

### .NET Aspire Dashboard

When you run the AppHost, Aspire automatically launches a web-based dashboard:

**Features:**
- ?? **Service Overview** - Status of all running services
- ?? **Distributed Tracing** - End-to-end request traces across services
- ?? **Metrics** - Real-time performance metrics
- ?? **Logs** - Aggregated structured logs from all services
- ?? **Service Discovery** - Automatic endpoint resolution
- ?? **Health Checks** - Service health monitoring

**Access:**
- Dashboard automatically opens in browser
- Default: `https://localhost:15000` (or shown in console)
- Username/password shown in console on first run

### Service Discovery

**Automatic Configuration:**
- Services reference each other by name (e.g., `ntg-agent-orchestrator`)
- Aspire resolves endpoints dynamically
- No hardcoded URLs needed
- Works across different environments

**Example:**
```csharp
// In Orchestrator service
var knowledgeEndpoint = Environment.GetEnvironmentVariable("services__ntg-agent-knowledge__https__0");
```

Aspire automatically sets this to the correct endpoint.

### External HTTP Endpoints

Services configured with `.WithExternalHttpEndpoints()` are accessible from outside:

- **NTG.Agent.WebClient** - User interface
- **NTG.Agent.Admin** - Admin interface
- **NTG.Agent.Orchestrator** - API endpoints (for direct API access)

Other services are internal-only (MCP Server, Knowledge).

### Dependency Management

**`.WithReference(service)`**
- Creates service-to-service dependency
- Enables service discovery
- Provides environment variables for connection

**`.WaitFor(service)`**
- Waits for service to be healthy before starting
- Prevents startup failures due to missing dependencies
- Ensures proper initialization order

## How to Run

### Prerequisites

- .NET 10 SDK
- SQL Server (LocalDB or full instance)
- Visual Studio 2026 / Rider / VS Code

### Setup

1. **Configure Secrets**

Each service needs its secrets configured. Run from solution root:

```bash
# Knowledge Service - GitHub Models API
cd NTG.Agent.Knowledge
dotnet user-secrets set "KernelMemory:Services:OpenAI:APIKey" "your-github-token"

# MCP Server - Google Search (optional)
cd ../NTG.Agent.MCP.Server
dotnet user-secrets set "Google:ApiKey" "your-google-api-key"
dotnet user-secrets set "Google:SearchEngineId" "your-search-engine-id"

# Return to root
cd ..
```

2. **Configure Database Connection** (if different from default)

Default connection string:
```
Server=.;Database=NTGAgent;Trusted_Connection=True;TrustServerCertificate=true;MultipleActiveResultSets=true
```

If you need different configuration, update `appsettings.Development.json` in:
- NTG.Agent.Admin
- NTG.Agent.Orchestrator
- NTG.Agent.WebClient

3. **Apply Database Migrations**

```bash
cd NTG.Agent.Admin/NTG.Agent.Admin
dotnet ef database update

cd ../../NTG.Agent.Orchestrator
dotnet ef database update

cd ..
```

4. **Run the AppHost**

```bash
cd NTG.Agent.AppHost
dotnet run
```

Or in Visual Studio:
- Set `NTG.Agent.AppHost` as startup project
- Press F5

### What Happens

1. ? AppHost starts
2. ? Aspire Dashboard opens in browser
3. ? Services start in dependency order:
   - MCP Server
   - Knowledge Service
   - Orchestrator (waits for MCP & Knowledge)
   - WebClient (waits for Orchestrator)
   - Admin (waits for Orchestrator)
4. ? Service discovery configured
5. ? External endpoints exposed
6. ? Telemetry collection begins

### Accessing Services

Once running, access through the dashboard or directly:

- **Aspire Dashboard**: `https://localhost:15000`
- **Web Client**: `https://localhost:7000` (or shown in dashboard)
- **Admin Portal**: `https://localhost:7001` (admin@ntgagent.com / Ntg@123)
- **Orchestrator API**: `https://localhost:5002`

## Aspire Dashboard Features

### Services Tab

Shows all running services with:
- Service name and status
- Endpoint URLs
- Environment variables
- Console logs
- Restart controls

### Traces Tab

Distributed tracing visualization:
- Request flow across services
- Timing breakdown
- Error identification
- Dependency mapping

Example trace:
```
WebClient ? Orchestrator ? Knowledge ? External API
   50ms        120ms          80ms        200ms
   ???????????? Total: 450ms ?????????????
```

### Metrics Tab

Real-time metrics:
- Request rate (requests/second)
- Response times (p50, p95, p99)
- Error rates
- CPU and memory usage
- Custom business metrics

### Logs Tab

Aggregated structured logs:
- Filter by service
- Search by text
- Filter by log level
- Correlation by trace ID
- Export capabilities

### Containers Tab

If using containerized resources (Redis, SQL Server via Aspire):
- Container status
- Port mappings
- Volume mounts
- Logs

## Configuration

### Service Configuration

Customize service settings in `appsettings.json` or environment-specific files:

```json
{
  "Services": {
    "ntg-agent-orchestrator": {
      "Replicas": 2,
      "MinReplicas": 1,
      "MaxReplicas": 5
    }
  }
}
```

### Dashboard Configuration

```json
{
  "Aspire": {
    "Dashboard": {
      "Port": 15000,
      "EnableAnonymousAccess": false
    }
  }
}
```

## Development Workflow

### 1. Start Everything

```bash
cd NTG.Agent.AppHost
dotnet run
```

### 2. Develop and Debug

- Make code changes in any service
- Service auto-restarts (if using `dotnet watch`)
- View changes in dashboard
- Monitor traces and logs

### 3. Test End-to-End

- Use WebClient to test user flows
- Use Admin to configure agents
- View distributed traces to debug issues
- Check metrics for performance

### 4. Troubleshoot

Dashboard shows:
- ? Service startup failures
- ?? Health check failures
- ?? Errors in logs
- ?? Performance bottlenecks

## Adding New Services

### 1. Add Project Reference

```xml
<!-- In NTG.Agent.AppHost.csproj -->
<ItemGroup>
  <ProjectReference Include="..\MyNewService\MyNewService.csproj" />
</ItemGroup>
```

### 2. Configure in Program.cs

```csharp
var myService = builder.AddProject<Projects.MyNewService>("my-new-service")
    .WithExternalHttpEndpoints()  // If needs external access
    .WithReference(orchestrator);  // If depends on Orchestrator

// If other services depend on it
var orchestrator = builder.AddProject<Projects.NTG_Agent_Orchestrator>("ntg-agent-orchestrator")
    .WithReference(myService);  // Now Orchestrator depends on MyNewService
```

### 3. Run

```bash
dotnet run
```

New service appears in dashboard automatically!

## Production Deployment

### Important Note

.NET Aspire AppHost is primarily for **local development**. For production:

1. **Container Deployment**
   - Build each service as container
   - Use Kubernetes/Docker Compose
   - Implement proper service mesh

2. **Service Discovery**
   - Use production service discovery (Consul, Kubernetes DNS)
   - Configure production endpoints
   - Remove Aspire-specific configuration

3. **Observability**
   - Use production OTLP collector
   - Send to APM platform (Application Insights, Datadog, New Relic)
   - Configure production dashboards

### Export Manifest (for production deployment)

```bash
dotnet run --publisher manifest --output-path manifest.json
```

This generates deployment manifest that can be used with:
- Azure Container Apps
- Kubernetes
- Docker Compose

## Troubleshooting

### Services Won't Start

1. Check service dependencies in order
2. Verify database connections
3. Check API keys are configured
4. Review service logs in dashboard

### Dashboard Won't Open

1. Check port 15000 is not in use
2. Look for dashboard URL in console output
3. Check credentials in console

### Service Discovery Failures

1. Verify service names match in configuration
2. Check `.WithReference()` calls are correct
3. Review environment variables in dashboard

### Slow Startup

1. Check database migrations (first run is slower)
2. Verify external API connectivity
3. Check for service dependency issues

## Additional Resources

- [Main Solution README](../README.md)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire)
- [Aspire Dashboard Guide](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard)
- [Service Discovery](https://learn.microsoft.com/dotnet/aspire/service-discovery/overview)
- [OpenTelemetry Integration](https://learn.microsoft.com/dotnet/aspire/fundamentals/telemetry)
