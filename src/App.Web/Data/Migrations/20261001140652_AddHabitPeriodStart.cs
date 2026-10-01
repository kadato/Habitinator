using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddHabitPeriodStart : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "HabitPeriodStart",
            table: "BoardItems",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "HabitPeriodStart",
            table: "BoardItems");
    }
}
