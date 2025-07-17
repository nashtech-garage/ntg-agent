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
            .Select(c => new ConversationListItem (c.Id, c.Name))
            .ToListAsync();
    }

    [Authorize]
    [HttpGet("{id}/messages")]
    public async Task<ActionResult<IList<ChatMessageListItem>>> GetConversationMessage(Guid id)
    {
        var chatMessages = await _context.ChatMessages
            .Where(x => x.ConversationId == id)
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

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteConversation(Guid id)
    {
        var conversation = await _context.Conversations.FindAsync(id);
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
