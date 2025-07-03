
using Microsoft.EntityFrameworkCore;

namespace NTG.Agent.Orchestrator.Repository;

public class ChatDbContext : DbContext
{
  public DbSet<ChatHistoryRecord> ChatHistories { get; set; }

  public ChatDbContext(DbContextOptions<ChatDbContext> options)
      : base(options)
  {
  }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<ChatHistoryRecord>(entity =>
    {
      entity.HasKey(e => e.Id);
      entity.Property(e => e.SerializedMessages).HasColumnType("TEXT");
    });
  }
}
