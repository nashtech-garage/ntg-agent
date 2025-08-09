using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Shared.Dtos.SharedConversations;

[ApiController]
[Route("api/[controller]")]
public class SharedConversationsController : ControllerBase
{
    private readonly AgentDbContext _context;

    public SharedConversationsController(AgentDbContext context)
    {
        _context = context;
    }
    /// <summary>
    /// Shares a conversation by creating a snapshot of its messages and associating it with the requesting user.
    /// </summary>
    /// <remarks>The method requires the user to be authenticated. If the conversation contains no messages, a
    /// bad request response is returned. The shared conversation can optionally include an expiration date, as
    /// specified in the request.</remarks>
    /// <param name="conversationId">The unique identifier of the conversation to be shared.</param>
    /// <param name="request">The request containing additional details for sharing the conversation, such as a note or expiration date.</param>
    /// <returns>A string representing the unique identifier of the newly created shared conversation.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [Authorize]
    [HttpPost("{conversationId}")]
    public async Task<ActionResult<string>> ShareConversation(Guid conversationId, [FromBody] ShareConversationRequest request)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");

        // Get messages to snapshot
        var messages = await _context.ChatMessages
            .Where(m => m.ConversationId == conversationId && !m.IsSummary)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        if (!messages.Any())
            return BadRequest("Conversation has no messages.");

        var share = new SharedConversation
        {
            OriginalConversationId = conversationId,
            UserId = userId,
            Note = request.Note,
        };

        if (request.ExpiresAt.HasValue && request.ExpiresAt!= DateTime.MinValue)
        {
            share.ExpiresAt = request.ExpiresAt;
        }

        foreach (var msg in messages)
        {
            share.Messages.Add(new SharedChatMessage
            {
                Content = msg.Content,
                Role = msg.Role,
                CreatedAt = msg.CreatedAt,
                UpdatedAt = msg.UpdatedAt,
                SharedConversationId = share.Id
            });
        }

        _context.SharedConversations.Add(share);
        await _context.SaveChangesAsync();

        return Ok(share.Id);
    }
    
    /// <summary>
    /// Retrieves the shared chat messages associated with the specified share ID.
    /// </summary>
    /// <remarks>The shared chat must be active and not expired for its messages to be retrieved. 
    /// Messages are returned in ascending order of their creation time.</remarks>
    /// <param name="shareId">The unique identifier of the shared chat to retrieve.</param>
    /// <returns>An <see cref="ActionResult{T}"/> containing an <see cref="IEnumerable{T}"/> of  <see cref="SharedChatMessage"/>
    /// objects if the shared chat is found and active;  otherwise, a <see cref="NotFoundResult"/>.</returns>
    [HttpGet("public/{shareId}")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<SharedChatMessage>>> GetSharedConversation(Guid shareId)
    {
        var sharedMessages = await _context.SharedChatMessages
            .Include(s => s.SharedConversation)
            .Where(s => s.SharedConversationId == shareId && s.SharedConversation.IsActive &&
                        (s.SharedConversation.ExpiresAt.HasValue && s.SharedConversation.ExpiresAt > DateTime.Now))
            .ToListAsync();

        if (sharedMessages == null)
            return NotFound();

        return Ok(sharedMessages.OrderBy(m => m.CreatedAt));
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
    /// Revokes access to a shared conversation for the authenticated user.
    /// </summary>
    /// <param name="sharedConversationId">The unique identifier of the shared conversation to be unshared.</param>
    /// <returns>A <see cref="NoContentResult"/> if the operation is successful, or a <see cref="NotFoundResult"/>  if the
    /// specified shared conversation does not exist or does not belong to the authenticated user.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [Authorize]
    [HttpDelete("unshare/{sharedConversationId}")]
    public async Task<IActionResult> Unshare(Guid sharedConversationId)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var shared = await _context.SharedConversations
            .FirstOrDefaultAsync(s => s.Id == sharedConversationId && s.UserId == userId);

        if (shared == null)
            return NotFound();

        shared.IsActive = false;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Updates the note associated with a shared conversation.
    /// </summary>
    /// <remarks>The user must be authenticated to perform this operation. If the user is not authenticated,
    /// an <see cref="UnauthorizedAccessException"/> is thrown.</remarks>
    /// <param name="sharedConversationId">The unique identifier of the shared conversation to update.</param>
    /// <param name="request">The request containing the updated note.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation.  Returns <see cref="NotFoundResult"/> if
    /// the shared conversation does not exist or does not belong to the authenticated user.  Returns <see
    /// cref="NoContentResult"/> if the update is successful.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user is not authenticated.</exception>
    [Authorize]
    [HttpPut("{sharedConversationId}")]
    public async Task<IActionResult> UpdateNote(Guid sharedConversationId, [FromBody] ShareConversationRequest request)
    {
        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("User is not authenticated.");
        var shared = await _context.SharedConversations
            .FirstOrDefaultAsync(s => s.Id == sharedConversationId && s.UserId == userId);

        if (shared == null)
            return NotFound();

        shared.Note = request.Note;
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
