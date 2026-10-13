using App.Web.Auth;

using ModelContextProtocol;

namespace App.Web.Mcp;

internal static class McpUser
{
    public static Guid RequireId(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        if (AuthenticatedUserId.TryGet(user) is { } id)
        {
            return id;
        }

        throw new McpProtocolException("Sign in required. Call with a valid JWT bearer token or auth cookie.", McpErrorCode.InvalidRequest);
    }

    public static McpProtocolException MissingAccount()
    {
        return new McpProtocolException(
            "You are signed in, but the server has no record for you. Register or sign in again.",
            McpErrorCode.InternalError);
    }
}
