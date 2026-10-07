using System.Threading.RateLimiting;

using App.Web;
using App.Web.Auth;
using App.Web.DependencyInjection;
using App.Web.Services;

using Microsoft.AspNetCore.Components.Authorization;

using MudBlazor;
using MudBlazor.Services;

using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddMemoryCache();
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
    config.SnackbarConfiguration.ShowTransitionDuration = 100;
    config.SnackbarConfiguration.HideTransitionDuration = 100;
    config.SnackbarConfiguration.NewestOnTop = true;
});

builder.Services.AddWebOptions(builder.Configuration, builder.Environment);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddWebAuthenticationAndAuthorization(builder.Configuration, builder.Environment);
builder.Services.AddApplicationServices(builder.Environment);
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument();

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
Program.ConfigureTelemetry(builder.Services, otlpEndpoint);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.ZstandardCompressionProvider>();
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

Program.ConfigureRateLimiting(builder.Services, builder.Environment);
var app = builder.Build();

if (app.Environment.IsEnvironment(AppEnvironment.Testing))
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

    internal static void ConfigureTelemetry(IServiceCollection services, string? otlpEndpoint)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("habitinator-web"))
            .WithTracing(tracing => ConfigureTracing(tracing, otlpEndpoint))
            .WithMetrics(metrics => ConfigureMetrics(metrics, otlpEndpoint))
            .WithLogging(logging =>
            {
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    logging.AddOtlpExporter();
                }
            });
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing, string? otlpEndpoint)
    {
        tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource(App.Web.Services.AppTelemetry.SourceName);
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter();
        }
    }

    private static void ConfigureMetrics(MeterProviderBuilder metrics, string? otlpEndpoint)
    {
        metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter(App.Web.Services.AppTelemetry.MeterName);
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter();
        }
    }

    internal static void ConfigureRateLimiting(IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new { detail = "Too many requests. Please try again later." }, token);
            };

            options.AddPolicy("auth", context => CreateAuthPolicy(context, environment));
            options.AddPolicy("api", context => CreateApiPolicy(context, environment));
        });
    }

    private static RateLimitPartition<string> CreateAuthPolicy(HttpContext context, IWebHostEnvironment environment)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        var limit = IsDevOrTest(environment) ? 100 : 20;
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromSeconds(10),
            QueueLimit = 0
        });
    }

    private static RateLimitPartition<string> CreateApiPolicy(HttpContext context, IWebHostEnvironment environment)
    {
        var key = AuthenticatedUserId.TryGet(context.User)?.ToString()
                  ?? context.Connection.RemoteIpAddress?.ToString()
                  ?? "unknown";

        var isDevOrTest = IsDevOrTest(environment);
        var limit = isDevOrTest ? 1000 : 300;
        var queueLimit = isDevOrTest ? 50 : 10;

        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = queueLimit
        });
    }

    private static bool IsDevOrTest(IWebHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment(AppEnvironment.Testing);
}
