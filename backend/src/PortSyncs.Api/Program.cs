using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using PortSyncs.Api.Database;
using PortSyncs.Api.Domain;
using PortSyncs.Api.Infrastructure;
using PortSyncs.Api.Infrastructure.Maps;
using PortSyncs.Api.Infrastructure.Sms;
using PortSyncs.Api.Modules.Auth;
using PortSyncs.Api.Modules.Fleet;

var builder = WebApplication.CreateBuilder(args);

// All settings come from environment variables (e.g. Storage__Provider=Local),
// so the same image runs on the VPS (H1) and in the cloud (H2/H3).
builder.Services.AddPortSyncsInfrastructure(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings__Postgres is required.");
builder.Services.AddDbContext<PortSyncsDbContext>(o => o.UseNpgsql(connectionString));

// --- Login (T20) -----------------------------------------------------------
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
authOptions.SigningKeyBytes(); // fail fast at startup if the key is missing or too short
var tokenService = new TokenService(authOptions);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(tokenService);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep "sub" as "sub"
        o.TokenValidationParameters = tokenService.ValidationParameters();
    });
builder.Services.AddAuthorization();

var smsProvider = builder.Configuration["Sms:Provider"] ?? "Console";
builder.Services.AddSingleton<ISmsSender>(sp => smsProvider switch
{
    "Console" => ActivatorUtilities.CreateInstance<ConsoleSmsSender>(sp),
    _ => throw new InvalidOperationException($"Unknown Sms__Provider '{smsProvider}'."),
});

// Brute-force protection on login endpoints, per client IP (on top of per-phone limits).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// The API sits behind Nginx on the same host (port bound to 127.0.0.1 only),
// so the client IP comes from X-Forwarded-For.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
#pragma warning disable CS0618 // property renamed in newer frameworks; clearing it is still required
    o.KnownNetworks.Clear();
#pragma warning restore CS0618
    o.KnownProxies.Clear();
});

var app = builder.Build();

if (smsProvider == "Console" && app.Environment.IsProduction())
    app.Logger.LogWarning("Sms__Provider=Console: login codes are written to the log, not sent. Do not use with real users.");

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await MigrationRunner.RunAsync(connectionString, app.Logger);

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "portsyncs-api" }));
app.MapGet("/health/db", async (PortSyncsDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { database = "ok" })
        : Results.Problem("Database unreachable", statusCode: 503));

// Small utility endpoints used by the apps and portals.
var tools = app.MapGroup("/api/v1/tools");

tools.MapGet("/container/{number}/validate", (string number) =>
    ContainerNumber.TryParse(number, out var parsed)
        ? Results.Ok(new { valid = true, number = parsed.Value })
        : Results.Ok(new { valid = false, number }));

tools.MapGet("/plate/normalize", (string country, string plate) =>
    Results.Ok(new { country = country.ToUpperInvariant(), normalized = Plate.Normalize(plate) }));

tools.MapGet("/navigate", (double lat, double lon, NavigationApp target, IMapProvider maps) =>
    Results.Ok(new { url = maps.BuildNavigationLink(lat, lon, target).ToString() }));

app.MapAuth();
app.MapFleet();

app.Run();

public partial class Program { }
