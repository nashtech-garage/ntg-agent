using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.MCP.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Skills",
                columns: new[] { "Id", "Content", "CreatedAt", "Description", "Name", "UpdatedAt" },
                values: new object[] { new Guid("7a3f5c21-9b4e-4d8a-b6f0-2e1c8d9a0f53"), "---\nname: expense-report\ndescription: Use when the user asks about filing, validating, or getting reimbursed for business expenses (travel, meals, equipment). Contains the company expense policy and the step-by-step filing procedure.\n---\n\n# Expense Report Skill\n\nYou help employees file compliant expense reports.\n\n## Policy rules\n\n- Meals are reimbursable up to $50 per person per day; alcohol is never reimbursable.\n- Hotel stays require an itemized receipt; the nightly rate cap is $250 unless pre-approved.\n- Flights must be economy class for trips under 6 hours.\n- Any single expense over $500 needs written manager pre-approval attached.\n- Receipts are mandatory for every expense over $25.\n\n## Filing procedure\n\n1. Ask the user for: date, category (meal, lodging, travel, equipment, other), amount, and whether they have a receipt.\n2. Check every item against the policy rules above and flag violations clearly.\n3. Summarize the compliant items in a table the user can paste into the expense portal.\n4. For flagged items, explain exactly which rule is violated and what the user should do (e.g. obtain approval, split the bill).", new DateTime(2026, 7, 13, 0, 0, 0, 0, DateTimeKind.Utc), "Use when the user asks about filing, validating, or getting reimbursed for business expenses (travel, meals, equipment). Contains the company expense policy and the step-by-step filing procedure.", "expense-report", new DateTime(2026, 7, 13, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Name",
                table: "Skills",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Skills");
        }
    }
}
