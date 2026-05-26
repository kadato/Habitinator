using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserActivityEventsStreakIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserActivityEvents_UserId_BoardItemId_OccurredAtUtc",
                table: "UserActivityEvents",
                columns: new[] { "UserId", "BoardItemId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserActivityEvents_UserId_BoardItemId_OccurredAtUtc",
                table: "UserActivityEvents");
        }
    }
}
