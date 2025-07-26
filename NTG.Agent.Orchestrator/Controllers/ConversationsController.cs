using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Shared.Dtos.Chats;
using NTG.Agent.Shared.Dtos.Conversations;

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

    [HttpGet("{id}")]
    public async Task<ActionResult<Conversation>> GetConversation(Guid id)
    {
        var conversation = await _context.Conversations.FindAsync(id);

        if (conversation == null)
        {
            return NotFound();
        }

        return conversation;
    }

    [Authorize]
    [HttpGet("{id}/messages")]
    public async Task<ActionResult<IList<ChatMessageListItem>>> GetConversationMessage(Guid id)
    {
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

    [HttpPost]
    public async Task<ActionResult<Conversation>> PostConversation()
    {
        Guid? userId = User.GetUserId();
        var conversation = new Conversation
        {
            Name = "New Conversation", // Default name, can be modified later
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            UserId = userId
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        return CreatedAtAction("GetConversation", new { id = conversation.Id }, new ConversationCreated { Id = conversation.Id, Name = conversation.Name });
    }

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
