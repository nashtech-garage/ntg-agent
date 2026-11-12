using System.Text.Json.Serialization;

namespace NTG.Agent.Orchestrator.Models.Chat;

public class Conversation
{
    public Conversation()
    {     
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    [JsonRequired]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }

    [JsonRequired]
    public DateTime CreatedAt { get; set; }

    [JsonRequired]
    public DateTime UpdatedAt { get; set; }

    public List<PChatMessage> Messages { get; set; } = new List<PChatMessage>();

}
