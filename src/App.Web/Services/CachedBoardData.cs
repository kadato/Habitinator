using App.Shared.RCL.Models;

namespace App.Web.Services;

/// <summary>Board snapshot plus the local day it was built for, so a snapshot from yesterday is never reused.</summary>
public sealed record CachedBoardSnapshot(BoardSnapshot Snapshot, DateOnly Today);

/// <summary>Daily streak map plus the local day it was calculated for.</summary>
public sealed record CachedStreakMap(Dictionary<Guid, int> Streaks, DateOnly Today);
