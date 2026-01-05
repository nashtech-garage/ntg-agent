using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.TokenUsage;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator.Services.TokenTracking;

public class TokenTrackingService : ITokenTrackingService
{
    private readonly AgentDbContext _context;
    private readonly ILogger<TokenTrackingService> _logger;

    public TokenTrackingService(AgentDbContext context, ILogger<TokenTrackingService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TokenUsageStatsDto> GetUsageStatsAsync(
        Guid? userId = null,
        Guid? sessionId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TokenUsages.AsNoTracking().AsQueryable();

        if (userId is Guid uid)
            query = query.Where(t => t.UserId == uid);

        if (sessionId is Guid sid)
            query = query.Where(t => t.SessionId == sid);

        if (fromDate is DateTime frm)
            query = query.Where(t => t.CreatedAt >= frm);

        if (toDate is DateTime tr)
            query = query.Where(t => t.CreatedAt <= tr);

        var stats = await query
            .GroupBy(t => 1)
            .Select(g => new
            {
                TotalInputTokens = g.Sum(t => t.InputTokens),
                TotalOutputTokens = g.Sum(t => t.OutputTokens),
                TotalTokens = g.Sum(t => t.TotalTokens),
                TotalCost = g.Sum(t => t.TotalCost ?? 0),
                TotalCalls = g.Count(),
                UniqueUsers = g.Where(t => t.UserId != null).Select(t => t.UserId).Distinct().Count(),
                UniqueAnonymousSessions = g.Where(t => t.SessionId != null).Select(t => t.SessionId).Distinct().Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var tokensByModel = await query
            .GroupBy(t => t.ModelName)
            .Select(g => new { Model = g.Key, Tokens = g.Sum(t => t.TotalTokens) })
            .ToDictionaryAsync(x => x.Model, x => x.Tokens, cancellationToken);

        var tokensByOperation = await query
            .GroupBy(t => t.OperationType)
            .Select(g => new { Operation = g.Key, Tokens = g.Sum(t => t.TotalTokens) })
            .ToDictionaryAsync(x => x.Operation, x => x.Tokens, cancellationToken);

        var costByProvider = await query
            .GroupBy(t => t.ProviderName)
            .Select(g => new { Provider = g.Key, Cost = g.Sum(t => t.TotalCost ?? 0) })
            .ToDictionaryAsync(x => x.Provider, x => x.Cost, cancellationToken);

        return new TokenUsageStatsDto(
            TotalInputTokens: stats?.TotalInputTokens ?? 0,
            TotalOutputTokens: stats?.TotalOutputTokens ?? 0,
            TotalTokens: stats?.TotalTokens ?? 0,
            TotalCost: stats?.TotalCost ?? 0,
            TotalCalls: stats?.TotalCalls ?? 0,
            UniqueUsers: stats?.UniqueUsers ?? 0,
            UniqueAnonymousSessions: stats?.UniqueAnonymousSessions ?? 0,
            TokensByModel: tokensByModel,
            TokensByOperation: tokensByOperation,
            CostByProvider: costByProvider
        );
    }

    public async Task<PagedResult<TokenUsageDto>> GetUsageHistoryAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Base query for filtering and counting
        var baseQuery = _context.TokenUsages.AsNoTracking().AsQueryable();
        if (fromDate is DateTime frm)
        {
            baseQuery = baseQuery.Where(t => t.CreatedAt >= frm);
        }
        if (toDate is DateTime tr)
        {
            baseQuery = baseQuery.Where(t => t.CreatedAt <= tr);
        }
        var totalCount = await baseQuery.CountAsync(cancellationToken);
        // Apply paging to the filtered base query
        var pagedQuery = baseQuery
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
        // Single query with left joins to Users and Agents, projecting directly to DTO
        var items = await
            (from t in pagedQuery
             join u in _context.Users on t.UserId equals u.Id into userGroup
             from u in userGroup.DefaultIfEmpty()
             join a in _context.Agents on t.AgentId equals a.Id into agentGroup
             from a in agentGroup.DefaultIfEmpty()
             select new TokenUsageDto(
                 t.Id,
                 t.UserId,
                 t.SessionId,
                 t.UserId.HasValue ? u!.Email : null,
                 t.ConversationId,
                 "N/A",
                 t.MessageId,
                 t.AgentId,
                 a != null ? a.Name : "Unknown",
                 t.ModelName,
                 t.ProviderName,
                 t.InputTokens,
                 t.OutputTokens,
                 t.TotalTokens,
                 t.InputTokenCost,
                 t.OutputTokenCost,
                 t.TotalCost,
                 t.OperationType,
                 t.ResponseTime,
                 t.CreatedAt
             )).ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResult<TokenUsageDto>(items, totalCount, page, pageSize, totalPages);
    }

    public async Task<List<UserTokenStatsDto>> GetStatsByUserAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int topN = 0,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TokenUsages.AsQueryable();

        if (fromDate is DateTime frm)
            query = query.Where(t => t.CreatedAt >= frm);

        if (toDate is DateTime tr)
            query = query.Where(t => t.CreatedAt <= tr);

        // Group by UserId (authenticated users)
        var authenticatedStats = await query
            .Where(t => t.UserId != null)
            .GroupBy(t => t.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                SessionId = (Guid?)null,
                IsAnonymous = false,
                TotalInputTokens = g.Sum(t => t.InputTokens),
                TotalOutputTokens = g.Sum(t => t.OutputTokens),
                TotalTokens = g.Sum(t => t.TotalTokens),
                TotalCost = g.Sum(t => t.TotalCost ?? 0),
                ConversationCount = g.Select(t => t.ConversationId).Distinct().Count(),
                MessageCount = g.Count(),
                FirstActivity = g.Min(t => t.CreatedAt),
                LastActivity = g.Max(t => t.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        // Group by SessionId (anonymous users)
        var anonymousStats = await query
            .Where(t => t.SessionId != null)
            .GroupBy(t => t.SessionId)
            .Select(g => new
            {
                UserId = (Guid?)null,
                SessionId = g.Key,
                IsAnonymous = true,
                TotalInputTokens = g.Sum(t => t.InputTokens),
                TotalOutputTokens = g.Sum(t => t.OutputTokens),
                TotalTokens = g.Sum(t => t.TotalTokens),
                TotalCost = g.Sum(t => t.TotalCost ?? 0),
                ConversationCount = g.Select(t => t.ConversationId).Distinct().Count(),
                MessageCount = g.Count(),
                FirstActivity = g.Min(t => t.CreatedAt),
                LastActivity = g.Max(t => t.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        // Combine and sort
        var allStats = authenticatedStats.Concat(anonymousStats)
            .OrderByDescending(s => s.TotalTokens)
            .AsEnumerable();

        if (topN > 0)
            allStats = allStats.Take(topN);

        var statsList = allStats.ToList();

        var userIds = statsList.Where(s => !s.IsAnonymous && s.UserId.HasValue)
            .Select(s => s.UserId!.Value)
            .Distinct()
            .ToList();

        var userEmails = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? "Unknown", cancellationToken);

        // Map to DTOs
        var result = statsList.Select(stat =>
        {
            var email = stat.IsAnonymous
                ? $"Anonymous Session {stat.SessionId?.ToString().Substring(0, 8)}"
                : userEmails.TryGetValue(stat.UserId!.Value, out var userEmail) ? userEmail : "Unknown";

            return new UserTokenStatsDto(
                stat.UserId,
                stat.SessionId,
                email,
                stat.IsAnonymous,
                stat.TotalInputTokens,
                stat.TotalOutputTokens,
                stat.TotalTokens,
                stat.TotalCost,
                stat.ConversationCount,
                stat.MessageCount,
                stat.FirstActivity,
                stat.LastActivity
            );
        }).ToList();

        return result;
    }
}
