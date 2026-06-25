using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentToolLinkedAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedAgentId",
                table: "AgentTools",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTools_LinkedAgentId",
                table: "AgentTools",
                column: "LinkedAgentId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTools_Agents_LinkedAgentId",
                table: "AgentTools",
                column: "LinkedAgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentTools_Agents_LinkedAgentId",
                table: "AgentTools");

            migrationBuilder.DropIndex(
                name: "IX_AgentTools_LinkedAgentId",
                table: "AgentTools");

            migrationBuilder.DropColumn(
                name: "LinkedAgentId",
                table: "AgentTools");
        }
    }
}
