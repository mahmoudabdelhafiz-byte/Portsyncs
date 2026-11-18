using Microsoft.EntityFrameworkCore;
using PortSyncs.Api.Database;
using PortSyncs.Api.Domain;
using PortSyncs.Api.Infrastructure;
using PortSyncs.Api.Infrastructure.Maps;
using PortSyncs.Api.Modules.Fleet;

var builder = WebApplication.CreateBuilder(args);

// All settings come from environment variables (e.g. Storage__Provider=Local),
// so the same image runs on the VPS (H1) and in the cloud (H2/H3).
builder.Services.AddPortSyncsInfrastructure(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrWhiteSpace(connectionString))
    builder.Services.AddDbContext<PortSyncsDbContext>(o => o.UseNpgsql(connectionString));

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(connectionString) && app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await MigrationRunner.RunAsync(connectionString, app.Logger);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "portsyncs-api" }));

if (!string.IsNullOrWhiteSpace(connectionString))
{
    app.MapGet("/health/db", async (PortSyncsDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { database = "ok" })
            : Results.Problem("Database unreachable", statusCode: 503));
}

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

// Business endpoints have no login yet (T20). Never enable this flag on a server with real data.
if (app.Configuration.GetValue("Features:DevEndpoints", false))
{
    app.Logger.LogWarning("Development endpoints are enabled without authentication.");
    app.MapFleet();
}

app.Run();

public partial class Program { }
