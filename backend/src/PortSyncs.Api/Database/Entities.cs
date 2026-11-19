namespace PortSyncs.Api.Database;

// Mapped onto the tables created by Database/Migrations/0001_core_fleet.sql.
// Column names are the snake_case of the property names (see PortSyncsDbContext).

public sealed class Organization
{
    public Guid Id { get; set; }
    public string Type { get; set; } = OrganizationTypes.TruckingCompany;
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class User
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string Name { get; set; } = "";
    public string Locale { get; set; } = "en";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Role { get; set; } = "";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Driver
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? UserId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? LicenceNo { get; set; }
    public string? LicenceCountry { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Truck
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string PlateCountry { get; set; } = "";
    public string PlateNormalized { get; set; } = "";
    public string PlateDisplay { get; set; } = "";
    public string QrToken { get; set; } = "";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TruckAssignment
{
    public Guid Id { get; set; }
    public Guid TruckId { get; set; }
    public Guid DriverId { get; set; }
    public DateTimeOffset FromAt { get; set; }
    public DateTimeOffset? ToAt { get; set; }
}

public sealed class OtpChallenge
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public int Attempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? ClientIp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public string? DeviceName { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Trip
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid TruckId { get; set; }
    public Guid DriverId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = TripStatus.Active;
    public DateTimeOffset ConsentAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public DateTimeOffset? LastTs { get; set; }
    public double? LastLat { get; set; }
    public double? LastLon { get; set; }
    public float? LastSpeedMps { get; set; }
    public float? LastHeading { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Facility
{
    public Guid Id { get; set; }
    public string Type { get; set; } = FacilityTypes.Depot;
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string? City { get; set; }
    public string? Address { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public int? RadiusM { get; set; }
    public Guid? OperatorOrgId { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ExpectedVisit
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public Guid TruckingOrgId { get; set; }
    public Guid? TruckId { get; set; }
    public string? ContainerNumber { get; set; }
    public string Purpose { get; set; } = "";
    public DateTimeOffset WindowFrom { get; set; }
    public DateTimeOffset WindowTo { get; set; }
    public string Status { get; set; } = "expected";
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FacilityVisit
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? TruckId { get; set; }
    public string PlateCountry { get; set; } = "";
    public string PlateNormalized { get; set; } = "";
    public Guid? TruckingOrgId { get; set; }
    public Guid? ExpectedVisitId { get; set; }
    public string? ContainerIn { get; set; }
    public string? ContainerOut { get; set; }
    public DateTimeOffset GateInAt { get; set; }
    public DateTimeOffset? GateOutAt { get; set; }
    public string Method { get; set; } = "manual";
    public Guid? RecordedInBy { get; set; }
    public Guid? RecordedOutBy { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset Ts { get; set; }
    public Guid? UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Detail { get; set; }
}

public sealed class FileRecord
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public string StorageKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTimeOffset? RetentionUntil { get; set; }
    public Guid? UploadedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Eir
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public Guid FacilityVisitId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? TruckingOrgId { get; set; }
    public string ContainerNumber { get; set; } = "";
    public string Direction { get; set; } = "in";
    public string Condition { get; set; } = "sound";
    public string? SealNumber { get; set; }
    public string? Remarks { get; set; }
    public string Status { get; set; } = EirStatus.Draft;
    public Guid RecordedBy { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public Guid? DriverUserId { get; set; }
    public DateTimeOffset? DriverRespondedAt { get; set; }
    public string? DriverComment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EirDamageMark
{
    public Guid Id { get; set; }
    public Guid EirId { get; set; }
    public string Side { get; set; } = "";
    public string? Position { get; set; }
    public string DamageType { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EirPhoto
{
    public Guid EirId { get; set; }
    public Guid FileId { get; set; }
    public string Party { get; set; } = "facility";
    public string? Side { get; set; }
    public Guid AddedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class EirStatus
{
    public const string Draft = "draft";
    public const string Issued = "issued";
    public const string Confirmed = "confirmed";
    public const string Disputed = "disputed";
}

public static class EirVocabulary
{
    public static readonly string[] Sides = ["front", "rear", "left", "right", "roof", "floor", "door", "interior"];
    public static readonly string[] DamageTypes = ["dent", "hole", "cut", "bent", "rust", "crack", "missing_part", "dirty", "other"];
}

public static class FacilityTypes
{
    public const string Depot = "depot";
    public const string Terminal = "terminal";
}

public static class VisitPurposes
{
    public static readonly string[] All = ["empty_pickup", "empty_return", "full_pickup", "full_delivery"];
}

public static class TripStatus
{
    public const string Active = "active";
    public const string Ended = "ended";
}

public static class OrganizationTypes
{
    public const string TruckingCompany = "trucking_company";
    public const string Depot = "depot";
    public const string Terminal = "terminal";
    public const string Broker = "broker";
    public const string Platform = "platform";
}

public static class Roles
{
    public const string PlatformAdmin = "platform_admin";
    public const string CompanyAdmin = "company_admin";
    public const string Dispatcher = "dispatcher";
    public const string Driver = "driver";
    public const string DepotAdmin = "depot_admin";
    public const string GateOfficer = "gate_officer";
}
