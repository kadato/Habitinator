using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class BoardSyncIdempotencySoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "BoardItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BoardRequestIdempotencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestFingerprintHex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardRequestIdempotencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardRequestIdempotencies_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoardItems_DeletedAtUtc",
                table: "BoardItems",
                column: "DeletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BoardRequestIdempotencies_CreatedAtUtc",
                table: "BoardRequestIdempotencies",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BoardRequestIdempotencies_UserId_IdempotencyKey",
                table: "BoardRequestIdempotencies",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoardRequestIdempotencies");

            migrationBuilder.DropIndex(
                name: "IX_BoardItems_DeletedAtUtc",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "BoardItems");
        }
    }
}
