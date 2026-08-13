namespace App.Shared.RCL.Models;

/// <summary>
///     Authentication status serialized between the server and the WASM client.
///     Persisted via <c>PersistAsJson("auth_state", ...)</c> and <c>TryTakeFromJson</c>.
///     Property names must stay wire-compatible.
/// </summary>
public sealed record AuthStatusDto(bool IsAuthenticated, string? Email);
