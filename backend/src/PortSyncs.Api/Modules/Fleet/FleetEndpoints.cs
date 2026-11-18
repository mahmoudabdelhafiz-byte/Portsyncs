using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PortSyncs.Api.Database;
using PortSyncs.Api.Domain;

namespace PortSyncs.Api.Modules.Fleet;

/// <summary>
/// T26: trucking company sign-up, trucks and drivers.
/// Until login (T20) exists these endpoints are only mapped when Features__DevEndpoints=true.
/// </summary>
public static partial class FleetEndpoints
{
    public static void MapFleet(this IEndpointRouteBuilder app)
    {
        var companies = app.MapGroup("/api/v1/companies").WithTags("Fleet");

        companies.MapPost("/", SignUp);
        companies.MapGet("/{orgId:guid}", GetCompany);
        companies.MapPost("/{orgId:guid}/trucks", AddTruck);
        companies.MapGet("/{orgId:guid}/trucks", ListTrucks);
        companies.MapPost("/{orgId:guid}/drivers", AddDriver);
        companies.MapGet("/{orgId:guid}/drivers", ListDrivers);
        companies.MapPost("/{orgId:guid}/trucks/{truckId:guid}/assign", AssignDriver);
    }

    public sealed record SignUpRequest(string Name, string Country, string AdminName, string AdminPhone, string? Locale);
    public sealed record AddTruckRequest(string PlateCountry, string Plate);
    public sealed record AddDriverRequest(string Name, string Phone, string? LicenceNo, string? LicenceCountry);
    public sealed record AssignRequest(Guid DriverId);

    private static async Task<IResult> SignUp(SignUpRequest req, PortSyncsDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length < 2) errors["name"] = ["Company name is required."];
        if (!IsCountry(req.Country)) errors["country"] = ["Use a two-letter ISO country code, e.g. SA."];
        if (string.IsNullOrWhiteSpace(req.AdminName)) errors["adminName"] = ["Admin name is required."];
        if (!IsPhone(req.AdminPhone)) errors["adminPhone"] = ["Use international format, e.g. +966501234567."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var org = new Organization
        {
            Type = OrganizationTypes.TruckingCompany,
            Name = req.Name.Trim(),
            Country = req.Country.ToUpperInvariant(),
        };
        db.Organizations.Add(org);

        // A person signs in with one phone number across all the companies they work with.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == req.AdminPhone, ct);
        if (user is null)
        {
            user = new User { Phone = req.AdminPhone, Name = req.AdminName.Trim(), Locale = req.Locale ?? "en" };
            db.Users.Add(user);
        }
        await db.SaveChangesAsync(ct);

        db.Memberships.Add(new Membership { UserId = user.Id, OrganizationId = org.Id, Role = Roles.CompanyAdmin });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Created($"/api/v1/companies/{org.Id}", new { org.Id, org.Name, org.Country, adminUserId = user.Id });
    }

    private static async Task<IResult> GetCompany(Guid orgId, PortSyncsDbContext db, CancellationToken ct)
    {
        var org = await FindCompany(db, orgId, ct);
        if (org is null) return Results.NotFound();

        var trucks = await db.Trucks.CountAsync(t => t.OrganizationId == orgId && t.Status == "active", ct);
        var drivers = await db.Drivers.CountAsync(d => d.OrganizationId == orgId && d.Status == "active", ct);
        return Results.Ok(new { org.Id, org.Name, org.Country, org.Status, trucks, drivers });
    }

