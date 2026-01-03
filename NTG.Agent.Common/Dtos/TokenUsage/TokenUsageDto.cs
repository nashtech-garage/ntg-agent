namespace NTG.Agent.Common.Dtos.TokenUsage;

public record TokenUsageDto(
    Guid Id,
    Guid? UserId,
    Guid? SessionId,
    string? UserEmail,
    Guid ConversationId,
    string ConversationName,
    Guid? MessageId,
    Guid AgentId,
    string AgentName,
    string ModelName,
    string ProviderName,
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens,
    decimal? InputTokenCost,
    decimal? OutputTokenCost,
    decimal? TotalCost,
    string OperationType,
    TimeSpan ResponseTime,
    DateTime CreatedAt
);
