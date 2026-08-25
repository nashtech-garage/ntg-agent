using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class MigrateCustomProvidersToOpenAICompatible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ProviderType.Custom was removed from the enum; it behaved identically to
            // OpenAICompatible, so existing rows are converted losslessly.
            migrationBuilder.Sql("UPDATE Providers SET ProviderType = 4 WHERE ProviderType = 5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: converted rows cannot be distinguished from native OpenAICompatible rows.
        }
    }
}
