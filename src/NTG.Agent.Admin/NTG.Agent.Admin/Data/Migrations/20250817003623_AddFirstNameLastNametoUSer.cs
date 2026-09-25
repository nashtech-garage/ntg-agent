using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Admin.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstNameLastNametoUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71",
                columns: new[] { "FirstName", "LastName" },
                values: new object[] { null, null });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { "3dc04c42-9b42-4920-b7f2-29dfc2c5d169", null, "Anonymous", "ANONYMOUS" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "AspNetUsers");

            migrationBuilder.DeleteData(
               table: "AspNetRoles",
               keyColumn: "Id",
               keyValue: "3dc04c42-9b42-4920-b7f2-29dfc2c5d169");
        }
    }
}
