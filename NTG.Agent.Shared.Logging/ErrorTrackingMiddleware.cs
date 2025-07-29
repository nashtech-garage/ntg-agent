using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NTG.Agent.Shared.Logging;

public class ErrorTrackingMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    // Only Singleton services can be resolved by constructor injection in Middleware
    public async Task InvokeAsync(HttpContext context, IApplicationLogger<ErrorTrackingMiddleware> logger)
    {
        logger.LogInformation("Handling request: {Method} {Path}", context.Request.Method, context.Request.Path);
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex, logger);
                throw; // Re-throw to maintain the exception flow
            }
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception, IApplicationLogger<ErrorTrackingMiddleware> logger)
    {
        var correlationId = context.TraceIdentifier;
        var userId = context.User?.FindFirst("sub")?.Value ?? "anonymous";

        var errorDetails = new
        {
            CorrelationId = correlationId,
            UserId = userId,
            RequestPath = context.Request.Path.Value,
            RequestMethod = context.Request.Method,
            UserAgent = context.Request.Headers["User-Agent"].ToString(), // Capture browser/client info for debugging context
            IPAddress = context.Connection.RemoteIpAddress?.ToString(),
            QueryString = context.Request.QueryString.Value,
            Headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
            ExceptionType = exception.GetType().Name,
            ExceptionMessage = exception.Message,
            exception.StackTrace,
            InnerException = exception.InnerException?.Message,
            Timestamp = DateTime.UtcNow
        };

        logger.LogError(exception,
            "Unhandled exception occurred. CorrelationId: {CorrelationId}, UserId: {UserId}, Path: {Path}, Method: {Method}, Details: {@ErrorDetails}",
            correlationId, userId, context.Request.Path, context.Request.Method, errorDetails);

        if (IsCriticalError(exception))
        {
            logger.LogCritical(exception,
                "CRITICAL ERROR - Immediate attention required. CorrelationId: {CorrelationId}, Details: {@ErrorDetails}",
                correlationId, errorDetails);
        }

        logger.LogSecurity("UnhandledException", userId, errorDetails);

        return Task.CompletedTask;
    }

    private static bool IsCriticalError(Exception exception)
    {
        return exception is OutOfMemoryException ||
               exception is StackOverflowException ||
               exception is AccessViolationException ||
               exception.Message.Contains("database", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }
}
