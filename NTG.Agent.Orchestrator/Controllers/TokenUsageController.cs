using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTG.Agent.Common.Dtos.TokenUsage;
using NTG.Agent.Orchestrator.Services.TokenTracking;

namespace NTG.Agent.Orchestrator.Controllers;

/// <summary>
/// Controller for managing token usage tracking and statistics.
/// All endpoints require Admin role authorization.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin")]
public partial class TokenUsageController : ControllerBase
{
    private readonly ITokenTrackingService _tokenTrackingService;

    public TokenUsageController(ITokenTrackingService tokenTrackingService)
    {
        _tokenTrackingService = tokenTrackingService ?? throw new ArgumentNullException(nameof(tokenTrackingService));
    }

    /// <summary>
    /// Gets aggregated token usage statistics with optional filtering.
    /// </summary>
    /// <param name="userId">Optional user ID to filter by</param>
    /// <param name="sessionId">Optional session ID for anonymous users</param>
    /// <param name="from">Start date for filtering (UTC)</param>
    /// <param name="to">End date for filtering (UTC)</param>
    /// <returns>Aggregated token usage statistics</returns>
    [HttpGet("stats")]
    public async Task<ActionResult<TokenUsageStatsDto>> GetStats(
        [FromQuery] Guid? userId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var stats = await _tokenTrackingService.GetUsageStatsAsync(userId, sessionId, from, to);
        return Ok(stats);
    }

    /// <summary>
    /// Gets paginated token usage history with filtering options.
    /// </summary>
    /// <param name="from">Start date for filtering (UTC)</param>
    /// <param name="to">End date for filtering (UTC)</param>
    /// <param name="page">Page number (1-based, default: 1)</param>
    /// <param name="pageSize">Number of items per page (default: 50, max: 100)</param>
    /// <returns>Paginated token usage records</returns>
    [HttpGet("history")]
    public async Task<ActionResult<PagedResult<TokenUsageDto>>> GetHistory(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1)
            return BadRequest("Page number must be greater than 0");

        if (pageSize < 1 || pageSize > 100)
            return BadRequest("Page size must be between 1 and 100");

        var result = await _tokenTrackingService.GetUsageHistoryAsync(from, to, page, pageSize);

        return Ok(result);
    }

    /// <summary>
    /// Gets token usage statistics aggregated by user.
    /// </summary>
    /// <param name="from">Start date for filtering (UTC)</param>
    /// <param name="to">End date for filtering (UTC)</param>
    /// <param name="topN">Number of top users to return by token usage (0 = all users, default: 0)</param>
    /// <returns>List of user statistics ordered by total tokens descending</returns>
    [HttpGet("by-user")]
    public async Task<ActionResult<List<UserTokenStatsDto>>> GetStatsByUser(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int topN = 0)
    {
        if (topN < 0)
            return BadRequest("TopN must be greater than or equal to 0");

        var stats = await _tokenTrackingService.GetStatsByUserAsync(from, to, topN);
        return Ok(stats);
    }
}
