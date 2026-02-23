using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddAnonymousSessionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnonymousSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    MessageCount = table.Column<int>(type: "int", nullable: false),
                    FirstMessageAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResetAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnonymousSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnonymousSessions_IpAddress",
                table: "AnonymousSessions",
                column: "IpAddress");

            migrationBuilder.CreateIndex(
                name: "IX_AnonymousSessions_LastMessageAt",
                table: "AnonymousSessions",
                column: "LastMessageAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnonymousSessions_SessionId",
                table: "AnonymousSessions",
                column: "SessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnonymousSessions");
        }
    }
}
