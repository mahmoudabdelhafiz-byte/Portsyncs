using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PortSyncs.Api.Database;
using PortSyncs.Api.Domain;
using PortSyncs.Api.Events;
using PortSyncs.Api.Infrastructure.Messaging;
using PortSyncs.Api.Modules.Auth;

namespace PortSyncs.Api.Modules.Eirs;

/// <summary>
/// T33 digital EIR. Flow: the depot records condition (draft) with damage marks and photos,
/// issues it, and the driver confirms or disputes it with a comment and their own photos.
/// Issued EIRs cannot be edited, so both sides can rely on them in a damage claim.
/// </summary>
public static class EirEndpoints
{
    public static void MapEirs(this IEndpointRouteBuilder app)
    {
        var depot = app.MapGroup("/api/v1/depots/{orgId:guid}").WithTags("EIR")
            .RequireOrgRole(Roles.DepotAdmin, Roles.GateOfficer);
        depot.MapPost("/facilities/{facilityId:guid}/visits/{visitId:guid}/eirs", Create);
        depot.MapPut("/eirs/{eirId:guid}", Update);
        depot.MapPost("/eirs/{eirId:guid}/issue", Issue);
        depot.MapGet("/eirs", ListForDepot);

        var company = app.MapGroup("/api/v1/companies/{orgId:guid}").WithTags("EIR")
            .RequireOrgRole(Roles.CompanyAdmin, Roles.Dispatcher);
        company.MapGet("/eirs", ListForCompany);

        var shared = app.MapGroup("/api/v1/eirs").WithTags("EIR").RequireAuthorization();
        shared.MapGet("/pending", PendingForDriver);
        shared.MapGet("/{eirId:guid}", Get);
        shared.MapPost("/{eirId:guid}/confirm", Confirm);
        shared.MapPost("/{eirId:guid}/dispute", Dispute);
    }

    public sealed record MarkInput(string Side, string? Position, string DamageType, string? Note);
    public sealed record PhotoInput(Guid FileId, string? Side);
    public sealed record EirInput(
        string Direction, string? ContainerNumber, string Condition, string? SealNumber, string? Remarks,
        IReadOnlyList<MarkInput>? Marks, IReadOnlyList<PhotoInput>? Photos);
    public sealed record DisputeInput(string Comment, IReadOnlyList<PhotoInput>? Photos);

    // ---- Depot ---------------------------------------------------------------------------

    private static async Task<IResult> Create(
        Guid orgId, Guid facilityId, Guid visitId, EirInput input, HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();

        var visit = await (
                from v in db.FacilityVisits
                join f in db.Facilities on v.FacilityId equals f.Id
                where v.Id == visitId && v.FacilityId == facilityId && f.OperatorOrgId == orgId
                select v)
            .AsNoTracking().FirstOrDefaultAsync(ct);
        if (visit is null) return Results.NotFound();

        // Default the container to the one recorded at the gate for that direction.
        var containerInput = input.ContainerNumber ?? (input.Direction == "out" ? visit.ContainerOut : visit.ContainerIn);
        var (problem, container) = Validate(input with { ContainerNumber = containerInput });
        if (problem is not null) return problem;

        var photoProblem = await CheckPhotos(db, input.Photos, f => f.OrganizationId == orgId, ct);
        if (photoProblem is not null) return photoProblem;

        var eir = new Eir
        {
            FacilityVisitId = visit.Id,
            FacilityId = facilityId,
            TruckingOrgId = visit.TruckingOrgId,
            ContainerNumber = container!,
            Direction = input.Direction,
            Condition = input.Condition,
            SealNumber = Clean(input.SealNumber),
            Remarks = Clean(input.Remarks),
            RecordedBy = userId,
        };
        db.Eirs.Add(eir);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Conflict(new { error = "eir_exists_for_visit_direction_container" });
        }

