using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PortSyncs.Api.Database;
using PortSyncs.Api.Domain;
using PortSyncs.Api.Events;
using PortSyncs.Api.Infrastructure.Messaging;
using PortSyncs.Api.Modules.Auth;

namespace PortSyncs.Api.Modules.Depots;

/// <summary>
/// Depot side of the MVP: depot sign-up, facilities, truck lookup by plate or QR (T30)
/// and the gate log with pre-announced visits (T52).
/// Privacy (T16): a depot sees only what it needs at its gate — the company name, the plate and
/// the driver's first name — never phone numbers or GPS. Every lookup is written to the audit log.
/// </summary>
public static class DepotEndpoints
{
    public static void MapDepots(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/depots", SignUp).WithTags("Depots").RequireAuthorization();

        var depot = app.MapGroup("/api/v1/depots/{orgId:guid}").WithTags("Depots")
            .RequireOrgRole(Roles.DepotAdmin, Roles.GateOfficer);

        depot.MapGet("/facilities", ListFacilities);
        depot.MapPost("/facilities", AddFacility);
        depot.MapGet("/lookup", Lookup);
        depot.MapGet("/facilities/{facilityId:guid}/expected", Expected);
        depot.MapGet("/facilities/{facilityId:guid}/visits", Visits);
        depot.MapPost("/facilities/{facilityId:guid}/gate-in", GateIn);
        depot.MapPost("/facilities/{facilityId:guid}/visits/{visitId:guid}/gate-out", GateOut);
    }

    public sealed record SignUpRequest(string Name, string Country, string? City, string? Address, double? Lat, double? Lon, int? RadiusM);
    public sealed record FacilityRequest(string Name, string Country, string? City, string? Address, double? Lat, double? Lon, int? RadiusM);
    public sealed record GateInRequest(string? PlateCountry, string? Plate, string? Qr, string? ContainerNumber, Guid? ExpectedVisitId, string? Note);
    public sealed record GateOutRequest(string? ContainerNumber, string? Note);

    // ---- Sign-up and facilities ---------------------------------------------------------

    private static async Task<IResult> SignUp(SignUpRequest req, HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();
        var facilityReq = new FacilityRequest(req.Name, req.Country, req.City, req.Address, req.Lat, req.Lon, req.RadiusM);
        if (ValidateFacility(facilityReq) is { } problem) return problem;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var org = new Organization { Type = OrganizationTypes.Depot, Name = req.Name.Trim(), Country = req.Country.ToUpperInvariant() };
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);

