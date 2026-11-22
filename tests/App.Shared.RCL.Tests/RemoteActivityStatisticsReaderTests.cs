using System.Net;
using System.Text.Json;

using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Remote;

using FluentAssertions;

using NSubstitute;

namespace App.Shared.RCL.Tests;

public sealed class RemoteActivityStatisticsReaderTests
{
    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(handler(request));
        }
    }

    [Fact]
    public async Task GetOverviewAsync_ReturnsExpectedData_AndCachesSubsequentCall()
    {
        var overview = new ActivityOverviewDto(
            new ActivityDashboardDto(
                DailyGraphPeriods.Rolling370Days,
                [],
                [],
                53,
                10,
                30,
                5,
                new DateOnly(2026, 8, 1),
                new DateOnly(2025, 8, 1),
                new DateOnly(2026, 8, 17),
                370,
                new DateOnly(2025, 8, 1),
                new DateOnly(2026, 8, 17),
                ["tag1"]),
            new DailyContributionsViewDto(
                DailyGraphPeriods.Rolling370Days,
                [],
                [],
                new DateOnly(2025, 8, 1),
                new DateOnly(2026, 8, 17)),
            new HabitContributionsViewDto(
                DailyGraphPeriods.Rolling370Days,
                [],
                [],
                new DateOnly(2025, 8, 1),
                new DateOnly(2026, 8, 17)));

        var json = JsonSerializer.Serialize(overview, JsonDefaults.Api);

        var handler = new MockHttpMessageHandler(req =>
        {
            req.RequestUri!.PathAndQuery.Should().Contain("/api/activity/overview");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("api").Returns(httpClient);

        var reader = new RemoteActivityStatisticsReader(factory);

        // First call - should hit HTTP handler
        var result1 = await reader.GetOverviewAsync(DailyGraphPeriods.Rolling370Days);
        result1.Should().NotBeNull();
        result1.Dashboard.TotalEvents.Should().Be(10);
        handler.CallCount.Should().Be(1);

        // Second call with same parameters. Served from memory cache so call count stays 1.
        var result2 = await reader.GetOverviewAsync(DailyGraphPeriods.Rolling370Days);
        result2.Should().NotBeNull();
        result2.Dashboard.TotalEvents.Should().Be(10);
        handler.CallCount.Should().Be(1);

        // Invalidate cache - third call should hit HTTP handler again
        reader.InvalidateCache();
        var result3 = await reader.GetOverviewAsync(DailyGraphPeriods.Rolling370Days);
        result3.Should().NotBeNull();
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOverviewAsync_RefreshesPersistedOverviewWhenOnline()
    {
        var settings = new MemorySettingsStore();
        var firstHandler = new MockHttpMessageHandler(_ => JsonResponse(OverviewJson(totalEvents: 10)));
        var firstReader = new RemoteActivityStatisticsReader(Factory(firstHandler), settings);
        var first = await firstReader.GetOverviewAsync(DailyGraphPeriods.Rolling370Days);
        first.Dashboard.TotalEvents.Should().Be(10);
        firstHandler.CallCount.Should().Be(1);

        // A fresh app load hydrates the persisted overview for instant paint, but
        // must still fetch a fresh copy while online.
        var secondHandler = new MockHttpMessageHandler(_ => JsonResponse(OverviewJson(totalEvents: 99)));
        var secondReader = new RemoteActivityStatisticsReader(Factory(secondHandler), settings);
        var second = await secondReader.GetOverviewAsync(DailyGraphPeriods.Rolling370Days);

        second.Dashboard.TotalEvents.Should().Be(99);
        secondHandler.CallCount.Should().Be(1);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private static string OverviewJson(int totalEvents)
    {
        var overview = new ActivityOverviewDto(
            new ActivityDashboardDto(
                DailyGraphPeriods.Rolling370Days,
                [],
                [],
                53,
                totalEvents,
                30,
                5,
                new DateOnly(2026, 8, 1),
                new DateOnly(2025, 8, 1),
                new DateOnly(2026, 8, 17),
                370,
                new DateOnly(2025, 8, 1),
                new DateOnly(2026, 8, 17),
                []),
            new DailyContributionsViewDto(
                DailyGraphPeriods.Rolling370Days,
                [],
                [],
                new DateOnly(2025, 8, 1),
                new DateOnly(2026, 8, 17)),
            new HabitContributionsViewDto(
                DailyGraphPeriods.Rolling370Days,
                [],
                [],
                new DateOnly(2025, 8, 1),
                new DateOnly(2026, 8, 17)));
        return JsonSerializer.Serialize(overview, JsonDefaults.Api);
    }

    private static IHttpClientFactory Factory(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("api").Returns(httpClient);
        return factory;
    }

    private sealed class MemorySettingsStore : ILocalSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Read(string key, string? defaultValue = null) =>
            _values.TryGetValue(key, out var value) ? value : defaultValue;

        public void Write(string key, string value) => _values[key] = value;
    }
}
