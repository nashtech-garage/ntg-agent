using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddTitleGenerationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TitleGenerationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderModelName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderEndpoint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderApiKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleGenerationSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "TitleGenerationSettings",
                columns: new[] { "Id", "ProviderApiKey", "ProviderEndpoint", "ProviderModelName", "ProviderName", "UpdatedAt" },
                values: new object[] { new Guid("b5e7a3c1-9f2d-4e8a-bc10-000000000001"), "", "", "", "", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TitleGenerationSettings");
        }
    }
}
