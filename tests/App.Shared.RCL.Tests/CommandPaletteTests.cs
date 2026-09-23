using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.CommandPalette;

using Bunit;

using FluentAssertions;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class CommandPaletteTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IBoardDataService _boardData = Substitute.For<IBoardDataService>();
    private readonly GlobalTimerService _timer = new(new SystemClock());
    private readonly IUndoService _undo = Substitute.For<IUndoService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IUserPreferencesService _preferences = Substitute.For<IUserPreferencesService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly CommandPaletteService _paletteService;

    public CommandPaletteTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();

        _preferences.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(UserPreferences.CreateDefault()));

        _ctx.Services.AddSingleton(_boardData);
        _ctx.Services.AddSingleton(_timer);
        _ctx.Services.AddSingleton(_undo);
        _ctx.Services.AddSingleton(_dialogs);
        _ctx.Services.AddSingleton(_preferences);
        _ctx.Services.AddSingleton(_notifier);
        _ctx.Services.AddScoped<CommandPaletteService>();
        _ctx.Services.AddScoped<ICommandPaletteService>(sp => sp.GetRequiredService<CommandPaletteService>());

        _paletteService = _ctx.Services.GetRequiredService<CommandPaletteService>();
    }

    public async ValueTask DisposeAsync()
    {
        _timer.Dispose();
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void PaletteService_Toggle_ChangesIsOpen()
    {
        _paletteService.IsOpen.Should().BeFalse();

        _paletteService.Open();
        _paletteService.IsOpen.Should().BeTrue();

        _paletteService.Close();
        _paletteService.IsOpen.Should().BeFalse();

        _paletteService.Toggle();
        _paletteService.IsOpen.Should().BeTrue();

        _paletteService.Toggle();
        _paletteService.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task GetRootCommandsAsync_Returns_CuratedCommands()
    {
        var rootCommands = await _paletteService.GetRootCommandsAsync();

        rootCommands.Should().NotBeEmpty();
        rootCommands.Should().Contain(c => c.Id == "create-todo" && c.Category == "Suggested");
        rootCommands.Should().Contain(c => c.Id == "create-habit" && c.Category == "Suggested");
        rootCommands.Should().Contain(c => c.Id == "create-daily" && c.Category == "Suggested");
        rootCommands.Should().Contain(c => c.Id == "nav-board" && c.Category == "Navigation");
        rootCommands.Should().Contain(c => c.Id == "nav-stats" && c.Category == "Navigation");
        rootCommands.Should().Contain(c => c.Id == "nav-settings" && c.Category == "Navigation");
        rootCommands.Should().Contain(c => c.Id == "theme-dark" && c.Category == "Appearance");
    }

    [Fact]
    public async Task SearchBoardItemsAsync_Returns_MatchingItems_With_SubActions()
    {
        var habitId = Guid.NewGuid();
        var habit = new BoardItem(habitId, "Morning Meditation", Counter: 5);
        var snapshot = new BoardSnapshot([habit], [], []);

        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var results = await _paletteService.SearchBoardItemsAsync("meditation");

        results.Should().HaveCount(1);
        var item = results[0];
        item.Title.Should().Be("Morning Meditation");
        item.Category.Should().Be("Habits");
        item.ChildrenProvider.Should().NotBeNull();

        var subActions = await item.ChildrenProvider!();
        subActions.Should().Contain(a => a.Title.Contains("Increment"));
        subActions.Should().Contain(a => a.Title.Contains("Decrement"));
        subActions.Should().Contain(a => a.Title.Contains("Edit"));
        subActions.Should().Contain(a => a.Title.Contains("Archive"));
    }

    [Fact]
    public void CommandPalette_Renders_Only_When_Open()
    {
        var cut = _ctx.Render<CommandPalette>();
        cut.Markup.Should().BeEmpty();

        _paletteService.Open();
        cut.Render();

        cut.Find(".cmd-palette-dialog").Should().NotBeNull();
        cut.Find(".cmd-palette-input").Should().NotBeNull();

        _paletteService.Close();
        cut.Render();
        cut.Markup.Should().BeEmpty();
    }

    [Fact]
    public void CommandPalette_Escape_ClosesPalette_When_Query_Empty()
    {
        var cut = _ctx.Render<CommandPalette>();
        _paletteService.Open();
        cut.Render();

        var input = cut.Find(".cmd-palette-input");
        input.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _paletteService.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void CommandPalette_InitialState_SelectsFirstItem()
    {
        var cut = _ctx.Render<CommandPalette>();
        _paletteService.Open();
        cut.Render();

        var selected = cut.Find(".cmd-palette-item--selected");
        selected.Should().NotBeNull();
        selected.GetAttribute("aria-selected").Should().Be("true");

        var items = cut.FindAll(".cmd-palette-item");
        items.Count.Should().BeGreaterThan(0);
        items[0].ClassList.Should().Contain("cmd-palette-item--selected");
    }

    [Fact]
    public void CommandPalette_ArrowDown_UpdatesSelectedItem()
    {
        var cut = _ctx.Render<CommandPalette>();
        _paletteService.Open();
        cut.Render();

        var input = cut.Find(".cmd-palette-input");
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var items = cut.FindAll(".cmd-palette-item");
        items.Count.Should().BeGreaterThan(1);
        items[0].ClassList.Should().NotContain("cmd-palette-item--selected");
        items[1].ClassList.Should().Contain("cmd-palette-item--selected");
        items[1].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void CommandPalette_Renders_Distinct_Shortcut_Badges()
    {
        var cut = _ctx.Render<CommandPalette>();
        _paletteService.Open();
        cut.Render();

        var habitItem = cut.Find("#cmd-item-create-habit");
        var badges = habitItem.QuerySelectorAll(".cmd-palette-kbd");
        badges.Should().HaveCount(2);
        badges[0].TextContent.Trim().Should().Be("Ctrl");
        badges[1].TextContent.Trim().Should().Be("H");
    }

    [Fact]
    public async Task GetRootCommandsAsync_Returns_ExpandedCommandSet()
    {
        var rootCommands = await _paletteService.GetRootCommandsAsync();

        rootCommands.Should().Contain(c => c.Id == "action-delete-completed-todos" && c.IsDanger);
        rootCommands.Should().Contain(c => c.Id == "action-archive-completed-todos");
        rootCommands.Should().Contain(c => c.Id == "action-manage-items");
        rootCommands.Should().Contain(c => c.Id == "action-export-data");
        rootCommands.Should().Contain(c => c.Id == "action-yesterday-retro");
        rootCommands.Should().Contain(c => c.Id == "action-onboarding");
        rootCommands.Should().Contain(c => c.Id == "timer-toggle-pomodoro");
        rootCommands.Should().Contain(c => c.Id == "nav-settings-account");
        rootCommands.Should().Contain(c => c.Id == "nav-settings-notifications");
        rootCommands.Should().Contain(c => c.Id == "pref-toggle-shortcuts");
    }

    [Fact]
    public async Task SearchBoardItemsAsync_SubActions_Include_Delete_And_ExtendedActions()
    {
        var habit = new BoardItem(Guid.NewGuid(), "Morning Run", Counter: 3);
        var daily = new BoardItem(Guid.NewGuid(), "Brush Teeth", Counter: 10);
        var todo = new BoardItem(Guid.NewGuid(), "Pay Taxes", TodoDueDate: new DateOnly(2026, 10, 1));
        var snapshot = new BoardSnapshot([habit], [daily], [todo]);

        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        // Test Habit SubActions
        var habitResults = await _paletteService.SearchBoardItemsAsync("Run");
        habitResults.Should().HaveCount(1);
        var habitActions = await habitResults[0].ChildrenProvider!();
        habitActions.Should().Contain(a => a.Title == "Delete habit" && a.IsDanger);
        habitActions.Should().Contain(a => a.Title == "Reset counters");

        // Test Daily SubActions
        var dailyResults = await _paletteService.SearchBoardItemsAsync("Teeth");
        dailyResults.Should().HaveCount(1);
        var dailyActions = await dailyResults[0].ChildrenProvider!();
        dailyActions.Should().Contain(a => a.Title == "Delete daily" && a.IsDanger);
        dailyActions.Should().Contain(a => a.Title == "Mark completed for yesterday");
        dailyActions.Should().Contain(a => a.Title == "View completion heatmap");

        // Test To-do SubActions
        var todoResults = await _paletteService.SearchBoardItemsAsync("Taxes");
        todoResults.Should().HaveCount(1);
        var todoActions = await todoResults[0].ChildrenProvider!();
        todoActions.Should().Contain(a => a.Title == "Delete to-do" && a.IsDanger);
        todoActions.Should().Contain(a => a.Title == "Set due date to today");
        todoActions.Should().Contain(a => a.Title == "Set due date to tomorrow");
        todoActions.Should().Contain(a => a.Title == "Clear due date");
    }

    [Fact]
    public async Task SearchBoardItemsAsync_DirectDeleteIntent_Returns_DirectDeleteItems()
    {
        var habit = new BoardItem(Guid.NewGuid(), "Evening Walk");
        var snapshot = new BoardSnapshot([habit], [], []);

        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var results = await _paletteService.SearchBoardItemsAsync("delete Evening Walk");

        results.Should().HaveCount(1);
        results[0].Id.Should().StartWith("direct-delete-habit-");
        results[0].Title.Should().Be("Delete habit: Evening Walk");
        results[0].Category.Should().Be("Delete Items");
        results[0].IsDanger.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteItemAsync_CallsBoardData_And_ClosesPalette()
    {
        var habitId = Guid.NewGuid();
        _paletteService.Open();

        await _paletteService.DeleteItemAsync(BoardSection.Habit, habitId);

        await _boardData.Received(1).DeleteItemAsync(BoardSection.Habit, habitId);
        _paletteService.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteCompletedTodosAsync_DeletesOnlyCompletedTodos_InBatch()
    {
        var doneId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var doneTodo = new BoardItem(doneId, "Finished Task", IsCompleted: true);
        var openTodo = new BoardItem(pendingId, "Open Task", IsCompleted: false);
        var snapshot = new BoardSnapshot([], [], [doneTodo, openTodo]);

        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));
        _boardData.DeleteItemAsync(BoardSection.Todo, doneId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await _paletteService.DeleteCompletedTodosAsync();

        _undo.Received(1).BeginBatch(Arg.Is<string>(s => s.Contains("1 done to-dos")));
        await _boardData.Received(1).DeleteItemAsync(BoardSection.Todo, doneId);
        await _boardData.DidNotReceive().DeleteItemAsync(BoardSection.Todo, pendingId);
    }

    [Fact]
    public async Task ArchiveCompletedTodosAsync_ArchivesOnlyCompletedTodos_InBatch()
    {
        var doneId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();
        var doneTodo = new BoardItem(doneId, "Finished Task", IsCompleted: true);
        var openTodo = new BoardItem(pendingId, "Open Task", IsCompleted: false);
        var snapshot = new BoardSnapshot([], [], [doneTodo, openTodo]);

        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));
        _boardData.ArchiveItemAsync(BoardSection.Todo, doneId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BoardItem?>(doneTodo));

        await _paletteService.ArchiveCompletedTodosAsync();

        _undo.Received(1).BeginBatch(Arg.Is<string>(s => s.Contains("1 done to-dos")));
        await _boardData.Received(1).ArchiveItemAsync(BoardSection.Todo, doneId);
        await _boardData.DidNotReceive().ArchiveItemAsync(BoardSection.Todo, pendingId);
    }

    [Fact]
    public async Task ManageItems_ChildrenProvider_Returns_AllItemsWithSubactions()
    {
        var habit = new BoardItem(Guid.NewGuid(), "Habit 1");
        var daily = new BoardItem(Guid.NewGuid(), "Daily 1");
        var todo = new BoardItem(Guid.NewGuid(), "Todo 1");
        var snapshot = new BoardSnapshot([habit], [daily], [todo]);

        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var root = await _paletteService.GetRootCommandsAsync();
        var manageCmd = root.Find(c => c.Id == "action-manage-items");
        manageCmd.Should().NotBeNull();
        manageCmd.ChildrenProvider.Should().NotBeNull();

        var items = await manageCmd.ChildrenProvider!();
        items.Should().HaveCount(3);
        items.Should().Contain(i => i.Title == "Habit 1");
        items.Should().Contain(i => i.Title == "Daily 1");
        items.Should().Contain(i => i.Title == "Todo 1");
    }

    [Fact]
    public void CommandPalette_Renders_DangerClass_When_Item_IsDanger()
    {
        var cut = _ctx.Render<CommandPalette>();
        _paletteService.Open();
        cut.Render();

        var deleteCompletedItem = cut.Find("#cmd-item-action-delete-completed-todos");
        deleteCompletedItem.Should().NotBeNull();
        deleteCompletedItem.ClassList.Should().Contain("cmd-palette-item--danger");
    }

    [Fact]
    public async Task TogglePomodoroModeAsync_TogglesTimerAndFiresStateChanged()
    {
        var stateChangedFired = false;
        _timer.StateChanged += () => stateChangedFired = true;

        _timer.PomodoroModeEnabled.Should().BeFalse();
        await _paletteService.TogglePomodoroModeAsync();

        _timer.PomodoroModeEnabled.Should().BeTrue();
        stateChangedFired.Should().BeTrue();

        stateChangedFired = false;
        await _paletteService.TogglePomodoroModeAsync();

        _timer.PomodoroModeEnabled.Should().BeFalse();
        stateChangedFired.Should().BeTrue();
    }

    [Fact]
    public async Task TimerRootCommands_ReflectRunningAndStoppedState()
    {
        // Idle
        var root = await _paletteService.GetRootCommandsAsync();
        root.Should().Contain(c => c.Id == "timer-start");
        root.Should().NotContain(c => c.Id == "timer-stop");

        // Running
        _timer.Start();
        root = await _paletteService.GetRootCommandsAsync();
        root.Should().Contain(c => c.Id == "timer-stop");
        root.Should().Contain(c => c.Id == "timer-pause");

        // Stop via service
        await _paletteService.StopTimerSessionAsync();
        _timer.IsRunning.Should().BeFalse();
        _timer.Elapsed.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task TimerSetupAndStart_InStopwatchMode_SelectsModeTargetAndDuration_AndStartsImmediately()
    {
        var habit = new BoardItem(Guid.NewGuid(), "Drink Water");
        var snapshot = new BoardSnapshot([habit], [], []);
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var root = await _paletteService.GetRootCommandsAsync();
        var setupCmd = root.Find(c => c.Id == "timer-setup-and-start");
        Assert.NotNull(setupCmd);
        Assert.NotNull(setupCmd.ChildrenProvider);

        // Step 1: Mode selection
        var modes = await setupCmd.ChildrenProvider();
        modes.Should().Contain(m => m.Id == "mode-stopwatch");
        modes.Should().Contain(m => m.Id == "mode-pomodoro");

        var stopwatchMode = modes.Find(m => m.Id == "mode-stopwatch");
        Assert.NotNull(stopwatchMode);
        Assert.NotNull(stopwatchMode.ChildrenProvider);

        // Step 2: Target selection
        var targets = await stopwatchMode.ChildrenProvider();
        targets.Should().Contain(t => t.Id == "timer-target-empty");
        targets.Should().Contain(t => t.Id == "timer-target-custom");
        targets.Should().Contain(t => t.Title == "Drink Water");

        // Step 3: Duration selection for untargeted session
        var emptyTarget = targets.Find(t => t.Id == "timer-target-empty");
        Assert.NotNull(emptyTarget);
        Assert.NotNull(emptyTarget.ChildrenProvider);
        var durations = await emptyTarget.ChildrenProvider();
        durations.Should().Contain(d => d.Title == "25 minutes");
        durations.Should().Contain(d => d.Title.Contains("Custom duration"));
        durations[0].Title.Should().StartWith("Custom duration");

        // Act - Select 25 minutes duration
        var duration25 = durations.Find(d => d.Title == "25 minutes");
        Assert.NotNull(duration25);
        Assert.NotNull(duration25.Action);
        await duration25.Action();

        // Verify timer actually started
        _timer.IsRunning.Should().BeTrue();
        _timer.PomodoroModeEnabled.Should().BeFalse();
        _timer.FocusAlertAfter.Should().Be(TimeSpan.FromMinutes(25));
        _timer.TargetId.Should().BeNull();
    }

    [Fact]
    public async Task TimerSetupAndStart_InPomodoroMode_SelectsModeTargetAndInterval_AndStartsImmediately()
    {
        var habit = new BoardItem(Guid.NewGuid(), "Reading");
        var snapshot = new BoardSnapshot([habit], [], []);
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var root = await _paletteService.GetRootCommandsAsync();
        var setupCmd = root.Find(c => c.Id == "timer-setup-and-start");
        Assert.NotNull(setupCmd);
        Assert.NotNull(setupCmd.ChildrenProvider);

        // Step 1: Mode Selection
        var modes = await setupCmd.ChildrenProvider();
        var pomodoroMode = modes.Find(m => m.Id == "mode-pomodoro");
        Assert.NotNull(pomodoroMode);
        Assert.NotNull(pomodoroMode.ChildrenProvider);

        // Step 2: Target Selection
        var targets = await pomodoroMode.ChildrenProvider();
        targets.Should().Contain(t => t.Id == "timer-target-empty");
        targets.Should().Contain(t => t.Title == "Reading");

        var habitTarget = targets.Find(t => t.Title == "Reading");
        Assert.NotNull(habitTarget);
        Assert.NotNull(habitTarget.ChildrenProvider);

        // Step 3: Pomodoro Interval Selection
        var intervals = await habitTarget.ChildrenProvider();
        intervals.Should().Contain(m => m.Id.EndsWith("-pomodoro", StringComparison.Ordinal));
        intervals.Should().Contain(m => m.Id.Contains("-pomo-25m", StringComparison.Ordinal));
        intervals.Should().Contain(m => m.Id.EndsWith("-custom", StringComparison.Ordinal));
        intervals[0].Title.Should().StartWith("Custom work duration");

        // Act - Start 25m Pomodoro interval on target habit
        var pomoOption = intervals.Find(m => m.Id.Contains("-pomo-25m", StringComparison.Ordinal));
        Assert.NotNull(pomoOption);
        Assert.NotNull(pomoOption.Action);
        await pomoOption.Action();

        // Verify Pomodoro actually started
        _timer.IsRunning.Should().BeTrue();
        _timer.PomodoroModeEnabled.Should().BeTrue();
        _timer.CurrentPomodoroState.Should().Be(PomodoroState.Work);
        _timer.WorkDuration.Should().Be(TimeSpan.FromMinutes(25));
        _timer.TargetType.Should().Be("Habit");
        _timer.TargetId.Should().Be("Reading");
    }

    [Fact]
    public async Task TimerSetupAndStart_PomodoroWithEmptyTarget_StartsGeneralPomodoro()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));

        var root = await _paletteService.GetRootCommandsAsync();
        var setupCmd = root.Find(c => c.Id == "timer-setup-and-start");
        Assert.NotNull(setupCmd);
        var modes = await setupCmd.ChildrenProvider!();
        var pomodoroMode = modes.Find(m => m.Id == "mode-pomodoro");
        Assert.NotNull(pomodoroMode);

        var targets = await pomodoroMode.ChildrenProvider!();
        var emptyTarget = targets.Find(t => t.Id == "timer-target-empty");
        Assert.NotNull(emptyTarget);

        var intervals = await emptyTarget.ChildrenProvider!();
        var defaultPomo = intervals.Find(i => i.Id.EndsWith("-pomodoro", StringComparison.Ordinal));
        Assert.NotNull(defaultPomo);
        await defaultPomo.Action!();

        _timer.IsRunning.Should().BeTrue();
        _timer.PomodoroModeEnabled.Should().BeTrue();
        _timer.TargetId.Should().BeNull();
        _timer.CurrentPomodoroState.Should().Be(PomodoroState.Work);
    }

    [Fact]
    public async Task StartPomodoroSessionAsync_StartsSessionImmediately()
    {
        _timer.PomodoroModeEnabled = false;
        _timer.IsRunning.Should().BeFalse();

        await _paletteService.StartPomodoroSessionAsync();

        _timer.PomodoroModeEnabled.Should().BeTrue();
        _timer.IsRunning.Should().BeTrue();
        _timer.CurrentPomodoroState.Should().Be(PomodoroState.Work);
    }

    [Fact]
    public async Task TimerSetTarget_SetsActiveTargetInBothModes()
    {
        var habit = new BoardItem(Guid.NewGuid(), "Code Review");
        var snapshot = new BoardSnapshot([habit], [], []);
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(snapshot));

        var root = await _paletteService.GetRootCommandsAsync();
        var setTargetCmd = root.Find(c => c.Id == "timer-set-target");
        Assert.NotNull(setTargetCmd);
        Assert.NotNull(setTargetCmd.ChildrenProvider);

        var targetOptions = await setTargetCmd.ChildrenProvider();
        var habitOption = targetOptions.Find(t => t.Title == "Code Review");
        Assert.NotNull(habitOption);
        Assert.NotNull(habitOption.Action);

        await habitOption.Action();

        _timer.TargetType.Should().Be("Habit");
        _timer.TargetId.Should().Be("Code Review");
    }

    [Fact]
    public async Task SearchBoardItemsAsync_MatchesPomodoroAndDurationIntents()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));

        var pomoResults = await _paletteService.SearchBoardItemsAsync("pomodoro");
        pomoResults.Should().Contain(c => c.Id == "quick-start-pomodoro-search");

        var comodoroResults = await _paletteService.SearchBoardItemsAsync("comodoro");
        comodoroResults.Should().Contain(c => c.Id == "quick-start-pomodoro-search");

        var durationResults = await _paletteService.SearchBoardItemsAsync("timer 25");
        durationResults.Should().Contain(c => c.Id == "quick-start-duration-25");

        var startDurationCmd = durationResults.Find(c => c.Id == "quick-start-duration-25");
        Assert.NotNull(startDurationCmd);
        Assert.NotNull(startDurationCmd.Action);
        await startDurationCmd.Action();

        _timer.IsRunning.Should().BeTrue();
        _timer.FocusAlertAfter.Should().Be(TimeSpan.FromMinutes(25));
    }

    [Fact]
    public async Task TimerReset_ResetsSessionInBothModes()
    {
        // Pomodoro mode
        _timer.PomodoroModeEnabled = true;
        _timer.Start();
        _timer.CurrentPomodoroState.Should().Be(PomodoroState.Work);

        var root = await _paletteService.GetRootCommandsAsync();
        var resetCmd = root.Find(c => c.Id == "timer-reset");
        Assert.NotNull(resetCmd);
        Assert.NotNull(resetCmd.Action);

        await resetCmd.Action();

        _timer.IsRunning.Should().BeFalse();
        _timer.CurrentPomodoroState.Should().Be(PomodoroState.Idle);
    }

    [Fact]
    public async Task TimerSetDuration_InStopwatchMode_UpdatesFocusAlertAfter()
    {
        _timer.PomodoroModeEnabled = false;

        var root = await _paletteService.GetRootCommandsAsync();
        var setDurationCmd = root.Find(c => c.Id == "timer-set-duration");
        Assert.NotNull(setDurationCmd);
        Assert.NotNull(setDurationCmd.ChildrenProvider);

        var presets = await setDurationCmd.ChildrenProvider();
        var preset30 = presets.Find(p => p.Title == "30 minutes");
        Assert.NotNull(preset30);
        Assert.NotNull(preset30.Action);
        await preset30.Action();

        _timer.FocusAlertAfter.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task TimerPauseAndResume_WorksAcrossModesAndIntents()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));

        // Start timer and let small time pass
        _timer.Start();
        _timer.IsRunning.Should().BeTrue();
        await Task.Delay(15);

        // 1. Root commands while running
        var runningCommands = await _paletteService.GetRootCommandsAsync();
        runningCommands.Should().Contain(c => c.Id == "timer-pause");
        runningCommands.Should().Contain(c => c.Id == "timer-stop");
        runningCommands.Should().Contain(c => c.Id == "timer-reset");
        runningCommands.Should().NotContain(c => c.Id == "timer-resume");

        // 2. Search intent for pause
        var pauseResults = await _paletteService.SearchBoardItemsAsync("pause");
        pauseResults.Should().Contain(c => c.Id.StartsWith("timer-pause", StringComparison.Ordinal));

        // Act: Pause via action
        var pauseCmd = runningCommands.Find(c => c.Id == "timer-pause");
        Assert.NotNull(pauseCmd?.Action);
        await pauseCmd.Action();

        _timer.IsRunning.Should().BeFalse();
        _timer.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);

        // 3. Root commands while paused
        var pausedCommands = await _paletteService.GetRootCommandsAsync();
        pausedCommands.Should().Contain(c => c.Id == "timer-resume");
        pausedCommands.Should().Contain(c => c.Id == "timer-stop");
        pausedCommands.Should().Contain(c => c.Id == "timer-reset");
        pausedCommands.Should().NotContain(c => c.Id == "timer-pause");

        // 4. Search intent for resume
        var resumeResults = await _paletteService.SearchBoardItemsAsync("resume");
        resumeResults.Should().Contain(c => c.Id.StartsWith("timer-resume", StringComparison.Ordinal));

        // Act: Resume via action
        var resumeCmd = pausedCommands.Find(c => c.Id == "timer-resume");
        Assert.NotNull(resumeCmd?.Action);
        await resumeCmd.Action();

        _timer.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task TimerStop_DirectIntents_SupportLockAndLogKeywords()
    {
        _boardData.GetSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoardSnapshot([], [], [])));

        _timer.Start();
        _timer.IsRunning.Should().BeTrue();

        // "lock" intent
        var lockResults = await _paletteService.SearchBoardItemsAsync("lock");
        lockResults.Should().Contain(c => c.Id.StartsWith("timer-stop", StringComparison.Ordinal));

        // "log" intent
        var logResults = await _paletteService.SearchBoardItemsAsync("log");
        logResults.Should().Contain(c => c.Id.StartsWith("timer-stop", StringComparison.Ordinal));

        // "reset" intent
        var resetResults = await _paletteService.SearchBoardItemsAsync("reset");
        resetResults.Should().Contain(c => c.Id.StartsWith("timer-reset", StringComparison.Ordinal));

        // Execute stop
        var stopCmd = lockResults.Find(c => c.Id.StartsWith("timer-stop", StringComparison.Ordinal));
        Assert.NotNull(stopCmd?.Action);
        await stopCmd.Action();

        _timer.IsRunning.Should().BeFalse();
        _timer.Elapsed.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task TimerReset_InStopwatchMode_ResetsElapsedAndStopsTimer()
    {
        _timer.PomodoroModeEnabled = false;
        _timer.Start();
        _timer.IsRunning.Should().BeTrue();

        var root = await _paletteService.GetRootCommandsAsync();
        var resetCmd = root.Find(c => c.Id == "timer-reset");
        Assert.NotNull(resetCmd?.Action);

        await resetCmd.Action();

        _timer.IsRunning.Should().BeFalse();
        _timer.Elapsed.Should().Be(TimeSpan.Zero);
    }
}
