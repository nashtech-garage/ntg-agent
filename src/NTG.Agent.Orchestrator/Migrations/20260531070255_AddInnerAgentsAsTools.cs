using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddInnerAgentsAsTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgentKind",
                table: "Agents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AgentInnerAgents",
                columns: table => new
                {
                    OuterAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InnerAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentInnerAgents", x => new { x.OuterAgentId, x.InnerAgentId });
                    table.ForeignKey(
                        name: "FK_AgentInnerAgents_Agents_InnerAgentId",
                        column: x => x.InnerAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentInnerAgents_Agents_OuterAgentId",
                        column: x => x.OuterAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                column: "AgentKind",
                value: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AgentInnerAgents_InnerAgentId",
                table: "AgentInnerAgents",
                column: "InnerAgentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentInnerAgents");

            migrationBuilder.DropColumn(
                name: "AgentKind",
                table: "Agents");
        }
    }
}
