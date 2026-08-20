using System.Security.Claims;

using App.Shared.RCL.Models;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace App.Web;

internal static class AuthApiRoutes
{
    internal static void MapAuthApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRegisterEndpoints();
        endpoints.MapLoginEndpoints();
        endpoints.MapAccountEndpoints();
    }

    private static void MapRegisterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/register", RegisterAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/register-form", RegisterFormAsync).DisableAntiforgery().RequireRateLimiting("auth");
    }

    private static void MapLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", LoginAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/guest-jwt", GuestJwtLoginAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/guest-login", GuestLoginAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/cookie-login", CookieLoginAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/cookie-logout", CookieLogoutAsync).DisableAntiforgery();
        endpoints.MapGet("/api/auth/status", GetStatus);
    }

    private static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/account/change-password", ChangePasswordAsync).RequireAuthorization("BoardOrJwt").DisableAntiforgery();
        endpoints.MapPost("/api/account/delete", DeleteAccountAsync).RequireAuthorization("BoardOrJwt").DisableAntiforgery();
        endpoints.MapGet("/api/account/export", ExportDataAsync).RequireAuthorization("BoardOrJwt").DisableAntiforgery().RequireRateLimiting("api");
    }

    // Endpoint handlers

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IBoardChangeNotifier notifier,
        ILoggerFactory loggerFactory)
    {
        var (user, createResult) = await CreateUserCoreAsync(userManager, request.Email, request.Password);
        if (user is null)
        {
            if (createResult is null)
            {
                return Results.BadRequest<IEnumerable<string>>(["Enter a valid email address."]);
            }

            return Results.BadRequest(MapRegistrationErrorsToUserFacing(createResult.Errors));
        }

        await SeedNewUserBoardAsync(dbContext, notifier, user.Id, loggerFactory);

        return Results.Ok(new { message = "Registration successful." });
    }

    private static async Task<IResult> RegisterFormAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IBoardChangeNotifier notifier,
        ILoggerFactory loggerFactory)
    {
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var email = form["Email"].ToString();
        var password = form["Password"].ToString();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Results.LocalRedirect("/auth/register?error=1");
        }

        var (user, createResult) = await CreateUserCoreAsync(userManager, email, password);
        if (user is null)
        {
            var retry = createResult is null
                ? "?error=1"
                : $"?error=1&email={Uri.EscapeDataString(email)}";
            return Results.LocalRedirect($"/auth/register{retry}");
        }

        await SeedNewUserBoardAsync(dbContext, notifier, user.Id, loggerFactory);

        return Results.LocalRedirect("/auth/login?registered=1");
    }

    private static async Task<(ApplicationUser? User, IdentityResult? CreateResult)> CreateUserCoreAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password)
    {
        if (!RegistrationEmailValidation.IsValid(email))
        {
            return (null, null);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };

        var result = await userManager.CreateAsync(user, password);
        return result.Succeeded ? (user, null) : (null, result);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        JwtTokenService jwtTokenService)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            RunDummyPasswordCheck(request.Password);
            return Results.Unauthorized();
        }

        var loginResult = await signInManager.PasswordSignInAsync(
            user,
            request.Password,
            request.RememberMe,
            true);

        if (!loginResult.Succeeded)
        {
            return Results.Unauthorized();
        }

        var token = jwtTokenService.CreateToken(user);
        return Results.Ok(new LoginResponse(token, user.Email ?? string.Empty));
    }

    private static async Task<IResult> GuestJwtLoginAsync(
        HttpContext httpContext,
        SignInManager<ApplicationUser> signInManager,
        JwtTokenService jwtTokenService,
        IOptions<DemoUserOptions> demoOptions,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthApiRoutes");
        var user = await FindOrRecoverDemoGuestAsync(httpContext.RequestServices, logger, httpContext.RequestAborted);
        if (user is null)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var loginResult = await signInManager.PasswordSignInAsync(
            user,
            demoOptions.Value.Password,
            false,
            true);

        if (!loginResult.Succeeded)
        {
            return Results.Unauthorized();
        }

        var token = jwtTokenService.CreateToken(user);
        return Results.Ok(new LoginResponse(token, user.Email ?? string.Empty));
    }

    private static async Task<IResult> GuestLoginAsync(
        HttpContext httpContext,
        SignInManager<ApplicationUser> signInManager,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthApiRoutes");
        var guestUser = await FindOrRecoverDemoGuestAsync(httpContext.RequestServices, logger, httpContext.RequestAborted);
        if (guestUser is null)
        {
            return Results.LocalRedirect("/auth/login?guest=missing");
        }

        await signInManager.SignInAsync(guestUser, true);
        return Results.LocalRedirect("/");
    }

    private static async Task<IResult> CookieLoginAsync(
        HttpContext httpContext,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var email = form["Email"].ToString();
        var password = form["Password"].ToString();
        var rememberMe = form["RememberMe"] == "true";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Results.LocalRedirect("/auth/login?error=1");
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            RunDummyPasswordCheck(password);
            return Results.LocalRedirect("/auth/login?error=1");
        }

        var loginResult = await signInManager.PasswordSignInAsync(
            user,
            password,
            rememberMe,
            true);

        if (!loginResult.Succeeded)
        {
            return Results.LocalRedirect("/auth/login?error=1");
        }

        return Results.LocalRedirect("/");
    }

    private static async Task<IResult> CookieLogoutAsync(
        HttpContext context,
        SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        context.Response.Cookies.Delete(ThemeCookie.Name);
        return Results.LocalRedirect("/");
    }

    private static IResult GetStatus(ClaimsPrincipal user)
    {
        var isAuthenticated = user.Identity?.IsAuthenticated == true;
        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity?.Name;
        return Results.Ok(new { isAuthenticated, email });
    }

    private static async Task<IResult> ChangePasswordAsync(
        CurrentUserId user,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ChangePasswordRequest body)
    {
        var appUser = await userManager.FindByIdAsync(user.Value.ToString());
        if (appUser is null)
        {
            return Results.NotFound();
        }

        var result = await userManager.ChangePasswordAsync(appUser, body.CurrentPassword, body.NewPassword);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { detail = "Password change failed. Check your current password." });
        }

        await signInManager.RefreshSignInAsync(appUser);
        return Results.NoContent();
    }

    private static async Task<IResult> ExportDataAsync(
        CurrentUserId user,
        UserDataExportService exportService,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await exportService.BuildAsync(user.Value, cancellationToken));
    }

    private static async Task<IResult> DeleteAccountAsync(
        CurrentUserId user,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var appUser = await userManager.FindByIdAsync(user.Value.ToString());
        if (appUser is null)
        {
            return Results.NotFound();
        }

        var result = await userManager.DeleteAsync(appUser);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { detail = "Account could not be deleted. Try again." });
        }

        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    // Helpers

    private static readonly PasswordHasher<ApplicationUser> DummyHasher = new();

    private static readonly string DummyPasswordHash = DummyHasher.HashPassword(
        new ApplicationUser(),
        "timing-equalizer-dummy-password");

    /// <summary>
    ///     Runs a password hash verification against a fixed dummy hash so the unknown user
    ///     branch takes the same time as a real password check, preventing account enumeration
    ///     by response timing.
    /// </summary>
    private static void RunDummyPasswordCheck(string password)
    {
        DummyHasher.VerifyHashedPassword(new ApplicationUser(), DummyPasswordHash, password);
    }

    private static List<string> MapRegistrationErrorsToUserFacing(IEnumerable<IdentityError> errors) =>
        [.. errors.Select(static e => e.Code switch
        {
            "DuplicateUserName" or "DuplicateEmail" => "This email is already registered.",
            "InvalidUserName" or "InvalidEmail" => "Enter a valid email address.",
            _ => (e.Description ?? "Registration could not be completed.").Replace(
                "Username", "Email", StringComparison.OrdinalIgnoreCase)
        })];

    private static async Task<ApplicationUser?> FindOrRecoverDemoGuestAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var email = services.GetRequiredService<IOptions<DemoUserOptions>>().Value.Email;
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            await TryRecoverDemoGuestAsync(services, logger, cancellationToken);
            user = await userManager.FindByEmailAsync(email);
        }

        return user;
    }

    private static async Task TryRecoverDemoGuestAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            await DemoDataSeeder.SeedAsync(services, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "On-demand demo data seeding failed.");
        }
    }

    private static async Task SeedNewUserBoardAsync(
        ApplicationDbContext db,
        IBoardChangeNotifier notifier,
        Guid userId,
        ILoggerFactory loggerFactory)
    {
        try
        {
            await NewUserBoardSeeder.SeedIfMissingAsync(db, notifier, userId);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("AuthApiRoutes").LogWarning(ex,
                "Failed to seed the getting-started board for new user {UserId}.", userId);
        }
    }
}
