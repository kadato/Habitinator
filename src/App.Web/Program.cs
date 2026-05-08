using System.Security.Claims;
using System.Text;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Hubs;
using App.Web.Models;
using App.Web.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using MudBlazor.Services;

using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => { options.DetailedErrors = builder.Environment.IsDevelopment(); });
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, HttpContextAuthenticationStateProvider>();
builder.Services.AddMudServices();

var dbConnectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("habitinatordb")
    ?? throw new InvalidOperationException(
        "No PostgreSQL connection string configured. Set ConnectionStrings:DefaultConnection or run through Aspire (habitinatordb).");

// Options must be singleton so IDbContextFactory (singleton) can be constructed; the DbContext
// instance remains scoped. Same pattern: https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory-eg-for-blazor
builder.Services.AddDbContext<ApplicationDbContext>(
    options => options.UseNpgsql(dbConnectionString),
    contextLifetime: ServiceLifetime.Scoped,
    optionsLifetime: ServiceLifetime.Singleton);
// Isolates read queries from the scoped context so Blazor + SignalR cannot interleave
// two operations on the same instance (e.g. GetSnapshot from BoardChanged while CreateItem awaits).
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(dbConnectionString));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        // Default is 1; set explicitly so simple passwords (e.g. repeated keys) are not blocked.
        options.Password.RequiredUniqueChars = 1;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddSignInManager()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<DemoUserOptions>(builder.Configuration.GetSection(DemoUserOptions.SectionName));
builder.Services.PostConfigure<DemoUserOptions>(static o =>
{
    var defaults = new DemoUserOptions();
    if (string.IsNullOrWhiteSpace(o.Email)) o.Email = defaults.Email;
    if (string.IsNullOrWhiteSpace(o.Password)) o.Password = defaults.Password;
});
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<GlobalTimerService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<DemoUserResolver>();
builder.Services.AddScoped<IRemoteBoardRefreshService, RemoteBoardRefreshService>();
builder.Services.AddSingleton<IBoardSyncStatus, NoOpBoardSyncStatus>();
builder.Services.AddScoped<BoardRemoteNotifyBridge>();
builder.Services.AddSingleton<IBoardChangeNotifier, BoardChangeNotifier>();
builder.Services.AddScoped<BoardPersistenceService>();
builder.Services.AddScoped<BoardIdempotencyService>();
builder.Services.Configure<BoardMaintenanceOptions>(
    builder.Configuration.GetSection(BoardMaintenanceOptions.SectionName));
builder.Services.Configure<DemoInitializationOptions>(
    builder.Configuration.GetSection(DemoInitializationOptions.SectionName));
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<DemoDataInitializationHostedService>();
}

builder.Services.AddHostedService<BoardMaintenanceHostedService>();
builder.Services.AddScoped<IBoardDataService, WebBoardDataService>();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddScoped<ActivityStatisticsService>();
builder.Services.AddScoped<IActivityStatisticsReader, WebActivityStatisticsReader>();
builder.Services.AddScoped<IUserActivityLogService, WebUserActivityLogService>();
builder.Services.AddScoped<INotificationSettingsService, WebNotificationSettingsService>();
builder.Services.AddScoped<IUserPreferencesService, WebUserPreferencesService>();
builder.Services.AddScoped<IUserNotifier, UserNotifier>();
builder.Services.AddScoped<IFocusTimerClientAlerts, FocusTimerClientAlerts>();
builder.Services.AddScoped<IDailyRetroPromptStore, JsDailyRetroPromptStore>();
builder.Services.AddScoped<IUserTimeZoneService, UserTimeZoneService>();
builder.Services.AddScoped<INotificationSettingsRules, NotificationSettingsRules>();
builder.Services.AddScoped<IUserDateFormatService, UserDateFormatService>();
builder.Services.AddScoped<IAccountActionsService, WebAccountActionsService>();
builder.Services.AddHttpClient();

var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    });

authBuilder.AddIdentityCookies();
authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey))
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (!string.IsNullOrEmpty(context.Token)) return Task.CompletedTask;

            var accessToken = context.Request.Query["access_token"];
            var path = context.Request.Path;
            if (path.StartsWithSegments("/hubs") && !string.IsNullOrEmpty(accessToken)) context.Token = accessToken;

            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("BoardOrJwt", policy =>
    {
        policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });

var app = builder.Build();

