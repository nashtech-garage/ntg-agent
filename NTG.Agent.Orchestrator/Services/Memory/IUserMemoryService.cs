using NTG.Agent.Common.Dtos.Memory;
using NTG.Agent.Orchestrator.Models.Memory;

namespace NTG.Agent.Orchestrator.Services.Memory;

public interface IUserMemoryService
{
    /// <summary>
    /// Analyzes a user message using LLM to determine if it contains memorable information.
    /// </summary>
    Task<MemoryExtractionResultDto> ExtractMemoryAsync(string userMessage, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Stores a new memory for a user.
    /// </summary>
    Task<UserMemory> StoreMemoryAsync(Guid userId, string content, string category, string? tags = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves relevant memories for a user, optionally filtered by query or category.
    /// </summary>
    Task<List<UserMemory>> RetrieveMemoriesAsync(Guid userId, string? query = null, int topN = 5, string? category = null, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing memory.
    /// </summary>
    Task<UserMemory?> UpdateMemoryAsync(Guid memoryId, string content, string category, string? tags, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Deletes a specific memory by ID.
    /// </summary>
    Task<bool> DeleteMemoryAsync(Guid memoryId, CancellationToken ct = default);

    /// <summary>
    /// Deletes all memories for a specific user.
    /// </summary>
    Task<int> DeleteAllMemoriesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Gets a memory by ID.
    /// </summary>
    Task<UserMemory?> GetMemoryByIdAsync(Guid memoryId, CancellationToken ct = default);

    /// <summary>
    /// Formats memories into a string suitable for injection into system prompts.
    /// </summary>
    string FormatMemoriesForPrompt(List<UserMemory> memories);
}