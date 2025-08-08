using Microsoft.Extensions.Logging;

namespace NTG.Agent.ServiceDefaults.Logging;

public interface IApplicationLogger<T> : ILogger<T>
{
    void LogUserAction(string userId, string action, object? data = null);
    void LogBusinessEvent(string eventName, object? data = null);
    void LogPerformance(string operation, TimeSpan duration, object? metadata = null);
    void LogSecurity(string eventType, string? userId = null, object? data = null);
    IDisposable BeginScope(string operation, object? parameters = null);
}