async Task TryRecoverDemoGuestAsync(ILogger logger, CancellationToken cancellationToken = default)
{
    try
    {
        await DemoDataSeeder.SeedAsync(app.Services, cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "On-demand demo data seeding failed.");
    }
}

if (app.Environment.IsEnvironment("Testing"))
{
    await DemoDataSeeder.SeedAsync(app.Services);
}

List<string> MapRegistrationErrorsToUserFacing(IEnumerable<IdentityError> errors) =>
    errors.Select(static e => e.Code switch
    {
        "DuplicateUserName" or "DuplicateEmail" => "This email is already registered.",
        "InvalidUserName" or "InvalidEmail" => "Enter a valid email address.",
        _ => (e.Description ?? "Registration could not be completed.").Replace(
            "Username", "Email", StringComparison.OrdinalIgnoreCase)
    }).ToList();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseExceptionHandler("/Error", true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();
    app.UseAntiforgery();
}

app.UseAuthentication();
app.UseAuthorization();

// Used by AppHost WithHttpHealthCheck; anonymous, no auth required.
app.MapGet("/health", () => Results.Text("OK", "text/plain"));
app.MapOpenApi();
app.MapStaticAssets();
app.MapRazorComponents<App.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapHub<BoardHub>("/hubs/board");

app.MapPost("/api/auth/register", async (
    RegisterRequest request,
    UserManager<ApplicationUser> userManager) =>
{
    if (!RegistrationEmailValidation.IsValid(request.Email))
        return Results.BadRequest(new List<string> { "Enter a valid email address." });

    var user = new ApplicationUser
    {
        UserName = request.Email,
        Email = request.Email
    };

    var result = await userManager.CreateAsync(user, request.Password);
    if (!result.Succeeded) return Results.BadRequest(MapRegistrationErrorsToUserFacing(result.Errors));

    return Results.Ok(new { message = "Registration successful." });
}).DisableAntiforgery();

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    JwtTokenService jwtTokenService) =>
{
    var user = await userManager.FindByEmailAsync(request.Email);
    if (user is null) return Results.Unauthorized();

    var loginResult = await signInManager.PasswordSignInAsync(
        user,
        request.Password,
        request.RememberMe,
        true);

    if (!loginResult.Succeeded) return Results.Unauthorized();

    var token = jwtTokenService.CreateToken(user);
    return Results.Ok(new LoginResponse(token, user.Email ?? string.Empty));
}).DisableAntiforgery();

/// <summary>Same demo guest as cookie guest-login, but returns a JWT for native/API clients (MAUI).</summary>
app.MapPost("/api/auth/guest-jwt", async (
    HttpContext httpContext,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    JwtTokenService jwtTokenService,
    IOptions<DemoUserOptions> demoOptions,
    ILogger<Program> logger) =>
{
    var email = demoOptions.Value.Email;
    var user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        await TryRecoverDemoGuestAsync(logger, httpContext.RequestAborted);
        user = await userManager.FindByEmailAsync(email);
    }

    if (user is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    var loginResult = await signInManager.PasswordSignInAsync(
        user,
        demoOptions.Value.Password,
        false,
        true);

    if (!loginResult.Succeeded) return Results.Unauthorized();

    var token = jwtTokenService.CreateToken(user);
    return Results.Ok(new LoginResponse(token, user.Email ?? string.Empty));
}).DisableAntiforgery();

app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return Results.Ok(new { message = "Logged out." });
    })
    .RequireAuthorization()
    .DisableAntiforgery();

// Full browser POST so Set-Cookie runs before the response is committed (works with Blazor; interactive components cannot set cookies after streaming starts).
app.MapPost("/api/auth/guest-login", async (
    HttpContext httpContext,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IOptions<DemoUserOptions> demoOptions,
    ILogger<Program> logger) =>
{
    var email = demoOptions.Value.Email;
    var guestUser = await userManager.FindByEmailAsync(email);
    if (guestUser is null)
    {
        await TryRecoverDemoGuestAsync(logger, httpContext.RequestAborted);
        guestUser = await userManager.FindByEmailAsync(email);
    }

    if (guestUser is null) return Results.LocalRedirect("/auth/login?guest=missing");

    await signInManager.SignInAsync(guestUser, true);
    return Results.LocalRedirect("/");
}).DisableAntiforgery();

