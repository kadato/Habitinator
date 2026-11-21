using System.Net;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Shared.RCL.Services.Remote;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class RemoteUserActivityLogServiceTests
{
    private const string PendingKey = "habitinator_activity_pending_v1";

    [Fact]
    public async Task Flush_keeps_events_enqueued_while_the_batch_is_in_flight()
    {
        var store = new MemoryLocalSettingsStore();
        var handler = new BlockingHandler();
        using var service = new RemoteUserActivityLogService(
            new StubHttpClientFactory(handler),
            statsReader: null,
            eventStore: null,
            localStore: store,
            clock: null);

        var queued = new ActivityLogRequest(ActivityEventType.HabitPlus, null, null, "Queued", Guid.NewGuid());
        store.Write(PendingKey, JsonSerializer.Serialize(new[] { queued }, JsonDefaults.Api));

        var flushTask = service.TryFlushPendingAsync();
        await handler.FirstRequestStarted.Task;

        // Enqueue a newer event while the queued batch is on the wire. Its own post fails, so it
        // lands in the pending queue. The flush must not overwrite it when it finishes.
        await service.LogActivityAsync(ActivityEventType.HabitPlus, null, null, "Newer");

        handler.ReleaseFirstRequest.SetResult();
        await flushTask;

        // The newer event may still be in its own background flush. Wait until
        // the failed flush writes it back to the queue before reading.
        List<ActivityLogRequest> remaining = [];
        for (var i = 0; i < 50; i++)
        {
            var raw = store.Read(PendingKey);
            remaining = string.IsNullOrEmpty(raw)
                ? []
                : JsonSerializer.Deserialize<List<ActivityLogRequest>>(raw, JsonDefaults.Api)!;
            if (remaining.Count == 1 && remaining[0].CustomLabel == "Newer")
            {
                break;
            }

            await Task.Delay(20);
        }

        remaining.Should().ContainSingle();
        remaining[0].CustomLabel.Should().Be("Newer");
    }

    [Fact]
    public async Task Flush_sends_the_pending_queue_concurrently()
    {
        var store = new MemoryLocalSettingsStore();
        var handler = new BarrierHandler(expectedRequests: 6);
        using var service = new RemoteUserActivityLogService(
            new StubHttpClientFactory(handler),
            statsReader: null,
            eventStore: null,
            localStore: store,
            clock: null);

        var events = Enumerable.Range(0, 6)
            .Select(i => new ActivityLogRequest(ActivityEventType.HabitPlus, null, null, $"Event {i}", Guid.NewGuid()))
            .ToList();
        store.Write(PendingKey, JsonSerializer.Serialize(events, JsonDefaults.Api));

        var flushTask = service.TryFlushPendingAsync();
        await handler.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.ReleaseAll.SetResult();
        await flushTask;

        handler.PeakInFlight.Should().Be(6);
        store.Read(PendingKey).Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Flush_requeues_only_the_failed_event()
    {
        var store = new MemoryLocalSettingsStore();
        var handler = new BarrierHandler(expectedRequests: 2, failLabel: "fail");
        using var service = new RemoteUserActivityLogService(
            new StubHttpClientFactory(handler),
            statsReader: null,
            eventStore: null,
            localStore: store,
            clock: null);

        var events = new List<ActivityLogRequest>
        {
            new(ActivityEventType.HabitPlus, null, null, "fail", Guid.NewGuid()),
            new(ActivityEventType.HabitPlus, null, null, "ok", Guid.NewGuid())
        };
        store.Write(PendingKey, JsonSerializer.Serialize(events, JsonDefaults.Api));

        var flushTask = service.TryFlushPendingAsync();
        await handler.AllStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.ReleaseAll.SetResult();
        await flushTask;

        var remaining = JsonSerializer.Deserialize<List<ActivityLogRequest>>(store.Read(PendingKey)!, JsonDefaults.Api)!;
        remaining.Should().ContainSingle();
        remaining[0].CustomLabel.Should().Be("fail");
    }

    private sealed class MemoryLocalSettingsStore : ILocalSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Read(string key, string? defaultValue = null) =>
            _values.TryGetValue(key, out var value) ? value : defaultValue;

        public void Write(string key, string value) => _values[key] = value;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://test.local/") };
    }

    private sealed class BarrierHandler(int expectedRequests, string? failLabel = null) : HttpMessageHandler
    {
        private int _inFlight;
        private int _peakInFlight;

        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseAll { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int PeakInFlight => Volatile.Read(ref _peakInFlight);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _inFlight);
            Interlocked.Exchange(ref _peakInFlight, Math.Max(Volatile.Read(ref _peakInFlight), current));
            if (current >= expectedRequests)
            {
                AllStarted.TrySetResult();
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var logged = JsonSerializer.Deserialize<ActivityLogRequest>(body, JsonDefaults.Api)!;
            await ReleaseAll.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            Interlocked.Decrement(ref _inFlight);

            return string.Equals(logged.CustomLabel, failLabel, StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private int _requestCount;

        public TaskCompletionSource FirstRequestStarted { get; } = new();

        public TaskCompletionSource ReleaseFirstRequest { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _requestCount);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var logged = JsonSerializer.Deserialize<ActivityLogRequest>(body, JsonDefaults.Api)!;

            if (index == 1)
            {
                FirstRequestStarted.SetResult();
                await ReleaseFirstRequest.Task;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return logged.CustomLabel == "Newer"
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
