using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Models.Chat;

namespace NTG.Agent.Orchestrator.Data;

public class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations { get; set; } = null!;

    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;
}
