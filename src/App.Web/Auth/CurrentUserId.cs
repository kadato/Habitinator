namespace App.Web.Auth;

internal readonly record struct CurrentUserId(Guid Value)
{
    public static implicit operator Guid(CurrentUserId id)
    {
        return id.Value;
    }

    public static ValueTask<CurrentUserId?> BindAsync(HttpContext context)
    {
        var id = AuthenticatedUserId.TryGet(context.User);
        return ValueTask.FromResult(id.HasValue ? new CurrentUserId?(new CurrentUserId(id.Value)) : null);
    }
}
