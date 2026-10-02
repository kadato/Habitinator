using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class UserDataExportDtoTests
{
    [Fact]
    public void LegacyFile_WithoutNewFields_Deserializes_With_Defaults()
    {
        const string legacy = """{"exportedAtUtc":"2024-01-08T00:00:00Z","items":[],"events":[]}""";
        var dto = JsonSerializer.Deserialize<UserDataExportDto>(legacy, JsonDefaults.Api);
        Assert.NotNull(dto);
        dto.Preferences.Should().BeNull();
        dto.NotificationSettings.Should().BeNull();
        dto.ExportedForLocalDay.Should().Be(default);
        UserDataImportValidator.Validate(dto).Should().BeNull();
    }

    [Fact]
    public void RoundTrip_Keeps_Sections_Preferences_And_Local_Day()
    {
        var dto = new UserDataExportDto(
            DateTimeOffset.UtcNow,
            [new BoardSyncItem(BoardSection.Todo, new BoardItem(Guid.NewGuid(), "Milk", TodoDueDate: new DateOnly(2024, 1, 9)))],
            [new UserActivityEventRecord(DateTimeOffset.UtcNow, ActivityEventType.TodoComplete, null, null)],
            new UserPreferences { DisplayName = "Ada" },
            new NotificationSettings { DailyReminderEnabled = false },
            new DateOnly(2024, 1, 8));

        var json = JsonSerializer.Serialize(dto, JsonDefaults.Export);
        var back = JsonSerializer.Deserialize<UserDataExportDto>(json, JsonDefaults.Api);
        Assert.NotNull(back);
        back.Items.Should().ContainSingle().Which.Section.Should().Be(BoardSection.Todo);
        back.Preferences!.DisplayName.Should().Be("Ada");
        back.NotificationSettings!.DailyReminderEnabled.Should().BeFalse();
        back.ExportedForLocalDay.Should().Be(new DateOnly(2024, 1, 8));
        UserDataImportValidator.Validate(back).Should().BeNull();
    }
}
