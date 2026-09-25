using NTG.Agent.Common.Dtos.TokenUsage;

namespace NTG.Agent.Orchestrator.Services.TokenTracking;

public interface ITokenTrackingService
{
    /// <summary>
    /// Gets aggregated token usage statistics for a specific user or all users within a date range.
    /// </summary>
    /// <param name="userId">Optional user ID to filter by</param>
    /// <param name="sessionId">Optional session ID for anonymous users</param>
    /// <param name="fromDate">Start date for filtering</param>
    /// <param name="toDate">End date for filtering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated statistics</returns>
    Task<TokenUsageStatsDto> GetUsageStatsAsync(
        Guid? userId = null,
        Guid? sessionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated token usage history with optional filtering.
    /// </summary>
    /// <param name="fromDate">Start date for filtering</param>
    /// <param name="toDate">End date for filtering</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated token usage records</returns>
    Task<PagedResult<TokenUsageDto>> GetUsageHistoryAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets token usage statistics aggregated by user.
    /// </summary>
    /// <param name="fromDate">Start date for filtering</param>
    /// <param name="toDate">End date for filtering</param>
    /// <param name="topN">Number of top users to return (0 = all)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of user statistics</returns>
    Task<List<UserTokenStatsDto>> GetStatsByUserAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int topN = 0,
        CancellationToken cancellationToken = default);
}
