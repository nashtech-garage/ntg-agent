using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Memory;
using NTG.Agent.Common.Logger;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Services.Memory;

namespace NTG.Agent.Orchestrator.Controllers;

/// <summary>
/// Controller for managing long-term user memories that persist across chat sessions.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class MemoriesController : ControllerBase
{
    private readonly IUserMemoryService _memoryService;
    private readonly AgentDbContext _dbContext;
    private readonly ILogger<MemoriesController> _logger;

    public MemoriesController(
        IUserMemoryService memoryService,
        AgentDbContext dbContext,
        ILogger<MemoriesController> logger)
    {
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves all memories for the current user.
    /// </summary>
    /// <param name="category">Optional category filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of user memories.</returns>
    /// <response code="200">Returns the list of memories.</response>
    /// <response code="401">If the user is not authenticated.</response>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MemoryDto>>> GetMemories(
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId == null)
        {
            return Unauthorized("User is not authenticated.");
        }

        var query = _dbContext.UserMemories
            .Where(m => m.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(m => m.Category == category);
        }

        var memories = await query
            .OrderByDescending(m => m.UpdatedAt)
            .Select(m => new MemoryDto(
                m.Id,
                m.UserId,
                m.Content,
                m.Category,
                m.Tags,
                m.CreatedAt,
                m.UpdatedAt,
                m.IsActive,
                m.AccessCount,
                m.LastAccessedAt
            ))
            .ToListAsync(ct);

        return Ok(memories);
    }

    /// <summary>
    /// Retrieves a specific memory by ID.
    /// </summary>
    /// <param name="id">The unique identifier of the memory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The requested memory.</returns>
    /// <response code="200">Returns the memory.</response>
    /// <response code="404">If the memory is not found.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user doesn't own this memory.</response>
    [HttpGet("{id}")]
    public async Task<ActionResult<MemoryDto>> GetMemoryById(Guid id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId == null)
        {
            return Unauthorized("User is not authenticated.");
        }

        var memory = await _dbContext.UserMemories
            .Where(m => m.Id == id && m.UserId == userId.Value)
            .Select(m => new MemoryDto(
                m.Id,
                m.UserId,
                m.Content,
                m.Category,
                m.Tags,
                m.CreatedAt,
                m.UpdatedAt,
                m.IsActive,
                m.AccessCount,
                m.LastAccessedAt
            ))
            .FirstOrDefaultAsync(ct);

        if (memory == null)
        {
            return NotFound();
        }

        return Ok(memory);
    }

    /// <summary>
    /// Creates a new memory manually for the current user.
    /// </summary>
    /// <param name="dto">The memory creation details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created memory.</returns>
    /// <response code="201">Returns the newly created memory.</response>
    /// <response code="400">If the content is empty or invalid.</response>
    /// <response code="401">If the user is not authenticated.</response>
    [HttpPost]
    public async Task<ActionResult<MemoryDto>> CreateMemory(
        [FromBody] MemoryCreateDto dto,
        CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId == null)
        {
            return Unauthorized("User is not authenticated.");
        }

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return BadRequest("Memory content is required.");
        }

        var memory = await _memoryService.StoreMemoryAsync(
            userId.Value,
            dto.Content.Trim(),
            dto.Category,
            dto.Tags,
            ct);

        var result = new MemoryDto(
            memory.Id,
            memory.UserId,
            memory.Content,
            memory.Category,
            memory.Tags,
            memory.CreatedAt,
            memory.UpdatedAt,
            memory.IsActive,
            memory.AccessCount,
            memory.LastAccessedAt
        );

        _logger.LogBusinessEvent("MemoryCreated", new { MemoryId = memory.Id, UserId = userId.Value });
        return CreatedAtAction(nameof(GetMemoryById), new { id = memory.Id }, result);
    }

    /// <summary>
    /// Updates an existing memory.
    /// </summary>
    /// <param name="id">The unique identifier of the memory to update.</param>
    /// <param name="dto">The updated memory details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">The memory was successfully updated.</response>
    /// <response code="400">If the content is empty or invalid.</response>
    /// <response code="404">If the memory is not found.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user doesn't own this memory.</response>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateMemory(
        Guid id,
        [FromBody] MemoryUpdateDto dto,
        CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId == null)
        {
            return Unauthorized("User is not authenticated.");
        }

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return BadRequest("Memory content is required.");
        }

        // Verify ownership
        var existingMemory = await _dbContext.UserMemories
            .Where(m => m.Id == id && m.UserId == userId.Value)
            .FirstOrDefaultAsync(ct);

        if (existingMemory == null)
        {
            return NotFound();
        }

        var updatedMemory = await _memoryService.UpdateMemoryAsync(
            id,
            dto.Content.Trim(),
            dto.Category,
            dto.Tags,
            dto.IsActive,
            ct);

        if (updatedMemory == null)
        {
            return NotFound();
        }

        _logger.LogBusinessEvent("MemoryUpdated", new { MemoryId = id, UserId = userId.Value });
        return NoContent();
    }

    /// <summary>
    /// Deletes a specific memory by ID.
    /// </summary>
    /// <param name="id">The unique identifier of the memory to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">The memory was successfully deleted.</response>
    /// <response code="404">If the memory is not found.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="403">If the user doesn't own this memory.</response>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMemory(Guid id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId == null)
        {
            return Unauthorized("User is not authenticated.");
        }

        // Verify ownership
        var memory = await _dbContext.UserMemories
            .Where(m => m.Id == id && m.UserId == userId.Value)
            .FirstOrDefaultAsync(ct);

        if (memory == null)
        {
            return NotFound();
        }

        var deleted = await _memoryService.DeleteMemoryAsync(id, ct);
        if (!deleted)
        {
            return NotFound();
        }

        _logger.LogBusinessEvent("MemoryDeleted", new { MemoryId = id, UserId = userId.Value });
        return NoContent();
    }

    /// <summary>
    /// Deletes all memories for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of memories deleted.</returns>
    /// <response code="200">Returns the count of deleted memories.</response>
    /// <response code="401">If the user is not authenticated.</response>
    [HttpDelete("all")]
    public async Task<ActionResult<int>> DeleteAllMemories(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId == null)
        {
            return Unauthorized("User is not authenticated.");
        }

        var count = await _memoryService.DeleteAllMemoriesAsync(userId.Value, ct);

        _logger.LogBusinessEvent("AllMemoriesDeleted", new { UserId = userId.Value, Count = count });
        return Ok(new { DeletedCount = count });
    }

    /// <summary>
    /// Searches for memories using semantic or text-based search.
    /// </summary>
    /// <param name="request">The search request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of relevant memories.</returns>
    /// <response code="200">Returns the search results.</response>
    /// <response code="401">If the user is not authenticated.</response>
    [HttpPost("search")]
    public async Task<ActionResult<IEnumerable<MemoryDto>>> SearchMemories(
        [FromBody] MemorySearchRequest request,
        CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId == null)
        {
            return Unauthorized("User is not authenticated.");
        }

        // Ensure the user can only search their own memories
        if (request.UserId != userId.Value)
        {
            return Forbid();
        }

        var memories = await _memoryService.RetrieveMemoriesAsync(
            request.UserId,
            request.Query,
            request.TopN,
            request.Category,
            ct);

        var results = memories.Select(m => new MemoryDto(
            m.Id,
            m.UserId,
            m.Content,
            m.Category,
            m.Tags,
            m.CreatedAt,
            m.UpdatedAt,
            m.IsActive,
            m.AccessCount,
            m.LastAccessedAt
        )).ToList();

        return Ok(results);
    }

    /// <summary>
    /// Gets memory statistics for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Memory statistics.</returns>
    /// <response code="200">Returns memory statistics.</response>
    /// <response code="401">If the user is not authenticated.</response>
    [HttpGet("statistics")]
    public async Task<ActionResult<object>> GetMemoryStatistics(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        if (userId == null)
        {
            return Unauthorized("User is not authenticated.");
        }

        var stats = await _dbContext.UserMemories
            .Where(m => m.UserId == userId.Value)
            .GroupBy(m => m.Category)
            .Select(g => new
            {
                Category = g.Key,
                Count = g.Count(),
                TotalAccesses = g.Sum(m => m.AccessCount)
            })
            .ToListAsync(ct);

        var totalCount = await _dbContext.UserMemories
            .CountAsync(m => m.UserId == userId.Value, ct);

        return Ok(new
        {
            TotalMemories = totalCount,
            CategoriesBreakdown = stats
        });
    }
}
