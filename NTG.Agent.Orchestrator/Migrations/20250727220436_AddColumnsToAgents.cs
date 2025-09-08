using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddColumnsToAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Agents",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Agents",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Agents",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "Agents",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                columns: new[] { "CreatedAt", "OwnerUserId", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"), new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71") });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_OwnerUserId",
                table: "Agents",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_UpdatedByUserId",
                table: "Agents",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Agents_OwnerUserId",
                table: "Agents");

            migrationBuilder.DropIndex(
                name: "IX_Agents_UpdatedByUserId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Agents");
        }
    }
}
