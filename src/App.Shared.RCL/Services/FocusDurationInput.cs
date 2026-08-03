using System.Globalization;
using System.Text.RegularExpressions;

namespace App.Shared.RCL.Services;

/// <summary>Parses and formats the "Time's up after" field (optional focus / alert length).</summary>
public static class FocusDurationInput
{
    /// <summary>Help copy for the UI tooltip (plain text, can include line breaks).</summary>
    public const string HelpTooltip =
        "Optional. When the running timer reaches this length, a \"time's up\" notice runs once. Leave empty for no end alert.\n\n"
        + "You can use minutes, seconds, and h:mm or h:mm:ss. Examples: 25 (25 min), 1:15 (1 h 15 min), 0:0:30 (30 s), 0:1:5 (1 min 5 s), 90s, 5m30s, 1h1m1s, 1:0:0 (1 h), 0:5:0 (5 min). Max 23:59:59.";

    public const string ParseErrorHint =
        "Not recognized. Use minutes (25), 1:20 (1 h 20 min), 0:0:45 (h:mm:ss), 90s, 5m30s, 1h1m, or 1h2m3s. Max 23:59:59, or leave empty for no alert.";

    public static TimeSpan MaxFocusDuration { get; } = new(23, 59, 59);

    private static readonly Regex SecondsSuffixRegex = new(@"^(\d+)\s*s$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex MinutesSecondsRegex = new(@"^(\d+)\s*m\s*(\d+)\s*s$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex ThreePartColonRegex = new(@"^(\d+):(\d+):(\d+)$", RegexOptions.None, TimeSpan.FromMilliseconds(250));
    private static readonly Regex TwoPartColonRegex = new(@"^(\d+):(\d+)$", RegexOptions.None, TimeSpan.FromMilliseconds(250));
    private static readonly Regex MinutesOnlySuffixRegex = new(@"^(\d+)\s*m$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex HmsLongFormARegex = new(@"^(\d{1,2})h(\d{1,2})m(\d{1,2})s$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex HmsLongFormBRegex = new(@"^(\d{1,2})h(\d{1,2})m$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex HmsLongFormCRegex = new(@"^(\d{1,2})h(\d{1,2})s$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex HMSuffixFormRegex = new(@"^(\d{1,2})\s*h(?:\s*(\d{1,2})\s*m?)?$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
    private static readonly Regex PlainMinutesRegex = new(@"^\d{1,4}$", RegexOptions.None, TimeSpan.FromMilliseconds(250));

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
        if (TryParseSecondsSuffix(input, out var sec))
        {
            return Assign(ref duration, sec);
        }

        if (TryParseMinutesSeconds(input, out var ms))
        {
            return Assign(ref duration, ms);
        }

        if (TryParseThreePartColon(input, out var three))
        {
            return Assign(ref duration, three);
        }

        if (TryParseTwoPartColon(input, out var two))
        {
            return Assign(ref duration, two);
        }

        if (TryParseMinutesOnlySuffix(input, out var mOnly))
        {
            return Assign(ref duration, mOnly);
        }

        if (TryParseHmsLongForm(input, out var hms))
        {
            return Assign(ref duration, hms);
        }

        if (TryParseHMSuffixForm(input, out var hm))
        {
            return Assign(ref duration, hm);
        }

        if (TryParsePlainMinutes(input, out var plain))
        {
            return Assign(ref duration, plain);
        }

        return false;
    }

    private static bool Assign(ref TimeSpan? slot, TimeSpan? value)
    {
        if (value is not { } v || v <= TimeSpan.Zero)
        {
            return false;
        }

        v = Clamp(v);
        if (v <= TimeSpan.Zero)
        {
            return false;
        }

        slot = v;
        return true;
    }

    private static bool TryParseSecondsSuffix(string s, out TimeSpan? duration)
    {
        duration = null;
        var m = SecondsSuffixRegex.Match(s);
        if (!m.Success)
        {
            return false;
        }

        if (!int.TryParse(m.Groups[1].Value, out var sec) || sec <= 0)
        {
            return false;
        }

        return TryFromTotalSeconds(sec, out duration);
    }

    private static bool TryParseMinutesSeconds(string s, out TimeSpan? duration)
    {
        duration = null;
        var m = MinutesSecondsRegex.Match(s);
        if (!m.Success)
        {
            return false;
        }

        if (!int.TryParse(m.Groups[1].Value, out var min) ||
            !int.TryParse(m.Groups[2].Value, out var sec))
        {
            return false;
        }

        if (sec is < 0 or > 59 || min < 0)
        {
            return false;
        }

        return TryFromTotalSeconds(min * 60L + sec, out duration);
    }

    private static bool TryParseThreePartColon(string s, out TimeSpan? duration)
    {
        duration = null;
        var m = ThreePartColonRegex.Match(s);
        if (!m.Success)
        {
            return false;
        }

        if (!int.TryParse(m.Groups[1].Value, out var h) ||
            !int.TryParse(m.Groups[2].Value, out var min) ||
            !int.TryParse(m.Groups[3].Value, out var sec))
        {
            return false;
        }

        if (min is < 0 or > 59 || sec is < 0 or > 59 || h is < 0 or > 23)
        {
            return false;
        }

        if (h == 0 && min == 0 && sec == 0)
        {
            return false;
        }

        return TryFromTotalSeconds(h * 3600L + min * 60L + sec, out duration);
    }

    private static bool TryParseTwoPartColon(string s, out TimeSpan? duration)
    {
        duration = null;
        var m = TwoPartColonRegex.Match(s);
        if (!m.Success)
        {
            return false;
        }

        if (!int.TryParse(m.Groups[1].Value, out var h) ||
            !int.TryParse(m.Groups[2].Value, out var min))
        {
            return false;
        }

        if (min is < 0 or > 59 || h is < 0 or > 23)
        {
            return false;
        }

        if (h == 0 && min == 0)
        {
            return false;
        }

        return TryFromTotalSeconds(h * 3600L + min * 60L, out duration);
    }

    private static bool TryParseMinutesOnlySuffix(string s, out TimeSpan? duration)
    {
        duration = null;
        var m = MinutesOnlySuffixRegex.Match(s);
        if (!m.Success)
        {
            return false;
        }

        if (!int.TryParse(m.Groups[1].Value, out var mins) || mins <= 0)
        {
            return false;
        }

        return TryFromTotalSeconds(mins * 60L, out duration);
    }

    private static bool TryParseHmsLongForm(string s, out TimeSpan? duration)
    {
        duration = null;
        var a = HmsLongFormARegex.Match(s);
        if (a.Success)
        {
            return TryValidateAndCreate(
                int.Parse(a.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(a.Groups[3].Value, CultureInfo.InvariantCulture),
                out duration);
        }

        var b = HmsLongFormBRegex.Match(s);
        if (b.Success)
        {
            return TryValidateAndCreate(
                int.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(b.Groups[2].Value, CultureInfo.InvariantCulture),
                0,
                out duration);
        }

        var c = HmsLongFormCRegex.Match(s);
        if (c.Success)
        {
            return TryValidateAndCreate(
                int.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture),
                0,
                int.Parse(c.Groups[2].Value, CultureInfo.InvariantCulture),
                out duration);
        }

        return false;
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

    private static bool TryParseHMSuffixForm(string s, out TimeSpan? duration)
    {
        duration = null;
        var m = HMSuffixFormRegex.Match(s);
        if (!m.Success)
        {
            return false;
        }

        var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (min is < 0 or > 59 || h is < 0 or > 23)
        {
            return false;
        }

        if (h == 0 && min == 0)
        {
            return false;
        }

        return TryFromTotalSeconds(h * 3600L + min * 60L, out duration);
    }

    private static bool TryParsePlainMinutes(string s, out TimeSpan? duration)
    {
        duration = null;
        if (!PlainMinutesRegex.IsMatch(s))
        {
            return false;
        }

        var mins = int.Parse(s, CultureInfo.InvariantCulture);
        if (mins <= 0)
        {
            return false;
        }

        return TryFromTotalSeconds(mins * 60L, out duration);
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
