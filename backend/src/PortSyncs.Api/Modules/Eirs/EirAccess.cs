using Microsoft.EntityFrameworkCore;
using PortSyncs.Api.Database;

namespace PortSyncs.Api.Modules.Eirs;

/// <summary>Who may see an EIR: the depot that issued it, the trucking company, and the truck's driver.</summary>
public static class EirAccess
{
    public static async Task<bool> CanViewAsync(PortSyncsDbContext db, Guid userId, Eir eir, CancellationToken ct)
    {
        var myOrgs = db.Memberships.Where(m => m.UserId == userId && m.Status == "active").Select(m => m.OrganizationId);

        var operatorOrg = await db.Facilities.Where(f => f.Id == eir.FacilityId).Select(f => f.OperatorOrgId).FirstOrDefaultAsync(ct);
        if (operatorOrg is { } op && await myOrgs.AnyAsync(o => o == op, ct)) return true;

        if (eir.TruckingOrgId is { } trucking &&
            await db.Memberships.AnyAsync(m => m.UserId == userId && m.OrganizationId == trucking && m.Status == "active"
                                              && m.Role != Roles.Driver, ct))
            return true;

        return eir.DriverUserId == userId || await DrivesVisitTruckAsync(db, userId, eir.FacilityVisitId, ct);
    }

    /// <summary>True when the user is the driver of the truck in that gate visit (assigned or on an active trip).</summary>
    public static async Task<bool> DrivesVisitTruckAsync(PortSyncsDbContext db, Guid userId, Guid visitId, CancellationToken ct)
    {
        var truckId = await db.FacilityVisits.Where(v => v.Id == visitId).Select(v => v.TruckId).FirstOrDefaultAsync(ct);
        if (truckId is null) return false;
        return await TruckIdsDrivenBy(db, userId).ContainsAsync(truckId.Value, ct);
    }

    /// <summary>Trucks the user currently drives: open assignment to one of their driver records, or an active trip.</summary>
    public static IQueryable<Guid> TruckIdsDrivenBy(PortSyncsDbContext db, Guid userId) =>
        (from a in db.TruckAssignments
         join d in db.Drivers on a.DriverId equals d.Id
         where d.UserId == userId && d.Status == "active" && a.ToAt == null
         select a.TruckId)
        .Union(db.Trips.Where(t => t.UserId == userId && t.Status == TripStatus.Active).Select(t => t.TruckId));
}
