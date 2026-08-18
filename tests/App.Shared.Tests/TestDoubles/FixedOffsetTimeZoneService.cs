using App.Shared.RCL.Services;

namespace App.Shared.Tests.TestDoubles;

/// <summary>Timezone service with a fixed offset from UTC for day-boundary tests.</summary>
public sealed class FixedOffsetTimeZoneService(TimeSpan offset) : IUserTimeZoneService
{
    public string? TimeZoneId => $"UTC{offset.TotalHours:+#;-#;0}";
    public bool IsDetected => true;
    public void SetOverride(string? timeZoneId) { }
    public DateOnly LocalToday => throw new NotSupportedException();
    public Task InitializeAsync() => Task.CompletedTask;
    public DateTimeOffset ConvertToLocal(DateTimeOffset utcTime) => utcTime.ToOffset(offset);
    public DateTimeOffset ConvertToUtc(DateTimeOffset localTime) => localTime.ToOffset(TimeSpan.Zero);
    public TimeSpan ConvertLocalTimeToUtc(TimeSpan localTime) => localTime;
    public TimeSpan ConvertUtcTimeToLocal(TimeSpan utcTime) => utcTime;
    public string GetTimeZoneAbbreviation() => "UTC";
}
