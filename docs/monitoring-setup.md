# Monitoring and logging

How this solution produces logs, metrics, traces and health signals — and which pieces are
actually wired up, because not all of them are wired up everywhere.

Everything below comes from `NTG.Agent.ServiceDefaults` and `NTG.Agent.Common`. There are no
third-party logging or APM packages: it is built-in `ILogger`, `System.Diagnostics.Metrics`
and OpenTelemetry.

## What you get from one line

Every service calls `builder.AddServiceDefaults()` in its `Program.cs`. That single call
(`NTG.Agent.ServiceDefaults/Extensions.cs`) does five things:

| Registered | Effect |
|---|---|
| `ConfigureOpenTelemetry()` | Logging → OTel, plus ASP.NET Core / HttpClient / runtime instrumentation, plus the `NTG.Agent` meter |
| `AddDefaultHealthChecks()` | A `self` liveness check tagged `live` |
| `AddScoped<IMetricsCollector, MetricsCollector>()` | Custom business metrics |
| `AddExceptionHandler<GlobalExceptionHandler>()` | Structured logging of unhandled exceptions |
| Service discovery + standard resilience on all `HttpClient`s | Retries, timeouts, circuit breaker |

`app.MapDefaultEndpoints()` then adds the health endpoints. It adds **nothing else** — it does
not add exception-handling or logging middleware.

## Where the pieces live

| Component | File |
|---|---|
| `AddServiceDefaults`, `ConfigureOpenTelemetry`, `MapDefaultEndpoints` | `NTG.Agent.ServiceDefaults/Extensions.cs` |
| `GlobalExceptionHandler` | `NTG.Agent.ServiceDefaults/Logging/GlobalExceptionHandler.cs` |
| `IMetricsCollector` / `MetricsCollector` | `NTG.Agent.ServiceDefaults/Logging/Metrics/` |
| `LogUserAction` / `LogBusinessEvent` / `LogPerformance` / `LogSecurity` | `NTG.Agent.Common/Logger/ApplicationLogExtention.cs` |

## Structured logging

There is no custom logger type. You inject the ordinary `ILogger<T>` and call four
source-generated extension methods on it, from `NTG.Agent.Common.Logger`:

| Method | Level | EventId |
|---|---|---|
| `LogUserAction(userId, action, data)` | Information | 1000 |
| `LogBusinessEvent(eventName, data)` | Information | 1001 |
| `LogPerformance(operation, durationMs, metadata)` | Information | 1002 |
| `LogSecurity(eventType, userId, data)` | Warning | 1003 |

They are `[LoggerMessage]` partials, so the message template is compiled in — no boxing, no
runtime format parsing, and a stable EventId you can filter on.

## Metrics

`IMetricsCollector` wraps a `System.Diagnostics.Metrics.Meter` named `NTG.Agent`, which
`ConfigureOpenTelemetry` subscribes to via `.AddMeter("NTG.Agent")`.

```csharp
void IncrementCounter(string name, double value = 1, params (string Key, string Value)[] tags);
void RecordValue(string name, double value, params (string Key, string Value)[] tags);
void RecordDuration(string name, TimeSpan duration, params (string Key, string Value)[] tags);
IDisposable StartTimer(string name, params (string Key, string Value)[] tags);
void RecordBusinessMetric(string eventName, object data, params (string Key, string Value)[] tags);
```

Two things to know about the implementation:

- `RecordDuration` and `StartTimer` record into a histogram named `<name>.duration_ms`, not
  `<name>`.
- `RecordBusinessMetric` increments a counter named `business.<eventName>`. The `data` argument
  is logged, not turned into metric dimensions — only `tags` become dimensions.

## A real call site

`DocumentsController.GetDocuments` is the worked example in the codebase:

