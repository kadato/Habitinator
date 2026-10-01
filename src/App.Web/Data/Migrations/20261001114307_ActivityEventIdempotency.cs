using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class ActivityEventIdempotency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "EventId",
            table: "UserActivityEvents",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserActivityEvents_UserId_EventId",
            table: "UserActivityEvents",
            columns: new[] { "UserId", "EventId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_UserActivityEvents_UserId_EventId",
            table: "UserActivityEvents");

        migrationBuilder.DropColumn(
            name: "EventId",
            table: "UserActivityEvents");
    }
}
