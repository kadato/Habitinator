using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddAiUsageDaily : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiUsageDaily",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Day = table.Column<DateOnly>(type: "date", nullable: false),
                Requests = table.Column<int>(type: "integer", nullable: false),
                InputTokens = table.Column<long>(type: "bigint", nullable: false),
                OutputTokens = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiUsageDaily", x => new { x.UserId, x.Day });
                table.ForeignKey(
                    name: "FK_AiUsageDaily_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiUsageDaily");
    }
}
