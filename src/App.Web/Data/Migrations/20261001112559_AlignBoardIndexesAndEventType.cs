using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class AlignBoardIndexesAndEventType : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_UserActivityEvents_UserId_BoardItemId_OccurredAtUtc",
            table: "UserActivityEvents");

        migrationBuilder.AlterColumn<int>(
            name: "EventType",
            table: "UserActivityEvents",
            type: "integer",
            nullable: false,
            oldClrType: typeof(byte),
            oldType: "smallint");

        migrationBuilder.CreateIndex(
            name: "IX_UserActivityEvents_UserId_BoardItemId_EventType_OccurredAtU~",
            table: "UserActivityEvents",
            columns: new[] { "UserId", "BoardItemId", "EventType", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_BoardItems_UserId_DeletedAtUtc",
            table: "BoardItems",
            columns: new[] { "UserId", "DeletedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_BoardItems_UserId_UpdatedAtUtc",
            table: "BoardItems",
            columns: new[] { "UserId", "UpdatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_UserActivityEvents_UserId_BoardItemId_EventType_OccurredAtU~",
            table: "UserActivityEvents");

        migrationBuilder.DropIndex(
            name: "IX_BoardItems_UserId_DeletedAtUtc",
            table: "BoardItems");

        migrationBuilder.DropIndex(
            name: "IX_BoardItems_UserId_UpdatedAtUtc",
            table: "BoardItems");

        migrationBuilder.AlterColumn<byte>(
            name: "EventType",
            table: "UserActivityEvents",
            type: "smallint",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer");

        migrationBuilder.CreateIndex(
            name: "IX_UserActivityEvents_UserId_BoardItemId_OccurredAtUtc",
            table: "UserActivityEvents",
            columns: new[] { "UserId", "BoardItemId", "OccurredAtUtc" });
    }
}
