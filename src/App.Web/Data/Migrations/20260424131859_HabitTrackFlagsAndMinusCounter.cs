using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class HabitTrackFlagsAndMinusCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NegativeCounter",
                table: "BoardItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TrackMinus",
                table: "BoardItems",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrackPlus",
                table: "BoardItems",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.DropColumn(
                name: "IsGoodHabit",
                table: "BoardItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NegativeCounter",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "TrackMinus",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "TrackPlus",
                table: "BoardItems");

            migrationBuilder.AddColumn<bool>(
                name: "IsGoodHabit",
                table: "BoardItems",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }
    }
}
