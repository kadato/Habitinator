using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class ActivityEventIdempotency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Idempotent for the same reason as the previous migration: a column or index may already
        // exist in databases that were repaired by hand or created outside the chain.
        migrationBuilder.Sql(
            "ALTER TABLE \"UserActivityEvents\" ADD COLUMN IF NOT EXISTS \"EventId\" uuid NULL;");

        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_UserActivityEvents_UserId_EventId\" " +
            "ON \"UserActivityEvents\" (\"UserId\", \"EventId\");");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_UserActivityEvents_UserId_EventId\";");
        migrationBuilder.Sql("ALTER TABLE \"UserActivityEvents\" DROP COLUMN IF EXISTS \"EventId\";");
    }
}
