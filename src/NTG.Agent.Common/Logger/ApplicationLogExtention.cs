using Microsoft.Extensions.Logging;

namespace NTG.Agent.Common.Logger;

public static partial class ApplicationLogExtention
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "User action performed. UserId: {UserId}, Action: {Action}, Data: {@Data}")]
    public static partial void LogUserAction(this ILogger logger, string userId, string action, object? data);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Business event occurred. Event: {EventName}, Data: {@Data}")]
    public static partial void LogBusinessEvent(this ILogger logger, string eventName, object? data);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Performance metric. Operation: {Operation}, Duration: {Duration}ms, Metadata: {@Metadata}")]
    public static partial void LogPerformance(this ILogger logger, string operation, double duration, object? metadata);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "Security event. Type: {EventType}, UserId: {UserId}, Data: {@Data}")]
    public static partial void LogSecurity(this ILogger logger, string eventType, string? userId, object? data);
}
