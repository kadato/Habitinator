namespace App.Web.Data;

/// <summary>Stores completed idempotent board API responses (Idempotency-Key header).</summary>
public sealed class BoardRequestIdempotencyEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser? User { get; set; }

    /// <summary>Client-supplied key (MAUI uses outbox OperationId).</summary>
    public string IdempotencyKey { get; set; } = "";

    /// <summary>SHA-256 hex (64 chars) of method + path + canonical request fingerprint.</summary>
    public string RequestFingerprintHex { get; set; } = "";

    /// <summary>-1 = in-flight. Otherwise HTTP status code.</summary>
    public int ResponseStatusCode { get; set; }

    public string ResponseBody { get; set; } = "";

    public DateTimeOffset CreatedAtUtc { get; set; }
}
