using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderModelsAndAzureDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Azure AI Foundry discovery settings (deployments endpoint).
            migrationBuilder.AddColumn<string>(
                name: "AzureAiAccountName",
                table: "Providers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureAiProjectName",
                table: "Providers",
                type: "nvarchar(max)",
                nullable: true);

            // The provider-level default model is removed: each agent must pick a model from
            // the provider's enabled list. Backfill existing agents with their provider's
            // default so nothing silently breaks at runtime.
            migrationBuilder.Sql(@"
                UPDATE a
                SET a.ModelOverride = p.DefaultModel
                FROM Agents a
                INNER JOIN Providers p ON a.ProviderId = p.Id
                WHERE a.ModelOverride IS NULL AND p.DefaultModel IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "DefaultModel",
                table: "Providers");

            migrationBuilder.CreateTable(
                name: "ProviderModel",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AllowsThinking = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderModel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderModel_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                column: "ModelOverride",
                value: "gpt-4o");

            migrationBuilder.InsertData(
                table: "ProviderModel",
                columns: new[] { "Id", "AllowsThinking", "DisplayName", "ModelId", "ProviderId" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000002"), false, null, "gpt-4o", new Guid("00000000-0000-0000-0000-000000000001") });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderModel_ProviderId",
                table: "ProviderModel",
                column: "ProviderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderModel");

            migrationBuilder.AddColumn<string>(
                name: "DefaultModel",
                table: "Providers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "AzureAiAccountName",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "AzureAiProjectName",
                table: "Providers");

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                column: "ModelOverride",
                value: null);

            migrationBuilder.UpdateData(
                table: "Providers",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "DefaultModel",
                value: "gpt-4o");
        }
    }
}
