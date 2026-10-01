using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class AlignBoardIndexesAndEventType : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Idempotent on purpose. Databases that predate the migration chain can already carry some
        // of these indexes, and a plain CREATE INDEX would abort the whole migration run, leaving
        // later migrations unapplied.
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_UserActivityEvents_UserId_BoardItemId_OccurredAtUtc\";");

        migrationBuilder.Sql("ALTER TABLE \"UserActivityEvents\" ALTER COLUMN \"EventType\" TYPE integer;");

        migrationBuilder.Sql(
            "CREATE INDEX IF NOT EXISTS \"IX_UserActivityEvents_UserId_BoardItemId_EventType_OccurredAtU~\" " +
            "ON \"UserActivityEvents\" (\"UserId\", \"BoardItemId\", \"EventType\", \"OccurredAtUtc\");");

        migrationBuilder.Sql(
            "CREATE INDEX IF NOT EXISTS \"IX_BoardItems_UserId_DeletedAtUtc\" " +
            "ON \"BoardItems\" (\"UserId\", \"DeletedAtUtc\");");

        migrationBuilder.Sql(
            "CREATE INDEX IF NOT EXISTS \"IX_BoardItems_UserId_UpdatedAtUtc\" " +
            "ON \"BoardItems\" (\"UserId\", \"UpdatedAtUtc\");");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_UserActivityEvents_UserId_BoardItemId_EventType_OccurredAtU~\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_BoardItems_UserId_DeletedAtUtc\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_BoardItems_UserId_UpdatedAtUtc\";");

        migrationBuilder.Sql("ALTER TABLE \"UserActivityEvents\" ALTER COLUMN \"EventType\" TYPE smallint;");

        migrationBuilder.Sql(
            "CREATE INDEX IF NOT EXISTS \"IX_UserActivityEvents_UserId_BoardItemId_OccurredAtUtc\" " +
            "ON \"UserActivityEvents\" (\"UserId\", \"BoardItemId\", \"OccurredAtUtc\");");
    }
}
