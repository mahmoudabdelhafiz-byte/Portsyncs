using Microsoft.EntityFrameworkCore;
using PortSyncs.Api.Database;
using PortSyncs.Api.Domain;
using PortSyncs.Api.Modules.Auth;

namespace PortSyncs.Api.Modules.Depots;

/// <summary>
/// Trucking-company side of depot visits (T52): find depots, pre-announce trucks and containers,
/// and see the gate log of the company's own trucks.
/// </summary>
public static class CompanyVisitsEndpoints
{
    public static void MapCompanyVisits(this IEndpointRouteBuilder app)
    {
        // Public directory of facilities for signed-in users (no operator details).
        app.MapGet("/api/v1/facilities", Directory).WithTags("Depots").RequireAuthorization();

        var company = app.MapGroup("/api/v1/companies/{orgId:guid}").WithTags("Depots")
            .RequireOrgRole(Roles.CompanyAdmin, Roles.Dispatcher);
        company.MapPost("/expected-visits", Announce);
        company.MapGet("/expected-visits", ListAnnounced);
        company.MapPost("/expected-visits/{id:guid}/cancel", Cancel);
        company.MapGet("/gate-log", GateLog);
    }

    public sealed record AnnounceRequest(
        Guid FacilityId, Guid? TruckId, string? ContainerNumber, string Purpose, DateTimeOffset WindowFrom, DateTimeOffset WindowTo);

    private static async Task<IResult> Directory(string? country, string? type, string? q, PortSyncsDbContext db, CancellationToken ct)
    {
        var query = db.Facilities.AsNoTracking().Where(f => f.Status == "active");
        if (!string.IsNullOrWhiteSpace(country)) query = query.Where(f => f.Country == country.ToUpper());
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(f => f.Type == type);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim().Replace("%", "").Replace("_", "")}%";
            query = query.Where(f => EF.Functions.ILike(f.Name, pattern) || (f.City != null && EF.Functions.ILike(f.City, pattern)));
        }

        var list = await query.OrderBy(f => f.Name).Take(100)
            .Select(f => new { f.Id, f.Type, f.Name, f.Country, f.City, f.Lat, f.Lon, onPortSyncs = f.OperatorOrgId != null })
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> Announce(Guid orgId, AnnounceRequest req, HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();

        var errors = new Dictionary<string, string[]>();
        if (!VisitPurposes.All.Contains(req.Purpose)) errors["purpose"] = [$"One of: {string.Join(", ", VisitPurposes.All)}."];
        if (req.WindowTo <= req.WindowFrom) errors["windowTo"] = ["Must be after windowFrom."];
        if (req.WindowTo - req.WindowFrom > TimeSpan.FromDays(7)) errors["windowTo"] = ["Window can be at most 7 days."];
        if (req.WindowTo < DateTimeOffset.UtcNow) errors["windowTo"] = ["Window is already over."];
        if (req.TruckId is null && string.IsNullOrWhiteSpace(req.ContainerNumber)) errors["truckId"] = ["Give a truck, a container number, or both."];

        string? container = null;
        if (!string.IsNullOrWhiteSpace(req.ContainerNumber))
        {
            if (ContainerNumber.TryParse(req.ContainerNumber, out var parsed)) container = parsed.Value;
            else errors["containerNumber"] = ["Not a valid container number (check digit)."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var facility = await db.Facilities.AsNoTracking().FirstOrDefaultAsync(f => f.Id == req.FacilityId && f.Status == "active", ct);
        if (facility is null) return Results.NotFound(new { error = "facility_not_found" });

        if (req.TruckId is { } truckId &&
            !await db.Trucks.AnyAsync(t => t.Id == truckId && t.OrganizationId == orgId && t.Status == "active", ct))
            return Results.NotFound(new { error = "truck_not_found" });

        var visit = new ExpectedVisit
        {
            FacilityId = facility.Id,
            TruckingOrgId = orgId,
            TruckId = req.TruckId,
            ContainerNumber = container,
            Purpose = req.Purpose,
            WindowFrom = req.WindowFrom,
            WindowTo = req.WindowTo,
            CreatedBy = userId,
        };
        db.ExpectedVisits.Add(visit);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/companies/{orgId}/expected-visits/{visit.Id}", new
        {
            visit.Id, facility = facility.Name, facilityOnPortSyncs = facility.OperatorOrgId is not null,
            visit.TruckId, visit.ContainerNumber, visit.Purpose, visit.WindowFrom, visit.WindowTo, visit.Status,
        });
    }

    private static async Task<IResult> ListAnnounced(Guid orgId, PortSyncsDbContext db, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-2);
        var list = await (
                from e in db.ExpectedVisits
                join f in db.Facilities on e.FacilityId equals f.Id
                join t in db.Trucks on e.TruckId equals t.Id into tj
                from t in tj.DefaultIfEmpty()
                where e.TruckingOrgId == orgId && e.WindowTo > since
                orderby e.WindowFrom
                select new
                {
                    e.Id, facility = f.Name, plate = t != null ? t.PlateDisplay : null,
                    e.ContainerNumber, e.Purpose, e.WindowFrom, e.WindowTo, e.Status,
                })
            .Take(500)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> Cancel(Guid orgId, Guid id, PortSyncsDbContext db, CancellationToken ct)
    {
        var visit = await db.ExpectedVisits.FirstOrDefaultAsync(e => e.Id == id && e.TruckingOrgId == orgId, ct);
        if (visit is null) return Results.NotFound();
        if (visit.Status != "expected") return Results.Conflict(new { error = "not_cancellable", visit.Status });

        visit.Status = "cancelled";
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Gate-in and gate-out of the company's own trucks at any facility, with time inside.</summary>
    private static async Task<IResult> GateLog(Guid orgId, DateTimeOffset? from, PortSyncsDbContext db, CancellationToken ct)
    {
        var since = from ?? DateTimeOffset.UtcNow.AddDays(-7);
        var rows = await (
                from v in db.FacilityVisits
                join f in db.Facilities on v.FacilityId equals f.Id
                where v.TruckingOrgId == orgId && v.GateInAt >= since
                orderby v.GateInAt descending
                select new { v, facility = f.Name })
            .Take(1000)
            .ToListAsync(ct);

        return Results.Ok(rows.Select(r => new
        {
            r.v.Id, r.facility, plate = r.v.PlateNormalized, r.v.ContainerIn, r.v.ContainerOut,
            r.v.GateInAt, r.v.GateOutAt,
            minutesInside = r.v.GateOutAt is { } outAt ? (int?)(outAt - r.v.GateInAt).TotalMinutes : null,
        }));
    }
}
