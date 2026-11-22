using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Board.Local;
using App.Shared.RCL.Services.CommandPalette;
using App.Shared.RCL.Services.Remote;

using FluentAssertions;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

using MudBlazor;

using NSubstitute;

namespace App.Shared.Tests;

public sealed class ClientCompositionTests
{
    [Fact]
    public void SharedRegistration_ResolvesForBothHostLifetimes()
    {
        ResolveCore(ServiceLifetime.Scoped);
        ResolveCore(ServiceLifetime.Singleton);
    }

    private static void ResolveCore(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILocalSettingsStore, MemorySettingsStore>();
        services.AddSingleton<IClientSessionProvider, MemorySession>();
        services.AddSingleton<ICurrentUserKeyProvider, MemoryUserKeys>();
        services.AddSingleton<IBoardLocalStore, InMemoryBoardLocalStore>();
        services.AddSingleton<IHttpClientFactory, EmptyHttpFactory>();
        services.AddSingleton(Substitute.For<IJSRuntime>());
        services.AddSingleton(Substitute.For<NavigationManager>());
        services.AddSingleton(Substitute.For<IDialogService>());
        services.AddSingleton(Substitute.For<ISnackbar>());
        services.AddHabitinatorClientCore(lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUserDayService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IBoardDataService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ICommandPaletteService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITimerSessionLogService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IUserNotifier>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IDailyRetroPromptStore>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IActivityStatisticsReader>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IUserDataImportService>().Should().NotBeNull();
    }

    private sealed class MemorySettingsStore : ILocalSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Read(string key, string? defaultValue = null) =>
            _values.TryGetValue(key, out var value) ? value : defaultValue;

        public void Write(string key, string value) => _values[key] = value;
    }

    private sealed class MemorySession : IClientSessionProvider
    {
        public bool IsLoggedIn => true;

        public string? Email => "test@example.test";

#pragma warning disable CS0067 // Interface event is not raised by this test double.
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }

    private sealed class MemoryUserKeys : ICurrentUserKeyProvider
    {
        public Task<string?> GetUserKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("TEST@EXAMPLE.TEST");

        public Task<bool> HasAuthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class EmptyHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new EmptyHandler())
        {
            BaseAddress = new Uri("https://test.local/")
        };

        private sealed class EmptyHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }
}
