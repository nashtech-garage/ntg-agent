using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Admin.Migrations
{
    /// <inheritdoc />
    public partial class FixSeedDataDynamicValuesForIdentityRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "3dc04c42-9b42-4920-b7f2-29dfc2c5d169",
                column: "ConcurrencyStamp",
                value: "94602b5b-18d2-4043-9761-c64818c856cd");

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "d5147680-87f5-41dc-aff2-e041959c2fa1",
                column: "ConcurrencyStamp",
                value: "c3a91a6b-a975-4542-af12-321515222481");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "3dc04c42-9b42-4920-b7f2-29dfc2c5d169",
                column: "ConcurrencyStamp",
                value: null);

            migrationBuilder.UpdateData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "d5147680-87f5-41dc-aff2-e041959c2fa1",
                column: "ConcurrencyStamp",
                value: null);
        }
    }
}
