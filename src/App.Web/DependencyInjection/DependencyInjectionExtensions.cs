using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web.Auth;
using App.Web.Data;
using App.Web.Hubs;
using App.Web.Middleware;
using App.Web.Models;
using App.Web.Services;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

using Npgsql;

namespace App.Web.DependencyInjection;

public static class DependencyInjectionExtensions
{
    private const string TestingEnvironment = "Testing";

    public static IServiceCollection AddWebOptions(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options =>
            {
                if (!environment.IsDevelopment())
                {
                    var key = options.SigningKey;
                    if (string.Equals(key, "replace-with-long-random-key-change-in-production", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "replace-this-with-a-long-random-64-char-minimum-key", StringComparison.OrdinalIgnoreCase) ||
                        key.Contains("replace-", StringComparison.OrdinalIgnoreCase))
                    {
                        return false; // Validation fails
                    }
                }
                return true;
            }, "JWT SigningKey must be changed in non-development environments.")
            .ValidateOnStart();

        services.AddOptions<SitePublicOptions>()
            .BindConfiguration(SitePublicOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DemoUserOptions>()
            .BindConfiguration(DemoUserOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.PostConfigure<DemoUserOptions>(static o =>
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

        services.AddOptions<BoardMaintenanceOptions>()
            .BindConfiguration(BoardMaintenanceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DemoInitializationOptions>()
            .BindConfiguration(DemoInitializationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var dbConnectionString = PostgresResilienceConnectionString.EnsureColdStartTimeouts(
            configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetConnectionString("habitinatordb")
            ?? throw new InvalidOperationException(
                "No PostgreSQL connection string configured. Set ConnectionStrings:DefaultConnection or run through Aspire (habitinatordb)."));

        services.AddNpgsqlDataSource(dbConnectionString, builder =>
        {
            builder.EnableDynamicJson();
        });

        services.AddDbContext<ApplicationDbContext>(
            (sp, options) =>
            {
                var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
                options.UseNpgsql(dataSource, PostgresDbContextOptions.ConfigureNpgsqlResilience);
            },
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddDbContextFactory<ApplicationDbContext>(
            (sp, options) =>
            {
                var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
                options.UseNpgsql(dataSource, PostgresDbContextOptions.ConfigureNpgsqlResilience);
            });

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddScoped<JwtTokenService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<GlobalTimerService>();
        services.AddScoped<ITimerSessionLogService, TimerSessionLogService>();
        services.AddHttpContextAccessor();
        services.AddScoped<DemoUserResolver>();
        services.AddScoped<IRemoteBoardRefreshService, RemoteBoardRefreshService>();
        services.AddSingleton<IBoardSyncStatus, NoOpBoardSyncStatus>();
        services.AddSingleton<ConflictResolutionService>();
        services.AddScoped<BoardRemoteNotifyBridge>();
        services.AddSingleton<BoardSnapshotCache>();
        services.AddSingleton<ActivityStatisticsCache>();
        services.AddSingleton<IBoardChangeNotifier, BoardChangeNotifier>();
        services.AddScoped<BoardPersistenceService>();
        services.AddScoped<IInitialBoardLoadGate, InitialBoardLoadGate>();
        services.AddScoped<BoardIdempotencyService>();

        if (!environment.IsEnvironment(TestingEnvironment))
        {
            services.AddHostedService<DemoDataInitializationHostedService>();
        }

        services.AddHostedService<BoardMaintenanceHostedService>();
        services.AddScoped<IUndoService, UndoService>();
        services.AddScoped<WebBoardDataService>();
        services.AddScoped<IBoardDataService>(sp =>
        {
            var inner = sp.GetRequiredService<WebBoardDataService>();
            var undoService = sp.GetRequiredService<IUndoService>();
            return new UndoableBoardDataService(inner, undoService);
        });

        services.AddScoped<ActivityStatisticsService>();
        services.AddScoped<IActivityStatisticsReader, WebActivityStatisticsReader>();
        services.AddScoped<IUserActivityLogService, WebUserActivityLogService>();
        services.AddScoped<INotificationSettingsService, WebNotificationSettingsService>();
        services.AddScoped<IUserPreferencesService, WebUserPreferencesService>();
        services.AddScoped<IUserNotifier, UserNotifier>();
        services.AddScoped<IFocusTimerClientAlerts, FocusTimerClientAlerts>();
        services.AddScoped<IDailyRetroPromptStore, JsDailyRetroPromptStore>();
        services.AddScoped<IUserTimeZoneService, UserTimeZoneService>();
        services.AddScoped<INotificationSettingsRules, NotificationSettingsRules>();
        services.AddScoped<IUserDateFormatService, UserDateFormatService>();
        services.AddScoped<IAccountActionsService, WebAccountActionsService>();
        services.AddHttpClient();
        services.AddValidation();

        return services;
    }

    public static IServiceCollection AddWebAuthenticationAndAuthorization(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddSignInManager()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        });

        authBuilder.AddIdentityCookies();

        services.ConfigureApplicationCookie(o => ConfigureAuthCookie(o, environment));
        services.ConfigureExternalCookie(o => ConfigureAuthCookie(o, environment));

        authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
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

        services.AddAuthorizationBuilder()
            .AddPolicy("BoardOrJwt", policy =>
            {
                policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });

        return services;
    }

    private static void ConfigureAuthCookie(CookieAuthenticationOptions options, IWebHostEnvironment environment)
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    }
}
