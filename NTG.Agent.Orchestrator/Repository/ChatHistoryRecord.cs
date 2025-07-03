
namespace NTG.Agent.Orchestrator.Repository;

public class ChatHistoryRecord
{
  public int Id { get; set; }
  public string ConversationId { get; set; }
  public string SerializedMessages { get; set; }
  public DateTime LastUpdated { get; set; }
}
