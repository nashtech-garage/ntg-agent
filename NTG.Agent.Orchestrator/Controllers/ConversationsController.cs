using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Extentions;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Shared.Dtos.Conversations;
using NTG.Agent.Shared.Dtos.Chats;

namespace NTG.Agent.Orchestrator.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConversationsController : ControllerBase
    {
        private readonly AgentDbContext _context;

        public ConversationsController(AgentDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ConversationSummary>>> GetConversations()
        {
            Guid? userId = User.GetUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var conversations = await _context.Conversations
                .Where(c => c.UserId == userId)
                .Include(c => c.Messages)
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new ConversationSummary
                {
                    Id = c.Id,
                    Name = c.Name,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    MessageCount = c.Messages.Count,
                    LastMessage = c.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault() != null ? 
                        c.Messages.OrderByDescending(m => m.CreatedAt).First().Content : null
                })
                .ToListAsync();

            return conversations;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ConversationDetails>> GetConversation(Guid id)
        {
            Guid? userId = User.GetUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var conversation = await _context.Conversations
                .Where(c => c.Id == id && c.UserId == userId)
                .Include(c => c.Messages)
                .FirstOrDefaultAsync();

            if (conversation == null)
            {
                return NotFound();
            }

            var conversationDetails = new ConversationDetails
            {
                Id = conversation.Id,
                Name = conversation.Name,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt,
                Messages = conversation.Messages
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => new ChatMessageDto
                    {
                        Id = m.Id,
                        Content = m.Content,
                        Role = m.Role.ToString(),
                        CreatedAt = m.CreatedAt
                    })
                    .ToList()
            };

            return conversationDetails;
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
}
