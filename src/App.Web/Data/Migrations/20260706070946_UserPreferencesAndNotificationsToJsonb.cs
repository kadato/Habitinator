using App.Shared.RCL.Models;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace App.Web.Data.Migrations;

/// <inheritdoc />
public partial class UserPreferencesAndNotificationsToJsonb : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"AspNetUsers\" ALTER COLUMN \"UserPreferencesJson\" TYPE jsonb USING NULLIF(\"UserPreferencesJson\", '')::jsonb;");
        migrationBuilder.Sql("ALTER TABLE \"AspNetUsers\" ALTER COLUMN \"NotificationSettingsJson\" TYPE jsonb USING NULLIF(\"NotificationSettingsJson\", '')::jsonb;");

        migrationBuilder.AlterColumn<UserPreferences>(
            name: "UserPreferencesJson",
            table: "AspNetUsers",
            type: "jsonb",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);

        migrationBuilder.AlterColumn<NotificationSettings>(
            name: "NotificationSettingsJson",
            table: "AspNetUsers",
            type: "jsonb",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "UserPreferencesJson",
            table: "AspNetUsers",
            type: "text",
            nullable: true,
            oldClrType: typeof(UserPreferences),
            oldType: "jsonb",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "NotificationSettingsJson",
            table: "AspNetUsers",
            type: "text",
            nullable: true,
            oldClrType: typeof(NotificationSettings),
            oldType: "jsonb",
            oldNullable: true);
    }
}
