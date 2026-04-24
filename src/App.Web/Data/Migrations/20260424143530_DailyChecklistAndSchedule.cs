using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DailyChecklistAndSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChecklistJson",
                table: "BoardItems",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DailyRepeatInterval",
                table: "BoardItems",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "DailyRepeatType",
                table: "BoardItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DailyStartDate",
                table: "BoardItems",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChecklistJson",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "DailyRepeatInterval",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "DailyRepeatType",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "DailyStartDate",
                table: "BoardItems");
        }
    }
}