```csharp
private readonly ILogger<DocumentsController> _logger;
private readonly IMetricsCollector _metrics;

using var scope = _logger.BeginScope(new Dictionary<string, object> { ["AgentId"] = agentId });
using var timer = _metrics.StartTimer("documents.get", ("agent_id", agentId.ToString()));

// ... business logic ...

_logger.LogBusinessEvent("DocumentsRetrieved", new { AgentId = agentId, DocumentCount = documents.Count });
_metrics.RecordBusinessMetric("DocumentsRetrieved", new { AgentId = agentId, documents.Count });
```

`BeginScope` takes a dictionary, not a name plus an object. OpenTelemetry only carries scope
values through because `ConfigureOpenTelemetry` sets `logging.IncludeScopes = true`.

## Exception handling

`GlobalExceptionHandler` implements `IExceptionHandler`. On an unhandled exception it emits up
to three records, all carrying the same `errorDetails` payload (correlation id from
`HttpContext.TraceIdentifier`, the `sub` claim or `"anonymous"`, path, method, user agent,
remote IP, query string, exception type/message/stack, inner message, UTC timestamp):

1. `LogError` — always.
2. `LogCritical` — when `IsCriticalError` matches: `OutOfMemoryException`,
   `StackOverflowException`, `AccessViolationException`, or a message containing `"database"`
   or `"timeout"`.
3. `LogWarning` labelled `Security event` — always.

It returns `false`, so it logs and then lets the normal pipeline produce the response. It never
shapes the response body.

**Two caveats that matter in practice.**

`AddExceptionHandler` only registers the handler. It runs when the app also calls
`UseExceptionHandler(...)` — the path overload counts, because
`ExceptionHandlerMiddleware` runs every registered `IExceptionHandler` before falling back to
the configured path. Where each app calls it:

| App | Development | Production |
|---|---|---|
| Orchestrator | yes — `/error-development` | yes — `/error` |
| WebClient | **no** | yes — `/Error` |
| Admin | **no** | yes — `/Error` |

So in local development, an unhandled exception in either Blazor host is not captured by
`GlobalExceptionHandler`.

The `"database"` / `"timeout"` substring match in `IsCriticalError` fires on any message that
happens to contain those words, including ordinary validation text. Treat `LogCritical` from
this handler as a hint, not an alert condition.

## Health endpoints

Registered by `MapDefaultEndpoints()` **only when `app.Environment.IsDevelopment()`** — by
design, per `https://aka.ms/dotnet/aspire/healthchecks`.

| Endpoint | Passes when |
|---|---|
| `/health` | every registered check is healthy |
| `/alive` | every check tagged `live` is healthy |

Only one check exists today: `self`, which always returns healthy. Nothing probes SQL Server,
Qdrant or LightRAG. A green `/health` means the process is answering HTTP, nothing more.

## Traces

`ConfigureOpenTelemetry` adds an activity source named after
`builder.Environment.ApplicationName`, plus ASP.NET Core and HttpClient instrumentation. gRPC
instrumentation and the Azure Monitor exporter are present but commented out.

## Exporting

The OTLP exporter is enabled only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set:

```json
{ "OTEL_EXPORTER_OTLP_ENDPOINT": "http://localhost:4317" }
```

Under `NTG.Agent.AppHost` the Aspire dashboard sets this for every child resource, and its
Traces / Metrics / Structured-logs tabs are the intended place to read all of the above.
Starting a service standalone without that variable produces logs on the console and drops
metrics and traces on the floor — this is a common cause of "the app started and then died"
when a service is launched by hand.

## Log level configuration

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

## Known rough edges

- `MetricsCollector` is registered **scoped** but constructs a `Meter` and caches instruments
  per instance, so each request scope builds its own. OpenTelemetry reconciles them by name so
  the numbers are right, but it is avoidable allocation. A singleton would be the correct
  lifetime.
- Every `IncrementCounter` / `RecordValue` call also writes an Information log line. At any
  volume this makes metrics the loudest thing in the log stream.
- No health check covers a dependency, so readiness is not actually being measured.
