using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentHandoffWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSelectable",
                table: "Agents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AgentHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentHandoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentHandoffs_Agents_SourceAgentId",
                        column: x => x.SourceAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentHandoffs_Agents_TargetAgentId",
                        column: x => x.TargetAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                column: "IsSelectable",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentHandoffs_SourceAgentId_TargetAgentId",
                table: "AgentHandoffs",
                columns: new[] { "SourceAgentId", "TargetAgentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentHandoffs_TargetAgentId",
                table: "AgentHandoffs",
                column: "TargetAgentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentHandoffs");

            migrationBuilder.DropColumn(
                name: "IsSelectable",
                table: "Agents");
        }
    }
}
