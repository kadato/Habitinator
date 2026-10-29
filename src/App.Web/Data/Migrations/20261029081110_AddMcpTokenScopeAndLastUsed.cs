using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddMcpTokenScopeAndLastUsed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastUsedAtUtc",
            table: "McpTokens",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Scope",
            table: "McpTokens",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "full");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LastUsedAtUtc",
            table: "McpTokens");

        migrationBuilder.DropColumn(
            name: "Scope",
            table: "McpTokens");
    }
}
