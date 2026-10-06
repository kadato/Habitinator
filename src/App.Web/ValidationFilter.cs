using System.ComponentModel.DataAnnotations;

namespace App.Web;

/// <summary>
/// Validates complex endpoint parameters with DataAnnotations and returns
/// RFC7807 <c>ValidationProblem</c> (400) before the handler runs.
/// Replaces the removed framework <c>WithParameterValidation</c> hook with
/// an explicit filter that works on the current SDK.
/// </summary>
internal sealed class DtoValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var failures = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var argument in context.Arguments)
        {
            await CollectFailuresAsync(argument, failures);
        }

        if (failures.Count > 0)
        {
            return Results.ValidationProblem(failures);
        }

        return await next(context);
    }

    private static async Task CollectFailuresAsync(object? argument, Dictionary<string, string[]> failures)
    {
        if (argument is null)
        {
            return;
        }

        var type = argument.GetType();
        if (!IsDtoType(type))
        {
            return;
        }

#pragma warning disable S4158, S2583 // Validator.TryValidateObjectAsync populates results via its ICollection<ValidationResult> parameter; symbolic execution models the list as still empty.
        var results = new List<ValidationResult>();
        var validationContext = new ValidationContext(argument);
        var isValid = await Validator.TryValidateObjectAsync(argument, validationContext, results, validateAllProperties: true);

        if (!isValid)
        {
            foreach (var result in results)
            {
                var key = result.MemberNames.FirstOrDefault() ?? type.Name;
                var messages = new[] { result.ErrorMessage ?? "Invalid value." };
                if (failures.TryGetValue(key, out var existing))
                {
                    failures[key] = [.. existing, .. messages];
                }
                else
                {
                    failures[key] = messages;
                }
            }
        }
#pragma warning restore S4158, S2583
    }

    private static bool IsDtoType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum)
        {
            return false;
        }

        return type switch
        {
            _ when type == typeof(string) => false,
            _ when type == typeof(Guid) => false,
            _ when type == typeof(DateOnly) => false,
            _ when type == typeof(DateTime) => false,
            _ when type == typeof(DateTimeOffset) => false,
            _ when type == typeof(TimeSpan) => false,
            _ when type == typeof(decimal) => false,
            _ when type == typeof(HttpContext) => false,
            _ when type == typeof(CancellationToken) => false,
            _ when typeof(Stream).IsAssignableFrom(type) => false,
            _ when type.Namespace?.StartsWith("Microsoft.", StringComparison.Ordinal) == true => false,
            _ => true,
        };
    }
}

internal static class DtoValidationExtensions
{
    internal static RouteGroupBuilder WithDtoValidation(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<DtoValidationFilter>();
        return group;
    }

    internal static RouteHandlerBuilder WithDtoValidation(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter<DtoValidationFilter>();
        return builder;
    }
}
