using App.Shared.RCL.Services;

using FsCheck;
using FsCheck.Xunit;

namespace App.Shared.Tests;

public sealed class FocusDurationInputFuzzTests
{
    [Property]
    public void TryParse_NeverThrows(string? input)
    {
        // Parsing arbitrary inputs must never throw exceptions
        FocusDurationInput.TryParse(input, out _);
    }

    [Property]
    public void Roundtrip_ValidTimeSpans(int hoursVal, int minutesVal, int secondsVal)
    {
        // Generate a valid TimeSpan within bounds [00:00:01, 23:59:59]
        var h = Math.Abs(hoursVal) % 24;
        var m = Math.Abs(minutesVal) % 60;
        var s = Math.Abs(secondsVal) % 60;

        if (h == 0 && m == 0 && s == 0)
        {
            return; // Ignore zero TimeSpan as it is invalid to parse
        }

        var ts = new TimeSpan(h, m, s);

        // Format to a field string representation
        var formatted = FocusDurationInput.FormatForField(ts);
        Assert.False(string.IsNullOrWhiteSpace(formatted));

        // Parse back the formatted string
        var parsedSuccess = FocusDurationInput.TryParse(formatted, out var parsed);

        Assert.True(parsedSuccess);
        Assert.NotNull(parsed);
        Assert.Equal(ts, parsed!.Value);
    }

    [Property]
    public void FormatForField_Clamping_Invariants(TimeSpan ts)
    {
        // For any TimeSpan (including negative, zero, and huge ones)
        var formatted = FocusDurationInput.FormatForField(ts);

        if (ts.TotalSeconds <= 0)
        {
            Assert.Equal(string.Empty, formatted);
        }
        else
        {
            // If it is positive, parsing the formatted representation must succeed
            var parsedSuccess = FocusDurationInput.TryParse(formatted, out var parsed);
            Assert.True(parsedSuccess);
            Assert.NotNull(parsed);

            // The resulting parsed time must be clamped to MaxFocusDuration
            var expected = ts > FocusDurationInput.MaxFocusDuration ? FocusDurationInput.MaxFocusDuration : ts;
            // Since FormatForField/TryParse deals with whole seconds:
            Assert.Equal(TimeSpan.FromSeconds((int)expected.TotalSeconds), parsed!.Value);
        }
    }
}
