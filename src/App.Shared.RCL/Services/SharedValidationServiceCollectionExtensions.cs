using Microsoft.Extensions.DependencyInjection;

namespace App.Shared.RCL.Services;

/// <summary>
///     Registers validation in the shared assembly so the source generator emits
///     metadata for the request DTOs declared in this assembly. App.Web calls this
///     before its own AddValidation so both assemblies contribute metadata.
/// </summary>
public static class SharedValidationServiceCollectionExtensions
{
    public static IServiceCollection AddSharedValidation(this IServiceCollection services) =>
        services.AddValidation();
}
