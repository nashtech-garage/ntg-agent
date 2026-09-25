using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class RenameInnerAgentsToSubAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "AgentInnerAgents",
                newName: "AgentSubAgents");

            migrationBuilder.RenameColumn(
                name: "OuterAgentId",
                table: "AgentSubAgents",
                newName: "AgentId");

            migrationBuilder.RenameColumn(
                name: "InnerAgentId",
                table: "AgentSubAgents",
                newName: "SubAgentId");

            migrationBuilder.RenameIndex(
                name: "IX_AgentInnerAgents_InnerAgentId",
                table: "AgentSubAgents",
                newName: "IX_AgentSubAgents_SubAgentId");

            migrationBuilder.Sql("EXEC sp_rename N'[PK_AgentInnerAgents]', N'PK_AgentSubAgents', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'[FK_AgentInnerAgents_Agents_OuterAgentId]', N'FK_AgentSubAgents_Agents_AgentId', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'[FK_AgentInnerAgents_Agents_InnerAgentId]', N'FK_AgentSubAgents_Agents_SubAgentId', N'OBJECT';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("EXEC sp_rename N'[PK_AgentSubAgents]', N'PK_AgentInnerAgents', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'[FK_AgentSubAgents_Agents_AgentId]', N'FK_AgentInnerAgents_Agents_OuterAgentId', N'OBJECT';");
            migrationBuilder.Sql("EXEC sp_rename N'[FK_AgentSubAgents_Agents_SubAgentId]', N'FK_AgentInnerAgents_Agents_InnerAgentId', N'OBJECT';");

            migrationBuilder.RenameIndex(
                name: "IX_AgentSubAgents_SubAgentId",
                table: "AgentSubAgents",
                newName: "IX_AgentInnerAgents_InnerAgentId");

            migrationBuilder.RenameColumn(
                name: "AgentId",
                table: "AgentSubAgents",
                newName: "OuterAgentId");

            migrationBuilder.RenameColumn(
                name: "SubAgentId",
                table: "AgentSubAgents",
                newName: "InnerAgentId");

            migrationBuilder.RenameTable(
                name: "AgentSubAgents",
                newName: "AgentInnerAgents");
        }
    }
}
