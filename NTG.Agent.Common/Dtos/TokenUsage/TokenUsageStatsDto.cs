namespace NTG.Agent.Common.Dtos.TokenUsage;

public record TokenUsageStatsDto(
    long? TotalInputTokens,
    long? TotalOutputTokens,
    long? TotalTokens,
    decimal TotalCost,
    int TotalCalls,
    int UniqueUsers,
    int UniqueAnonymousSessions,
    Dictionary<string, long?> TokensByModel,
    Dictionary<string, long?> TokensByOperation,
    Dictionary<string, decimal> CostByProvider
);
