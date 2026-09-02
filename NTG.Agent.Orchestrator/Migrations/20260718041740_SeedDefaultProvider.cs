using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insert the default provider first so the FK constraint passes
            var defaultProviderId = new Guid("00000000-0000-0000-0000-000000000001");
            migrationBuilder.Sql($"IF NOT EXISTS (SELECT 1 FROM Providers WHERE Id = '{defaultProviderId}') INSERT INTO Providers (Id, Name, ProviderType, DefaultModel, CreatedAt, UpdatedAt) VALUES ('{defaultProviderId}', 'Default Provider', 0, 'gpt-4o', '2025-06-24', '2025-06-24')");

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                column: "ProviderId",
                value: defaultProviderId);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Providers",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                column: "ProviderId",
                value: null);
        }
    }
}
