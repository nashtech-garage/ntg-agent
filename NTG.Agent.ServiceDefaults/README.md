# NTG Agent - Service Defaults

## Project Summary

The **NTG Agent Service Defaults** project is a shared infrastructure library that provides common cross-cutting concerns for all services in the NTG Agent solution. Built following .NET Aspire best practices, this project standardizes configuration for OpenTelemetry (distributed tracing, metrics, and logging), health checks, service discovery, HTTP resilience patterns, and global exception handling across all microservices.

By referencing this project, services automatically gain production-ready observability, reliability, and monitoring capabilities with minimal configuration.

## Project Structure

```
NTG.Agent.ServiceDefaults/
??? Logging/
?   ??? GlobalExceptionHandler.cs       # Centralized exception handling
?   ??? Metrics/
?       ??? IMetricsCollector.cs        # Metrics interface
?       ??? MetricsCollector.cs         # Metrics implementation
??? Extensions.cs                        # Service registration extensions
??? NTG.Agent.ServiceDefaults.csproj
```

## Main Components

### OpenTelemetry Configuration

**Distributed Tracing:**
- Automatic instrumentation for ASP.NET Core requests
- HTTP client tracing
- Custom activity sources
- Microsoft Agent Framework telemetry
- OTLP exporter configuration

**Metrics:**
- ASP.NET Core metrics (request duration, count, etc.)
- HTTP client metrics
- Runtime metrics (GC, thread pool, exceptions)
- Custom business metrics via `IMetricsCollector`
- Agent Framework metrics

**Logging:**
- Structured logging with OpenTelemetry
- Formatted messages included
- Scope information captured
- OTLP log export

### Health Checks

**Default Health Checks:**
- `self` - Liveness check (always healthy)
- Custom checks can be added per service

**Endpoints:**
- `/health` - All health checks must pass (readiness)
- `/alive` - Only liveness checks (for k8s liveness probes)

### Service Discovery

**Features:**
- Automatic service endpoint resolution
- Integration with .NET Aspire
- Support for multiple schemes (HTTP, HTTPS)
- Dynamic configuration updates

### HTTP Resilience

**Standard Resilience Handler:**
- **Retry Policy** - Transient failure retry with exponential backoff
- **Circuit Breaker** - Prevents cascading failures
- **Timeout** - Request timeout protection
- **Rate Limiting** - Request rate control

Applied to all HTTP clients by default.

### Metrics Collection

**IMetricsCollector Interface:**
```csharp
public interface IMetricsCollector
{
    IDisposable StartTimer(string metricName, params (string, string)[] tags);
    void RecordBusinessMetric(string eventName, object eventData);
}
```

**Usage:**
```csharp
using var timer = _metrics.StartTimer("documents.get", ("agent_id", agentId.ToString()));
// Operation
_metrics.RecordBusinessMetric("DocumentsRetrieved", new { AgentId = agentId, Count = 10 });
```

### Global Exception Handling

**GlobalExceptionHandler:**
- Catches unhandled exceptions
- Logs with structured data
- Returns user-friendly error responses
- Prevents sensitive data leaks

## Design Patterns Used

1. **Extension Method Pattern** - Fluent service configuration
2. **Builder Pattern** - Progressive service configuration
3. **Decorator Pattern** - HTTP client resilience wrapping
4. **Observer Pattern** - Telemetry and metrics collection
5. **Template Method Pattern** - Standardized service startup

## Features

### Automatic Service Configuration

Services that reference this project and call `builder.AddServiceDefaults()` automatically get:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();  // ? One line setup!
```

This configures:
- ? OpenTelemetry (traces, metrics, logs)
- ? Health checks
- ? Service discovery
- ? HTTP resilience
- ? Exception handling
- ? Metrics collection

### Observability Out-of-the-Box

**Traces:**
- Every HTTP request and response
- Database queries (when using instrumented providers)
- HTTP client calls
- Custom activities

**Metrics:**
- Request rate and duration
- HTTP client success/failure rates
- Runtime performance (CPU, memory, GC)
- Custom business metrics

**Logs:**
- Structured logging with correlation IDs
- Exception details
- Request/response logging
- Custom log entries

### Standardized Endpoints

All services expose:
- `/health` - Health status (development only for security)
- `/alive` - Liveness check (development only)

## Dependencies

### NuGet Packages

- **OpenTelemetry.Exporter.OpenTelemetryProtocol** - OTLP exporter
- **OpenTelemetry.Extensions.Hosting** - DI integration
- **OpenTelemetry.Instrumentation.AspNetCore** - ASP.NET Core tracing
- **OpenTelemetry.Instrumentation.Http** - HTTP client tracing
- **OpenTelemetry.Instrumentation.Runtime** - Runtime metrics
- **Microsoft.Extensions.ServiceDiscovery** - Service discovery
- **Microsoft.Extensions.Http.Resilience** - HTTP resilience

## How to Use

### 1. Add Project Reference

```xml
<ItemGroup>
  <ProjectReference Include="..\NTG.Agent.ServiceDefaults\NTG.Agent.ServiceDefaults.csproj" />
</ItemGroup>
```

### 2. Configure in Program.cs

```csharp
using NTG.Agent.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Add all service defaults
builder.AddServiceDefaults();

// Add your service-specific configuration
builder.Services.AddControllers();
builder.Services.AddDbContext<MyDbContext>(...);

