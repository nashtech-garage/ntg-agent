using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class SeedFolderData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Folders",
                columns: new[] { "Id", "AgentId", "CreatedAt", "CreatedByUserId", "IsDeletable", "Name", "ParentId", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new Guid("d1f8c2b3-4e5f-4c6a-8b7c-9d0e1f2a3b4c"), new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"), new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"), false, "All Folders", null, new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71") });

            migrationBuilder.InsertData(
                table: "Folders",
                columns: new[] { "Id", "AgentId", "CreatedAt", "CreatedByUserId", "IsDeletable", "Name", "ParentId", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new Guid("a2b3c4d5-e6f7-8a9b-0c1d-2e3f4f5a6b7c"), new Guid("31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"), new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71"), false, "Default Folder", new Guid("d1f8c2b3-4e5f-4c6a-8b7c-9d0e1f2a3b4c"), new DateTime(2025, 6, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Folders",
                keyColumn: "Id",
                keyValue: new Guid("a2b3c4d5-e6f7-8a9b-0c1d-2e3f4f5a6b7c"));

            migrationBuilder.DeleteData(
                table: "Folders",
                keyColumn: "Id",
                keyValue: new Guid("d1f8c2b3-4e5f-4c6a-8b7c-9d0e1f2a3b4c"));
        }
    }
}
