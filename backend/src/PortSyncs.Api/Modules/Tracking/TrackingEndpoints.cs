using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PortSyncs.Api.Database;
using PortSyncs.Api.Events;
using PortSyncs.Api.Infrastructure.Messaging;
using PortSyncs.Api.Modules.Auth;

namespace PortSyncs.Api.Modules.Tracking;

/// <summary>
/// T21: trips with consent-based location sharing, and the company's live fleet view (T27 backend).
/// Location is accepted only for the signed-in driver's own active trip.
/// </summary>
public static class TrackingEndpoints
{
    public static void MapTracking(this IEndpointRouteBuilder app)
    {
        var trips = app.MapGroup("/api/v1/trips").WithTags("Tracking").RequireAuthorization();
        trips.MapPost("/start", StartTrip);
        trips.MapGet("/current", CurrentTrip);
        trips.MapPost("/{tripId:guid}/positions", UploadPositions);
        trips.MapPost("/{tripId:guid}/end", EndOwnTrip);

        var company = app.MapGroup("/api/v1/companies/{orgId:guid}").WithTags("Tracking")
            .RequireOrgRole(Roles.CompanyAdmin, Roles.Dispatcher);
        company.MapGet("/fleet/live", LiveFleet);
        company.MapGet("/trips/{tripId:guid}/track", TripTrack);
        company.MapPost("/trips/{tripId:guid}/end", EndTripByCompany);
    }

    public sealed record StartTripRequest(bool Consent, Guid? OrganizationId, Guid? TruckId);
    public sealed record PositionPoint(DateTimeOffset Ts, double Lat, double Lon, float? AccuracyM, float? SpeedMps, float? Heading);
    public sealed record PositionBatch(IReadOnlyList<PositionPoint> Points);
    public sealed record TrackPoint(DateTime Ts, double Lat, double Lon, float? SpeedMps);

    private static async Task<IResult> StartTrip(
        StartTripRequest req, HttpContext http, PortSyncsDbContext db, IEventBus bus, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();

        // Location sharing needs an explicit yes each time a trip starts (privacy, T07).
        if (!req.Consent) return Results.BadRequest(new { error = "consent_required" });

        var drivers = await db.Drivers
            .Where(d => d.UserId == userId && d.Status == "active")
            .Where(d => req.OrganizationId == null || d.OrganizationId == req.OrganizationId)
            .ToListAsync(ct);
        if (drivers.Count == 0) return Results.Json(new { error = "not_a_driver" }, statusCode: 403);
        if (drivers.Count > 1)
            return Results.BadRequest(new { error = "choose_company", organizationIds = drivers.Select(d => d.OrganizationId) });
        var driver = drivers[0];

        var truckId = req.TruckId ?? await db.TruckAssignments
            .Where(a => a.DriverId == driver.Id && a.ToAt == null)
            .Select(a => (Guid?)a.TruckId)
            .FirstOrDefaultAsync(ct);
        if (truckId is null) return Results.BadRequest(new { error = "no_truck_assigned" });

        var truck = await db.Trucks.AsNoTracking().FirstOrDefaultAsync(
            t => t.Id == truckId && t.OrganizationId == driver.OrganizationId && t.Status == "active", ct);
        if (truck is null) return Results.NotFound(new { error = "truck_not_found" });

        var mine = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && t.Status == TripStatus.Active, ct);
        if (mine is not null) return Results.Conflict(new { error = "trip_already_active", tripId = mine.Id });

