using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Models.Documents;
using NTG.Agent.Orchestrator.Models.Identity;

namespace NTG.Agent.Orchestrator.Data;

public class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations { get; set; } = null!;

    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;

    public DbSet<Models.Agents.Agent> Agents { get; set; } = null!;

    public DbSet<Models.Documents.Document> Documents { get; set; } = null!;

    public DbSet<Models.Documents.Folder> Folders { get; set; } = null!;

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

        modelBuilder.Entity<Folder>().HasData(
            new Folder
            {
                Id = new Guid("d1f8c2b3-4e5f-4c6a-8b7c-9d0e1f2a3b4c"),
                Name = "All Folders",
                ParentId = null,
                IsDeletable = false,
                CreatedByUserId = new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"),
                UpdatedByUserId = new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"),
                CreatedAt = new DateTime(2025, 6, 24),
                UpdatedAt = new DateTime(2025, 6, 24),
                AgentId = new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5")
            }
        );

        modelBuilder.Entity<Folder>().HasData(
            new Folder
            {
                Id = new Guid("a2b3c4d5-e6f7-8a9b-0c1d-2e3f4f5a6b7c"),
                Name = "Default Folder",
                ParentId = new Guid("d1f8c2b3-4e5f-4c6a-8b7c-9d0e1f2a3b4c"),
                IsDeletable = false,
                SortOrder = 0,
                CreatedByUserId = new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"),
                UpdatedByUserId = new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"),
                CreatedAt = new DateTime(2025, 6, 24),
                UpdatedAt = new DateTime(2025, 6, 24),
                AgentId = new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5")
            }
        );
    }
}
