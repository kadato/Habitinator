using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using FluentAssertions;
using MudBlazor;
using NSubstitute;
using Xunit;

namespace App.Shared.Tests;

public sealed class UndoServiceTests
{
    private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();
    private readonly INotificationSettingsService _settingsService = Substitute.For<INotificationSettingsService>();
    private readonly INotificationSettingsRules _notificationRules = Substitute.For<INotificationSettingsRules>();
    private readonly UndoService _undoService;

    public UndoServiceTests()
    {
        _settingsService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new NotificationSettings()));
        _notificationRules.VisibleStateDurationMs(Arg.Any<NotificationToastDuration>())
            .Returns(3000);

        _undoService = new UndoService(_snackbar, _settingsService, _notificationRules);
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
        var actionCalled = false;
        var stateChangedCalled = false;
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
        var actionCalled = false;
        var undoPerformedCalled = false;

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
        var action1Called = false;
        var action2Called = false;

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
    public void Clear_should_empty_stack()
    {
        _undoService.RegisterUndo("Test Action", () => Task.CompletedTask);
        _undoService.CanUndo.Should().BeTrue();

        _undoService.Clear();

        _undoService.CanUndo.Should().BeFalse();
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
        var item = new BoardItem(Guid.NewGuid(), "New Task");
        _inner.CreateItemAsync(BoardSection.Todo, "New Task", Arg.Any<CancellationToken>())
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
        var item = new BoardItem(Guid.NewGuid(), "Old Task");
        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot(
                new[] { item }, Array.Empty<BoardItem>(), Array.Empty<BoardItem>()
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
    public async Task DeleteItemAsync_should_register_undo_with_recreate()
    {
        var item = new BoardItem(Guid.NewGuid(), "Deleted Todo");
        _inner.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot(
                Array.Empty<BoardItem>(), Array.Empty<BoardItem>(), new[] { item }
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
        var item = new BoardItem(Guid.NewGuid(), "Task", IsCompleted: true);
        _inner.ToggleItemAsync(BoardSection.Todo, item.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(item));
        _undoService.IsUndoing.Returns(false);

        await _undoableService.ToggleItemAsync(BoardSection.Todo, item.Id);

        _undoService.Received(1).RegisterUndo(
            Arg.Is("Complete \"Task\""),
            Arg.Any<Func<Task>>());
    }
}
