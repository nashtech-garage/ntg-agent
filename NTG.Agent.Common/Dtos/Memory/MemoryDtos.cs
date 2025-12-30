namespace NTG.Agent.Common.Dtos.Memory;

/// <summary>
/// DTO for retrieving a user memory.
/// </summary>
public record MemoryDto(
    Guid Id,
    Guid UserId,
    string Content,
    string Category,
    string? Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsActive,
    int AccessCount,
    DateTime? LastAccessedAt
);

/// <summary>
/// DTO for creating a new memory manually.
/// </summary>
public record MemoryCreateDto(
    string Content,
    string Category = "general",
    string? Tags = null
);

/// <summary>
/// DTO for updating an existing memory.
/// </summary>
public record MemoryUpdateDto(
    string Content,
    string Category,
    string? Tags,
    bool IsActive
);

/// <summary>
/// DTO for memory extraction result from LLM.
/// </summary>
public record MemoryExtractionResultDto(
    bool ShouldWriteMemory,
    float? Confidence,
    string? MemoryToWrite,
    string? Category,
    string? Tags
);

/// <summary>
/// DTO for memory search/retrieval request.
/// </summary>
public record MemorySearchRequest(
    Guid UserId,
    string? Query = null,
    int TopN = 5,
    string? Category = null
);

/// <summary>
/// DTO for bulk memory deletion.
/// </summary>
public record MemoryDeleteRequest(
    Guid UserId,
    List<Guid>? MemoryIds = null,
    bool DeleteAll = false
);
