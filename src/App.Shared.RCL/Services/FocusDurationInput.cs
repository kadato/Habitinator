using System.Globalization;
using System.Text.RegularExpressions;

namespace App.Shared.RCL.Services;

/// <summary>Parses and formats the "Time's up after" field, the optional focus or alert length.</summary>
public static partial class FocusDurationInput
{
    /// <summary>Help copy for the UI tooltip. Plain text, can include line breaks.</summary>
    public const string HelpTooltip =
        "Optional. When the running timer reaches this length, a \"time's up\" notice runs once. Leave empty for no end alert.\n\n"
        + "You can use minutes, seconds, and h:mm or h:mm:ss. Examples: 25 (25 min), 1:15 (1 h 15 min), 0:0:30 (30 s), 0:1:5 (1 min 5 s), 90s, 5m30s, 1h1m1s, 1:0:0 (1 h), 0:5:0 (5 min). Max 23:59:59.";

    public const string ParseErrorHint =
        "Not recognized. Use minutes (25), 1:20 (1 h 20 min), 0:0:45 (h:mm:ss), 90s, 5m30s, 1h1m, or 1h2m3s. Max 23:59:59, or leave empty for no alert.";

    public static TimeSpan MaxFocusDuration { get; } = new(23, 59, 59);

    [GeneratedRegex(
        @"^((?<secSuffix>\d+)\s*s"
        + @"|(?<minSecMin>\d+)\s*m\s*(?<minSecSec>\d+)\s*s"
        + @"|(?<threeH>\d+):(?<threeM>\d+):(?<threeS>\d+)"
        + @"|(?<twoH>\d+):(?<twoM>\d+)"
        + @"|(?<minOnly>\d+)\s*m"
        + @"|(?<hmsH>\d{1,2})h(?<hmsM>\d{1,2})m(?<hmsS>\d{1,2})s"
        + @"|(?<hmsBH>\d{1,2})h(?<hmsBM>\d{1,2})m"
        + @"|(?<hmsCH>\d{1,2})h(?<hmsCS>\d{1,2})s"
        + @"|(?<sufH>\d{1,2})\s*h(?:\s*(?<sufM>\d{1,2})\s*m?)?"
        + @"|(?<plain>\d{1,4}))$",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex GetFocusDurationRegex();

    public static string FormatForAlertLabel(TimeSpan t)
    {
        t = Clamp(t);
        if (t <= TimeSpan.Zero)
        {
            return "0s";
        }

        if (t.TotalSeconds < 60)
        {
            var s = (int)Math.Ceiling(t.TotalSeconds);
            return $"{s}s";
        }

        if (t.Seconds == 0 && t.Milliseconds == 0)
        {
            var min = (int)Math.Ceiling(t.TotalMinutes);
            if (min < 60)
            {
                return $"{min} min";
            }

            var h = min / 60;
            var rest = min % 60;
            if (rest == 0)
            {
                return $"{h}h";
            }

            return $"{h}h {rest}m";
        }

        if (t.Hours == 0)
        {
            return $"{t.Minutes}m {t.Seconds}s";
        }

        if (t.Seconds == 0)
        {
            return t.Minutes == 0
                ? $"{t.Hours}h"
                : $"{t.Hours}h {t.Minutes}m";
        }

        return $"{t.Hours}h {t.Minutes}m {t.Seconds}s";
    }

    public static string FormatForField(TimeSpan? d)
    {
        if (d is not { TotalSeconds: > 0 } t)
        {
            return string.Empty;
        }

        t = Clamp(t);
        var h = t.Hours;
        var m = t.Minutes;
        var s = t.Seconds;
        if (h == 0 && m == 0)
        {
            return $"{s}s";
        }

        if (h == 0 && s == 0)
        {
            return m.ToString(CultureInfo.InvariantCulture);
        }

        if (h == 0)
        {
            return $"{m}m{s}s";
        }

        if (s == 0)
        {
            return m == 0
                ? $"{h}h"
                : $"{h}:{m:D2}";
        }

        return $"{h}:{m:D2}:{s:D2}";
    }

    /// <summary>Empty/whitespace input is valid and yields <see langword="null" />.</summary>
    public static bool TryParse(string? raw, out TimeSpan? duration)
    {
        duration = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var input = raw.Trim();
        var m = GetFocusDurationRegex().Match(input);
        if (!m.Success)
        {
            return false;
        }

        if (m.Groups["secSuffix"].Success)
        {
            return TryParseSecondsSuffix(m, ref duration);
        }

        if (m.Groups["minSecMin"].Success)
        {
            return TryParseMinutesAndSeconds(m, ref duration);
        }

        if (m.Groups["threeH"].Success)
        {
            return TryParseHms(m, "threeH", "threeM", "threeS", ref duration);
        }

        if (m.Groups["twoH"].Success)
        {
            return TryParseHms(m, "twoH", "twoM", null, ref duration);
        }

        if (m.Groups["minOnly"].Success)
        {
            return TryParseMinutesOnly(m, ref duration);
        }

        if (m.Groups["hmsH"].Success)
        {
            return TryParseHms(m, "hmsH", "hmsM", "hmsS", ref duration);
        }

        if (m.Groups["hmsBH"].Success)
        {
            return TryParseHms(m, "hmsBH", "hmsBM", null, ref duration);
        }

        if (m.Groups["hmsCH"].Success)
        {
            return TryParseHms(m, "hmsCH", null, "hmsCS", ref duration);
        }

        if (m.Groups["sufH"].Success)
        {
            return TryParseSuffixedHours(m, ref duration);
        }

        return TryParsePlainMinutes(m, ref duration);
    }

    private static bool TryParseSecondsSuffix(Match m, ref TimeSpan? duration)
    {
        if (!int.TryParse(m.Groups["secSuffix"].Value, out var sec) || sec <= 0)
        {
            return false;
        }

        return Assign(ref duration, TryFromTotalSeconds(sec, out var d) ? d : null);
    }

    private static bool TryParseMinutesAndSeconds(Match m, ref TimeSpan? duration)
    {
        if (!int.TryParse(m.Groups["minSecMin"].Value, out var min) ||
            !int.TryParse(m.Groups["minSecSec"].Value, out var sec))
        {
            return false;
        }

        if (sec is < 0 or > 59 || min < 0)
        {
            return false;
        }

        return Assign(ref duration, TryFromTotalSeconds(min * 60L + sec, out var d) ? d : null);
    }

    private static bool TryParseMinutesOnly(Match m, ref TimeSpan? duration)
    {
        if (!int.TryParse(m.Groups["minOnly"].Value, out var mins) || mins <= 0)
        {
            return false;
        }

        return Assign(ref duration, TryFromTotalSeconds(mins * 60L, out var d) ? d : null);
    }

    private static bool TryParseSuffixedHours(Match m, ref TimeSpan? duration)
    {
        var h = int.Parse(m.Groups["sufH"].Value, CultureInfo.InvariantCulture);
        var min = m.Groups["sufM"].Success
            ? int.Parse(m.Groups["sufM"].Value, CultureInfo.InvariantCulture)
            : 0;
        return TryValidateAndCreate(h, min, 0, out duration);
    }

    private static bool TryParsePlainMinutes(Match m, ref TimeSpan? duration)
    {
        var plainMins = int.Parse(m.Groups["plain"].Value, CultureInfo.InvariantCulture);
        if (plainMins <= 0)
        {
            return false;
        }

        return Assign(ref duration, TryFromTotalSeconds(plainMins * 60L, out var plainDuration) ? plainDuration : null);
    }

    private static bool TryParseHms(Match m, string hGroup, string? minGroup, string? secGroup, ref TimeSpan? duration)
    {
        if (!int.TryParse(m.Groups[hGroup].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var h))
        {
            return false;
        }

        var min = 0;
        if (minGroup is not null &&
            !int.TryParse(m.Groups[minGroup].Value, NumberStyles.None, CultureInfo.InvariantCulture, out min))
        {
            return false;
        }

        var sec = 0;
        if (secGroup is not null &&
            !int.TryParse(m.Groups[secGroup].Value, NumberStyles.None, CultureInfo.InvariantCulture, out sec))
        {
            return false;
        }

        return TryValidateAndCreate(h, min, sec, out duration);
    }

    private static bool Assign(ref TimeSpan? slot, TimeSpan? value)
    {
        if (value is not { } v || v <= TimeSpan.Zero)
        {
            return false;
        }

        v = Clamp(v);
        slot = v;
        return true;
    }

    private static bool TryValidateAndCreate(int h, int m, int sec, out TimeSpan? duration)
    {
        duration = null;
        if (m is < 0 or > 59 || sec is < 0 or > 59 || h is < 0 or > 23)
        {
            return false;
        }

        if (h == 0 && m == 0 && sec == 0)
        {
            return false;
        }

        return TryFromTotalSeconds(h * 3600L + m * 60L + sec, out duration);
    }

    private static bool TryFromTotalSeconds(long totalSec, out TimeSpan? duration)
    {
        duration = null;
        if (totalSec <= 0)
        {
            return false;
        }

        if (totalSec > (long)MaxFocusDuration.TotalSeconds)
        {
            return false;
        }

        var t = TimeSpan.FromSeconds(totalSec);
        duration = t;
        return true;
    }

    private static TimeSpan Clamp(TimeSpan t)
    {
        return t > MaxFocusDuration ? MaxFocusDuration : t;
    }
}
