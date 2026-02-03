using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddMemoryPreferencesToUserPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLongTermMemoryEnabled",
                table: "UserPreferences",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMemorySearchEnabled",
                table: "UserPreferences",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLongTermMemoryEnabled",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "IsMemorySearchEnabled",
                table: "UserPreferences");
        }
    }
}
