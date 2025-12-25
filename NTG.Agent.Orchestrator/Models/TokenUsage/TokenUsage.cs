namespace NTG.Agent.Orchestrator.Models.TokenUsage;

public class TokenUsage
{
    public TokenUsage()
    {
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }

    // AI call context
    public Guid ConversationId { get; set; }
    public Guid? MessageId { get; set; }
    public Guid AgentId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;

    // Token metrics
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? TotalTokens { get; set; }

    // Cost tracking (optional)
    public decimal? InputTokenCost { get; set; }
    public decimal? OutputTokenCost { get; set; }
    public decimal? TotalCost { get; set; }

    // Call metadata
    public string OperationType { get; set; } = string.Empty; // "Chat", "Summarize", "GenerateName"
    public TimeSpan ResponseTime { get; set; }

    public DateTime CreatedAt { get; set; }
}
