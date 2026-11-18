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
