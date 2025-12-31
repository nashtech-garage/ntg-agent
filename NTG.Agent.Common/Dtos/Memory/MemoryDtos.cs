namespace NTG.Agent.Common.Dtos.Memory;

/// <summary>
/// DTO representing a user memory stored in Kernel Memory.
/// </summary>
public record UserMemoryDto(
    Guid Id,
    Guid UserId,
    string Content,
    string Category,
    string? Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastAccessedAt = null
);

/// <summary>
/// DTO for memory extraction result from LLM.
/// </summary>
public record MemoryExtractionResultDto(
    bool ShouldWriteMemory,
    float? Confidence,
    string? MemoryToWrite,
    string? Category,
    string? Tags,
    string? SearchQuery
);
