using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NTG.Agent.Orchestrator.Repository;
using System.Text.Json;

namespace NTG.Agent.Orchestrator.Agents;

public interface IChatHistoryService
{
  void AddUserMessage(string content);
  void AddAssistantMessage(string content);
  void AddSystemMessage(string content);

  IReadOnlyList<ChatMessageContent> GetMessagesForChatCompletion();

  string GetFormattedHistory();

  void Clear();
  Task SaveAsync(string conversationId);
  Task LoadAsync(string conversationId);
  Task<string> CreateNewConversationAsync(string? conversationId = null);
}

public class ChatHistoryService : IChatHistoryService
{
  private readonly ChatDbContext _db;
  private readonly ILogger<ChatHistoryService> _logger;
  private readonly List<ChatMessageContent> _messages = new();

  public ChatHistoryService(
      ChatDbContext db,
      ILogger<ChatHistoryService> logger)
  {
    _db = db;
    _logger = logger;
  }

  public void AddUserMessage(string content)
  {
    _messages.Add(new ChatMessageContent(AuthorRole.User, content));
    _logger.LogDebug("Added user message: {Content}", content);
  }

  public void AddAssistantMessage(string content)
  {
    _messages.Add(new ChatMessageContent(AuthorRole.Assistant, content));
    _logger.LogDebug("Added assistant message: {Content}", content);
  }

  public void AddSystemMessage(string content)
  {
    _messages.Add(new ChatMessageContent(AuthorRole.System, content));
    _logger.LogDebug("Added system message: {Content}", content);
  }

  public IReadOnlyList<ChatMessageContent> GetMessagesForChatCompletion()
  {
    return _messages.AsReadOnly();
  }

  public string GetFormattedHistory()
  {
    var text = string.Join("\n", _messages.Select(m =>
        $"{m.Role}: {m.Content}"
    ));

    _logger.LogDebug("Formatted history:\n{Text}", text);

    return text;
  }

  public void Clear()
  {
    _messages.Clear();
    _logger.LogDebug("Cleared chat history.");
  }

  public async Task<string> CreateNewConversationAsync(string? conversationId = null)
  {
    Clear();

    if (string.IsNullOrEmpty(conversationId))
    {
      conversationId = Guid.NewGuid().ToString();
    }

    var newRecord = new ChatHistoryRecord
    {
      ConversationId = conversationId,
      SerializedMessages = JsonSerializer.Serialize(new List<ChatMessageContent>()),
      LastUpdated = DateTime.UtcNow
    };

    _db.ChatHistories.Add(newRecord);
    await _db.SaveChangesAsync();

    _logger.LogDebug("Created new conversation with ID {ConversationId}", conversationId);

    return conversationId;
  }

  public async Task SaveAsync(string conversationId)
  {
    var json = JsonSerializer.Serialize(_messages);

    var record = await _db.ChatHistories
        .FirstOrDefaultAsync(x => x.ConversationId == conversationId);

    if (record == null)
    {
      record = new ChatHistoryRecord
      {
        ConversationId = conversationId,
        SerializedMessages = json,
        LastUpdated = DateTime.UtcNow
      };
      _db.ChatHistories.Add(record);
    }
    else
    {
      record.SerializedMessages = json;
      record.LastUpdated = DateTime.UtcNow;
    }

    await _db.SaveChangesAsync();

    _logger.LogDebug("Saved history for conversation {ConversationId}", conversationId);
  }

  public async Task LoadAsync(string conversationId)
  {
    var record = await _db.ChatHistories
        .FirstOrDefaultAsync(x => x.ConversationId == conversationId);

    if (record != null)
    {
      var deserialized = JsonSerializer.Deserialize<List<ChatMessageContent>>(record.SerializedMessages);
      _messages.Clear();
      _messages.AddRange(deserialized ?? []);
      _logger.LogDebug("Loaded history for conversation {ConversationId}", conversationId);
    }
    else
    {
      _logger.LogDebug("No history found for conversation {ConversationId}", conversationId);
      _messages.Clear();
    }
  }
}