app.MapPost("/api/auth/cookie-login", async (HttpContext httpContext, SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var email = form["Email"].ToString();
    var password = form["Password"].ToString();
    var rememberMe = form["RememberMe"] == "true";

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        return Results.LocalRedirect("/auth/login?error=1");

    var user = await userManager.FindByEmailAsync(email);
    if (user is null) return Results.LocalRedirect("/auth/login?error=1");

    var loginResult = await signInManager.PasswordSignInAsync(
        user,
        password,
        rememberMe,
        true);

    if (!loginResult.Succeeded) return Results.LocalRedirect("/auth/login?error=1");

    return Results.LocalRedirect("/");
}).DisableAntiforgery();

app.MapPost("/api/auth/cookie-logout", async (SignInManager<ApplicationUser> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/");
    })
    .RequireAuthorization()
    .DisableAntiforgery();

app.MapPost("/api/account/change-password", async (
    ClaimsPrincipal user,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ChangePasswordRequest body) =>
{
    if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

    var appUser = await userManager.FindByIdAsync(userId.ToString());
    if (appUser is null) return Results.NotFound();

    var result = await userManager.ChangePasswordAsync(appUser, body.CurrentPassword, body.NewPassword);
    if (!result.Succeeded) return Results.BadRequest();

    await signInManager.RefreshSignInAsync(appUser);
    return Results.NoContent();
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/api/account/delete", async (
    ClaimsPrincipal user,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) =>
{
    if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

    var appUser = await userManager.FindByIdAsync(userId.ToString());
    if (appUser is null) return Results.NotFound();

    var result = await userManager.DeleteAsync(appUser);
    if (!result.Succeeded) return Results.BadRequest();

    await signInManager.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/api/auth/register-form", async (HttpContext httpContext, UserManager<ApplicationUser> userManager) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var email = form["Email"].ToString();
    var password = form["Password"].ToString();

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        return Results.LocalRedirect("/auth/register?error=1");

    if (!RegistrationEmailValidation.IsValid(email))
        return Results.LocalRedirect("/auth/register?error=1");

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
}).DisableAntiforgery();

app.MapBoardApi();

var activityApi = app.MapGroup("/api/activity")
    .DisableAntiforgery()
    .RequireAuthorization("BoardOrJwt");

activityApi.MapGet("dashboard",
    async (ClaimsPrincipal user, ActivityStatisticsService stats, string? period, string? tag,
        CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        return Results.Ok(await stats.GetDashboardAsync(userId, period, tag, cancellationToken));
    });
activityApi.MapGet("daily-contributions",
    async (ClaimsPrincipal user, ActivityStatisticsService stats, string? period, string? tag,
        CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        return Results.Ok(await stats.GetDailyContributionsAsync(userId, period, tag, cancellationToken));
    });
activityApi.MapGet("day",
    async (ClaimsPrincipal user, ActivityStatisticsService stats, DateOnly date, string? tag,
        CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        return Results.Ok(await stats.GetActivityDayDetailAsync(userId, date, tag, cancellationToken));
    });

var settingsApi = app.MapGroup("/api/settings")
    .DisableAntiforgery()
    .RequireAuthorization("BoardOrJwt");

settingsApi.MapGet("/notifications",
    async (ClaimsPrincipal user, ApplicationDbContext db, CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null) return Results.NotFound();

        var settings = NotificationSettingsJson.DeserializeOrDefault(row.NotificationSettingsJson);
        return Results.Ok(settings);
    });

settingsApi.MapPut("/notifications",
    async (ClaimsPrincipal user, ApplicationDbContext db, IBoardChangeNotifier boardChangeNotifier,
        NotificationSettings body, CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null) return Results.NotFound();

        row.NotificationSettingsJson = NotificationSettingsJson.Serialize(body);
        await db.SaveChangesAsync(cancellationToken);
        await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return Results.NoContent();
    });

settingsApi.MapGet("/preferences",
    async (ClaimsPrincipal user, ApplicationDbContext db, CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        var row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null) return Results.NotFound();

        var settings = UserPreferencesJson.DeserializeOrDefault(row.UserPreferencesJson);
        return Results.Ok(settings);
    });

settingsApi.MapPut("/preferences",
    async (ClaimsPrincipal user, ApplicationDbContext db, IBoardChangeNotifier boardChangeNotifier,
        UserPreferences body, CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        var row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null) return Results.NotFound();

        row.UserPreferencesJson = UserPreferencesJson.Serialize(body);
        await db.SaveChangesAsync(cancellationToken);
        await boardChangeNotifier.NotifyBoardChangedAsync(userId, cancellationToken);
        return Results.NoContent();
    });

app.Run();

/// <summary>Enables <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> in integration tests.</summary>
public partial class Program { }
