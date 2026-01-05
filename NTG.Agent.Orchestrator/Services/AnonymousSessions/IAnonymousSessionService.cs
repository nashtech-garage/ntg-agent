using NTG.Agent.Common.Dtos.AnonymousSessions;

namespace NTG.Agent.Orchestrator.Services.AnonymousSessions;

public interface IAnonymousSessionService
{
    Task<RateLimitStatus> CheckRateLimitAsync(Guid sessionId, string? ipAddress);
    
    Task IncrementMessageCountAsync(Guid sessionId, string? ipAddress);
}
