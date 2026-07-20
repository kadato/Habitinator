#pragma warning disable MUD0012

using System.Globalization;

using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using MudBlazor;
using MudBlazor.Services;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class UserPreferencesSectionTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IUserPreferencesService _preferencesService = Substitute.For<IUserPreferencesService>();
    private readonly IUserNotifier _notifier = Substitute.For<IUserNotifier>();
    private readonly IUserTimeZoneService _timeZoneService = Substitute.For<IUserTimeZoneService>();

    public UserPreferencesSectionTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.Services.AddSingleton<IUserPreferencesService>(_preferencesService);
        _ctx.Services.AddSingleton<IUserNotifier>(_notifier);
        _ctx.Services.AddSingleton<IUserTimeZoneService>(_timeZoneService);

        // Render PopoverProvider to satisfy MudBlazor dropdowns/pickers
        _ctx.Render<MudPopoverProvider>();

        // Set default mocks for timezone service
        _timeZoneService.TimeZoneId.Returns("UTC");
        _timeZoneService.IsDetected.Returns(true);
        _timeZoneService.GetTimeZoneAbbreviation().Returns("UTC");
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public void Renders_LoadingSkeletons_Initially()
    {
        // Arrange
        var tcs = new TaskCompletionSource<UserPreferences>();
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(tcs.Task);

        // Act
        var cut = _ctx.Render<UserPreferencesSection>();

        // Assert
        cut.FindComponents<MudSkeleton>().Should().NotBeEmpty();
    }

    [Fact]
    public void Renders_Preferences_OnceLoaded()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DisplayName = "Jane Doe",
            DateFormat = "yyyy-MM-dd",
            DayStartLocalTime = TimeSpan.FromHours(5),
            TimeZoneOverrideId = "America/New_York",
            Theme = AppTheme.Dark
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        // Act
        var cut = _ctx.Render<UserPreferencesSection>();

        // Assert
        var textFields = cut.FindComponents<MudTextField<string>>();
        textFields.Should().HaveCountGreaterThanOrEqualTo(2);
        textFields[0].Instance.Value.Should().Be("Jane Doe");
        textFields[1].Instance.Value.Should().Be("yyyy-MM-dd");

        var timePicker = cut.FindComponent<MudTimePicker>();
        timePicker.Instance.Time.Should().Be(TimeSpan.FromHours(5));

        var tzSelect = cut.FindComponent<MudSelect<string>>();
        tzSelect.Instance.Value.Should().Be("America/New_York");

        // Theme is now a 2-state toggle (MudSwitch + MudButton), not a MudSelect
        var themeSwitch = cut.FindComponents<MudSwitch<bool>>()[0];
        // Preference is Dark (pinned), so "Sync with system" is OFF
        themeSwitch.Instance.Value.Should().BeFalse();
        // The toggle button label should reflect the opposite of pinned (Light mode)
        cut.Markup.Should().Contain("Light mode");
    }

    [Fact]
    public async Task AutoSaves_DisplayName_OnBlur()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DisplayName = "Jane Doe",
            DateFormat = "yyyy-MM-dd",
            DayStartLocalTime = TimeSpan.FromHours(5)
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var displayNameField = cut.FindComponents<MudTextField<string>>()[0];

        // Act - change display name and blur
        await cut.InvokeAsync(() => displayNameField.Instance.ValueChanged.InvokeAsync("Jane Smith"));
        await cut.InvokeAsync(() => displayNameField.Instance.OnBlur.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));

        // Assert
        await _preferencesService.Received().SaveAsync(Arg.Is<UserPreferences>(p => p.DisplayName == "Jane Smith"));
    }

    [Fact]
    public async Task AutoSaves_DateFormat_OnBlur()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DisplayName = "Jane Doe",
            DateFormat = "yyyy-MM-dd"
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var dateFormatField = cut.FindComponents<MudTextField<string>>()[1];

        // Act - change date format and blur
        await cut.InvokeAsync(() => dateFormatField.Instance.ValueChanged.InvokeAsync("dd/MM/yyyy"));
        await cut.InvokeAsync(() => dateFormatField.Instance.OnBlur.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));

        // Assert
        await _preferencesService.Received().SaveAsync(Arg.Is<UserPreferences>(p => p.DateFormat == "dd/MM/yyyy"));
    }

    [Fact]
    public async Task AutoSaves_DayStart_Immediately()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DayStartLocalTime = TimeSpan.FromHours(5)
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var timePicker = cut.FindComponent<MudTimePicker>();

        // Act - change time
        await cut.InvokeAsync(() => timePicker.Instance.TimeChanged.InvokeAsync(TimeSpan.FromHours(7)));

        // Assert
        await _preferencesService.Received().SaveAsync(Arg.Is<UserPreferences>(p => p.DayStartLocalTime == TimeSpan.FromHours(7)));
    }

    [Fact]
    public async Task AutoSaves_TimeZone_Immediately()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            TimeZoneOverrideId = "UTC"
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var tzSelect = cut.FindComponent<MudSelect<string>>();

        // Act - change timezone
        await cut.InvokeAsync(() => tzSelect.Instance.ValueChanged.InvokeAsync("Europe/London"));

        // Assert
        await _preferencesService.Received().SaveAsync(Arg.Is<UserPreferences>(p => p.TimeZoneOverrideId == "Europe/London"));
        _timeZoneService.Received().SetOverride("Europe/London");
    }

    [Fact]
    public async Task AutoSaves_Theme_WhenTogglingOffSystemSync()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            Theme = AppTheme.System
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();

        // The first MudSwitch<bool> is the "Sync with system" theme switch
        var themeSwitch = cut.FindComponents<MudSwitch<bool>>()[0];

        // Initial: Sync with system is ON
        themeSwitch.Instance.Value.Should().BeTrue();

        // Act - turn off system sync (pins to dark)
        await cut.InvokeAsync(() => themeSwitch.Instance.ValueChanged.InvokeAsync(false));

        // Assert - default pinned theme is Dark when unpinning
        await _preferencesService.Received().SaveAsync(Arg.Is<UserPreferences>(p => p.Theme == AppTheme.Dark));
    }

    [Fact]
    public async Task AutoSaves_Theme_WhenTogglingPinnedTheme()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            Theme = AppTheme.Dark
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();

        // Theme is dark, so "Sync with system" is OFF, and button shows "Light mode"
        var themeSwitch = cut.FindComponents<MudSwitch<bool>>()[0];
        themeSwitch.Instance.Value.Should().BeFalse();

        // Find the theme toggle button (the MudButton next to the switch)
        var themeBtn = cut.FindAll(".text-transform-none").First(b => b.TextContent.Contains("mode"));

        // Act - click the toggle button to switch from Dark to Light
        await cut.InvokeAsync(() => themeBtn.Click());

        // Assert
        await _preferencesService.Received().SaveAsync(Arg.Is<UserPreferences>(p => p.Theme == AppTheme.Light));
    }

    [Fact]
    public async Task Displays_ValidationError_IfDisplayNameTooLong()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DisplayName = "Jane Doe"
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var displayNameField = cut.FindComponents<MudTextField<string>>()[0];

        // Act - set long name and blur
        var longName = new string('A', 41);
        await cut.InvokeAsync(() => displayNameField.Instance.ValueChanged.InvokeAsync(longName));
        await cut.InvokeAsync(() => displayNameField.Instance.OnBlur.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));

        // Assert
        displayNameField.Instance.Error.Should().BeTrue();
        displayNameField.Instance.ErrorText.Should().Be("Display name must be 40 characters or fewer.");
        await _preferencesService.DidNotReceiveWithAnyArgs().SaveAsync(null!);
    }

    [Fact]
    public async Task Displays_ValidationError_IfDateFormatInvalid()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DateFormat = "yyyy-MM-dd"
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var dateFormatField = cut.FindComponents<MudTextField<string>>()[1];

        // Act - set invalid format and blur
        await cut.InvokeAsync(() => dateFormatField.Instance.ValueChanged.InvokeAsync("%"));
        await cut.InvokeAsync(() => dateFormatField.Instance.OnBlur.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));

        // Assert
        dateFormatField.Instance.Error.Should().BeTrue();
        dateFormatField.Instance.ErrorText.Should().Be("Date format is not valid.");
        await _preferencesService.DidNotReceiveWithAnyArgs().SaveAsync(null!);
    }

    [Fact]
    public void Renders_InvalidFormatError_Immediately_IfLoadedFormatIsInvalid()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DateFormat = "%" // Invalid format
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        // Act
        var cut = _ctx.Render<UserPreferencesSection>();
        var dateFormatField = cut.FindComponents<MudTextField<string>>()[1];

        // Assert
        dateFormatField.Instance.Error.Should().BeTrue();
        dateFormatField.Instance.ErrorText.Should().Be("Date format is not valid.");

        // The preview should contain "Invalid format"
        cut.Markup.Should().Contain("Preview: Invalid format");
    }

    [Fact]
    public async Task Updates_Preview_Dynamically_OnUserInputChange()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DateFormat = "yyyy-MM-dd"
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var dateFormatField = cut.FindComponents<MudTextField<string>>()[1];

        // Act - change to a new valid format dynamically (before blur)
        var newFormat = "dd/MM/yyyy";
        var expectedPreview = DateTime.Now.ToString(newFormat, CultureInfo.InvariantCulture);
        await cut.InvokeAsync(() => dateFormatField.Instance.ValueChanged.InvokeAsync(newFormat));

        // Assert preview updates immediately
        cut.Markup.Should().Contain($"Preview: {expectedPreview}");
        dateFormatField.Instance.Error.Should().BeFalse();

        // Act - change to an invalid format dynamically
        await cut.InvokeAsync(() => dateFormatField.Instance.ValueChanged.InvokeAsync("%"));

        // Assert preview shows invalid and error is displayed immediately
        cut.Markup.Should().Contain("Preview: Invalid format");
        dateFormatField.Instance.Error.Should().BeTrue();
        dateFormatField.Instance.ErrorText.Should().Be("Date format is not valid.");
    }

    [Theory]
    [InlineData("yyyy-MM-dd")]
    [InlineData("dd/MM/yyyy")]
    [InlineData("MM/dd/yyyy")]
    [InlineData("yyyy.MM.dd")]
    [InlineData("dd-MMM-yyyy")]
    [InlineData("yyyy/MM/dd HH:mm")]
    [InlineData("yyyy")]
    [InlineData("MM-dd")]
    public async Task Allows_Saving_Typical_ValidFormats_OnBlur(string validFormat)
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DateFormat = "yyyy-MM-dd"
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var dateFormatField = cut.FindComponents<MudTextField<string>>()[1];

        // Act - change to the valid format and blur
        await cut.InvokeAsync(() => dateFormatField.Instance.ValueChanged.InvokeAsync(validFormat));
        await cut.InvokeAsync(() => dateFormatField.Instance.OnBlur.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));

        // Assert
        dateFormatField.Instance.Error.Should().BeFalse();
        await _preferencesService.Received().SaveAsync(Arg.Is<UserPreferences>(p => p.DateFormat == validFormat));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("z")] // Invalid single-character format specifier
    [InlineData("yyyy-MM-dd %")] // Trailing percent sign without a format specifier
    [InlineData("yyyy-MM-dd \\")] // Trailing escape character without a character to escape
    public async Task Blocks_Saving_And_DisplaysError_For_Typical_InvalidFormats_OnBlur(string invalidFormat)
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DateFormat = "yyyy-MM-dd"
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var dateFormatField = cut.FindComponents<MudTextField<string>>()[1];

        // Act - change to the invalid format and blur
        await cut.InvokeAsync(() => dateFormatField.Instance.ValueChanged.InvokeAsync(invalidFormat));
        await cut.InvokeAsync(() => dateFormatField.Instance.OnBlur.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));

        // Assert
        dateFormatField.Instance.Error.Should().BeTrue();
        dateFormatField.Instance.ErrorText.Should().Be("Date format is not valid.");
        await _preferencesService.DidNotReceiveWithAnyArgs().SaveAsync(null!);
    }

    [Fact]
    public async Task Clears_Error_And_Saves_When_Corrected_From_Invalid_To_Valid()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            DateFormat = "yyyy-MM-dd"
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        var dateFormatField = cut.FindComponents<MudTextField<string>>()[1];

        // 1. Set invalid format and blur -> expect error and no save
        await cut.InvokeAsync(() => dateFormatField.Instance.ValueChanged.InvokeAsync("%"));
        await cut.InvokeAsync(() => dateFormatField.Instance.OnBlur.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));

        dateFormatField.Instance.Error.Should().BeTrue();
        await _preferencesService.DidNotReceiveWithAnyArgs().SaveAsync(null!);

        // 2. Set valid format and blur -> expect error cleared and save called
        await cut.InvokeAsync(() => dateFormatField.Instance.ValueChanged.InvokeAsync("dd/MM/yyyy"));
        await cut.InvokeAsync(() => dateFormatField.Instance.OnBlur.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));

        dateFormatField.Instance.Error.Should().BeFalse();
        dateFormatField.Instance.ErrorText.Should().BeNull();
        await _preferencesService.Received().SaveAsync(Arg.Is<UserPreferences>(p => p.DateFormat == "dd/MM/yyyy"));
    }

    [Fact]
    public void Displays_ErrorAlert_OnLoadException()
    {
        // Arrange
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(x => Task.FromException<UserPreferences>(new InvalidOperationException("Network failure")));

        // Act
        var cut = _ctx.Render<UserPreferencesSection>();

        // Assert
        var alert = cut.FindComponent<MudAlert>();
        alert.Instance.Severity.Should().Be(Severity.Error);
        cut.Markup.Should().Contain("Network failure");
    }

    [Fact]
    public async Task Changes_EnableKeyboardShortcuts_And_Saves()
    {
        // Arrange
        var prefs = new UserPreferences
        {
            EnableKeyboardShortcuts = true
        };
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(prefs));

        var cut = _ctx.Render<UserPreferencesSection>();
        // There are now 2 MudSwitch<bool>: theme sync + keyboard shortcuts
        // The keyboard shortcuts switch is labeled "Enable keyboard shortcuts"
        var switches = cut.FindComponents<MudSwitch<bool>>();
        var kbSwitch = switches.Last(s => s.Instance.Label == "Enable keyboard shortcuts");
        kbSwitch.Instance.Value.Should().BeTrue();

        // Act
        await cut.InvokeAsync(() => kbSwitch.Instance.ValueChanged.InvokeAsync(false));

        // Assert
        await _preferencesService.Received().SaveAsync(Arg.Is<UserPreferences>(p => !p.EnableKeyboardShortcuts));
    }
}
