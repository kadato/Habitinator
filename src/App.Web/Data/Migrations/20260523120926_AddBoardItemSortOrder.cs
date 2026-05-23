using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardItemSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SortOrder",
                table: "BoardItems",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            // Preserve existing visual order: epoch seconds of creation time are unique and monotonic per user habit.
            migrationBuilder.Sql(
                """
                UPDATE "BoardItems"
                SET "SortOrder" = EXTRACT(EPOCH FROM "CreatedAtUtc");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "BoardItems");
        }
    }
}
