using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentProvisioningStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProvisionedAt",
                table: "Agents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningError",
                table: "Agents",
                type: "nvarchar(max)",
                nullable: true);

            // Backfill existing agents as Ready (2): they were already provisioned before this
            // column existed. New agents are set to Provisioning (1) in code at creation time.
            migrationBuilder.AddColumn<int>(
                name: "ProvisioningStatus",
                table: "Agents",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                columns: new[] { "ProvisionedAt", "ProvisioningError", "ProvisioningStatus" },
                values: new object[] { null, null, 2 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProvisionedAt",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ProvisioningError",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ProvisioningStatus",
                table: "Agents");
        }
    }
}
