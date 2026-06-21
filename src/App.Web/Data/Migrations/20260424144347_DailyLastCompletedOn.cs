using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class DailyLastCompletedOn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DailyLastCompletedOn",
            table: "BoardItems",
            type: "timestamp with time zone",
            nullable: true);

        // Completed dailies before this column had no date; treat them as done on upgrade day (UTC).
        migrationBuilder.Sql(
            """
            UPDATE "BoardItems"
            SET "DailyLastCompletedOn" = date_trunc('day', NOW() AT TIME ZONE 'utc')
            WHERE "Section" = 'Daily' AND "IsCompleted" = true;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DailyLastCompletedOn",
            table: "BoardItems");
    }
}
