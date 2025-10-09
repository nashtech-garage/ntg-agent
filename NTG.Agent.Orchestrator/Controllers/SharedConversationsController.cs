using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Shared.Dtos.Enums;
using NTG.Agent.Shared.Dtos.SharedConversations;

/// <summary>
/// Controller responsible for managing shared conversations in the application.
/// </summary>
/// <remarks>
/// This controller provides endpoints for creating, retrieving, updating, and deleting shared conversations.
/// Most operations require user authentication except for public access to shared conversations.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class SharedConversationsController : ControllerBase
{
    private readonly AgentDbContext _context;

    public SharedConversationsController(AgentDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }
    
    /// <summary>
    /// Creates a shared snapshot of an existing conversation.
    /// </summary>
    /// <remarks>
    /// This endpoint creates a snapshot of the specified conversation's messages and makes them shareable.
    /// The user must be authenticated and can only share their own conversations.
    /// An optional expiration date can be set to automatically expire the shared conversation.
    /// </remarks>
    /// <param name="request">The request containing conversation ID, optional expiration date, and optional name.</param>
    /// <returns>An <see cref="ActionResult{T}"/> containing the unique identifier of the newly created shared conversation.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<string>> ShareConversation([FromBody] ShareConversationRequest request)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var messages = new List<PChatMessage>();
        if (request.ChatId.HasValue)
        {
            var message = await _context.ChatMessages
                .FirstOrDefaultAsync(m => m.Id == request.ChatId && m.UserId == userId && !m.IsSummary);
            if (message != null)
            {
                messages.Add(message);
            }
        }
        else {
            messages = await _context.ChatMessages
                 .Where(m => m.ConversationId == request.ConversationId && !m.IsSummary && m.UserId == userId)
                 .OrderBy(m => m.CreatedAt)
                 .ToListAsync();
        }

        if (!messages.Any())
            return BadRequest("Conversation has no messages.");

        var sharedConversation = new SharedConversation
        {
            OriginalConversationId = request.ConversationId,
            UserId = userId,
            Name = request.Name,
            Type = request.ChatId.HasValue ? SharedType.Message : SharedType.Conversation
        };

        if(string.IsNullOrWhiteSpace(request.Name))
        {
            var conversationName = await _context.Conversations
                .Where(c => c.Id == request.ConversationId && c.UserId == userId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();
            sharedConversation.Name = conversationName;
        }

        if (request.ExpiresAt.HasValue && request.ExpiresAt!= DateTime.MinValue)
        {
            sharedConversation.ExpiresAt = request.ExpiresAt;
        }

        foreach (var msg in messages)
        {
            sharedConversation.Messages.Add(new SharedChatMessage
            {
                Content = msg.Content,
                Role = msg.Role,
                CreatedAt = msg.CreatedAt,
                UpdatedAt = msg.UpdatedAt,
                SharedConversationId = sharedConversation.Id
            });
        }

        _context.SharedConversations.Add(sharedConversation);
        await _context.SaveChangesAsync();

        return Ok(sharedConversation.Id);
    }

    /// <summary>
    /// Retrieves a shared conversation by its unique identifier.
    /// </summary>
    /// <remarks>
    /// This endpoint allows anonymous access to previously shared conversations. It performs several validations:
    /// - Returns 404 (Not Found) if the conversation doesn't exist
    /// - Returns 403 (Forbidden) if the conversation is not active
    /// - Returns 410 (Gone) if the conversation has expired
    /// If all validations pass, it returns the messages associated with the shared conversation.
    /// </remarks>
    /// <param name="shareId">The unique identifier of the shared conversation to retrieve.</param>
    /// <returns>An <see cref="ActionResult{T}"/> containing the messages of the shared conversation if found and valid.</returns>
    [HttpGet("public/{shareId}")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<SharedChatMessage>>> GetSharedConversation(Guid shareId)
    {
        var shared = await _context.SharedConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == shareId);

        if (shared is null)
            return NotFound();

        if (!shared.IsActive)
            return StatusCode(StatusCodes.Status403Forbidden);

        var now = DateTime.UtcNow;
        if (shared.ExpiresAt.HasValue && shared.ExpiresAt.Value <= now)
            return StatusCode(StatusCodes.Status410Gone);

        var messages = await _context.SharedChatMessages
            .AsNoTracking()
            .Where(m => m.SharedConversationId == shareId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        return Ok(messages);
    }

    /// <summary>
    /// Retrieves a list of conversations shared with the authenticated user.
    /// </summary>
    /// <remarks>This method returns all shared conversations associated with the currently authenticated
    /// user, ordered by the creation date in descending order. The user must be authenticated to access this
    /// endpoint.</remarks>
    /// <returns>An <see cref="ActionResult{T}"/> containing an <see cref="IEnumerable{T}"/> of <see cref="SharedConversation"/>
    /// objects representing the shared conversations.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [Authorize]
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<SharedConversation>>> GetMyShares()
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var list = await _context.SharedConversations
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// Updates the active status of a shared conversation.
    /// </summary>
    /// <remarks>This endpoint allows the owner to toggle the active status of a shared conversation.
    /// When a conversation is set to inactive (flag=false), it is effectively unshared and cannot be accessed
    /// by anyone else. The user must be authenticated to perform this operation.</remarks>
    /// <param name="sharedConversationId">The unique identifier of the shared conversation to update.</param>
    /// <param name="flag">The new active status to set (true=active, false=inactive).</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation. Returns <see cref="NotFoundResult"/> if
    /// the shared conversation does not exist or does not belong to the authenticated user. Returns <see
    /// cref="NoContentResult"/> if the update is successful.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [Authorize]
    [HttpPut("update-share/{sharedConversationId}/{flag}")]
    public async Task<IActionResult> Unshare(Guid sharedConversationId, bool flag)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var shared = await _context.SharedConversations
            .FirstOrDefaultAsync(s => s.Id == sharedConversationId && s.UserId == userId);

        if (shared == null)
            return NotFound();

        shared.IsActive = flag;
        shared.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Updates the expiration date associated with a shared conversation.
    /// </summary>
    /// <remarks>The user must be authenticated to perform this operation. If the user is not authenticated,
    /// an <see cref="UnauthorizedAccessException"/> is thrown.</remarks>
    /// <param name="sharedConversationId">The unique identifier of the shared conversation to update.</param>
    /// <param name="request">The request containing the updated expiration date.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation.  Returns <see cref="NotFoundResult"/> if
    /// the shared conversation does not exist or does not belong to the authenticated user.  Returns <see
    /// cref="NoContentResult"/> if the update is successful.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [Authorize]
    [HttpPut("{sharedConversationId}/expiration")]
    public async Task<IActionResult> UpdateExpiration(Guid sharedConversationId, [FromBody] UpdateExpirationRequest request)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var shared = await _context.SharedConversations
            .FirstOrDefaultAsync(s => s.Id == sharedConversationId && s.UserId == userId);

        if (shared == null)
            return NotFound();

        shared.ExpiresAt = request.ExpiresAt;
        shared.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return NoContent();
    }
    /// <summary>
    /// Deletes a shared conversation associated with the specified identifier.
    /// </summary>
    /// <remarks>This operation is restricted to the authenticated user who owns the shared conversation.  If
    /// the user is not authenticated, an <see cref="UnauthorizedAccessException"/> is thrown.</remarks>
    /// <param name="sharedConversationId">The unique identifier of the shared conversation to delete.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation.  Returns <see cref="NoContentResult"/> if
    /// the deletion is successful,  <see cref="NotFoundResult"/> if the shared conversation does not exist,  or <see
    /// cref="UnauthorizedResult"/> if the user is not authenticated.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [Authorize]
    [HttpDelete("{sharedConversationId}")]
    public async Task<IActionResult> DeleteSharedConversation(Guid sharedConversationId)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var shared = await _context.SharedConversations
            .FirstOrDefaultAsync(s => s.Id == sharedConversationId && s.UserId == userId);

        if (shared == null)
            return NotFound();

        _context.SharedConversations.Remove(shared);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
