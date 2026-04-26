using App.Web.Components;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Services;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, HttpContextAuthenticationStateProvider>();
builder.Services.AddMudServices();

string dbConnectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("habitinatordb")
    ?? throw new InvalidOperationException("No PostgreSQL connection string configured. Set ConnectionStrings:DefaultConnection or run through Aspire (habitinatordb).");

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
builder.Services.AddScoped<BoardPersistenceService>();
builder.Services.AddScoped<IBoardDataService, WebBoardDataService>();
builder.Services.AddScoped<ActivityStatisticsService>();
builder.Services.AddScoped<IActivityStatisticsReader, WebActivityStatisticsReader>();
builder.Services.AddScoped<IUserActivityLogService, WebUserActivityLogService>();
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
    JwtOptions jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
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
});
builder.Services.AddAuthorization();

var app = builder.Build();
try
{
    await DemoDataSeeder.SeedAsync(app.Services);
}
catch (NpgsqlException ex)
{
    ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Skipping startup demo data seeding because PostgreSQL is unreachable. Check ConnectionStrings:DefaultConnection and ensure PostgreSQL is running.");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<global::App.Web.Components.App>()
    .AddInteractiveServerRenderMode();

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

    IdentityResult result = await userManager.CreateAsync(user, request.Password);
    if (!result.Succeeded)
    {
        return Results.BadRequest(result.Errors.Select(x => x.Description));
    }

    return Results.Ok(new { message = "Registration successful." });
}).DisableAntiforgery();

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    JwtTokenService jwtTokenService) =>
{
    ApplicationUser? user = await userManager.FindByEmailAsync(request.Email);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    SignInResult loginResult = await signInManager.PasswordSignInAsync(
        user,
        request.Password,
        request.RememberMe,
        lockoutOnFailure: true);

    if (!loginResult.Succeeded)
    {
        return Results.Unauthorized();
    }

    string token = jwtTokenService.CreateToken(user);
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
    ApplicationUser? guestUser = await userManager.FindByEmailAsync(demoOptions.Value.Email);
    if (guestUser is null)
    {
        return Results.LocalRedirect("/auth/login?guest=missing");
    }

    await signInManager.SignInAsync(guestUser, isPersistent: true);
    return Results.LocalRedirect("/");
}).DisableAntiforgery();

app.MapPost("/api/auth/cookie-login", async (HttpContext httpContext, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager) =>
{
    IFormCollection form = await httpContext.Request.ReadFormAsync();
    string email = form["Email"].ToString();
    string password = form["Password"].ToString();
    bool rememberMe = form["RememberMe"] == "true";

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        return Results.LocalRedirect("/auth/login?error=1");
    }

    ApplicationUser? user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        return Results.LocalRedirect("/auth/login?error=1");
    }

    SignInResult loginResult = await signInManager.PasswordSignInAsync(
        user,
        password,
        rememberMe,
        lockoutOnFailure: true);

    if (!loginResult.Succeeded)
    {
        return Results.LocalRedirect("/auth/login?error=1");
    }

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
    IFormCollection form = await httpContext.Request.ReadFormAsync();
    string email = form["Email"].ToString();
    string password = form["Password"].ToString();
    string timezone = string.IsNullOrWhiteSpace(form["Timezone"].ToString()) ? "Europe/Budapest" : form["Timezone"].ToString()!;

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        return Results.LocalRedirect("/auth/register?error=1");
    }

    var user = new ApplicationUser
    {
        UserName = email,
        Email = email,
        Timezone = timezone
    };

    IdentityResult result = await userManager.CreateAsync(user, password);
    if (!result.Succeeded)
    {
        return Results.LocalRedirect("/auth/register?error=1");
    }

    return Results.LocalRedirect("/auth/login?registered=1");
}).DisableAntiforgery();

var boardApi = app.MapGroup("/api/board").DisableAntiforgery();
boardApi.MapGet("/", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService) =>
{
    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    BoardSnapshot snapshot = await boardPersistenceService.GetSnapshotAsync(userId);
    return Results.Ok(snapshot);
});
boardApi.MapPost("/{section}", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService, BoardSection section, ItemTitleRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.BadRequest("Title is required.");
    }

    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    BoardItem item = await boardPersistenceService.CreateItemAsync(userId, section, request.Title.Trim());
    return Results.Ok(item);
});
boardApi.MapPut("/{section}/{itemId:guid}", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService, BoardSection section, Guid itemId, ItemTitleRequest request) =>
{
    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    BoardItem? updated = await boardPersistenceService.RenameItemAsync(userId, section, itemId, request.Title.Trim());
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapDelete("/{section}/{itemId:guid}", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService, BoardSection section, Guid itemId) =>
{
    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    bool removed = await boardPersistenceService.DeleteItemAsync(userId, section, itemId);
    return removed ? Results.NoContent() : Results.NotFound();
});
boardApi.MapPost("/{section}/{itemId:guid}/toggle", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService, BoardSection section, Guid itemId) =>
{
    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    BoardItem? updated = await boardPersistenceService.ToggleItemAsync(userId, section, itemId);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapPost("/habits/{itemId:guid}/increment", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService, Guid itemId) =>
{
    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    BoardItem? updated = await boardPersistenceService.IncrementHabitPlusAsync(userId, itemId);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapPost("/habits/{itemId:guid}/decrement", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService, Guid itemId) =>
{
    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    BoardItem? updated = await boardPersistenceService.IncrementHabitMinusAsync(userId, itemId);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapPut("/habits/{itemId:guid}", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService, Guid itemId, HabitUpdateRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.BadRequest("Title is required.");
    }

    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    BoardItem? updated = await boardPersistenceService.UpdateHabitAsync(
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
boardApi.MapPut("/todos/{itemId:guid}", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService, Guid itemId, TodoUpdateRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.BadRequest("Title is required.");
    }

    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    BoardItem? updated = await boardPersistenceService.UpdateTodoAsync(
        userId,
        itemId,
        request.Title.Trim(),
        request.Notes,
        request.Tags,
        request.ChecklistJson,
        request.DueDate);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
boardApi.MapPut("/dailies/{itemId:guid}", async (HttpContext httpContext, DemoUserResolver demoUserResolver, BoardPersistenceService boardPersistenceService, Guid itemId, DailyUpdateRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.BadRequest("Title is required.");
    }

    Guid userId = await demoUserResolver.ResolveUserIdAsync(httpContext.User);
    BoardItem? updated = await boardPersistenceService.UpdateDailyAsync(
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

app.Run();
