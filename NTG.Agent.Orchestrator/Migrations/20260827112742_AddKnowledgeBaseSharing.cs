using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBaseSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UploadedViaAgentId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadedViaAgentName",
                table: "Documents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "KnowledgeOwnerAgentId",
                table: "Agents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                column: "KnowledgeOwnerAgentId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_KnowledgeOwnerAgentId",
                table: "Agents",
                column: "KnowledgeOwnerAgentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_Agents_KnowledgeOwnerAgentId",
                table: "Agents",
                column: "KnowledgeOwnerAgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill provenance for documents that predate knowledge-base sharing. Until now
            // AgentId meant both "the workspace" and "the agent that uploaded this", because they
            // were the same thing; from here AgentId means only the former. Every existing agent
            // owns its own knowledge base, so AgentId is already the correct owner and needs no
            // change — it is only the "uploaded via" label that has to be recovered from it.
            // LEFT JOIN: a document whose agent row is already gone keeps a null name rather than
            // dropping out of the update.
            migrationBuilder.Sql(@"
                UPDATE d
                SET d.UploadedViaAgentId   = d.AgentId,
                    d.UploadedViaAgentName = a.Name
                FROM Documents d
                LEFT JOIN Agents a ON a.Id = d.AgentId;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_Agents_KnowledgeOwnerAgentId",
                table: "Agents");

            migrationBuilder.DropIndex(
                name: "IX_Agents_KnowledgeOwnerAgentId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "UploadedViaAgentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "UploadedViaAgentName",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "KnowledgeOwnerAgentId",
                table: "Agents");
        }
    }
}
