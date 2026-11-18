using PortSyncs.Api.Domain;
using PortSyncs.Api.Infrastructure;
using PortSyncs.Api.Infrastructure.Maps;

var builder = WebApplication.CreateBuilder(args);

// All settings come from environment variables (e.g. Storage__Provider=Local),
// so the same image runs on the VPS (H1) and in the cloud (H2/H3).
builder.Services.AddPortSyncsInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "portsyncs-api" }));

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

app.Run();

public partial class Program { }