        var now = DateTimeOffset.UtcNow;
        var trip = new Trip
        {
            OrganizationId = driver.OrganizationId,
            TruckId = truck.Id,
            DriverId = driver.Id,
            UserId = userId,
            ConsentAt = now,
            StartedAt = now,
        };
        db.Trips.Add(trip);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Results.Conflict(new { error = "truck_in_use" });
        }

        await Publish(bus, EventTypes.TripStarted, trip, now, new { trip.Id, consentAt = trip.ConsentAt }, ct);
        return Results.Ok(TripDto(trip, truck.PlateDisplay));
    }

    private static async Task<IResult> CurrentTrip(HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();

        var trip = await db.Trips.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Status == TripStatus.Active, ct);
        if (trip is null) return Results.NoContent();

        var plate = await db.Trucks.Where(t => t.Id == trip.TruckId).Select(t => t.PlateDisplay).FirstAsync(ct);
        return Results.Ok(TripDto(trip, plate));
    }

    private static async Task<IResult> UploadPositions(
        Guid tripId, PositionBatch batch, HttpContext http, PortSyncsDbContext db,
        TrackingOptions options, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();
        if (batch.Points is not { Count: > 0 } || batch.Points.Count > options.MaxPointsPerBatch)
            return Results.BadRequest(new { error = "batch_size", max = options.MaxPointsPerBatch });

        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, ct);
        if (trip is null) return Results.NotFound();
        if (trip.Status != TripStatus.Active) return Results.Conflict(new { error = "trip_ended" });

        // Accept points from the trip start (phones send late batches after losing signal)
        // up to a little clock drift into the future, and only inside the retention window.
        var now = DateTimeOffset.UtcNow;
        var earliest = Max(trip.StartedAt.AddMinutes(-1), now.AddDays(-options.RetentionDays + 1));
        var latest = now.AddMinutes(2);

        var valid = batch.Points
            .Where(p => p.Ts >= earliest && p.Ts <= latest)
            .Where(p => p.Lat is >= -90 and <= 90 && p.Lon is >= -180 and <= 180)
            .Where(p => !(p.Lat == 0 && p.Lon == 0)) // a common GPS error value
            .GroupBy(p => p.Ts.UtcDateTime)
            .Select(g => g.First())
            .OrderBy(p => p.Ts)
            .ToList();

        if (valid.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO positions (trip_id, ts, lat, lon, accuracy_m, speed_mps, heading)
                SELECT @trip, u.ts, u.lat, u.lon, u.acc, u.spd, u.hdg
                FROM unnest(@ts, @lat, @lon, @acc, @spd, @hdg) AS u(ts, lat, lon, acc, spd, hdg)
                ON CONFLICT DO NOTHING
                """,
                [
                    new NpgsqlParameter("trip", NpgsqlDbType.Uuid) { Value = trip.Id },
                    new NpgsqlParameter("ts", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = valid.Select(p => p.Ts.UtcDateTime).ToArray() },
                    new NpgsqlParameter("lat", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = valid.Select(p => p.Lat).ToArray() },
                    new NpgsqlParameter("lon", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = valid.Select(p => p.Lon).ToArray() },
                    new NpgsqlParameter("acc", NpgsqlDbType.Array | NpgsqlDbType.Real) { Value = valid.Select(p => p.AccuracyM is >= 0 ? p.AccuracyM : null).ToArray() },
                    new NpgsqlParameter("spd", NpgsqlDbType.Array | NpgsqlDbType.Real) { Value = valid.Select(p => p.SpeedMps is >= 0 ? p.SpeedMps : null).ToArray() },
                    new NpgsqlParameter("hdg", NpgsqlDbType.Array | NpgsqlDbType.Real) { Value = valid.Select(p => p.Heading is >= 0 and < 360 ? p.Heading : null).ToArray() },
                ],
                ct);

            var newest = valid[^1];
            if (trip.LastTs is null || newest.Ts > trip.LastTs)
            {
                trip.LastTs = newest.Ts;
                trip.LastLat = newest.Lat;
                trip.LastLon = newest.Lon;
                trip.LastSpeedMps = newest.SpeedMps is >= 0 ? newest.SpeedMps : null;
                trip.LastHeading = newest.Heading is >= 0 and < 360 ? newest.Heading : null;
                await db.SaveChangesAsync(ct);
            }
        }

        return Results.Ok(new { accepted = valid.Count, rejected = batch.Points.Count - valid.Count });
    }

    private static async Task<IResult> EndOwnTrip(Guid tripId, HttpContext http, PortSyncsDbContext db, IEventBus bus, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId, ct);
        return await End(trip, "driver", db, bus, ct);
    }

    private static async Task<IResult> EndTripByCompany(Guid orgId, Guid tripId, PortSyncsDbContext db, IEventBus bus, CancellationToken ct)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId && t.OrganizationId == orgId, ct);
        return await End(trip, "company", db, bus, ct);
    }

    private static async Task<IResult> End(Trip? trip, string reason, PortSyncsDbContext db, IEventBus bus, CancellationToken ct)
    {
        if (trip is null) return Results.NotFound();
        if (trip.Status == TripStatus.Ended) return Results.Ok(new { trip.Id, status = trip.Status, trip.EndedAt });

        var now = DateTimeOffset.UtcNow;
        trip.Status = TripStatus.Ended;
        trip.EndedAt = now;
        trip.EndReason = reason;
        await db.SaveChangesAsync(ct);

        await Publish(bus, EventTypes.TripEnded, trip, now, new { trip.Id, reason }, ct);
        return Results.Ok(new { trip.Id, status = trip.Status, trip.EndedAt });
    }

    /// <summary>All active trucks of the company with their driver and latest position, if on a trip.</summary>
    private static async Task<IResult> LiveFleet(Guid orgId, PortSyncsDbContext db, TrackingOptions options, CancellationToken ct)
    {
        var trucks = await db.Trucks.AsNoTracking()
            .Where(t => t.OrganizationId == orgId && t.Status == "active")
            .OrderBy(t => t.PlateNormalized)
            .Select(t => new { t.Id, t.PlateDisplay })
            .ToListAsync(ct);

        var active = await (
                from trip in db.Trips
                join d in db.Drivers on trip.DriverId equals d.Id
                where trip.OrganizationId == orgId && trip.Status == TripStatus.Active
                select new { trip, driverName = d.Name })
            .AsNoTracking()
            .ToListAsync(ct);
        var byTruck = active.ToDictionary(a => a.trip.TruckId);

        var now = DateTimeOffset.UtcNow;
        var result = trucks.Select(t =>
        {
            if (!byTruck.TryGetValue(t.Id, out var a))
                return new { truckId = t.Id, plate = t.PlateDisplay, state = "off_trip", tripId = (Guid?)null, driver = (string?)null,
                             lat = (double?)null, lon = (double?)null, speedMps = (float?)null, heading = (float?)null, lastTs = (DateTimeOffset?)null };

            var stale = a.trip.LastTs is null || now - a.trip.LastTs > TimeSpan.FromMinutes(options.LiveStaleMinutes);
            return new { truckId = t.Id, plate = t.PlateDisplay, state = stale ? "no_signal" : "live", tripId = (Guid?)a.trip.Id,
                         driver = (string?)a.driverName, lat = a.trip.LastLat, lon = a.trip.LastLon,
                         speedMps = a.trip.LastSpeedMps, heading = a.trip.LastHeading, lastTs = a.trip.LastTs };
        });

        return Results.Ok(result);
    }

    /// <summary>The GPS track of one trip (within the retention window), for replay on the map.</summary>
    private static async Task<IResult> TripTrack(Guid orgId, Guid tripId, PortSyncsDbContext db, CancellationToken ct)
    {
        var exists = await db.Trips.AnyAsync(t => t.Id == tripId && t.OrganizationId == orgId, ct);
        if (!exists) return Results.NotFound();

        var points = await db.Database.SqlQuery<TrackPoint>($"""
            SELECT ts AS "Ts", lat AS "Lat", lon AS "Lon", speed_mps AS "SpeedMps"
            FROM positions
            WHERE trip_id = {tripId}
            ORDER BY ts
            LIMIT 20000
            """).ToListAsync(ct);

        return Results.Ok(new { tripId, points });
    }

    private static object TripDto(Trip t, string plate) => new
    {
        t.Id, t.Status, t.OrganizationId, t.TruckId, plate, t.StartedAt, t.ConsentAt,
        last = t.LastTs is null ? null : new { ts = t.LastTs, lat = t.LastLat, lon = t.LastLon },
    };

    private static Task Publish(IEventBus bus, string type, Trip trip, DateTimeOffset now, object payload, CancellationToken ct) =>
        bus.PublishAsync(new EventEnvelope(
            EventId: Guid.NewGuid(),
            Type: type,
            SchemaVersion: 1,
            OccurredAt: now,
            RecordedAt: now,
            Source: new EventSource("driver_app", null, trip.OrganizationId.ToString()),
            Subject: new EventSubject(TruckId: trip.TruckId.ToString(), DriverId: trip.DriverId.ToString(), TripId: trip.Id.ToString()),
            Location: trip.LastLat is { } lat && trip.LastLon is { } lon ? new EventLocation(lat, lon, null, null) : null,
            CorrelationId: trip.Id.ToString(),
            IdempotencyKey: $"{type}:{trip.Id}",
            Payload: JsonSerializer.SerializeToElement(payload)), ct);

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
