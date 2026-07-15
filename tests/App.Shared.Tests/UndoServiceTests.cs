using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using FluentAssertions;

using MudBlazor;

using NSubstitute;

namespace App.Shared.Tests;

public sealed class UndoServiceTests : IDisposable
{
    private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();
    private readonly INotificationSettingsService _settingsService = Substitute.For<INotificationSettingsService>();
    private readonly INotificationSettingsRules _notificationRules = Substitute.For<INotificationSettingsRules>();
    private readonly UndoService _undoService;

    public UndoServiceTests()
    {
        _settingsService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new NotificationSettings()));
        _notificationRules.UndoVisibleStateDurationMs(Arg.Any<NotificationToastDuration>())
            .Returns(12_000);

        _undoService = new UndoService(_snackbar, _settingsService, _notificationRules);
    }

    public void Dispose()
    {
        _undoService.Dispose();
    }

    [Fact]
    public void CanUndo_should_be_false_initially()
    {
        _undoService.CanUndo.Should().BeFalse();
        _undoService.LastActionDescription.Should().BeNull();
    }

    [Fact]
    public void RegisterUndo_should_push_to_stack_and_notify()
    {
        bool actionCalled = false;
        bool stateChangedCalled = false;
        _undoService.OnStateChanged += () => stateChangedCalled = true;

        _undoService.RegisterUndo("Test Action", () =>
        {
            actionCalled = true;
            return Task.CompletedTask;
        });

        _undoService.CanUndo.Should().BeTrue();
        _undoService.LastActionDescription.Should().Be("Test Action");
        stateChangedCalled.Should().BeTrue();
        actionCalled.Should().BeFalse();
    }

    [Fact]
    public async Task UndoAsync_should_execute_action_and_pop_from_stack()
    {
        bool actionCalled = false;
        bool undoPerformedCalled = false;

        _undoService.RegisterUndo("Test Action", () =>
        {
            actionCalled = true;
            return Task.CompletedTask;
        });
        _undoService.OnUndoPerformed += () => undoPerformedCalled = true;

        await _undoService.UndoAsync();

        actionCalled.Should().BeTrue();
        undoPerformedCalled.Should().BeTrue();
        _undoService.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task BeginBatch_should_group_multiple_actions_into_one()
    {
        bool action1Called = false;
        bool action2Called = false;

        using (_undoService.BeginBatch("Batch Action"))
        {
            _undoService.RegisterUndo("Sub 1", () =>
            {
                action1Called = true;
                return Task.CompletedTask;
            });
            _undoService.RegisterUndo("Sub 2", () =>
            {
                action2Called = true;
                return Task.CompletedTask;
            });
        }

        _undoService.CanUndo.Should().BeTrue();
        _undoService.LastActionDescription.Should().Be("Batch Action");

        await _undoService.UndoAsync();

        action1Called.Should().BeTrue();
        action2Called.Should().BeTrue();
        _undoService.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task UndoAsync_specific_id_should_only_undo_that_action()
    {
        List<string> undone = [];

        Guid firstId = _undoService.RegisterUndo("First", () =>
        {
            undone.Add("first");
            return Task.CompletedTask;
        });
        _undoService.RegisterUndo("Second", () =>
        {
            undone.Add("second");
            return Task.CompletedTask;
        });
        _undoService.RegisterUndo("Third", () =>
        {
            undone.Add("third");
            return Task.CompletedTask;
        });

        await _undoService.UndoAsync(firstId);

        undone.Should().Equal("first");
        _undoService.CanUndo.Should().BeTrue();
        _undoService.LastActionDescription.Should().Be("Third");
    }

    [Fact]
    public async Task UndoAsync_older_then_newer_action()
    {
        List<string> undone = [];

        Guid firstId = _undoService.RegisterUndo("First", () =>
        {
            undone.Add("first");
            return Task.CompletedTask;
        });
        Guid secondId = _undoService.RegisterUndo("Second", () =>
        {
            undone.Add("second");
            return Task.CompletedTask;
        });

        await _undoService.UndoAsync(firstId);
        await _undoService.UndoAsync(secondId);

        undone.Should().Equal("first", "second");
    }

    [Fact]
    public async Task UndoAsync_concurrent_calls_should_not_block()
    {
        TaskCompletionSource tcs1 = new();
        TaskCompletionSource tcs2 = new();
        List<string> undone = [];

        _undoService.RegisterUndo("First", async () =>
        {
            await tcs1.Task;
            undone.Add("first");
        });
        _undoService.RegisterUndo("Second", async () =>
        {
            await tcs2.Task;
            undone.Add("second");
        });

        Task task2 = _undoService.UndoAsync();
        Task task1 = _undoService.UndoAsync();

        tcs2.SetResult();
        tcs1.SetResult();

        await Task.WhenAll(task1, task2);

        undone.Should().Equal("second", "first");
    }
}

public sealed class UndoableBoardDataServiceTests
{
    private readonly IBoardDataService _inner = Substitute.For<IBoardDataService>();
    private readonly IUndoService _undoService = Substitute.For<IUndoService>();
    private readonly UndoableBoardDataService _undoableService;

    public UndoableBoardDataServiceTests()
    {
        _undoableService = new UndoableBoardDataService(_inner, _undoService);
    }

    [Fact]
    public async Task CreateItemAsync_should_register_undo_with_delete()
    {
        BoardItem item = new(Guid.NewGuid(), "New Task");
        _inner.CreateItemAsync(BoardSection.Todo, "New Task", Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(item));
        _undoService.IsUndoing.Returns(false);

        await _undoableService.CreateItemAsync(BoardSection.Todo, "New Task");

        _undoService.Received(1).RegisterUndo(
            Arg.Is("Add \"New Task\""),
            Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task RenameItemAsync_should_register_undo_with_old_name()
    {
        BoardItem item = new(Guid.NewGuid(), "Old Task");
        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot(
                [item], [], []
            )));
        _inner.RenameItemAsync(BoardSection.Habit, item.Id, "New Task", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(new BoardItem(item.Id, "New Task")));
        _undoService.IsUndoing.Returns(false);

        await _undoableService.RenameItemAsync(BoardSection.Habit, item.Id, "New Task");

        _undoService.Received(1).RegisterUndo(
            Arg.Is("Rename \"Old Task\" to \"New Task\""),
            Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task CreateItemAsync_with_zalgo_title_should_register_undo_with_sanitized_title()
    {
        const string zalgoTitle = "k\u0300\u0301\u0302\u0303\u0304a\u0300\u0301\u0302\u0303\u0304r\u0300\u0301\u0302\u0303\u0304o\u0300\u0301\u0302\u0303\u0304l\u0300\u0301\u0302\u0303\u0304y\u0300\u0301\u0302\u0303\u0304";
        BoardItem item = new(Guid.NewGuid(), "karoly");
        _inner.CreateItemAsync(BoardSection.Todo, zalgoTitle, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(item));
        _undoService.IsUndoing.Returns(false);

        await _undoableService.CreateItemAsync(BoardSection.Todo, zalgoTitle);

        _undoService.Received(1).RegisterUndo(
            Arg.Is("Add \"karoly\""),
            Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task RenameItemAsync_with_zalgo_title_should_register_undo_with_sanitized_title()
    {
        BoardItem item = new(Guid.NewGuid(), "Old Task");
        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot(
                [item], [], []
            )));
        const string zalgoTitle = "k\u0300\u0301\u0302\u0303\u0304a\u0300\u0301\u0302\u0303\u0304r\u0300\u0301\u0302\u0303\u0304o\u0300\u0301\u0302\u0303\u0304l\u0300\u0301\u0302\u0303\u0304y\u0300\u0301\u0302\u0303\u0304";
        _inner.RenameItemAsync(BoardSection.Habit, item.Id, zalgoTitle, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(new BoardItem(item.Id, "karoly")));
        _undoService.IsUndoing.Returns(false);

        await _undoableService.RenameItemAsync(BoardSection.Habit, item.Id, zalgoTitle);

        _undoService.Received(1).RegisterUndo(
            Arg.Is("Rename \"Old Task\" to \"karoly\""),
            Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task DeleteItemAsync_should_register_undo_with_recreate()
    {
        BoardItem item = new(Guid.NewGuid(), "Deleted Todo");
        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot(
                [], [], [item]
            )));
        _inner.DeleteItemAsync(BoardSection.Todo, item.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _undoService.IsUndoing.Returns(false);

        await _undoableService.DeleteItemAsync(BoardSection.Todo, item.Id);

        _undoService.Received(1).RegisterUndo(
            Arg.Is("Delete \"Deleted Todo\""),
            Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task ToggleItemAsync_should_register_undo_with_toggle()
    {
        BoardItem item = new(Guid.NewGuid(), "Task", IsCompleted: true);
        _inner.ToggleItemAsync(BoardSection.Todo, item.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(item));
        _undoService.IsUndoing.Returns(false);

        await _undoableService.ToggleItemAsync(BoardSection.Todo, item.Id);

        _undoService.Received(1).RegisterUndo(
            Arg.Is("Complete \"Task\""),
            Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task UpdateHabitAsync_sort_only_should_not_register_undo()
    {
        BoardItem item = new(Guid.NewGuid(), "Habit", SortOrder: 1.0);
        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([item], [], [])));
        _inner.UpdateHabitAsync(
                item.Id,
                new UpdateHabitArgs(
                    item.Title, item.Notes, item.Tags,
                    item.TrackPlus, item.TrackMinus, item.ResetPeriod,
                    item.Counter, item.NegativeCounter, item.ChecklistJson, 2.0),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(item with { SortOrder = 2.0 }));
        _undoService.IsUndoing.Returns(false);

        await _undoableService.UpdateHabitAsync(
            item.Id,
            new UpdateHabitArgs(
                item.Title, item.Notes, item.Tags,
                item.TrackPlus, item.TrackMinus, item.ResetPeriod,
                item.Counter, item.NegativeCounter, item.ChecklistJson, SortOrder: 2.0));

        _undoService.DidNotReceive().RegisterUndo(Arg.Any<string>(), Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task UpdateHabitAsync_with_title_change_should_register_undo()
    {
        BoardItem item = new(Guid.NewGuid(), "Habit", SortOrder: 1.0);
        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([item], [], [])));
        _inner.UpdateHabitAsync(
                Arg.Any<Guid>(), Arg.Any<UpdateHabitArgs>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(item with { Title = "Renamed" }));
        _undoService.IsUndoing.Returns(false);

        await _undoableService.UpdateHabitAsync(
            item.Id,
            new UpdateHabitArgs(
                "Renamed", item.Notes, item.Tags,
                item.TrackPlus, item.TrackMinus, item.ResetPeriod,
                item.Counter, item.NegativeCounter, item.ChecklistJson, SortOrder: 2.0));

        _undoService.Received(1).RegisterUndo(
            Arg.Is("Edit \"Habit\""),
            Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task Delete_then_Edit_Undo_sequence_should_preserve_original_guid()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        BoardItem originalItem = new(itemId, "Original Name");
        BoardItem editedItem = new(itemId, "Edited Name");

        // Mock snapshot to return our items when requested
        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new BoardSnapshot([originalItem], [], [])), // For the rename call
                Task.FromResult(new BoardSnapshot([editedItem], [], []))  // For the delete call
            );

        // Track registered callbacks
        Func<Task>? renameUndoCallback = null;
        Func<Task>? deleteUndoCallback = null;

        _undoService.RegisterUndo(Arg.Any<string>(), Arg.Any<Func<Task>>())
            .Returns(x =>
            {
                string desc = (string)x[0];
                Func<Task> callback = (Func<Task>)x[1];
                if (desc.StartsWith("Rename", StringComparison.Ordinal))
                {
                    renameUndoCallback = callback;
                }
                else if (desc.StartsWith("Delete", StringComparison.Ordinal))
                {
                    deleteUndoCallback = callback;
                }
                return Guid.NewGuid();
            });

        // 1. Rename item from "Original Name" to "Edited Name"
        _inner.RenameItemAsync(BoardSection.Habit, itemId, "Edited Name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(editedItem));

        await _undoableService.RenameItemAsync(BoardSection.Habit, itemId, "Edited Name");

        // 2. Delete item
        _inner.DeleteItemAsync(BoardSection.Habit, itemId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _inner.CreateItemAsync(BoardSection.Habit, "Edited Name", itemId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(editedItem));

        await _undoableService.DeleteItemAsync(BoardSection.Habit, itemId);

        // Assert: We captured both callbacks
        renameUndoCallback.Should().NotBeNull();
        deleteUndoCallback.Should().NotBeNull();

        // 3. Simulate undoing delete first, which should recreate the item using its original Guid
        await deleteUndoCallback!();

        // Verify that CreateItemAsync was called with the original itemId
        await _inner.Received(1).CreateItemAsync(
            BoardSection.Habit,
            "Edited Name",
            itemId,
            Arg.Any<CancellationToken>());

        // 4. Simulate undoing the edit (rename) second, which should rename the restored item using the original Guid
        await renameUndoCallback!();

        // Verify that RenameItemAsync was called on the original itemId to restore the original name
        await _inner.Received(1).RenameItemAsync(
            BoardSection.Habit,
            itemId,
            "Original Name",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncrementHabitPlusAsync_undo_should_perform_relative_decrement()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        BoardItem initialItem = new(itemId, "Habit", Counter: 3);
        BoardItem itemAfterFirstIncrement = new(itemId, "Habit", Counter: 4);
        BoardItem itemAfterSecondIncrement = new(itemId, "Habit", Counter: 5);

        // Snapshot and Increment setup
        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new BoardSnapshot([initialItem], [], [])), // First find before increment
                Task.FromResult(new BoardSnapshot([itemAfterFirstIncrement], [], [])), // Find during first undo registration or second increment
                Task.FromResult(new BoardSnapshot([itemAfterSecondIncrement], [], [])), // Find during first undo execution
                Task.FromResult(new BoardSnapshot([itemAfterFirstIncrement], [], [])) // Find during second undo execution (if counter is now 4)
            );

        _inner.IncrementHabitPlusAsync(itemId, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<BoardItem?>(itemAfterFirstIncrement),
                Task.FromResult<BoardItem?>(itemAfterSecondIncrement)
            );

        Func<Task>? firstUndoCallback = null;
        Func<Task>? secondUndoCallback = null;

        _undoService.RegisterUndo(Arg.Any<string>(), Arg.Any<Func<Task>>())
            .Returns(x =>
            {
                Func<Task> callback = (Func<Task>)x[1];
                if (firstUndoCallback is null)
                {
                    firstUndoCallback = callback;
                }
                else
                {
                    secondUndoCallback ??= callback;
                }
                return Guid.NewGuid();
            });

        // 1. Perform first increment (Counter goes 3 -> 4)
        await _undoableService.IncrementHabitPlusAsync(itemId);

        // 2. Perform second increment (Counter goes 4 -> 5)
        await _undoableService.IncrementHabitPlusAsync(itemId);

        firstUndoCallback.Should().NotBeNull();
        secondUndoCallback.Should().NotBeNull();

        // 3. Simulate undoing the first increment first (out of order).
        // The current state is Counter = 5. Undoing first increment should decrement Counter to 4.
        await firstUndoCallback!();

        await _inner.Received(1).UpdateHabitAsync(
            itemId,
            Arg.Is<UpdateHabitArgs>(args =>
                args.Title == "Habit"
                && args.Notes == null
                && args.Tags == null
                && args.TrackPlus
                && args.TrackMinus
                && args.ResetPeriod == HabitResetPeriod.Daily
                && args.Counter == 4
                && args.NegativeCounter == 0
                && args.ChecklistJson == null),
            Arg.Any<CancellationToken>());

        // 4. Simulate undoing the second increment.
        // The current state in this scenario is Counter = 4. Undoing second increment should decrement Counter to 3.
        await secondUndoCallback!();

        await _inner.Received(1).UpdateHabitAsync(
            itemId,
            Arg.Is<UpdateHabitArgs>(args =>
                args.Title == "Habit"
                && args.Notes == null
                && args.Tags == null
                && args.TrackPlus
                && args.TrackMinus
                && args.ResetPeriod == HabitResetPeriod.Daily
                && args.Counter == 3
                && args.NegativeCounter == 0
                && args.ChecklistJson == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncrementHabitPlusAsync_concurrent_undos_should_decrement_relatively_to_correct_value()
    {
        // Arrange
        Guid itemId = Guid.NewGuid();
        int counter = 3;
        string title = "Habit";

        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                BoardItem item = new(itemId, title, Counter: counter);
                return Task.FromResult(new BoardSnapshot([item], [], []));
            });

        _inner.IncrementHabitPlusAsync(itemId, Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                counter++;
                BoardItem item = new(itemId, title, Counter: counter);
                return Task.FromResult<BoardItem?>(item);
            });

        _inner.UpdateHabitAsync(
            itemId, Arg.Any<UpdateHabitArgs>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                UpdateHabitArgs args = (UpdateHabitArgs)x[1];
                counter = args.Counter;
                BoardItem item = new(itemId, title, Counter: counter);
                return Task.FromResult<BoardItem?>(item);
            });

        ISnackbar snackbar = Substitute.For<ISnackbar>();
        INotificationSettingsService settingsService = Substitute.For<INotificationSettingsService>();
        INotificationSettingsRules notificationRules = Substitute.For<INotificationSettingsRules>();
        settingsService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new NotificationSettings()));
        notificationRules.UndoVisibleStateDurationMs(Arg.Any<NotificationToastDuration>())
            .Returns(12_000);

        UndoService realUndoService = new(snackbar, settingsService, notificationRules);
        UndoableBoardDataService undoableService = new(_inner, realUndoService);

        // Act
        // 1. Increment twice: 3 -> 4, then 4 -> 5
        await undoableService.IncrementHabitPlusAsync(itemId);
        await undoableService.IncrementHabitPlusAsync(itemId);

        counter.Should().Be(5);
        realUndoService.CanUndo.Should().BeTrue();

        // 2. Trigger both undos concurrently
        Task task1 = realUndoService.UndoAsync();
        Task task2 = realUndoService.UndoAsync();

        await Task.WhenAll(task1, task2);

        // Assert
        counter.Should().Be(3);
    }
}
