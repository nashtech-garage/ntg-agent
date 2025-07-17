namespace NTG.Agent.Shared.Dtos.Conversations;

public class ConversationSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int MessageCount { get; set; }
    public string? LastMessage { get; set; }
}