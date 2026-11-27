namespace App.Web.Services;

/// <summary>
///     Limits for the shared operator AI key. Requests made with a user-owned key
///     are only bounded by the AI rate limit policy.
/// </summary>
public sealed class AiQuotaOptions
{
    public const string SectionName = "AiQuota";

    /// <summary>
    ///     When false, OPENCODE_API_KEY and OPENAI_API_KEY are ignored and every user
    ///     must supply a key. Keep it false in production unless the key is intended
    ///     as a free tier.
    /// </summary>
    public bool AllowSharedOperatorKey { get; set; }

    /// <summary>When false, the demo guest cannot spend the shared operator key.</summary>
    public bool AllowSharedKeyForGuests { get; set; }

    public int SharedKeyDailyRequests { get; set; } = 50;

    public int SharedKeyDailyTokens { get; set; } = 100_000;

    public int GuestSharedKeyDailyRequests { get; set; } = 10;

    public int GuestSharedKeyDailyTokens { get; set; } = 20_000;
}
