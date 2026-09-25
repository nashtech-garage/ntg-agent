namespace NTG.Agent.Orchestrator.Models.AnonymousSessions;

public class AnonymousSession
{
    public AnonymousSession()
    {
        CreatedAt = DateTime.UtcNow;
        FirstMessageAt = DateTime.UtcNow;
        LastMessageAt = DateTime.UtcNow;
        ResetAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public string? IpAddress { get; set; }

    public int MessageCount { get; set; }

    public DateTime FirstMessageAt { get; set; }

    public DateTime LastMessageAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ResetAt { get; set; }

    public bool IsBlocked { get; set; }
}
