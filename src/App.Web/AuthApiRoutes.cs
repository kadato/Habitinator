using System.Security.Claims;

using App.Shared.RCL.Models;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Models;
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
        endpoints.MapPost("/api/auth/logout", LogoutAsync).RequireAuthorization().DisableAntiforgery();
        endpoints.MapPost("/api/auth/guest-login", GuestLoginAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/cookie-login", CookieLoginAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/cookie-logout", CookieLogoutAsync).RequireAuthorization().DisableAntiforgery();
        endpoints.MapGet("/api/auth/status", GetStatus);
    }

    private static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/account/change-password", ChangePasswordAsync).RequireAuthorization().DisableAntiforgery();
        endpoints.MapPost("/api/account/delete", DeleteAccountAsync).RequireAuthorization().DisableAntiforgery();
    }

    // --- Endpoint Handlers ---

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager)
    {
        if (!RegistrationEmailValidation.IsValid(request.Email))
        {
            return Results.BadRequest<IEnumerable<string>>(["Enter a valid email address."]);
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Results.BadRequest(MapRegistrationErrorsToUserFacing(result.Errors));
        }

        return Results.Ok(new { message = "Registration successful." });
    }

    private static async Task<IResult> RegisterFormAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var email = form["Email"].ToString();
        var password = form["Password"].ToString();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Results.LocalRedirect("/auth/register?error=1");
        }

        if (!RegistrationEmailValidation.IsValid(email))
        {
            return Results.LocalRedirect("/auth/register?error=1");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var retry = $"?error=1&email={Uri.EscapeDataString(email)}";
            return Results.LocalRedirect("/auth/register" + retry);
        }

        return Results.LocalRedirect("/auth/login?registered=1");
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
        UserManager<ApplicationUser> userManager,
        JwtTokenService jwtTokenService,
        IOptions<DemoUserOptions> demoOptions,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthApiRoutes");
        var email = demoOptions.Value.Email;
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            await TryRecoverDemoGuestAsync(httpContext.RequestServices, logger, httpContext.RequestAborted);
            user = await userManager.FindByEmailAsync(email);
        }

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

    private static async Task<IResult> LogoutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Ok(new { message = "Logged out." });
    }

    private static async Task<IResult> GuestLoginAsync(
        HttpContext httpContext,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IOptions<DemoUserOptions> demoOptions,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("AuthApiRoutes");
        var email = demoOptions.Value.Email;
        var guestUser = await userManager.FindByEmailAsync(email);
        if (guestUser is null)
        {
            await TryRecoverDemoGuestAsync(httpContext.RequestServices, logger, httpContext.RequestAborted);
            guestUser = await userManager.FindByEmailAsync(email);
        }

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
        context.Response.Cookies.Delete("habitinator_theme");
        return Results.LocalRedirect("/");
    }

    private static IResult GetStatus(ClaimsPrincipal user)
    {
        var isAuthenticated = user.Identity?.IsAuthenticated == true;
        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity?.Name;
        return Results.Ok(new { isAuthenticated, email });
    }

    private static async Task<IResult> ChangePasswordAsync(
        ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ChangePasswordRequest body)
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var appUser = await userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
        {
            return Results.NotFound();
        }

        var result = await userManager.ChangePasswordAsync(appUser, body.CurrentPassword, body.NewPassword);
        if (!result.Succeeded)
        {
            return Results.BadRequest();
        }

        await signInManager.RefreshSignInAsync(appUser);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAccountAsync(
        ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var appUser = await userManager.FindByIdAsync(userId.ToString());
        if (appUser is null)
        {
            return Results.NotFound();
        }

        var result = await userManager.DeleteAsync(appUser);
        if (!result.Succeeded)
        {
            return Results.BadRequest();
        }

        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    // --- Helpers ---

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
}
