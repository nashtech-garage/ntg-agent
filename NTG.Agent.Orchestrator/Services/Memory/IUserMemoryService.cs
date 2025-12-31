using NTG.Agent.Common.Dtos.Memory;

namespace NTG.Agent.Orchestrator.Services.Memory;

public interface IUserMemoryService
{
    /// <summary>
    /// Analyzes a user message using LLM to determine if it contains memorable information.
    /// Returns a list of memory items to store separately.
    /// </summary>
    Task<List<MemoryExtractionResultDto>> ExtractMemoryAsync(string userMessage, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Stores a new memory for a user.
    /// </summary>
    Task<UserMemoryDto> StoreMemoryAsync(Guid userId, string content, string category, string? tags = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves relevant memories for a user, optionally filtered by query or category.
    /// </summary>
    Task<List<UserMemoryDto>> RetrieveMemoriesAsync(Guid userId, string? query = null, int topN = 5, string? category = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves memories for a user by specific field tag (for precise conflict detection).
    /// </summary>
    Task<List<UserMemoryDto>> RetrieveMemoriesByFieldAsync(Guid userId, string fieldTag, string? category = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes a specific memory by ID.
    /// </summary>
    Task<bool> DeleteMemoryAsync(Guid memoryId, CancellationToken ct = default);

    /// <summary>
    /// Formats memories into a string suitable for injection into system prompts.
    /// </summary>
    string FormatMemoriesForPrompt(List<UserMemoryDto> memories);
}