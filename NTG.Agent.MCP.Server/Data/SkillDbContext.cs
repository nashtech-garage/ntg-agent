using Microsoft.EntityFrameworkCore;
using NTG.Agent.MCP.Server.Models;

namespace NTG.Agent.MCP.Server.Data;

public class SkillDbContext(DbContextOptions<SkillDbContext> options) : DbContext(options)
{
    public DbSet<Skill> Skills { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Skill>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(64);
            entity.HasIndex(s => s.Name).IsUnique();
            entity.Property(s => s.Description).HasMaxLength(1024);
        });

        SeedSampleSkill(modelBuilder);
    }

    private static void SeedSampleSkill(ModelBuilder modelBuilder)
    {
        const string content = """
            ---
            name: expense-report
            description: Use when the user asks about filing, validating, or getting reimbursed for business expenses (travel, meals, equipment). Contains the company expense policy and the step-by-step filing procedure.
            ---

            # Expense Report Skill

            You help employees file compliant expense reports.

            ## Policy rules

            - Meals are reimbursable up to $50 per person per day; alcohol is never reimbursable.
            - Hotel stays require an itemized receipt; the nightly rate cap is $250 unless pre-approved.
            - Flights must be economy class for trips under 6 hours.
            - Any single expense over $500 needs written manager pre-approval attached.
            - Receipts are mandatory for every expense over $25.

            ## Filing procedure

            1. Ask the user for: date, category (meal, lodging, travel, equipment, other), amount, and whether they have a receipt.
            2. Check every item against the policy rules above and flag violations clearly.
            3. Summarize the compliant items in a table the user can paste into the expense portal.
            4. For flagged items, explain exactly which rule is violated and what the user should do (e.g. obtain approval, split the bill).
            """;

        modelBuilder.Entity<Skill>().HasData(new Skill
        {
            Id = new Guid("7A3F5C21-9B4E-4D8A-B6F0-2E1C8D9A0F53"),
            Name = "expense-report",
            Description = "Use when the user asks about filing, validating, or getting reimbursed for business expenses (travel, meals, equipment). Contains the company expense policy and the step-by-step filing procedure.",
            Content = content,
            CreatedAt = new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
