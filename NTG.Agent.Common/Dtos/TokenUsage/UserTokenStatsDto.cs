namespace NTG.Agent.Common.Dtos.TokenUsage;

public record UserTokenStatsDto(
    Guid? UserId,
    Guid? SessionId,
    string Email,
    bool IsAnonymous,
    long? TotalInputTokens,
    long? TotalOutputTokens,
    long? TotalTokens,
    decimal TotalCost,
    int ConversationCount,
    int MessageCount,
    DateTime FirstActivity,
    DateTime LastActivity
);
