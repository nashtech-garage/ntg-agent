using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Admin.Migrations
{
    /// <inheritdoc />
    public partial class AddSeedUserAndRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { "d5147680-87f5-41dc-aff2-e041959c2fa1", null, "Admin", "ADMIN" });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71", 0, "101cd6ae-a8ef-4a37-97fd-04ac2dd630e4", "admin@ngtagent.com", true, true, null, "ADMIN@NTGAGENT.COM", "ADMIN@NTGAGENT.COM", "AQAAAAIAAYagAAAAEFsVZF5T07uT8PrK0tMUqluwlOJO4XIk/fjLX7QuyYlbKtSI01akUmzGGhOrF5j6lg==", null, false, "a9565acb-cee6-425f-9833-419a793f5fba", false, "admin@ngtagent.com" });

            migrationBuilder.InsertData(
                table: "AspNetUserRoles",
                columns: new[] { "RoleId", "UserId" },
                values: new object[] { "d5147680-87f5-41dc-aff2-e041959c2fa1", "e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUserRoles",
                keyColumns: new[] { "RoleId", "UserId" },
                keyValues: new object[] { "d5147680-87f5-41dc-aff2-e041959c2fa1", "e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71" });

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "d5147680-87f5-41dc-aff2-e041959c2fa1");

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71");
        }
    }
}
