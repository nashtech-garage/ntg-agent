using Microsoft.Extensions.Logging;

namespace NTG.Agent.ServiceDefaults.Logging;

public class ApplicationLogger<T>(ILogger<T> logger) : IApplicationLogger<T>
{
    private readonly ILogger<T> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _logger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel)
        => _logger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _logger.Log(logLevel, eventId, state, exception, formatter);

    public void LogUserAction(string userId, string action, object? data = null)
    {
        _logger.LogInformation("User action performed. UserId: {UserId}, Action: {Action}, Data: {@Data}",
            userId, action, data);
    }

    public void LogBusinessEvent(string eventName, object? data = null)
    {
        _logger.LogInformation("Business event occurred. Event: {EventName}, Data: {@Data}",
            eventName, data);
    }

    public void LogPerformance(string operation, TimeSpan duration, object? metadata = null)
    {
        _logger.LogInformation("Performance metric. Operation: {Operation}, Duration: {Duration}ms, Metadata: {@Metadata}",
            operation, duration.TotalMilliseconds, metadata);
    }

    public void LogSecurity(string eventType, string? userId = null, object? data = null)
    {
        _logger.LogWarning("Security event. Type: {EventType}, UserId: {UserId}, Data: {@Data}",
            eventType, userId, data);
    }

    public IDisposable BeginScope(string operation, object? parameters = null)
    {
        return _logger.BeginScope("Operation: {Operation}, Parameters: {@Parameters}", operation, parameters)
            ?? throw new InvalidOperationException("Failed to create logger scope");
    }
}
