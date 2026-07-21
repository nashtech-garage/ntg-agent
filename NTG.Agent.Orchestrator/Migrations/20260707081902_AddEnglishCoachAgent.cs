using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddEnglishCoachAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Agents",
                columns: new[] { "Id", "AgentKind", "CreatedAt", "Description", "Instructions", "IsDefault", "IsPublished", "McpServer", "Mode", "Name", "OwnerUserId", "ProviderApiKey", "ProviderEndpoint", "ProviderModelName", "ProviderName", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new Guid("f0b9a7d7-2f4e-4d8f-8c0b-5e3a1e2c4f11"), 0, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "A friendly AI English tutor for practicing conversation, improving grammar, and expanding vocabulary.", "You are a friendly English coach. Your goals are:\r\n- Help users improve spoken English.\r\n- Keep the conversation natural.\r\n- Correct only the most important grammar mistakes.\r\n- Explain corrections simply.\r\n- Suggest more natural vocabulary when appropriate.\r\n- Encourage the learner.\r\n- Always end your response with a follow-up question to continue the conversation.", false, true, "http://localhost:5136", 0, "English Coach Agent", "e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71", "", "https://models.github.ai/inference", "openai/gpt-4.1", "GitHubModel", new DateTime(2026, 6, 25, 0, 0, 0, 0, DateTimeKind.Utc), "e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("f0b9a7d7-2f4e-4d8f-8c0b-5e3a1e2c4f11"));
        }
    }
}
