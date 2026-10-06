using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace App.Web.Services;

/// <summary>
/// Shared telemetry for the web backend. Static so hot paths record
/// without extra DI. Export is wired in <c>Program.cs</c> via OTLP when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set.
/// </summary>
internal static class AppTelemetry
{
    internal const string SourceName = "Habitinator.App.Web";
    internal const string MeterName = "Habitinator.Board";

    internal static readonly ActivitySource Activity = new(SourceName);
    internal static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> BoardMutations = Meter.CreateCounter<long>(
        "board.mutations", description: "Board mutations by section and outcome.");

    private static readonly Counter<long> VersionConflicts = Meter.CreateCounter<long>(
        "board.version_conflicts", description: "Optimistic-concurrency conflicts by section.");

    private static readonly Counter<long> IdempotencyReplays = Meter.CreateCounter<long>(
        "board.idempotency_replays", description: "Idempotent replays served from stored outcome.");

    private static readonly Counter<long> IdempotencyMismatches = Meter.CreateCounter<long>(
        "board.idempotency_mismatches", description: "Idempotency key reuse with a different fingerprint.");

    private static readonly Counter<long> IdempotencyTimeouts = Meter.CreateCounter<long>(
        "board.idempotency_timeouts", description: "Idempotency waits that timed out on an in-flight claim.");

    private static readonly Counter<long> SnapshotCacheHits = Meter.CreateCounter<long>(
        "board.snapshot_cache_hits", description: "Board snapshot cache hits.");

    private static readonly Counter<long> SnapshotCacheMisses = Meter.CreateCounter<long>(
        "board.snapshot_cache_misses", description: "Board snapshot cache misses.");

    private static readonly Histogram<int> SyncDeltaSize = Meter.CreateHistogram<int>(
        "board.sync_delta_items", description: "Items per sync delta response.");

    private static readonly Histogram<int> SnapshotSize = Meter.CreateHistogram<int>(
        "board.snapshot_items", description: "Items per board snapshot response.");

    private static readonly Counter<long> BoardFanoutFailures = Meter.CreateCounter<long>(
        "board.fanout_failures", description: "SignalR board-change fan-out failures.");

    internal static void RecordMutation(string section, string outcome) =>
        BoardMutations.Add(1, new KeyValuePair<string, object?>("section", section),
            new KeyValuePair<string, object?>("outcome", outcome));

    internal static void RecordConflict(string section) =>
        VersionConflicts.Add(1, new KeyValuePair<string, object?>("section", section));

    internal static void RecordIdempotencyReplay() => IdempotencyReplays.Add(1);

    internal static void RecordIdempotencyMismatch() => IdempotencyMismatches.Add(1);

    internal static void RecordIdempotencyTimeout() => IdempotencyTimeouts.Add(1);

    internal static void RecordSnapshotCache(bool hit)
    {
        if (hit)
        {
            SnapshotCacheHits.Add(1);
        }
        else
        {
            SnapshotCacheMisses.Add(1);
        }
    }

    internal static void RecordSyncDelta(int upserts, int deletes) =>
        SyncDeltaSize.Record(upserts + deletes);

    internal static void RecordSnapshot(int items) => SnapshotSize.Record(items);

    internal static void RecordFanoutFailure() => BoardFanoutFailures.Add(1);
}