        db.Memberships.Add(new Membership { UserId = userId, OrganizationId = org.Id, Role = Roles.DepotAdmin });
        var facility = NewFacility(facilityReq, org.Id);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Created($"/api/v1/depots/{org.Id}", new { org.Id, org.Name, role = Roles.DepotAdmin, facility = FacilityDto(facility) });
    }

    private static async Task<IResult> ListFacilities(Guid orgId, PortSyncsDbContext db, CancellationToken ct)
    {
        var list = await db.Facilities.AsNoTracking()
            .Where(f => f.OperatorOrgId == orgId && f.Status == "active")
            .OrderBy(f => f.Name)
            .ToListAsync(ct);
        return Results.Ok(list.Select(FacilityDto));
    }

    private static async Task<IResult> AddFacility(Guid orgId, FacilityRequest req, HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (!await IsDepotAdmin(http, db, orgId, ct)) return Results.Forbid();
        if (ValidateFacility(req) is { } problem) return problem;

        var facility = NewFacility(req, orgId);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/depots/{orgId}/facilities/{facility.Id}", FacilityDto(facility));
    }

    // ---- Truck lookup (T30) -------------------------------------------------------------

    private static async Task<IResult> Lookup(
        Guid orgId, string? plateCountry, string? plate, string? qr, HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        var (truck, country, normalized, error) = await ResolveTruck(db, plateCountry, plate, qr, ct);
        if (error is not null) return error;

        db.AuditLog.Add(new AuditEntry
        {
            Ts = DateTimeOffset.UtcNow,
            UserId = http.User.UserId(),
            OrganizationId = orgId,
            Action = "truck_lookup",
            Entity = "truck",
            EntityId = truck?.Id.ToString() ?? $"{country}:{normalized}",
            Detail = JsonSerializer.Serialize(new { method = qr is null ? "plate" : "qr", found = truck is not null }),
        });
        await db.SaveChangesAsync(ct);

        if (truck is null)
            return Results.Ok(new { registered = false, plateCountry = country, plate = normalized });

        var company = await db.Organizations.Where(o => o.Id == truck.OrganizationId).Select(o => o.Name).FirstAsync(ct);
        var driverName = await (
                from a in db.TruckAssignments
                join d in db.Drivers on a.DriverId equals d.Id
                where a.TruckId == truck.Id && a.ToAt == null
                select d.Name)
            .FirstOrDefaultAsync(ct);

        var myFacilities = db.Facilities.Where(f => f.OperatorOrgId == orgId).Select(f => f.Id);
        var now = DateTimeOffset.UtcNow;
        var inside = await db.FacilityVisits.AnyAsync(v => myFacilities.Contains(v.FacilityId) && v.TruckId == truck.Id && v.GateOutAt == null, ct);
        var expected = await db.ExpectedVisits.AnyAsync(e => myFacilities.Contains(e.FacilityId) && e.TruckId == truck.Id
            && e.Status == "expected" && e.WindowTo > now.AddHours(-6) && e.WindowFrom < now.AddHours(24), ct);

        return Results.Ok(new
        {
            registered = true,
            truckId = truck.Id,
            plateCountry = truck.PlateCountry,
            plate = truck.PlateDisplay,
            company,
            driverFirstName = FirstName(driverName),
            insideNow = inside,
            expectedHere = expected,
            contactAvailable = true, // masked contact and escalation arrive with T34/T35
        });
    }

    // ---- Expected visits and gate log (T52) ---------------------------------------------

    private static async Task<IResult> Expected(
        Guid orgId, Guid facilityId, DateTimeOffset? from, DateTimeOffset? to, PortSyncsDbContext db, CancellationToken ct)
    {
        if (await OwnFacility(db, orgId, facilityId, ct) is null) return Results.NotFound();

        var now = DateTimeOffset.UtcNow;
        var start = from ?? now.AddHours(-12);
        var end = to ?? now.AddHours(24);

        var list = await (
                from e in db.ExpectedVisits
                join o in db.Organizations on e.TruckingOrgId equals o.Id
                join t in db.Trucks on e.TruckId equals t.Id into tj
                from t in tj.DefaultIfEmpty()
                where e.FacilityId == facilityId && e.Status == "expected" && e.WindowTo > start && e.WindowFrom < end
                orderby e.WindowFrom
                select new
                {
                    e.Id, company = o.Name, plate = t != null ? t.PlateDisplay : null,
                    e.ContainerNumber, e.Purpose, e.WindowFrom, e.WindowTo,
                })
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> Visits(Guid orgId, Guid facilityId, bool? inside, PortSyncsDbContext db, CancellationToken ct)
    {
        if (await OwnFacility(db, orgId, facilityId, ct) is null) return Results.NotFound();

        var since = DateTimeOffset.UtcNow.AddDays(-2);
        var query =
            from v in db.FacilityVisits
            join o in db.Organizations on v.TruckingOrgId equals o.Id into oj
            from o in oj.DefaultIfEmpty()
            where v.FacilityId == facilityId
            select new { v, company = o != null ? o.Name : null };

        query = inside == true
            ? query.Where(x => x.v.GateOutAt == null)
            : query.Where(x => x.v.GateInAt > since || x.v.GateOutAt == null);

        var list = await query.OrderByDescending(x => x.v.GateInAt).Take(500).ToListAsync(ct);
        return Results.Ok(list.Select(x => VisitDto(x.v, x.company)));
    }

    private static async Task<IResult> GateIn(
        Guid orgId, Guid facilityId, GateInRequest req, HttpContext http,
        PortSyncsDbContext db, IEventBus bus, CancellationToken ct)
    {
        var facility = await OwnFacility(db, orgId, facilityId, ct);
        if (facility is null) return Results.NotFound();

        var (truck, country, normalized, error) = await ResolveTruck(db, req.PlateCountry, req.Plate, req.Qr, ct);
        if (error is not null) return error;

        string? container = null;
        if (!string.IsNullOrWhiteSpace(req.ContainerNumber))
        {
            if (!ContainerNumber.TryParse(req.ContainerNumber, out var parsed))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["containerNumber"] = ["Not a valid container number (check digit)."] });
            container = parsed.Value;
        }

        var now = DateTimeOffset.UtcNow;
        var expected = await MatchExpected(db, facilityId, req.ExpectedVisitId, truck?.Id, container, now, ct);
        if (req.ExpectedVisitId is not null && expected is null) return Results.NotFound(new { error = "expected_visit_not_found" });

        var visit = new FacilityVisit
        {
            FacilityId = facilityId,
            TruckId = truck?.Id,
            PlateCountry = country!,
            PlateNormalized = normalized!,
            TruckingOrgId = truck?.OrganizationId ?? expected?.TruckingOrgId,
            ExpectedVisitId = expected?.Id,
            ContainerIn = container,
            GateInAt = now,
            Method = req.Qr is null ? "manual" : "qr",
            RecordedInBy = http.User.UserId(),
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
        };
        db.FacilityVisits.Add(visit);
        if (expected is not null) expected.Status = "arrived";

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { ConstraintName: "ux_facility_visits_inside" })
        {
            var open = await db.FacilityVisits.AsNoTracking()
                .Where(v => v.FacilityId == facilityId && v.PlateCountry == country && v.PlateNormalized == normalized && v.GateOutAt == null)
                .Select(v => v.Id).FirstOrDefaultAsync(ct);
            return Results.Conflict(new { error = "already_inside", visitId = open });
        }

        await PublishGate(bus, EventTypes.FacilityGateIn, visit, now, container, ct);

        var company = visit.TruckingOrgId is { } tid
            ? await db.Organizations.Where(o => o.Id == tid).Select(o => o.Name).FirstOrDefaultAsync(ct)
            : null;
        return Results.Ok(VisitDto(visit, company));
    }

    private static async Task<IResult> GateOut(
        Guid orgId, Guid facilityId, Guid visitId, GateOutRequest req, HttpContext http,
        PortSyncsDbContext db, IEventBus bus, CancellationToken ct)
    {
        if (await OwnFacility(db, orgId, facilityId, ct) is null) return Results.NotFound();

        var visit = await db.FacilityVisits.FirstOrDefaultAsync(v => v.Id == visitId && v.FacilityId == facilityId, ct);
        if (visit is null) return Results.NotFound();
        if (visit.GateOutAt is not null) return Results.Conflict(new { error = "already_out" });

        string? container = null;
        if (!string.IsNullOrWhiteSpace(req.ContainerNumber))
        {
            if (!ContainerNumber.TryParse(req.ContainerNumber, out var parsed))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["containerNumber"] = ["Not a valid container number (check digit)."] });
            container = parsed.Value;
        }

        var now = DateTimeOffset.UtcNow;
        visit.GateOutAt = now;
        visit.RecordedOutBy = http.User.UserId();
        visit.ContainerOut = container;
        if (!string.IsNullOrWhiteSpace(req.Note))
            visit.Note = string.IsNullOrEmpty(visit.Note) ? req.Note.Trim() : $"{visit.Note}\n{req.Note.Trim()}";

        if (visit.ExpectedVisitId is { } evId)
        {
            var ev = await db.ExpectedVisits.FirstOrDefaultAsync(e => e.Id == evId, ct);
            if (ev is not null) ev.Status = "completed";
        }
        await db.SaveChangesAsync(ct);

        await PublishGate(bus, EventTypes.FacilityGateOut, visit, now, container, ct);

        var company = visit.TruckingOrgId is { } tid
            ? await db.Organizations.Where(o => o.Id == tid).Select(o => o.Name).FirstOrDefaultAsync(ct)
            : null;
        return Results.Ok(VisitDto(visit, company));
    }

    // ---- Helpers ------------------------------------------------------------------------

    /// <summary>Finds a truck by QR token or by plate. Unknown plates are fine (unregistered truck).</summary>
    private static async Task<(Truck? Truck, string? Country, string? Normalized, IResult? Error)> ResolveTruck(
        PortSyncsDbContext db, string? plateCountry, string? plate, string? qr, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(qr))
        {
            var byQr = await db.Trucks.AsNoTracking().FirstOrDefaultAsync(t => t.QrToken == qr && t.Status == "active", ct);
            return byQr is null
                ? (null, null, null, Results.NotFound(new { error = "qr_not_recognised" }))
                : (byQr, byQr.PlateCountry, byQr.PlateNormalized, null);
        }

        var normalized = Plate.Normalize(plate);
        if (plateCountry is not { Length: 2 } || !plateCountry.All(char.IsAsciiLetter) || normalized.Length is 0 or > 20)
            return (null, null, null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["plate"] = ["Give plateCountry (two letters) and plate, or a QR code."],
            }));

        var country = plateCountry.ToUpperInvariant();
        var truck = await db.Trucks.AsNoTracking().FirstOrDefaultAsync(
            t => t.PlateCountry == country && t.PlateNormalized == normalized && t.Status == "active", ct);
        return (truck, country, normalized, null);
    }

    /// <summary>
    /// Links a gate-in to the trucking company's pre-announcement: the one given, or the earliest
    /// open one for this truck or container whose time window is near now.
    /// </summary>
    private static async Task<ExpectedVisit?> MatchExpected(
        PortSyncsDbContext db, Guid facilityId, Guid? expectedVisitId, Guid? truckId, string? container,
        DateTimeOffset now, CancellationToken ct)
    {
        var open = db.ExpectedVisits.Where(e => e.FacilityId == facilityId && e.Status == "expected");
        if (expectedVisitId is { } id) return await open.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (truckId is null && container is null) return null;

        return await open
            .Where(e => e.WindowFrom < now.AddHours(6) && e.WindowTo > now.AddHours(-6))
            .Where(e => (truckId != null && e.TruckId == truckId) || (container != null && e.ContainerNumber == container))
            .OrderBy(e => e.WindowFrom)
            .FirstOrDefaultAsync(ct);
    }

    private static Task<Facility?> OwnFacility(PortSyncsDbContext db, Guid orgId, Guid facilityId, CancellationToken ct) =>
        db.Facilities.AsNoTracking().FirstOrDefaultAsync(f => f.Id == facilityId && f.OperatorOrgId == orgId && f.Status == "active", ct);

    private static async Task<bool> IsDepotAdmin(HttpContext http, PortSyncsDbContext db, Guid orgId, CancellationToken ct) =>
        http.User.UserId() is { } userId && await OrgAccess.HasRoleAsync(db, userId, orgId, [Roles.DepotAdmin], ct);

    private static IResult? ValidateFacility(FacilityRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length < 2) errors["name"] = ["Name is required."];
        if (req.Country is not { Length: 2 } || !req.Country.All(char.IsAsciiLetter)) errors["country"] = ["Use a two-letter ISO country code."];
        if ((req.Lat is null) != (req.Lon is null)) errors["location"] = ["Give both lat and lon, or neither."];
        if (req.Lat is < -90 or > 90 || req.Lon is < -180 or > 180) errors["location"] = ["Coordinates out of range."];
        if (req.RadiusM is < 50 or > 5000) errors["radiusM"] = ["Radius must be 50 to 5000 metres."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    private static Facility NewFacility(FacilityRequest req, Guid orgId) => new()
    {
        Type = FacilityTypes.Depot,
        Name = req.Name.Trim(),
        Country = req.Country.ToUpperInvariant(),
        City = string.IsNullOrWhiteSpace(req.City) ? null : req.City.Trim(),
        Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim(),
        Lat = req.Lat,
        Lon = req.Lon,
        RadiusM = req.Lat is null ? null : req.RadiusM ?? 300,
        OperatorOrgId = orgId,
    };

    internal static object FacilityDto(Facility f) =>
        new { f.Id, f.Type, f.Name, f.Country, f.City, f.Address, f.Lat, f.Lon, f.RadiusM };

    private static object VisitDto(FacilityVisit v, string? company) => new
    {
        v.Id, v.FacilityId, plateCountry = v.PlateCountry, plate = v.PlateNormalized, registered = v.TruckId is not null,
        company, v.ContainerIn, v.ContainerOut, v.GateInAt, v.GateOutAt, inside = v.GateOutAt is null,
        v.ExpectedVisitId, v.Method, v.Note,
    };

    private static string? FirstName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim().Split(' ', 2)[0];

    private static Task PublishGate(IEventBus bus, string type, FacilityVisit v, DateTimeOffset now, string? container, CancellationToken ct) =>
        bus.PublishAsync(new EventEnvelope(
            EventId: Guid.NewGuid(),
            Type: type,
            SchemaVersion: 1,
            OccurredAt: now,
            RecordedAt: now,
            Source: new EventSource("depot_portal", null, null),
            Subject: new EventSubject(
                TruckId: v.TruckId?.ToString(),
                Plate: new PlateRef(v.PlateCountry, v.PlateNormalized),
                ContainerNumber: container,
                FacilityId: v.FacilityId.ToString()),
            Location: null,
            CorrelationId: v.Id.ToString(),
            IdempotencyKey: $"{type}:{v.Id}",
            Payload: JsonSerializer.SerializeToElement(new { visitId = v.Id, method = v.Method, expectedVisitId = v.ExpectedVisitId })), ct);
}
