# NTG Agent - Monitoring & Logging Setup

## 🎯 Overview

### **Built-in Monitoring System**
- **Exception Handling**: IExceptionHandler-based global error tracking
- **Performance Metrics**: OpenTelemetry metrics and custom business tracking
- **Health Monitoring**: ASP.NET Core health checks
- **Structured Logging**: Built-in ASP.NET Core logging with OpenTelemetry
- **Configuration-Driven**: Log levels controlled via appsettings.json

---

## 🚀 Features

### **1. Exception Handling (IExceptionHandler)**
- **GlobalExceptionHandler**: Modern ASP.NET Core exception handling
- **Critical Error Detection**: Automatic identification of system-threatening issues
- **Detailed Context**: Request info, user data, correlation IDs, stack traces
- **Security Event Logging**: Tracks unhandled exceptions as security events

### **2. Structured Logging**
- **Built-in ASP.NET Core Logging**: No external dependencies
- **OpenTelemetry Integration**: Tracing and metrics collection
- **Configuration-Driven**: Log levels set via `Logging:LogLevel:Default` in appsettings
- **ApplicationLogger**: Custom wrapper for business events and user actions

### **3. Performance Metrics**
- **MetricsCollector**: Custom business metrics using .NET Diagnostics.Metrics
- **OpenTelemetry**: Built-in ASP.NET Core and HTTP instrumentation
- **Business Intelligence**: Document operations, user actions, performance timers

### **4. Health Checks**
- **Standard Health Checks**: Basic application responsiveness
- **Development Endpoints**: `/health` and `/alive` in development mode

---

## 🔧 Configuration Examples


### **Development Logging Configuration**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.EntityFrameworkCore": "Information",
      "NTG.Agent": "Debug"
    },
    "Console": {
      "FormatterName": "simple",
      "FormatterOptions": {
        "SingleLine": false,
        "IncludeScopes": true,
        "TimestampFormat": "HH:mm:ss.fff "
      }
    }
  }
}
```

### **OpenTelemetry Configuration**
```json
{
  "OTEL_EXPORTER_OTLP_ENDPOINT": "http://localhost:4317"
}
```

---

## 📊 Monitoring Endpoints

### **Health Check URLs**
- `/health` - Overall application health (development only)
- `/alive` - Basic liveness probe (development only)

### **Sample Health Check Response**
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0123456",
  "entries": {
    "self": {
      "status": "Healthy",
      "tags": ["live"]
    }
  }
}
```

---

## 🔍 Usage Examples

### **ApplicationLogger in Controllers**
```csharp
public class DocumentsController : ControllerBase
{
    private readonly IApplicationLogger<DocumentsController> _logger;
    private readonly IMetricsCollector _metrics;

    public async Task<IActionResult> GetDocuments(Guid agentId)
    {
        using var scope = _logger.BeginScope("GetDocuments", new { AgentId = agentId });
        using var timer = _metrics.StartTimer("documents.get");

        _logger.LogUserAction(User.GetUserId()?.ToString(), "GetDocuments", new { AgentId = agentId });
        _metrics.IncrementCounter("documents.requests", 1, ("operation", "get"));

        // ... business logic ...

        _logger.LogBusinessEvent("DocumentsRetrieved", new { AgentId = agentId, Count = documents.Count });
        return Ok(documents);
    }
}
```

### **Sample Log Output**
```
info: Request started: GET /api/documents/123
info: User action performed. UserId: user123, Action: GetDocuments, Data: {"AgentId": "123"}
info: Business event occurred. Event: DocumentsRetrieved, Data: {"AgentId": "123", "Count": 5}
info: Request completed: GET /api/documents/123 - 200 in 45ms
```

---

## 🚨 Production Setup

### **ServiceDefaults Integration**
All logging and exception handling is automatically configured when using `builder.AddServiceDefaults()` in your Program.cs:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); // Sets up logging, metrics, and exception handling

var app = builder.Build();
app.MapDefaultEndpoints(); // Adds exception handling and logging middleware
```

### **Configuration Checklist**
- [ ] Set appropriate log levels in `appsettings.json`
- [ ] Configure OpenTelemetry endpoint if using external monitoring
- [ ] Test exception handling produces proper logs
- [ ] Verify health check endpoints work in development

### **Key Features**
- **No External Dependencies**: Uses only built-in ASP.NET Core logging
- **Modern Exception Handling**: IExceptionHandler instead of middleware
- **OpenTelemetry Ready**: Built-in metrics and tracing support
- **Configuration-Driven**: Log levels controlled via appsettings.json

---

## 🔄 Available Components

### **Logging Components (NTG.Agent.ServiceDefaults.Logging)**
- `IApplicationLogger<T>` - Enhanced logger with business methods
- `ApplicationLogger<T>` - Implementation with LogUserAction, LogBusinessEvent, etc.
- `IMetricsCollector` - Custom metrics collection interface
- `MetricsCollector` - OpenTelemetry-compatible metrics implementation
- `GlobalExceptionHandler` - IExceptionHandler implementation
- `LoggingMiddleware` - Request/response logging middleware

### **Built-in Logging Methods**
- `LogUserAction(userId, action, data)` - Track user activities
- `LogBusinessEvent(eventName, data)` - Record business events
- `LogPerformance(operation, duration, metadata)` - Performance tracking
- `LogSecurity(eventType, userId, data)` - Security event logging

---

## 📊 Monitoring

The system automatically provides:
- **Request/Response Logging**: Via LoggingMiddleware
- **Exception Tracking**: Via GlobalExceptionHandler with correlation IDs
- **OpenTelemetry Metrics**: ASP.NET Core, HTTP, and custom business metrics
- **Health Checks**: Basic liveness and readiness probes
- **Structured Logging**: JSON or simple format based on environment
