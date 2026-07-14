using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

using App.Shared.RCL;
using App.Shared.RCL.Models;
using App.Shared.RCL.Services;
using App.Web;
using App.Web.Auth;
using App.Web.Data;
using App.Web.DependencyInjection;
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

builder.Services.AddWebOptions(builder.Configuration, builder.Environment);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddWebAuthenticationAndAuthorization(builder.Configuration, builder.Environment);
builder.Services.AddApplicationServices(builder.Environment);
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument();
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
        var isDevOrTest = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment(TestingEnvironment);
        var limit = isDevOrTest ? 100 : 20;
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromSeconds(10),
            QueueLimit = 0
        });
    });

    options.AddPolicy("api", context =>
    {
        var key = AuthenticatedUserId.TryGet(context.User)?.ToString()
                  ?? context.Connection.RemoteIpAddress?.ToString()
                  ?? "unknown";

        var isDevOrTest = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment(TestingEnvironment);
        var limit = isDevOrTest ? 1000 : 300;
        var queueLimit = isDevOrTest ? 50 : 10;

        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = queueLimit
        });
    });
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