var app = builder.Build();

// Map default endpoints (health checks)
app.MapDefaultEndpoints();

// Your middleware and endpoints
app.MapControllers();

app.Run();
```

### 3. Use Metrics Collection

```csharp
public class MyController : ControllerBase
{
    private readonly IMetricsCollector _metrics;
    private readonly ILogger<MyController> _logger;

    public MyController(IMetricsCollector metrics, ILogger<MyController> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetData()
    {
        using var timer = _metrics.StartTimer("data.fetch");
        
        using var scope = _logger.BeginScope("GetData", new { RequestId = Guid.NewGuid() });
        
        try
        {
            var data = await FetchDataAsync();
            _metrics.RecordBusinessMetric("DataFetched", new { Count = data.Count });
            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch data");
            throw; // GlobalExceptionHandler will catch it
        }
    }
}
```

## Configuration

### OpenTelemetry Exporter

Set the OTLP endpoint via environment variable or appsettings:

```json
{
  "OTEL_EXPORTER_OTLP_ENDPOINT": "http://localhost:4317"
}
```

Or environment variable:
```bash
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"
```

### Azure Monitor (Optional)

Uncomment in Extensions.cs to enable Azure Application Insights:

```csharp
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
       .UseAzureMonitor();
}
```

Then set connection string:
```json
{
  "APPLICATIONINSIGHTS_CONNECTION_STRING": "InstrumentationKey=...;IngestionEndpoint=..."
}
```

### Service Discovery Schemes

Restrict allowed schemes if needed:

```csharp
builder.Services.Configure<ServiceDiscoveryOptions>(options =>
{
    options.AllowedSchemes = ["https"];  // Only HTTPS
});
```

## Observability with .NET Aspire

When running via NTG.Agent.AppHost (.NET Aspire):

1. **Dashboard Access:**
   - Automatically opens Aspire Dashboard
   - Shows all services, traces, metrics, logs
   - Real-time monitoring

2. **Distributed Tracing:**
   - See complete request flows across services
   - Identify bottlenecks
   - Debug failures

3. **Metrics Visualization:**
   - Service health metrics
   - Custom business metrics
   - Performance trends

4. **Log Aggregation:**
   - Centralized logs from all services
   - Correlation by trace ID
   - Filtering and search

## Development Best Practices

### Adding Custom Metrics

```csharp
// In your service
public class MyService
{
    private static readonly Meter s_meter = new("NTG.Agent.MyService");
    private static readonly Counter<int> s_operationCounter = 
        s_meter.CreateCounter<int>("operations_total", "count", "Total operations");

    public void DoOperation()
    {
        s_operationCounter.Add(1, new("operation", "create"));
    }
}
```

### Adding Custom Traces

```csharp
// In your service
public class MyService
{
    private static readonly ActivitySource s_activitySource = 
        new("NTG.Agent.MyService");

    public async Task ProcessAsync()
    {
        using var activity = s_activitySource.StartActivity("Process");
        activity?.SetTag("item.id", itemId);
        
        // Your processing logic
        
        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
```

### Health Check Extensions

```csharp
// In your service
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
    .AddDbContextCheck<MyDbContext>()
    .AddUrlGroup(new Uri("https://external-api.com"), "external-api");
```

## HTTP Resilience Configuration

The standard resilience handler can be customized:

```csharp
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });
    
    http.AddServiceDiscovery();
});
```

## Deployment Considerations

### Production Configuration

```json
{
  "OTEL_EXPORTER_OTLP_ENDPOINT": "https://otel-collector.production.com:4317",
  "ASPNETCORE_ENVIRONMENT": "Production"
}
```

### Kubernetes

Health check endpoints are disabled in non-development environments by default for security. Enable selectively:

```csharp
// In your service
if (app.Environment.IsProduction())
{
    app.MapHealthChecks("/healthz/ready");  // Readiness probe
    app.MapHealthChecks("/healthz/live");    // Liveness probe
}
```

### Docker

```yaml
services:
  my-service:
    environment:
      - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
      - ASPNETCORE_ENVIRONMENT=Production
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost/healthz/live"]
      interval: 30s
      timeout: 3s
      retries: 3
```

## Troubleshooting

### Telemetry Not Appearing

1. Check OTLP endpoint configuration
2. Verify exporter is running (`docker run -d -p 4317:4317 otel/opentelemetry-collector`)
3. Check service logs for export errors

### Health Checks Failing

1. Verify health check dependencies (databases, external APIs)
2. Check health check endpoint is called correctly
3. Review individual check status in logs

### Service Discovery Issues

1. Verify service names match in configuration
2. Check service is registered in Aspire AppHost
3. Review service discovery logs

## Testing

Mock IMetricsCollector in tests:

```csharp
public class MyServiceTests
{
    [Test]
    public async Task Test_Operation()
    {
        var metricsCollectorMock = new Mock<IMetricsCollector>();
        var service = new MyService(metricsCollectorMock.Object);
        
        await service.DoOperationAsync();
        
        metricsCollectorMock.Verify(
            m => m.RecordBusinessMetric("OperationCompleted", It.IsAny<object>()),
            Times.Once
        );
    }
}
```

## Additional Resources

- [Main Solution README](../README.md)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/net/)
- [.NET Health Checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)
- [HTTP Resilience](https://learn.microsoft.com/dotnet/core/resilience/)
