using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NTG.Agent.ServiceDefaults.Logging;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger = logger;

    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.TraceIdentifier;
        var userId = httpContext.User?.FindFirst("sub")?.Value ?? "anonymous";

        var errorDetails = new
        {
            CorrelationId = correlationId,
            UserId = userId,
            RequestPath = httpContext.Request.Path.Value,
            RequestMethod = httpContext.Request.Method,
            UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
            IPAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            QueryString = httpContext.Request.QueryString.Value,
            ExceptionType = exception.GetType().Name,
            ExceptionMessage = exception.Message,
            exception.StackTrace,
            InnerException = exception.InnerException?.Message,
            Timestamp = DateTime.UtcNow
        };

        _logger.LogError(exception,
            "Unhandled exception occurred. CorrelationId: {CorrelationId}, UserId: {UserId}, Path: {Path}, Method: {Method}, Details: {@ErrorDetails}",
            correlationId, userId, httpContext.Request.Path, httpContext.Request.Method, errorDetails);

        if (IsCriticalError(exception))
        {
            _logger.LogCritical(exception,
                "CRITICAL ERROR - Immediate attention required. CorrelationId: {CorrelationId}, Details: {@ErrorDetails}",
                correlationId, errorDetails);
        }

        _logger.LogWarning("Security event - Unhandled exception. UserId: {UserId}, Details: {@ErrorDetails}",
            userId, errorDetails);

        // Return false to allow default exception handling middleware to continue
        // This ensures the exception is still propagated and handled appropriately
        return ValueTask.FromResult(false);
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
