using System.Text;
using Microsoft.EntityFrameworkCore;

namespace PortSyncs.Api.Database;

public sealed class PortSyncsDbContext(DbContextOptions<PortSyncsDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Truck> Trucks => Set<Truck>();
    public DbSet<TruckAssignment> TruckAssignments => Set<TruckAssignment>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<ExpectedVisit> ExpectedVisits => Set<ExpectedVisit>();
    public DbSet<FacilityVisit> FacilityVisits => Set<FacilityVisit>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Organization>().ToTable("organizations");
        model.Entity<User>().ToTable("users");
        model.Entity<Membership>().ToTable("memberships");
        model.Entity<Driver>().ToTable("drivers");
        model.Entity<Truck>().ToTable("trucks");
        model.Entity<TruckAssignment>().ToTable("truck_assignments");
        model.Entity<OtpChallenge>().ToTable("otp_challenges");
        model.Entity<RefreshToken>().ToTable("refresh_tokens");
        model.Entity<Trip>().ToTable("trips");
        model.Entity<Facility>().ToTable("facilities");
        model.Entity<ExpectedVisit>().ToTable("expected_visits");
        model.Entity<FacilityVisit>().ToTable("facility_visits");
        model.Entity<AuditEntry>().ToTable("audit_log");
        model.Entity<AuditEntry>().Property(a => a.Detail).HasColumnType("jsonb");

        // The schema is owned by the SQL migrations: map every column to snake_case
        // and let the database fill ids and timestamps.
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));

            entity.FindProperty("Id")?.SetValueGenerated(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd);
            var createdAt = entity.FindProperty("CreatedAt");
            if (createdAt is not null)
            {
                createdAt.SetDefaultValueSql("now()");
                createdAt.SetValueGenerated(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd);
            }
        }

        model.Entity<Organization>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<User>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<Membership>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<Driver>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<Truck>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<TruckAssignment>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<OtpChallenge>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<RefreshToken>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<Trip>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<Facility>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<ExpectedVisit>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<FacilityVisit>().Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        model.Entity<AuditEntry>().Property(o => o.Id).UseIdentityAlwaysColumn();
        model.Entity<TruckAssignment>().Property(o => o.FromAt).HasDefaultValueSql("now()");
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