        AddMarksAndPhotos(db, eir.Id, input.Marks, input.Photos, "facility", userId);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/eirs/{eir.Id}", await Detail(db, eir, ct));
    }

    private static async Task<IResult> Update(Guid orgId, Guid eirId, EirInput input, HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();

        var eir = await DepotEir(db, orgId, eirId, ct);
        if (eir is null) return Results.NotFound();
        if (eir.Status != EirStatus.Draft) return Results.Conflict(new { error = "eir_already_issued" });

        var (problem, container) = Validate(input with { ContainerNumber = input.ContainerNumber ?? eir.ContainerNumber });
        if (problem is not null) return problem;
        var photoProblem = await CheckPhotos(db, input.Photos, f => f.OrganizationId == orgId, ct);
        if (photoProblem is not null) return photoProblem;

        eir.Direction = input.Direction;
        eir.ContainerNumber = container!;
        eir.Condition = input.Condition;
        eir.SealNumber = Clean(input.SealNumber);
        eir.Remarks = Clean(input.Remarks);

        // A draft is replaced as a whole: marks and depot photos are sent again each time.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.EirDamageMarks.Where(m => m.EirId == eirId).ExecuteDeleteAsync(ct);
        await db.EirPhotos.Where(p => p.EirId == eirId && p.Party == "facility").ExecuteDeleteAsync(ct);
        AddMarksAndPhotos(db, eirId, input.Marks, input.Photos, "facility", userId);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Conflict(new { error = "eir_exists_for_visit_direction_container" });
        }
        return Results.Ok(await Detail(db, eir, ct));
    }

    private static async Task<IResult> Issue(Guid orgId, Guid eirId, PortSyncsDbContext db, IEventBus bus, CancellationToken ct)
    {
        var eir = await DepotEir(db, orgId, eirId, ct);
        if (eir is null) return Results.NotFound();
        if (eir.Status != EirStatus.Draft) return Results.Conflict(new { error = "eir_already_issued" });

        // A damaged container needs at least one damage mark and one photo before it can be issued.
        if (eir.Condition == "damaged")
        {
            var marks = await db.EirDamageMarks.CountAsync(m => m.EirId == eirId, ct);
            var photos = await db.EirPhotos.CountAsync(p => p.EirId == eirId && p.Party == "facility", ct);
            if (marks == 0 || photos == 0)
                return Results.BadRequest(new { error = "damage_needs_marks_and_photos", marks, photos });
        }

        var now = DateTimeOffset.UtcNow;
        eir.Status = EirStatus.Issued;
        eir.IssuedAt = now;
        await db.SaveChangesAsync(ct);

        await Publish(bus, EventTypes.EirRecorded, eir, now, new { eir.Id, eir.Number, eir.Direction, eir.Condition }, "depot_portal", ct);
        // Push notification to the driver comes with T22.
        return Results.Ok(await Detail(db, eir, ct));
    }

    private static async Task<IResult> ListForDepot(Guid orgId, string? status, DateTimeOffset? from, PortSyncsDbContext db, CancellationToken ct)
    {
        var since = from ?? DateTimeOffset.UtcNow.AddDays(-7);
        var query =
            from e in db.Eirs
            join f in db.Facilities on e.FacilityId equals f.Id
            where f.OperatorOrgId == orgId && e.CreatedAt >= since
            select e;
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status == status);

        return Results.Ok(await Summaries(db, query, ct));
    }

    // ---- Company and driver ------------------------------------------------------------------

    private static async Task<IResult> ListForCompany(Guid orgId, string? status, DateTimeOffset? from, PortSyncsDbContext db, CancellationToken ct)
    {
        var since = from ?? DateTimeOffset.UtcNow.AddDays(-30);
        var query = db.Eirs.Where(e => e.TruckingOrgId == orgId && e.Status != EirStatus.Draft && e.CreatedAt >= since);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status == status);

        return Results.Ok(await Summaries(db, query, ct));
    }

    private static async Task<IResult> PendingForDriver(HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();

        var myTrucks = EirAccess.TruckIdsDrivenBy(db, userId);
        var query =
            from e in db.Eirs
            join v in db.FacilityVisits on e.FacilityVisitId equals v.Id
            where e.Status == EirStatus.Issued && v.TruckId != null && myTrucks.Contains(v.TruckId.Value)
                  && e.IssuedAt > DateTimeOffset.UtcNow.AddDays(-3)
            select e;

        return Results.Ok(await Summaries(db, query, ct));
    }

    private static async Task<IResult> Get(Guid eirId, HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();
        var eir = await db.Eirs.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eirId, ct);
        if (eir is null || !await EirAccess.CanViewAsync(db, userId, eir, ct)) return Results.NotFound();

        // Drafts are the depot's work in progress; others see an EIR once it is issued.
        if (eir.Status == EirStatus.Draft && !await IsDepotStaff(db, userId, eir, ct)) return Results.NotFound();
        return Results.Ok(await Detail(db, eir, ct));
    }

    private static async Task<IResult> Confirm(Guid eirId, HttpContext http, PortSyncsDbContext db, IEventBus bus, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();
        var (eir, error) = await ForDriverResponse(db, userId, eirId, ct);
        if (error is not null) return error;

        var now = DateTimeOffset.UtcNow;
        eir!.Status = EirStatus.Confirmed;
        eir.DriverUserId = userId;
        eir.DriverRespondedAt = now;
        await db.SaveChangesAsync(ct);

        await Publish(bus, EventTypes.EirConfirmed, eir, now, new { eir.Id, eir.Number }, "driver_app", ct);
        return Results.Ok(await Detail(db, eir, ct));
    }

    private static async Task<IResult> Dispute(Guid eirId, DisputeInput input, HttpContext http, PortSyncsDbContext db, IEventBus bus, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(input.Comment) || input.Comment.Length > 2000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["comment"] = ["Explain what is wrong (up to 2000 characters)."] });

        var (eir, error) = await ForDriverResponse(db, userId, eirId, ct);
        if (error is not null) return error;

        // Drivers attach only photos they uploaded themselves.
        var photoProblem = await CheckPhotos(db, input.Photos, f => f.UploadedBy == userId, ct);
        if (photoProblem is not null) return photoProblem;

        var now = DateTimeOffset.UtcNow;
        eir!.Status = EirStatus.Disputed;
        eir.DriverUserId = userId;
        eir.DriverRespondedAt = now;
        eir.DriverComment = input.Comment.Trim();
        AddMarksAndPhotos(db, eir.Id, null, input.Photos, "driver", userId);
        await db.SaveChangesAsync(ct);

        await Publish(bus, EventTypes.EirDisputed, eir, now, new { eir.Id, eir.Number, comment = eir.DriverComment }, "driver_app", ct);
        // A disputed damage EIR becomes a case in Phase 2 (T32).
        return Results.Ok(await Detail(db, eir, ct));
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static (IResult? Problem, string? Container) Validate(EirInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.Direction is not ("in" or "out")) errors["direction"] = ["Use in or out."];
        if (input.Condition is not ("sound" or "damaged")) errors["condition"] = ["Use sound or damaged."];

        string? container = null;
        if (string.IsNullOrWhiteSpace(input.ContainerNumber)) errors["containerNumber"] = ["Container number is required."];
        else if (ContainerNumber.TryParse(input.ContainerNumber, out var parsed)) container = parsed.Value;
        else errors["containerNumber"] = ["Not a valid container number (check digit)."];

        var marks = input.Marks ?? Array.Empty<MarkInput>();
        if (marks.Count > 50) errors["marks"] = ["At most 50 damage marks."];
        if (input.Condition == "sound" && marks.Count > 0) errors["marks"] = ["A sound container has no damage marks."];
        for (var i = 0; i < marks.Count; i++)
        {
            if (!EirVocabulary.Sides.Contains(marks[i].Side)) errors[$"marks[{i}].side"] = [$"One of: {string.Join(", ", EirVocabulary.Sides)}."];
            if (!EirVocabulary.DamageTypes.Contains(marks[i].DamageType)) errors[$"marks[{i}].damageType"] = [$"One of: {string.Join(", ", EirVocabulary.DamageTypes)}."];
        }

        var photos = input.Photos ?? Array.Empty<PhotoInput>();
        if (photos.Count > 30) errors["photos"] = ["At most 30 photos."];
        for (var i = 0; i < photos.Count; i++)
            if (photos[i].Side is { } side && !EirVocabulary.Sides.Contains(side)) errors[$"photos[{i}].side"] = ["Unknown side."];

        return (errors.Count > 0 ? Results.ValidationProblem(errors) : null, container);
    }

    private static async Task<IResult?> CheckPhotos(
        PortSyncsDbContext db, IReadOnlyList<PhotoInput>? photos, System.Linq.Expressions.Expression<Func<FileRecord, bool>> allowed, CancellationToken ct)
    {
        if (photos is not { Count: > 0 }) return null;
        if (photos.Count > 30) return Results.BadRequest(new { error = "too_many_photos", max = 30 });

        var ids = photos.Select(p => p.FileId).Distinct().ToList();
        var found = await db.Files.Where(f => ids.Contains(f.Id)).Where(allowed)
            .Where(f => f.ContentType.StartsWith("image/")).CountAsync(ct);
        return found == ids.Count ? null : Results.BadRequest(new { error = "photo_not_found_or_not_yours" });
    }

    private static void AddMarksAndPhotos(
        PortSyncsDbContext db, Guid eirId, IReadOnlyList<MarkInput>? marks, IReadOnlyList<PhotoInput>? photos, string party, Guid userId)
    {
        foreach (var m in marks ?? Array.Empty<MarkInput>())
            db.EirDamageMarks.Add(new EirDamageMark { EirId = eirId, Side = m.Side, Position = Clean(m.Position), DamageType = m.DamageType, Note = Clean(m.Note) });

        foreach (var p in (photos ?? Array.Empty<PhotoInput>()).DistinctBy(p => p.FileId))
            db.EirPhotos.Add(new EirPhoto { EirId = eirId, FileId = p.FileId, Party = party, Side = p.Side, AddedBy = userId });
    }

    private static async Task<(Eir? Eir, IResult? Error)> ForDriverResponse(PortSyncsDbContext db, Guid userId, Guid eirId, CancellationToken ct)
    {
        var eir = await db.Eirs.FirstOrDefaultAsync(e => e.Id == eirId, ct);
        if (eir is null || !await EirAccess.DrivesVisitTruckAsync(db, userId, eir.FacilityVisitId, ct))
            return (null, Results.NotFound());
        if (eir.Status != EirStatus.Issued)
            return (null, Results.Conflict(new { error = "not_awaiting_driver", eir.Status }));
        return (eir, null);
    }

    private static Task<Eir?> DepotEir(PortSyncsDbContext db, Guid orgId, Guid eirId, CancellationToken ct) =>
        (from e in db.Eirs
         join f in db.Facilities on e.FacilityId equals f.Id
         where e.Id == eirId && f.OperatorOrgId == orgId
         select e).FirstOrDefaultAsync(ct);

    private static async Task<bool> IsDepotStaff(PortSyncsDbContext db, Guid userId, Eir eir, CancellationToken ct)
    {
        var op = await db.Facilities.Where(f => f.Id == eir.FacilityId).Select(f => f.OperatorOrgId).FirstOrDefaultAsync(ct);
        return op is { } orgId && await OrgAccess.HasRoleAsync(db, userId, orgId, [Roles.DepotAdmin, Roles.GateOfficer], ct);
    }

    private static async Task<object> Summaries(PortSyncsDbContext db, IQueryable<Eir> eirs, CancellationToken ct)
    {
        var rows = await (
                from e in eirs
                join v in db.FacilityVisits on e.FacilityVisitId equals v.Id
                join f in db.Facilities on e.FacilityId equals f.Id
                join o in db.Organizations on e.TruckingOrgId equals o.Id into oj
                from o in oj.DefaultIfEmpty()
                orderby e.CreatedAt descending
                select new
                {
                    e.Id, number = "EIR-" + e.Number.ToString(), facility = f.Name, company = o != null ? o.Name : null,
                    plate = v.PlateNormalized, e.ContainerNumber, e.Direction, e.Condition, e.Status, e.IssuedAt, e.DriverRespondedAt,
                })
            .Take(500)
            .ToListAsync(ct);
        return rows;
    }

    private static async Task<object> Detail(PortSyncsDbContext db, Eir e, CancellationToken ct)
    {
        var marks = await db.EirDamageMarks.AsNoTracking().Where(m => m.EirId == e.Id).OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Id, m.Side, m.Position, m.DamageType, m.Note }).ToListAsync(ct);
        var photos = await db.EirPhotos.AsNoTracking().Where(p => p.EirId == e.Id).OrderBy(p => p.CreatedAt)
            .Select(p => new { p.FileId, p.Party, p.Side, url = "/api/v1/files/" + p.FileId }).ToListAsync(ct);
        var visit = await db.FacilityVisits.AsNoTracking().Where(v => v.Id == e.FacilityVisitId)
            .Select(v => new { v.PlateCountry, v.PlateNormalized, v.GateInAt, v.GateOutAt }).FirstAsync(ct);
        var facility = await db.Facilities.Where(f => f.Id == e.FacilityId).Select(f => f.Name).FirstAsync(ct);
        var company = e.TruckingOrgId is { } t
            ? await db.Organizations.Where(o => o.Id == t).Select(o => o.Name).FirstOrDefaultAsync(ct)
            : null;

        return new
        {
            e.Id, number = $"EIR-{e.Number:D6}", facility, company, visit,
            e.ContainerNumber, e.Direction, e.Condition, e.SealNumber, e.Remarks, e.Status,
            e.CreatedAt, e.IssuedAt, e.DriverRespondedAt, e.DriverComment, marks, photos,
        };
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, 500)];

    private static Task Publish(IEventBus bus, string type, Eir e, DateTimeOffset now, object payload, string source, CancellationToken ct) =>
        bus.PublishAsync(new EventEnvelope(
            EventId: Guid.NewGuid(),
            Type: type,
            SchemaVersion: 1,
            OccurredAt: now,
            RecordedAt: now,
            Source: new EventSource(source, null, e.TruckingOrgId?.ToString()),
            Subject: new EventSubject(ContainerNumber: e.ContainerNumber, FacilityId: e.FacilityId.ToString()),
            Location: null,
            CorrelationId: e.FacilityVisitId.ToString(),
            IdempotencyKey: $"{type}:{e.Id}",
            Payload: JsonSerializer.SerializeToElement(payload)), ct);
}
