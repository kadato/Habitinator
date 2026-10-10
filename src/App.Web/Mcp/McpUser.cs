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
}
