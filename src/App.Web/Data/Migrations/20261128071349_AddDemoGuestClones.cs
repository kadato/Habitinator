using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddDemoGuestClones : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DemoGuestClones",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DemoGuestClones", x => x.UserId);
                table.ForeignKey(
                    name: "FK_DemoGuestClones_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DemoGuestClones_CreatedAtUtc",
            table: "DemoGuestClones",
            column: "CreatedAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DemoGuestClones");
    }
}
