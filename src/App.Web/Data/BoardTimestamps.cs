namespace App.Web.Data;

/// <summary>
///     Timestamp helpers for board rows. PostgreSQL stores timestamps at
///     microsecond precision, so the in-memory timestamp must use the same
///     precision. Otherwise the timestamp returned to the client has ticks the
///     database cannot keep, and the next change conflicts on a value that
///     can never match the stored row.
/// </summary>
public static class BoardTimestamps
{
    private const long TicksPerMicrosecond = 10;

    public static DateTimeOffset UtcNow() => TruncateToMicroseconds(DateTimeOffset.UtcNow);

    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TicksPerMicrosecond));
}
