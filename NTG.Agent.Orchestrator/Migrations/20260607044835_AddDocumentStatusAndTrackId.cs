using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentStatusAndTrackId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            // Existing documents are all fully processed, so backfill them to Completed (2).
            // New uploads set Status explicitly to Processing (1) in code.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "TrackId",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TrackId",
                table: "Documents");
        }
    }
}
