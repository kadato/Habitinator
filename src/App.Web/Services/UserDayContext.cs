using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Data;

using Microsoft.EntityFrameworkCore;

namespace App.Web.Services;

/// <summary>
///     Resolves the user's local "today" and day-start once per request from preferences, applying
///     the stored timezone to the scoped timezone service. Shared by the board and statistics
///     services so streak, retro, and stats date math all use the same day.
/// </summary>
internal static class UserDayContext
{
    public static async Task<(DateOnly Today, TimeSpan? DayStartLocalTime)> LoadAsync(
        ApplicationDbContext db,
        Guid userId,
        IUserTimeZoneService timeZone,
        CancellationToken cancellationToken)
    {
        var prefs = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.UserPreferences)
            .FirstOrDefaultAsync(cancellationToken);
        timeZone.SetOverride(prefs?.TimeZoneOverrideId);
        return (DailySchedule.LocalToday(timeZone, prefs?.DayStartLocalTime), prefs?.DayStartLocalTime);
    }
}
