namespace NTG.Agent.Common.Dtos.AnonymousSessions;

public class RateLimitStatus
{
    public bool CanSendMessage { get; set; }

    public bool IsReadOnlyMode { get; set; }

    public int CurrentCount { get; set; }

    public int MaxMessages { get; set; }

    public int RemainingMessages { get; set; }

    public DateTime? ResetAt { get; set; }

    public string? BlockReason { get; set; }
}
