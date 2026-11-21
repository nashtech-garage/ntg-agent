using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NTG.Agent.Orchestrator.Migrations;

/// <inheritdoc />
public partial class ConfigureTokenUsageCostPrecision : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "InputTokenCost",
            table: "TokenUsages",
            type: "decimal(18,8)",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "OutputTokenCost",
            table: "TokenUsages",
            type: "decimal(18,8)",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReasoningTokenCost",
            table: "TokenUsages",
            type: "decimal(18,8)",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalCost",
            table: "TokenUsages",
            type: "decimal(18,8)",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "InputTokenCost",
            table: "TokenUsages",
            type: "decimal(18,2)",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,8)",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "OutputTokenCost",
            table: "TokenUsages",
            type: "decimal(18,2)",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,8)",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "ReasoningTokenCost",
            table: "TokenUsages",
            type: "decimal(18,2)",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,8)",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalCost",
            table: "TokenUsages",
            type: "decimal(18,2)",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,8)",
            oldNullable: true);
    }
}
