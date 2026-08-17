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

        // Second call with same parameters - should be served from memory cache (CallCount stays 1)
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
}
