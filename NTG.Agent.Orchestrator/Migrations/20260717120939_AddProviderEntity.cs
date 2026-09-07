using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderApiKey",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ProviderEndpoint",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ProviderModelName",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "Agents");

            migrationBuilder.AddColumn<string>(
                name: "ModelOverride",
                table: "Agents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderId",
                table: "Agents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Providers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProviderType = table.Column<int>(type: "int", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultModel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Providers", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                columns: new[] { "ModelOverride", "ProviderId" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_ProviderId",
                table: "Agents",
                column: "ProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_Providers_ProviderId",
                table: "Agents",
                column: "ProviderId",
                principalTable: "Providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_Providers_ProviderId",
                table: "Agents");

            migrationBuilder.DropTable(
                name: "Providers");

            migrationBuilder.DropIndex(
                name: "IX_Agents_ProviderId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ModelOverride",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "Agents");

            migrationBuilder.AddColumn<string>(
                name: "ProviderApiKey",
                table: "Agents",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderEndpoint",
                table: "Agents",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderModelName",
                table: "Agents",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "Agents",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Agents",
                keyColumn: "Id",
                keyValue: new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"),
                columns: new[] { "ProviderApiKey", "ProviderEndpoint", "ProviderModelName", "ProviderName" },
                values: new object[] { "", "", "", "" });
        }
    }
}
