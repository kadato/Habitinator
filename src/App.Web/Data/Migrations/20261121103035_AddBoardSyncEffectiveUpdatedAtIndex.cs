using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddBoardSyncEffectiveUpdatedAtIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The delta sync reads the watermark COALESCE(DeletedAtUtc, UpdatedAtUtc).
        // A plain (UserId, UpdatedAtUtc) index cannot serve that expression, so
        // idle sync polls would scan the user's board rows.
        migrationBuilder.Sql(
            """
            CREATE INDEX IF NOT EXISTS "IX_BoardItems_UserId_EffectiveUpdatedAtUtc"
            ON "BoardItems" ("UserId", COALESCE("DeletedAtUtc", "UpdatedAtUtc"));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "IX_BoardItems_UserId_EffectiveUpdatedAtUtc";
            """);
    }
}
