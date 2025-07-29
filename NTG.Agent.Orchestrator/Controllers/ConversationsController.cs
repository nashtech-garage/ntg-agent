using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Shared.Dtos.Chats;
using NTG.Agent.Shared.Dtos.Conversations;
using System;

namespace NTG.Agent.Orchestrator.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ConversationsController : ControllerBase
{
    private readonly AgentDbContext _context;

    public ConversationsController(AgentDbContext context)
    {
        _context = context;
    }
    /// <summary>
    /// Retrieves a list of conversations for the current user.
    /// </summary>
    /// <remarks>The conversations are returned in descending order based on the last update time. This method
    /// requires the user to be authenticated.</remarks>
    /// <returns>A task that represents the asynchronous operation. The task result contains an  <see cref="ActionResult{T}"/> of
    /// <see cref="IEnumerable{T}"/> containing  <see cref="ConversationListItem"/> objects, each representing a
    /// conversation.</returns>
    [Authorize]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConversationListItem>>> GetConversations()
    {
        return await _context.Conversations
            .Where(c => c.UserId == User.GetUserId())
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ConversationListItem(c.Id, c.Name))
            .ToListAsync();
    }

    /// <summary>
    /// Retrieves a conversation by its unique identifier.
    /// </summary>
    /// <remarks>This method supports both authenticated and unauthenticated users. Authenticated users are
    /// identified by their user ID, while unauthenticated users must provide a valid session ID. The method returns a
    /// conversation only if it is associated with the requesting user's ID or session ID.</remarks>
    /// <param name="id">The unique identifier of the conversation to retrieve.</param>
    /// <param name="currentSessionId">The session identifier for the current user session. This parameter is required for unauthenticated requests.</param>
    /// <returns>An <see cref="ActionResult{T}"/> containing the <see cref="Conversation"/> if found; otherwise, a <see
    /// cref="NotFoundResult"/> if the conversation does not exist or a <see cref="BadRequestResult"/> if the session ID
    /// is invalid for unauthenticated requests.</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<Conversation>> GetConversation(Guid id, [FromQuery] string? currentSessionId)
    {
        Guid? userId = User.GetUserId();
        Conversation? conversation;

        if (userId.HasValue)
        {
            // Authenticated user
            conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId.Value);
        }
        else
        {
            // Anonymous user: sessionId must be provided and valid
            if (!Guid.TryParse(currentSessionId, out Guid sessionId))
            {
                return BadRequest("A valid Session ID is required for unauthenticated requests.");
            }

            conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == id && c.SessionId == sessionId);
        }

        return conversation is not null ? conversation : NotFound();
    }


    /// <summary>
    /// Retrieves a list of chat messages for a specified conversation.
    /// </summary>
    /// <remarks>This method supports both authenticated and unauthenticated users. Authenticated users must
    /// have a valid user ID associated with the conversation. Unauthenticated users must provide a valid session ID to
    /// access the conversation.</remarks>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <param name="currentSessionId">The session ID for unauthenticated requests. Must be a valid GUID.</param>
    /// <returns>A list of <see cref="ChatMessageListItem"/> representing the messages in the conversation, ordered by creation
    /// time. Returns <see cref="NotFoundResult"/> if the conversation is not found or the user is not authorized.</returns>
    [HttpGet("{id}/messages")]
    public async Task<ActionResult<IList<ChatMessageListItem>>> GetConversationMessage(Guid id, [FromQuery] string? currentSessionId)
    {
        Guid? userId = User.GetUserId();
        bool isAuthorized = false;

        if (userId.HasValue)
        {
            isAuthorized = await _context.Conversations
                .AnyAsync(c => c.Id == id && c.UserId == userId.Value);
        }
        else
        {
            if (!Guid.TryParse(currentSessionId, out Guid sessionId))
            {
                return BadRequest("A valid Session ID is required for unauthenticated requests.");
            }

            isAuthorized = await _context.Conversations
                .AnyAsync(c => c.Id == id && c.SessionId == sessionId);
        }

        if (!isAuthorized)
        {
            return Unauthorized();
        }

        var chatMessages = await _context.ChatMessages
            .Where(x => x.ConversationId == id && !x.IsSummary)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new ChatMessageListItem
            {
                Id = x.Id,
                Content = x.Content,
                Role = (int)x.Role
            })
            .ToListAsync();

        return chatMessages;
    }


    /// <summary>
    /// Searches for conversation messages and conversation names containing the specified keyword.
    /// </summary>
    /// <remarks>This method searches both conversation names and messages for the specified keyword. The
    /// search is case-sensitive and uses a simple string containment check. The results include both conversation names
    /// and message contents, with each result indicating whether it is a conversation or a message.</remarks>
    /// <param name="keyword">The keyword to search for within conversation names and messages. Cannot be null or whitespace.</param>
    /// <returns>A list of <see cref="ChatSearchResultItem"/> containing the search results. Returns an empty list if the keyword
    /// is null, whitespace, or no matches are found.</returns>
    [Authorize]
    [HttpGet("search")]
    public async Task<ActionResult<IList<ChatSearchResultItem>>> SearchConversationMessages([FromQuery]string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Ok(new List<ChatSearchResultItem>());
        }

        var userId = User.GetUserId();
        
        // Execute both queries separately and then combine the results
        
        // Query for conversations - using standard string Contains instead of full-text search
        var conversationResults = await _context.Conversations
            .Where(c => c.UserId == userId && 
                  c.Name.Contains(keyword))
            .Select(c => new ChatSearchResultItem
            {
                ConversationId = c.Id,
                Content = c.Name,
                Role = (int)ChatRole.User,
                IsConversation = true
            })
            .ToListAsync();
        
        // Query for messages - using standard string Contains instead of full-text search
        var messagesQuery = await _context.ChatMessages
            .Where(m => m.UserId == userId && 
                  m.Content.Contains(keyword))
            .Select(m => new 
            {
                m.ConversationId,
                m.Content,
                m.Role
            })
            .ToListAsync();
            
        // Process message results with client-side method
        var messageResults = messagesQuery
            .Select(m => new ChatSearchResultItem
            {
                ConversationId = m.ConversationId,
                Content = GetContentWithKeywordContext(m.Content, keyword, m.Role == ChatRole.Assistant),
                Role = (int)m.Role,
                IsConversation = false
            })
            .ToList();
        
        // Combine the results
        var combinedResults = conversationResults.Concat(messageResults).ToList();
        
        return Ok(combinedResults);
    }

    /// <summary>
    /// Extracts content around the matched keyword to provide context
    /// </summary>
    private string GetContentWithKeywordContext(string content, string keyword, bool isAssistant)
    {
        // For non-assistant messages or short messages, return the full content
        if (!isAssistant || content.Length <= 200)
        {
            return content;
        }
        
        // For assistant messages, extract content around the keyword
        int maxContextLength = 200; // Max characters to show
        int keywordPos = content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        
        if (keywordPos < 0) // Shouldn't happen, but just in case
            return content.Length <= maxContextLength ? content : content.Substring(0, maxContextLength) + "...";
        
        int startPos = Math.Max(0, keywordPos - maxContextLength / 2);
        int endPos = Math.Min(content.Length, keywordPos + keyword.Length + maxContextLength / 2);
        int length = endPos - startPos;
        
        string result = content.Substring(startPos, length);
        
        // Add ellipses if we've trimmed the text
        if (startPos > 0)
            result = "..." + result;
        if (endPos < content.Length)
            result = result + "...";
        
        return result;
    }

    /// <summary>
    /// Updates an existing conversation with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation to update. Must match the <paramref name="conversation"/>.Id.</param>
    /// <param name="conversation">The conversation object containing updated data. The <see cref="Conversation.Id"/> must match the <paramref
    /// name="id"/>.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation. Returns <see cref="BadRequestResult"/> if
    /// the <paramref name="id"/> does not match the <paramref name="conversation"/>.Id. Returns <see
    /// cref="NotFoundResult"/> if the conversation does not exist. Returns <see cref="NoContentResult"/> if the update
    /// is successful.</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> PutConversation(Guid id, Conversation conversation)
    {
        if (id != conversation.Id)
        {
            return BadRequest();
        }

        _context.Entry(conversation).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ConversationExists(id))
            {
                return NotFound();
            }
            else
            {
                throw;
            }
        }

        return NoContent();
    }

    /// <summary>
    /// Renames an existing conversation for the current user.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation to rename.</param>
    /// <param name="newName">The new name to assign to the conversation. Cannot be null or empty.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation. Returns <see cref="BadRequestResult"/> if
    /// the conversation is not found or the user is unauthorized. Returns <see cref="NoContentResult"/> if the rename
    /// operation is successful.</returns>
    [Authorize]
    [HttpPut("{id}/rename")]
    public async Task<IActionResult> RenameConversation(Guid id, string newName)
    {
        var conversationToUpdate = await _context.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == User.GetUserId());
        if (conversationToUpdate == null)
        {
            return BadRequest();
        }
        conversationToUpdate.Name = newName;
        conversationToUpdate.UpdatedAt = DateTime.UtcNow;
        _context.Entry(conversationToUpdate).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ConversationExists(id))
            {
                return NotFound();
            }
            else
            {
                throw;
            }
        }
        return NoContent();
    }

    /// <summary>
    /// Creates a new conversation and saves it to the database.
    /// </summary>
    /// <remarks>If the user is authenticated, the conversation is associated with the user's ID. If the user
    /// is not authenticated and a valid session ID is provided, the conversation is associated with the session
    /// ID.</remarks>
    /// <param name="currentSessionId">The session identifier for the current user session. This parameter is used to associate the conversation with a
    /// session if the user is not authenticated.</param>
    /// <returns>An <see cref="ActionResult{T}"/> containing the created <see cref="Conversation"/> object, with a status code
    /// indicating the result of the operation.</returns>
    [HttpPost]
    public async Task<ActionResult<Conversation>> PostConversation([FromQuery]string? currentSessionId)
    {
        Guid? userId = User.GetUserId();
        var conversation = new Conversation
        {
            Name = "New Conversation", // Default name, can be modified later
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            UserId = userId,
            SessionId = (!userId.HasValue && !string.IsNullOrWhiteSpace(currentSessionId))? Guid.Parse(currentSessionId): null // Set SessionId if user is not authenticated. TODO: Implement a clean-up job/mechanism for the anonymous conversations + chats.
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        return CreatedAtAction("GetConversation", new { id = conversation.Id }, new ConversationCreated { Id = conversation.Id, Name = conversation.Name });
    }

    /// <summary>
    /// Deletes a conversation identified by the specified ID.
    /// </summary>
    /// <remarks>This method requires the user to be authorized. It deletes the conversation only if it
    /// belongs to the current user.</remarks>
    /// <param name="id">The unique identifier of the conversation to delete.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the result of the operation.  Returns <see cref="NotFoundResult"/> if
    /// the conversation does not exist or the user is not authorized to delete it. Returns <see
    /// cref="NoContentResult"/> if the deletion is successful.</returns>
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteConversation(Guid id)
    {
        var conversation = await _context.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == User.GetUserId());
        if (conversation == null)
        {
            return NotFound();
        }

        _context.Conversations.Remove(conversation);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool ConversationExists(Guid id)
    {
        return _context.Conversations.Any(e => e.Id == id);
    }
}
