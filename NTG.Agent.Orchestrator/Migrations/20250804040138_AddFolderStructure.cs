using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeletable = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: true),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Folders_Folders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Folders",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Folders",
                columns: new[] { "Id", "AgentId", "CreatedAt", "CreatedByUserId", "IsDeletable", "Name", "ParentId", "SortOrder", "UpdatedAt", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("d1f8c2b3-4e5f-4c6a-8b7c-9d0e1f2a3b4c"), new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"), new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"), false, "All Folders", null, null, new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71") },
                    { new Guid("a2b3c4d5-e6f7-8a9b-0c1d-2e3f4f5a6b7c"), new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"), new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"), false, "Default Folder", new Guid("d1f8c2b3-4e5f-4c6a-8b7c-9d0e1f2a3b4c"), 0, new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_FolderId",
                table: "Documents",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_ParentId",
                table: "Folders",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Folders_FolderId",
                table: "Documents",
                column: "FolderId",
                principalTable: "Folders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Folders_FolderId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "Folders");

            migrationBuilder.DropIndex(
                name: "IX_Documents_FolderId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Documents");
        }
    }
}
