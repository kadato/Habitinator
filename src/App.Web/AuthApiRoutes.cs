using System.Security.Claims;
using System.Text.Json;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Web;

internal static class AuthApiRoutes
{
    private const string BoardOrJwtPolicy = "BoardOrJwt";

    internal static void MapAuthApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRegisterEndpoints();
        endpoints.MapLoginEndpoints();
        endpoints.MapAccountEndpoints();
    }

    private static void MapRegisterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/register", RegisterAsync).DisableAntiforgery().RequireRateLimiting("auth").WithDtoValidation();
        endpoints.MapPost("/api/auth/register-form", RegisterFormAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/forgot-password", ForgotPasswordAsync).DisableAntiforgery().RequireRateLimiting("auth").WithDtoValidation();
        endpoints.MapPost("/api/auth/reset-password", ResetPasswordAsync).DisableAntiforgery().RequireRateLimiting("auth").WithDtoValidation();
    }

    private static void MapLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", LoginAsync).DisableAntiforgery().RequireRateLimiting("auth").WithDtoValidation();
        endpoints.MapPost("/api/auth/guest-jwt", GuestJwtLoginAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/guest-login", GuestLoginAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/cookie-login", CookieLoginAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapPost("/api/auth/cookie-logout", CookieLogoutAsync).DisableAntiforgery().RequireRateLimiting("auth");
        endpoints.MapGet("/api/auth/status", GetStatus).RequireRateLimiting("api");
    }

    private static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/account/change-password", ChangePasswordAsync).RequireAuthorization(BoardOrJwtPolicy).DisableAntiforgery().RequireRateLimiting("api").WithDtoValidation();
        endpoints.MapPost("/api/account/delete", DeleteAccountAsync).RequireAuthorization(BoardOrJwtPolicy).DisableAntiforgery().RequireRateLimiting("api");
        endpoints.MapGet("/api/account/export", ExportDataAsync).RequireAuthorization(BoardOrJwtPolicy).DisableAntiforgery().RequireRateLimiting("api");
        endpoints.MapPost("/api/account/import", ImportDataAsync).RequireAuthorization(BoardOrJwtPolicy).DisableAntiforgery().RequireRateLimiting("api");
        endpoints.MapPost("/api/account/mcp-token", CreateMcpTokenAsync).RequireAuthorization(BoardOrJwtPolicy).DisableAntiforgery().RequireRateLimiting("api");
        endpoints.MapGet("/api/account/mcp-tokens", ListMcpTokensAsync).RequireAuthorization(BoardOrJwtPolicy).DisableAntiforgery().RequireRateLimiting("api");
        endpoints.MapDelete("/api/account/mcp-tokens/{id:guid}", RevokeMcpTokenAsync).RequireAuthorization(BoardOrJwtPolicy).DisableAntiforgery().RequireRateLimiting("api");
        endpoints.MapPost("/api/account/mcp-tokens/revoke-all", RevokeAllMcpTokensAsync).RequireAuthorization(BoardOrJwtPolicy).DisableAntiforgery().RequireRateLimiting("api");
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

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            // Same work as the real branch so unknown emails cannot be enumerated by timing.
            RunDummyPasswordCheck(request.Email);
            return Results.Ok(new { message = "If an account exists for this email, the server sent a reset link." });
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = BuildResetLink(httpContext, user.Email, token);
        await emailSender.SendPasswordResetAsync(user.Email, resetLink, cancellationToken);
        return Results.Ok(new { message = "If an account exists for this email, the server sent a reset link." });
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Results.BadRequest(new { detail = "Invalid reset link. Request a new one." });
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { detail = "Invalid reset link. Request a new one." });
        }

        return Results.Ok(new { message = "Password has been reset. Sign in with your new password." });
    }

    private static string BuildResetLink(HttpContext httpContext, string email, string token)
    {
        var scheme = httpContext.Request.Scheme;
        var host = httpContext.Request.Host.Value;
        var query = $"email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        return $"{scheme}://{host}/auth/reset-password?{query}";
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

        string? returnUrl = null;
        if (httpContext.Request.HasFormContentType)
        {
            var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
            returnUrl = form["ReturnUrl"].ToString();
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                returnUrl = form["returnUrl"].ToString();
            }
        }
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            returnUrl = httpContext.Request.Query["returnUrl"].ToString();
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && (returnUrl.StartsWith('/') || returnUrl.StartsWith("~/", StringComparison.Ordinal)))
        {
            return Results.LocalRedirect(returnUrl);
        }

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
        var returnUrl = form["ReturnUrl"].ToString();
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            returnUrl = form["returnUrl"].ToString();
        }
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            returnUrl = httpContext.Request.Query["returnUrl"].ToString();
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            var retry = string.IsNullOrWhiteSpace(returnUrl)
                ? "/auth/login?error=1"
                : $"/auth/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}";
            return Results.LocalRedirect(retry);
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            RunDummyPasswordCheck(password);
            var retry = string.IsNullOrWhiteSpace(returnUrl)
                ? "/auth/login?error=1"
                : $"/auth/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}";
            return Results.LocalRedirect(retry);
        }

        var loginResult = await signInManager.PasswordSignInAsync(
            user,
            password,
            rememberMe,
            true);

        if (!loginResult.Succeeded)
        {
            var retry = string.IsNullOrWhiteSpace(returnUrl)
                ? "/auth/login?error=1"
                : $"/auth/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}";
            return Results.LocalRedirect(retry);
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && (returnUrl.StartsWith('/') || returnUrl.StartsWith("~/", StringComparison.Ordinal)))
        {
            return Results.LocalRedirect(returnUrl);
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
        try
        {
            return Results.Ok(await exportService.BuildAsync(user.Value, cancellationToken));
        }
        catch (UserDataExportTooLargeException ex)
        {
            return Results.Text(
                JsonSerializer.Serialize(new { detail = ex.Message }),
                "application/json",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
    }

    private static async Task<IResult> ImportDataAsync(
        CurrentUserId user,
        UserDataExportDto payload,
        UserDataImportService importService,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await importService.ImportAsync(user.Value, payload, cancellationToken));
        }
        catch (UserDataImportValidationException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
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

    private static async Task<IResult> CreateMcpTokenAsync(
        CurrentUserId user,
        UserManager<ApplicationUser> userManager,
        JwtTokenService jwt,
        ApplicationDbContext db,
        HttpContext httpContext,
        CreateMcpTokenRequest? request,
        CancellationToken cancellationToken)
    {
        var appUser = await userManager.FindByIdAsync(user.Value.ToString());
        if (appUser is null)
        {
            return Results.NotFound();
        }

        var days = Math.Clamp(request?.Days ?? 90, 1, 365);
        var label = NormalizeMcpLabel(request?.Label);
        var scope = McpTokenScopes.Normalize(request?.Scope);
        var lifetime = TimeSpan.FromDays(days);
        var now = DateTimeOffset.UtcNow;
        var row = new McpTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Value,
            Label = label,
            Scope = scope,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(lifetime),
        };
        db.McpTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        var token = jwt.CreateToken(appUser, lifetime, row.Id, scope);
        var expiresAtUtc = DateTime.UtcNow.Add(lifetime);
        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var endpoint = $"{baseUrl}/mcp";

        return Results.Ok(new CreateMcpTokenResponse(
            Token: token,
            ExpiresAtUtc: expiresAtUtc,
            Endpoint: endpoint,
            Days: days,
            Id: row.Id,
            Label: label,
            Scope: scope));
    }

    private static async Task<IResult> ListMcpTokensAsync(
        CurrentUserId user,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await db.McpTokens
            .Where(t => t.UserId == user.Value && t.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);

        var rows = await db.McpTokens
            .AsNoTracking()
            .Where(t => t.UserId == user.Value)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var infos = rows
            .Select(t => new McpTokenInfo(
                t.Id,
                t.Label,
                t.CreatedAtUtc.UtcDateTime,
                t.ExpiresAtUtc.UtcDateTime,
                t.RevokedAtUtc.HasValue ? t.RevokedAtUtc.Value.UtcDateTime : null,
                t.LastUsedAtUtc.HasValue ? t.LastUsedAtUtc.Value.UtcDateTime : null,
                t.Scope ?? McpTokenScopes.Full))
            .ToList();

        return Results.Ok(infos);
    }

    private static async Task<IResult> RevokeMcpTokenAsync(
        CurrentUserId user,
        Guid id,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var row = await db.McpTokens
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == user.Value, cancellationToken);
        if (row is null)
        {
            return Results.NotFound();
        }

        if (row.RevokedAtUtc is null)
        {
            row.RevokedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> RevokeAllMcpTokensAsync(
        CurrentUserId user,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        await db.McpTokens
            .Where(t => t.UserId == user.Value && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAtUtc, DateTimeOffset.UtcNow),
                cancellationToken);

        return Results.NoContent();
    }

    private static string NormalizeMcpLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "MCP token";
        }

        return label.Trim()[..Math.Min(100, label.Trim().Length)];
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
