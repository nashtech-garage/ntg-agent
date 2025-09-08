using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddIsSummaryToChatMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSummary",
                table: "ChatMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSummary",
                table: "ChatMessages");
        }
    }
}
