using System.Security.Claims;
using System.Text;

using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Hubs;
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

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(dbConnectionString));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddSignInManager()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<DemoUserOptions>(builder.Configuration.GetSection(DemoUserOptions.SectionName));
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<GlobalTimerService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<DemoUserResolver>();
builder.Services.AddScoped<IRemoteBoardRefreshService, RemoteBoardRefreshService>();
builder.Services.AddScoped<BoardRemoteNotifyBridge>();
builder.Services.AddSingleton<IBoardChangeNotifier, BoardChangeNotifier>();
builder.Services.AddScoped<BoardPersistenceService>();
builder.Services.AddScoped<IBoardDataService, WebBoardDataService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<ActivityStatisticsService>();
builder.Services.AddScoped<IActivityStatisticsReader, WebActivityStatisticsReader>();
builder.Services.AddScoped<IUserActivityLogService, WebUserActivityLogService>();
builder.Services.AddScoped<INotificationSettingsService, WebNotificationSettingsService>();
builder.Services.AddScoped<IUserNotifier, UserNotifier>();
builder.Services.AddScoped<IFocusTimerClientAlerts, FocusTimerClientAlerts>();
builder.Services.AddScoped<IDailyRetroPromptStore, JsDailyRetroPromptStore>();
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
try
{
    await DemoDataSeeder.SeedAsync(app.Services);
}
catch (NpgsqlException ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex,
        "Skipping startup demo data seeding because PostgreSQL is unreachable. Check ConnectionStrings:DefaultConnection and ensure PostgreSQL is running.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapHub<BoardHub>("/hubs/board");

app.MapPost("/api/auth/register", async (
    RegisterRequest request,
    UserManager<ApplicationUser> userManager) =>
{
    var user = new ApplicationUser
    {
        UserName = request.Email,
        Email = request.Email,
        Timezone = request.Timezone
    };

    var result = await userManager.CreateAsync(user, request.Password);
    if (!result.Succeeded) return Results.BadRequest(result.Errors.Select(x => x.Description));

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
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    JwtTokenService jwtTokenService,
    IOptions<DemoUserOptions> demoOptions) =>
{
    var email = demoOptions.Value.Email;
    var user = await userManager.FindByEmailAsync(email);
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
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IOptions<DemoUserOptions> demoOptions) =>
{
    var guestUser = await userManager.FindByEmailAsync(demoOptions.Value.Email);
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

app.MapPost("/api/auth/register-form", async (HttpContext httpContext, UserManager<ApplicationUser> userManager) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var email = form["Email"].ToString();
    var password = form["Password"].ToString();
    var timezone = string.IsNullOrWhiteSpace(form["Timezone"].ToString())
        ? "Europe/Budapest"
        : form["Timezone"].ToString()!;

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        return Results.LocalRedirect("/auth/register?error=1");

    var user = new ApplicationUser
    {
        UserName = email,
        Email = email,
        Timezone = timezone
    };

    var result = await userManager.CreateAsync(user, password);
    if (!result.Succeeded) return Results.LocalRedirect("/auth/register?error=1");

    return Results.LocalRedirect("/auth/login?registered=1");
}).DisableAntiforgery();

var boardApi = app.MapGroup("/api/board")
    .DisableAntiforgery()
    .RequireAuthorization("BoardOrJwt");

boardApi.MapGet("/", async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService) =>
{
    if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

    var snapshot = await boardPersistenceService.GetSnapshotAsync(userId);
    return Results.Ok(snapshot);
});
boardApi.MapPost("/{section}",
    async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService, BoardSection section,
        ItemTitleRequest request) =>
    {
        if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest("Title is required.");

        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        var item = await boardPersistenceService.CreateItemAsync(userId, section, request.Title.Trim());
        return Results.Ok(item);
    });
boardApi.MapPut("/{section}/{itemId:guid}", async (ClaimsPrincipal user,
    BoardPersistenceService boardPersistenceService, BoardSection section, Guid itemId, ItemTitleRequest request) =>
{
    if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

    var updated = await boardPersistenceService.RenameItemAsync(userId, section, itemId, request.Title.Trim());
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapDelete("/{section}/{itemId:guid}",
    async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService, BoardSection section, Guid itemId) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        var removed = await boardPersistenceService.DeleteItemAsync(userId, section, itemId);
        return removed ? Results.NoContent() : Results.NotFound();
    });
boardApi.MapPost("/{section}/{itemId:guid}/toggle", async (ClaimsPrincipal user,
    BoardPersistenceService boardPersistenceService, BoardSection section, Guid itemId) =>
{
    if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

    var updated = await boardPersistenceService.ToggleItemAsync(userId, section, itemId);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapPost("/habits/{itemId:guid}/increment",
    async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService, Guid itemId) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        var updated = await boardPersistenceService.IncrementHabitPlusAsync(userId, itemId);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    });
boardApi.MapPost("/habits/{itemId:guid}/decrement",
    async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService, Guid itemId) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        var updated = await boardPersistenceService.IncrementHabitMinusAsync(userId, itemId);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    });
boardApi.MapPut("/habits/{itemId:guid}", async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService,
    Guid itemId, HabitUpdateRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest("Title is required.");

    if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

    var updated = await boardPersistenceService.UpdateHabitAsync(
        userId,
        itemId,
        request.Title.Trim(),
        request.Notes,
        request.Tags,
        request.TrackPlus,
        request.TrackMinus,
        request.ResetPeriod,
        request.Counter,
        request.NegativeCounter,
        request.ChecklistJson);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapPut("/todos/{itemId:guid}", async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService,
    Guid itemId, TodoUpdateRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest("Title is required.");

    if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

    var updated = await boardPersistenceService.UpdateTodoAsync(
        userId,
        itemId,
        request.Title.Trim(),
        request.Notes,
        request.Tags,
        request.ChecklistJson,
        request.DueDate);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapPut("/dailies/{itemId:guid}", async (ClaimsPrincipal user, BoardPersistenceService boardPersistenceService,
    Guid itemId, DailyUpdateRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest("Title is required.");

    if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

    var updated = await boardPersistenceService.UpdateDailyAsync(
        userId,
        itemId,
        request.Title.Trim(),
        request.Notes,
        request.Tags,
        request.StartDate,
        request.Repeat,
        request.RepeatInterval,
        request.ChecklistJson,
        request.Streak);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapPost("/dailies/{itemId:guid}/complete-for-date", async (ClaimsPrincipal user,
    BoardPersistenceService boardPersistenceService, Guid itemId, DailyCompleteForDateRequest request) =>
{
    if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

    var updated = await boardPersistenceService.CompleteDailyForDateAsync(userId, itemId, request.CompletedOn);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

var activityApi = app.MapGroup("/api/activity")
    .DisableAntiforgery()
    .RequireAuthorization("BoardOrJwt");

activityApi.MapGet("dashboard",
    async (ClaimsPrincipal user, ActivityStatisticsService stats, string? period,
        CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        return Results.Ok(await stats.GetDashboardAsync(userId, period, cancellationToken));
    });
activityApi.MapGet("daily-contributions",
    async (ClaimsPrincipal user, ActivityStatisticsService stats, string? period,
        CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        return Results.Ok(await stats.GetDailyContributionsAsync(userId, period, cancellationToken));
    });
activityApi.MapGet("day",
    async (ClaimsPrincipal user, ActivityStatisticsService stats, DateOnly date, CancellationToken cancellationToken) =>
    {
        if (AuthenticatedUserId.TryGet(user) is not { } userId) return Results.Unauthorized();

        return Results.Ok(await stats.GetActivityDayDetailAsync(userId, date, cancellationToken));
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

app.Run();
