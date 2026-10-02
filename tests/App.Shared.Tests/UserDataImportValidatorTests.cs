using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class UserDataImportValidatorTests
{
    private static BoardSyncItem Daily(string title, Guid? id = null) => new(
        BoardSection.Daily,
        new BoardItem(
            id ?? Guid.NewGuid(),
            title,
            DailyStartDate: new DateOnly(2024, 1, 1),
            DailyRepeat: DailyRepeatType.Weekly,
            DailyRepeatInterval: 1,
            DailyWeekdays: DailyWeekdays.From(DayOfWeek.Monday, DayOfWeek.Wednesday)));

    private static UserDataExportDto Valid(params BoardSyncItem[] items) => new(
        DateTimeOffset.UtcNow,
        items,
        []);

    [Fact]
    public void NullFile_Fails()
    {
        UserDataImportValidator.Validate(null).Should().Contain("empty");
    }

    [Fact]
    public void ValidFile_Passes()
    {
        UserDataImportValidator.Validate(Valid(Daily("Gym"))).Should().BeNull();
    }

    [Fact]
    public void MissingItemData_Fails_With_ExportAgain_Hint()
    {
        var data = Valid(Daily("Gym")) with { Items = [new BoardSyncItem(BoardSection.Daily, null!)] };
        UserDataImportValidator.Validate(data).Should().Contain("Export again");
    }

    [Fact]
    public void DuplicateIds_Fail()
    {
        var id = Guid.NewGuid();
        UserDataImportValidator.Validate(Valid(Daily("A", id), Daily("B", id))).Should().Contain("reuses");
    }

    [Fact]
    public void UnknownSection_Fails()
    {
        var entry = new BoardSyncItem((BoardSection)99, new BoardItem(Guid.NewGuid(), "X"));
        UserDataImportValidator.Validate(Valid(entry)).Should().Contain("section");
    }

    [Fact]
    public void BlankTitle_Fails()
    {
        var entry = new BoardSyncItem(BoardSection.Habit, new BoardItem(Guid.NewGuid(), "  "));
        UserDataImportValidator.Validate(Valid(entry)).Should().Contain("title");
    }

    [Fact]
    public void OverlongTitle_Fails()
    {
        var entry = new BoardSyncItem(BoardSection.Habit, new BoardItem(Guid.NewGuid(), new string('x', 201)));
        UserDataImportValidator.Validate(Valid(entry)).Should().Contain("200");
    }

    [Fact]
    public void UnknownRepeat_Fails()
    {
        var item = new BoardItem(Guid.NewGuid(), "X", DailyRepeat: (DailyRepeatType)99);
        UserDataImportValidator.Validate(Valid(new BoardSyncItem(BoardSection.Daily, item))).Should().Contain("repeat");
    }

    [Fact]
    public void UnknownEventType_Fails()
    {
        var data = Valid() with
        {
            Events = [new UserActivityEventRecord(DateTimeOffset.UtcNow, (ActivityEventType)99, null, null)]
        };
        UserDataImportValidator.Validate(data).Should().Contain("event type");
    }

    [Fact]
    public void NegativeDuration_Fails()
    {
        var data = Valid() with
        {
            Events = [new UserActivityEventRecord(DateTimeOffset.UtcNow, ActivityEventType.TimerSession, null, -5)]
        };
        UserDataImportValidator.Validate(data).Should().Contain("duration");
    }

    [Fact]
    public void OversizedFile_Fails()
    {
        var items = Enumerable.Range(0, UserDataImportValidator.MaxItems + 1)
            .Select(i => Daily($"Item {i}"))
            .ToList();
        UserDataImportValidator.Validate(Valid([.. items])).Should().Contain("limit");
    }
}
