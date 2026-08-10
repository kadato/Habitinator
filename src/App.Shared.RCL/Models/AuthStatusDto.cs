namespace App.Shared.RCL.Models;

/// <summary>
///     Authentication status serialized between the server and the WASM client
///     (persisted via <c>PersistAsJson("auth_state", ...)</c> / <c>TryTakeFromJson</c>).
///     Property names must stay wire-compatible.
/// </summary>
public sealed record AuthStatusDto(bool IsAuthenticated, string? Email);
