using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Memory;

public interface IUserMemoryService
{
    /// <summary>
    /// Processes a user message to extract, validate, and store memories automatically.
    /// Handles conflict detection and memory updates based on configured settings.
    /// </summary>
    /// <param name="userMessage">The user's message to analyze</param>
    /// <param name="userId">The user's ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of memories successfully stored</returns>
    Task<int> ProcessAndStoreMemoriesAsync(string userMessage, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves relevant memories and formats them as a chat message for context injection.
    /// Returns null if no memories are found or feature is disabled.
    /// </summary>
    /// <param name="userId">The user's ID</param>
    /// <param name="userPrompt">The current user prompt for semantic search</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Formatted chat message with memory context, or null if no memories</returns>
    Task<ChatMessage?> RetrieveAndFormatMemoriesForChatAsync(Guid userId, string userPrompt, CancellationToken ct = default);
}