using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Models.Chat;

namespace NTG.Agent.Orchestrator.Data;

public class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations { get; set; } = null!;

    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;

    public DbSet<Models.Agents.Agent> Agents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Models.Agents.Agent>().HasData(new Models.Agents.Agent
        {
            Id = new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
            Name = "Default Agent",
            Instructions = "You are a helpful assistant. Answer questions to the best of your ability."
        });
    }
}
