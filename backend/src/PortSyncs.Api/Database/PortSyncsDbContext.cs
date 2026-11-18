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

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Organization>().ToTable("organizations");
        model.Entity<User>().ToTable("users");
        model.Entity<Membership>().ToTable("memberships");
        model.Entity<Driver>().ToTable("drivers");
        model.Entity<Truck>().ToTable("trucks");
        model.Entity<TruckAssignment>().ToTable("truck_assignments");

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
