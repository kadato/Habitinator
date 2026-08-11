using App.Shared.RCL;
using App.Shared.RCL.Models;

using FluentAssertions;

namespace App.Shared.Tests;

public sealed class ZalgoSanitizerTests
{
    // ── IsZalgo ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsZalgo_Null_ReturnsFalse()
        => ZalgoSanitizer.IsZalgo(null).Should().BeFalse();

    [Fact]
    public void IsZalgo_EmptyString_ReturnsFalse()
        => ZalgoSanitizer.IsZalgo(string.Empty).Should().BeFalse();

    [Theory]
    [InlineData("Hello, World!")]
    [InlineData("Plain ASCII text 1234")]
    [InlineData("café")]          // é = e + combining grave (1 combining mark)
    [InlineData("naïve")]         // ï = i + combining diaeresis (1 combining mark)
    [InlineData("résumé")]
    public void IsZalgo_CleanText_ReturnsFalse(string input)
        => ZalgoSanitizer.IsZalgo(input).Should().BeFalse();

    [Fact]
    public void IsZalgo_TwoCombiningPerBase_ReturnsFalse()
    {
        // Vietnamese "ợ" = o plus combining horn U+031B and combining dot below U+0323 - 2 marks
        var input = "o\u031B\u0323";
        ZalgoSanitizer.IsZalgo(input).Should().BeFalse();
    }

    [Fact]
    public void IsZalgo_ThreeCombiningOnOneBase_ReturnsTrue()
    {
        // 3 combining marks on a single 'a'
        var input = "a\u0300\u0301\u0302";
        ZalgoSanitizer.IsZalgo(input).Should().BeTrue();
    }

    [Fact]
    public void IsZalgo_HeavyZalgo_ReturnsTrue()
    {
        // Realistic Zalgo: 'h' with ~10 combining marks
        var input = "h\u0300\u0301\u0302\u0303\u0304\u0305\u0306\u0307\u0308\u0309";
        ZalgoSanitizer.IsZalgo(input).Should().BeTrue();
    }

    // ── Sanitize (nullable overload) ─────────────────────────────────────────

    [Fact]
    public void Sanitize_Null_ReturnsNull()
        => ZalgoSanitizer.Sanitize(null).Should().BeNull();

    [Fact]
    public void Sanitize_EmptyString_ReturnsEmpty()
        => ZalgoSanitizer.Sanitize(string.Empty).Should().Be(string.Empty);

    [Theory]
    [InlineData("Hello")]
    [InlineData("café")]
    [InlineData("naïve")]
    [InlineData("résumé")]
    [InlineData("こんにちは")]      // Japanese - no combining marks
    [InlineData("😀 emoji test")]  // Emoji as a surrogate pair - no combining marks
    public void Sanitize_CleanText_ReturnsSameInstance(string input)
    {
        // No Zalgo → should return the original string instance (fast path)
        var result = ZalgoSanitizer.Sanitize((string?)input);
        result.Should().Be(input);
        object.ReferenceEquals(result, input).Should().BeTrue("clean text must not allocate");
    }

    [Fact]
    public void Sanitize_TwoCombiningPerBase_ReturnsSameInstance()
    {
        var input = "o\u031B\u0323"; // Vietnamese ợ - 2 marks, at limit
        var result = ZalgoSanitizer.Sanitize((string?)input);
        result.Should().Be(input);
        object.ReferenceEquals(result, input).Should().BeTrue();
    }

    [Fact]
    public void Sanitize_ExcessCombining_StripsExcess()
    {
        // 'a' + 3 combining marks → should strip ALL combining marks from it (since it's Zalgo)
        var input = "a\u0300\u0301\u0302";
        var result = ZalgoSanitizer.Sanitize((string?)input);
        result.Should().Be("a");
    }

    [Fact]
    public void Sanitize_HeavyZalgo_StripsToLimit()
    {
        var input = "h\u0300\u0301\u0302\u0303\u0304\u0305\u0306\u0307\u0308\u0309";
        var result = ZalgoSanitizer.Sanitize((string?)input);

        // All combining marks should be stripped
        result.Should().Be("h");
    }

    [Fact]
    public void Sanitize_MixedCleanAndZalgo_OnlyStripsZalgoParts()
    {
        // "Hi" is clean; "a\u0300\u0301\u0302" is Zalgo; " ok" is clean
        var input = "Hi " + "a\u0300\u0301\u0302" + " ok";
        var result = ZalgoSanitizer.Sanitize((string?)input);
        result.Should().Be("Hi a ok");
    }

    [Fact]
    public void Sanitize_MultipleZalgoWords_AllStripped()
    {
        var zalgo1 = "a\u0300\u0301\u0302\u0303"; // 4 marks
        var zalgo2 = "b\u0330\u0331\u0332\u0333"; // 4 marks
        var input = $"{zalgo1} {zalgo2}";
        var result = ZalgoSanitizer.Sanitize((string?)input);
        result.Should().Be("a b");
    }

    // ── SanitizeAndTrim ─────────────────────────────────────────────────────

    [Fact]
    public void SanitizeAndTrim_Null_ReturnsEmptyString()
        => ZalgoSanitizer.SanitizeAndTrim(null).Should().Be(string.Empty);

    [Fact]
    public void SanitizeAndTrim_EmptyOrWhitespace_ReturnsEmptyString()
    {
        ZalgoSanitizer.SanitizeAndTrim(string.Empty).Should().Be(string.Empty);
        ZalgoSanitizer.SanitizeAndTrim("   ").Should().Be(string.Empty);
    }

    [Fact]
    public void SanitizeAndTrim_CleanInput_TrimsWhitespace()
        => ZalgoSanitizer.SanitizeAndTrim("   clean   ").Should().Be("clean");

    [Fact]
    public void SanitizeAndTrim_ZalgoInput_SanitizesAndTrims()
    {
        var result = ZalgoSanitizer.SanitizeAndTrim("   z\u0300\u0301\u0302\u0303   ");
        result.Should().Be("z");
    }

    // ── Real-world Zalgo samples ────────────────────────────────────────────

    [Fact]
    public void Sanitize_RealWorldZalgoTitle_IsSafe()
    {
        // Typical copy-pasted Zalgo from a Zalgo generator
        const string zalgo = "H\u0321\u035b\u034e\u0356\u034d\u032e\u033b\u0329a\u036f\u0339\u031e\u0332\u0331\u0345b\u0353\u0347\u0345i\u035a\u0332t";
        var result = ZalgoSanitizer.Sanitize((string?)zalgo);

        // Result must not contain Zalgo
        ZalgoSanitizer.IsZalgo(result).Should().BeFalse();

        // Base letters must still be present
        result.Should().Contain("H").And.Contain("a").And.Contain("b").And.Contain("i").And.Contain("t");
    }

    // ── DailyChecklistJson Sanitization ─────────────────────────────────────

    [Fact]
    public void DailyChecklistJson_Serialize_SanitizesChecklistItemText()
    {
        var items = new List<DailyChecklistItem>
        {
            new(Guid.NewGuid(), "  clean text  ", false),
            new(Guid.NewGuid(), "  z\u0300\u0301\u0302\u0303  ", true)
        };

        var json = DailyChecklistJson.Serialize(items);
        json.Should().NotBeNull();

        var parsed = DailyChecklistJson.Parse(json);
        parsed.Should().HaveCount(2);
        parsed[0].Text.Should().Be("clean text");
        parsed[1].Text.Should().Be("z");
    }
}
