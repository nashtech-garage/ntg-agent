using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentModeAndThinkingContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReasoningTokenCost",
                table: "TokenUsages",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReasoningTokens",
                table: "TokenUsages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThinkingContent",
                table: "ChatMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "Agents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                column: "Mode",
                value: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReasoningTokenCost",
                table: "TokenUsages");

            migrationBuilder.DropColumn(
                name: "ReasoningTokens",
                table: "TokenUsages");

            migrationBuilder.DropColumn(
                name: "ThinkingContent",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Agents");
        }
    }
}
