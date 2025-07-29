using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Models.Identity;

namespace NTG.Agent.Orchestrator.Data;

public class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations { get; set; } = null!;

    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;

    public DbSet<Models.Agents.Agent> Agents { get; set; } = null!;

    public DbSet<Models.Documents.Document> Documents { get; set; } = null!;

    public DbSet<User> Users { get; set; } = null!;

    public DbSet<User> Roles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
                    .ToTable("AspNetUsers", t => t.ExcludeFromMigrations());

        modelBuilder.Entity<Role>()
                    .ToTable("AspNetRoles", t => t.ExcludeFromMigrations());

        modelBuilder.Entity<UserRole>(t =>
        {
            t.HasKey(ur => new { ur.UserId, ur.RoleId });
            t.ToTable("AspNetUserRoles", t => t.ExcludeFromMigrations());
        });

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Models.Agents.Agent>().HasData(new Models.Agents.Agent
        {
            Id = new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
            UpdatedByUserId = new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"),
            OwnerUserId = new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"),
            CreatedAt = new DateTime(2025, 6, 24),
            UpdatedAt = new DateTime(2025, 6, 24),
            Name = "Default Agent",
            Instructions = "You are a helpful assistant. Answer questions to the best of your ability."
        });
    }
}
