using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Hubs;
using App.Web.Middleware;
using App.Web.Models;
using App.Web.Services;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using MudBlazor;
using MudBlazor.Services;

const string TestingEnvironment = "Testing";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
    config.SnackbarConfiguration.ShowTransitionDuration = 250;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.NewestOnTop = true;
});

var dbConnectionString = PostgresResilienceConnectionString.EnsureColdStartTimeouts(
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("habitinatordb")
    ?? throw new InvalidOperationException(
        "No PostgreSQL connection string configured. Set ConnectionStrings:DefaultConnection or run through Aspire (habitinatordb)."));

// Options must be singleton so IDbContextFactory (singleton) can be constructed; the DbContext
// instance remains scoped. Same pattern: https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory-eg-for-blazor
builder.Services.AddDbContext<ApplicationDbContext>(
    options => options.UseNpgsqlWithResilience(dbConnectionString),
    contextLifetime: ServiceLifetime.Scoped,
    optionsLifetime: ServiceLifetime.Singleton);
// Isolates read queries from the scoped context so Blazor + SignalR cannot interleave
// two operations on the same instance (e.g. GetSnapshot from BoardChanged while CreateItem awaits).
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsqlWithResilience(dbConnectionString));

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
builder.Services.Configure<SitePublicOptions>(builder.Configuration.GetSection(SitePublicOptions.SectionName));
builder.Services.Configure<DemoUserOptions>(builder.Configuration.GetSection(DemoUserOptions.SectionName));
builder.Services.PostConfigure<DemoUserOptions>(static o =>
{
    var defaults = new DemoUserOptions();
    if (string.IsNullOrWhiteSpace(o.Email))
    {
        o.Email = defaults.Email;
    }

    if (string.IsNullOrWhiteSpace(o.Password))
    {
        o.Password = defaults.Password;
    }
});
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<GlobalTimerService>();
builder.Services.AddScoped<ITimerSessionLogService, TimerSessionLogService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<DemoUserResolver>();
builder.Services.AddScoped<IRemoteBoardRefreshService, RemoteBoardRefreshService>();
builder.Services.AddSingleton<IBoardSyncStatus, NoOpBoardSyncStatus>();
builder.Services.AddSingleton<ConflictResolutionService>();
builder.Services.AddScoped<BoardRemoteNotifyBridge>();
builder.Services.AddSingleton<BoardSnapshotCache>();
builder.Services.AddSingleton<ActivityStatisticsCache>();
builder.Services.AddSingleton<IBoardChangeNotifier, BoardChangeNotifier>();
builder.Services.AddScoped<BoardPersistenceService>();
builder.Services.AddScoped<IInitialBoardLoadGate, InitialBoardLoadGate>();
builder.Services.AddScoped<BoardIdempotencyService>();
builder.Services.Configure<BoardMaintenanceOptions>(
    builder.Configuration.GetSection(BoardMaintenanceOptions.SectionName));
builder.Services.Configure<DemoInitializationOptions>(
    builder.Configuration.GetSection(DemoInitializationOptions.SectionName));
if (!builder.Environment.IsEnvironment(TestingEnvironment))
{
    builder.Services.AddHostedService<DemoDataInitializationHostedService>();
}

builder.Services.AddHostedService<BoardMaintenanceHostedService>();
builder.Services.AddScoped<IUndoService, UndoService>();
builder.Services.AddScoped<WebBoardDataService>();
builder.Services.AddScoped<IBoardDataService>(sp =>
{
    var inner = sp.GetRequiredService<WebBoardDataService>();
    var undoService = sp.GetRequiredService<IUndoService>();
    return new UndoableBoardDataService(inner, undoService);
});
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
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        ResponseCompressionMimeTypes);
});

builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Optimal;
});

builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Optimal;
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new { detail = "Too many requests. Please try again later." }, token);
    };

    options.AddPolicy("auth", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromSeconds(10),
            QueueLimit = 0
        });
    });

    options.AddPolicy("api", context =>
    {
        var key = AuthenticatedUserId.TryGet(context.User)?.ToString()
                  ?? context.Connection.RemoteIpAddress?.ToString()
                  ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 2
        });
    });
});

var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    });

authBuilder.AddIdentityCookies();

static void ConfigureAuthCookie(CookieAuthenticationOptions options, IWebHostEnvironment environment)
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
}

builder.Services.ConfigureApplicationCookie(o => ConfigureAuthCookie(o, builder.Environment));
builder.Services.ConfigureExternalCookie(o => ConfigureAuthCookie(o, builder.Environment));

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
            if (!string.IsNullOrEmpty(context.Token))
            {
                return Task.CompletedTask;
            }

            var accessToken = context.Request.Query["access_token"];
            var path = context.Request.Path;
            if (path.StartsWithSegments("/hubs") && !string.IsNullOrEmpty(accessToken))
            {
                context.Token = accessToken;
            }

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

if (app.Environment.IsEnvironment(TestingEnvironment))
{
    await DemoDataSeeder.SeedAsync(app.Services);
}

app.ConfigurePipeline();

app.MapBoardApi();
app.MapAuthApi();
app.MapActivityApi();
app.MapSettingsApi();


await app.RunAsync();

/// <summary>Enables <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> in integration tests.</summary>
public partial class Program
{
    protected Program() { }

    internal static readonly string[] ResponseCompressionMimeTypes = ["application/octet-stream", "application/wasm"];
}
