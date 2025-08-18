using Microsoft.EntityFrameworkCore;
using Microsoft.KernelMemory;
using NTG.Agent.Orchestrator.Models.Chat;
using NTG.Agent.Orchestrator.Models.Documents;
using NTG.Agent.Orchestrator.Models.Identity;
using NTG.Agent.Orchestrator.Models.Tags;
using NTG.Agent.Shared.Dtos.Constants;
namespace NTG.Agent.Orchestrator.Data;

public class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations { get; set; } = null!;

    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;

    public DbSet<SharedConversation> SharedConversations { get; set; } = null!;
    public DbSet<SharedChatMessage> SharedChatMessages { get; set; } = null!;

    public DbSet<Models.Agents.Agent> Agents { get; set; } = null!;

    public DbSet<Models.Documents.Document> Documents { get; set; } = null!;

    public DbSet<Models.Documents.Folder> Folders { get; set; } = null!;
    
    public DbSet<Tag> Tags { get; set; } = null!;

    public DbSet<TagRole> TagRoles { get; set; } = null!;

    public DbSet<DocumentTag> DocumentTags { get; set; } = null!;

    public DbSet<User> Users { get; set; } = null!;

    public DbSet<Role> Roles { get; set; } = null!;

    public DbSet<UserRole> UserRoles { get; set; } = null!;

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

        modelBuilder.Entity<SharedConversation>()
            .HasMany(sc => sc.Messages)
            .WithOne(m => m.SharedConversation)
            .HasForeignKey(m => m.SharedConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Tag>().HasData(
           new Tag
           {
               Id = new Guid("10dd4508-4e35-4c63-bd74-5d90246c7770"),
               Name = "Public",
               CreatedAt = new DateTime(2025, 6, 24),
               UpdatedAt = new DateTime(2025, 6, 24)
           }
       );

        modelBuilder.Entity<TagRole>().HasData(
           new TagRole
           {
               Id = new Guid("22c3bf7d-a7d0-4770-b9b2-cd6587089bd4"),
               TagId = new Guid("10dd4508-4e35-4c63-bd74-5d90246c7770"),
               RoleId = new Guid(Shared.Dtos.Constants.Constants.AnonymousRoleId),
               CreatedAt = new DateTime(2025, 6, 24),
               UpdatedAt = new DateTime(2025, 6, 24)
           }
       );
    }
}
