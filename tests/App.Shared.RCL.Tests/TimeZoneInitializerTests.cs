using App.Shared.RCL.Components;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class TimeZoneInitializerTests : IAsyncDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly IUserPreferencesService _preferencesService = Substitute.For<IUserPreferencesService>();
    private readonly IUserTimeZoneService _timeZoneService = Substitute.For<IUserTimeZoneService>();

    public TimeZoneInitializerTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddSingleton<IUserPreferencesService>(_preferencesService);
        _ctx.Services.AddSingleton<IUserTimeZoneService>(_timeZoneService);
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.DisposeAsync();
    }

    [Fact]
    public async Task DetectedTimeZone_IsSavedToPreferences_WhenDifferent()
    {
        _timeZoneService.TimeZoneId.Returns("Europe/Budapest");
        _preferencesService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(UserPreferences.CreateDefault());

        _ctx.Render<TimeZoneInitializer>();
        await WaitForInitializationAsync();

        await _preferencesService.Received(1).SaveAsync(
            Arg.Is<UserPreferences>(p => p!.TimeZoneOverrideId == "Europe/Budapest"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MatchingStoredTimeZone_DoesNotResave()
    {
        _timeZoneService.TimeZoneId.Returns("Europe/Budapest");
        var prefs = UserPreferences.CreateDefault();
        prefs.TimeZoneOverrideId = "Europe/Budapest";
        _preferencesService.GetAsync(Arg.Any<CancellationToken>()).Returns(prefs);

        _ctx.Render<TimeZoneInitializer>();
        await WaitForInitializationAsync();

        await _preferencesService.DidNotReceiveWithAnyArgs().SaveAsync(Arg.Any<UserPreferences>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UndetectedTimeZone_DoesNotSave()
    {
        _timeZoneService.TimeZoneId.Returns((string?)null);
        _preferencesService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(UserPreferences.CreateDefault());

        _ctx.Render<TimeZoneInitializer>();
        await WaitForInitializationAsync();

        await _preferencesService.DidNotReceiveWithAnyArgs().SaveAsync(Arg.Any<UserPreferences>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreferenceErrors_DoNotBreakInitialization()
    {
        _timeZoneService.TimeZoneId.Returns("Europe/Budapest");
        _preferencesService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UserPreferences>(new InvalidOperationException("offline")));

        _ctx.Render<TimeZoneInitializer>();
        await WaitForInitializationAsync();

        await _preferencesService.DidNotReceiveWithAnyArgs().SaveAsync(Arg.Any<UserPreferences>(), Arg.Any<CancellationToken>());
    }

    private async Task WaitForInitializationAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            if (_timeZoneService.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "InitializeAsync"))
            {
                await Task.Yield();
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("TimeZoneInitializer did not initialize.");
    }
}
