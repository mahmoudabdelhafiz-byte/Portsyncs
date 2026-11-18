using System.Text.Json;

namespace PortSyncs.Api.Events;

/// <summary>Standard event envelope (T12). See docs/architecture/event-model.md.</summary>
public sealed record EventEnvelope(
    Guid EventId,
    string Type,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EventSource Source,
    EventSubject Subject,
    EventLocation? Location,
    string? CorrelationId,
    string? IdempotencyKey,
    JsonElement Payload);

public sealed record EventSource(string Kind, string? ConnectorId, string? TenantId);

public sealed record EventSubject(
    string? TruckId = null,
    PlateRef? Plate = null,
    string? DriverId = null,
    string? ContainerNumber = null,
    string? FacilityId = null,
    string? TripId = null);

public sealed record PlateRef(string Country, string Normalized);

public sealed record EventLocation(double Lat, double Lon, double? AccuracyM, string? GeofenceId);

public static class EventTypes
{
    public const string TripStarted = "trip.started";
    public const string TripEnded = "trip.ended";
    public const string TripStopStatusChanged = "trip.stop_status_changed";
    public const string FacilityArrived = "facility.arrived";
    public const string FacilityGateIn = "facility.gate_in";
    public const string FacilityGateOut = "facility.gate_out";
    public const string FacilityWaitMeasured = "facility.wait_measured";
    public const string VisitExpected = "visit.expected";
    public const string EirRecorded = "eir.recorded";
    public const string EirConfirmed = "eir.confirmed";
    public const string EirDisputed = "eir.disputed";
    public const string ContainerPhotosTaken = "container.photos_taken";
    public const string ContainerStatusUpdated = "container.status_updated";
    public const string CaseOpened = "case.opened";
    public const string CaseEscalated = "case.escalated";
    public const string CaseAcknowledged = "case.acknowledged";
    public const string CaseDisputed = "case.disputed";
    public const string CaseClosed = "case.closed";
    public const string ContactRequested = "contact.requested";
    public const string DocumentExpiring = "document.expiring";
}