    private static async Task<IResult> AddTruck(Guid orgId, AddTruckRequest req, PortSyncsDbContext db, CancellationToken ct)
    {
        if (await FindCompany(db, orgId, ct) is null) return Results.NotFound();

        var normalized = Plate.Normalize(req.Plate);
        var errors = new Dictionary<string, string[]>();
        if (!IsCountry(req.PlateCountry)) errors["plateCountry"] = ["Use a two-letter ISO country code."];
        if (normalized.Length is 0 or > 20) errors["plate"] = ["Enter the truck plate."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var truck = new Truck
        {
            OrganizationId = orgId,
            PlateCountry = req.PlateCountry.ToUpperInvariant(),
            PlateNormalized = normalized,
            PlateDisplay = req.Plate.Trim(),
            QrToken = NewQrToken(),
        };
        db.Trucks.Add(truck);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ux_trucks_active_plate"))
        {
            // Another company already registered this plate. Do not reveal which one.
            return Results.Conflict(new { error = "plate_already_registered" });
        }

        return Results.Created($"/api/v1/companies/{orgId}/trucks/{truck.Id}", ToDto(truck));
    }

    private static async Task<IResult> ListTrucks(Guid orgId, PortSyncsDbContext db, CancellationToken ct)
    {
        if (await FindCompany(db, orgId, ct) is null) return Results.NotFound();

        var trucks = await db.Trucks.AsNoTracking()
            .Where(t => t.OrganizationId == orgId && t.Status == "active")
            .OrderBy(t => t.PlateNormalized)
            .ToListAsync(ct);
        return Results.Ok(trucks.Select(ToDto));
    }

    private static async Task<IResult> AddDriver(Guid orgId, AddDriverRequest req, PortSyncsDbContext db, CancellationToken ct)
    {
        if (await FindCompany(db, orgId, ct) is null) return Results.NotFound();

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["Driver name is required."];
        if (!IsPhone(req.Phone)) errors["phone"] = ["Use international format, e.g. +966501234567."];
        if (req.LicenceCountry is not null && !IsCountry(req.LicenceCountry)) errors["licenceCountry"] = ["Use a two-letter ISO country code."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var driver = new Driver
        {
            OrganizationId = orgId,
            Name = req.Name.Trim(),
            Phone = req.Phone,
            LicenceNo = string.IsNullOrWhiteSpace(req.LicenceNo) ? null : req.LicenceNo.Trim(),
            LicenceCountry = req.LicenceCountry?.ToUpperInvariant(),
        };
        db.Drivers.Add(driver);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ux_drivers_org_phone"))
        {
            return Results.Conflict(new { error = "driver_phone_already_in_company" });
        }

        return Results.Created($"/api/v1/companies/{orgId}/drivers/{driver.Id}",
            new { driver.Id, driver.Name, driver.Phone, driver.LicenceNo, driver.LicenceCountry });
    }

    private static async Task<IResult> ListDrivers(Guid orgId, PortSyncsDbContext db, CancellationToken ct)
    {
        if (await FindCompany(db, orgId, ct) is null) return Results.NotFound();

        var drivers = await db.Drivers.AsNoTracking()
            .Where(d => d.OrganizationId == orgId && d.Status == "active")
            .OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, d.Phone, d.LicenceNo, d.LicenceCountry })
            .ToListAsync(ct);
        return Results.Ok(drivers);
    }

    private static async Task<IResult> AssignDriver(Guid orgId, Guid truckId, AssignRequest req, PortSyncsDbContext db, CancellationToken ct)
    {
        var truck = await db.Trucks.FirstOrDefaultAsync(t => t.Id == truckId && t.OrganizationId == orgId && t.Status == "active", ct);
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Id == req.DriverId && d.OrganizationId == orgId && d.Status == "active", ct);
        if (truck is null || driver is null) return Results.NotFound();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var open = await db.TruckAssignments.FirstOrDefaultAsync(a => a.TruckId == truckId && a.ToAt == null, ct);
        if (open is not null)
        {
            if (open.DriverId == driver.Id) return Results.Ok(new { truckId, driverId = driver.Id, from = open.FromAt });
            open.ToAt = now;
            await db.SaveChangesAsync(ct);
        }

        var assignment = new TruckAssignment { TruckId = truckId, DriverId = driver.Id, FromAt = now };
        db.TruckAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new { truckId, driverId = driver.Id, from = assignment.FromAt });
    }

    private static Task<Organization?> FindCompany(PortSyncsDbContext db, Guid orgId, CancellationToken ct) =>
        db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orgId && o.Type == OrganizationTypes.TruckingCompany, ct);

    private static object ToDto(Truck t) =>
        new { t.Id, t.PlateCountry, plate = t.PlateDisplay, t.PlateNormalized, qrToken = t.QrToken };

    // URL-safe, unguessable token printed as a QR sticker on the truck (T31).
    private static string NewQrToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');

    private static bool IsCountry(string? c) => c is { Length: 2 } && c.All(char.IsAsciiLetter);

    private static bool IsPhone(string? p) => p is not null && PhoneRegex().IsMatch(p);

    private static bool IsUniqueViolation(DbUpdateException ex, string constraint) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == constraint;

    [GeneratedRegex(@"^\+[1-9][0-9]{6,14}$")]
    private static partial Regex PhoneRegex();
}
