using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDeletable2FolderTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
               name: "IsDeletable",
               table: "Folders",
               type: "bit",
               nullable: false,
               defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
               name: "IsDeletable",
               table: "Folders");
        }
    }
}
