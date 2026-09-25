using NTG.Agent.Common.Dtos.AnonymousSessions;

namespace NTG.Agent.Orchestrator.Exceptions;

public class AnonymousRateLimitExceededException : Exception
{
    public int MaxMessages { get; set; }
    
    public int CurrentCount { get; set; }
    
    public DateTime? ResetAt { get; set; }
    
    public bool IsReadOnlyMode { get; set; }
    
    public string BlockReason { get; set; } = string.Empty;

    public AnonymousRateLimitExceededException(string message, RateLimitStatus status) : base(message)
    {
        MaxMessages = status.MaxMessages;
        CurrentCount = status.CurrentCount;
        ResetAt = status.ResetAt;
        IsReadOnlyMode = status.IsReadOnlyMode;
        BlockReason = status.BlockReason ?? string.Empty;
    }
}
