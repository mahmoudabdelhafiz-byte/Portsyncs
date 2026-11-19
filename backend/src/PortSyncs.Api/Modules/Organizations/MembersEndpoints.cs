using Microsoft.EntityFrameworkCore;
using PortSyncs.Api.Database;
using PortSyncs.Api.Modules.Auth;

namespace PortSyncs.Api.Modules.Organizations;

/// <summary>
/// Staff of an organization (T29 for companies, gate officers for depots).
/// Only an admin of that organization can add or remove people. The person logs in
/// with their own phone (T20) and finds the membership already there.
/// </summary>
public static class MembersEndpoints
{
    // Which roles an admin may hand out, per organization type.
    private static readonly Dictionary<string, string[]> AssignableRoles = new()
    {
        [OrganizationTypes.TruckingCompany] = [Roles.CompanyAdmin, Roles.Dispatcher],
        [OrganizationTypes.Depot] = [Roles.DepotAdmin, Roles.GateOfficer],
    };

    public static void MapMembers(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("/api/v1/organizations/{orgId:guid}/members").WithTags("Organizations")
            .RequireOrgRole(Roles.CompanyAdmin, Roles.DepotAdmin);

        members.MapGet("/", List);
        members.MapPost("/", Add);
        members.MapDelete("/{membershipId:guid}", Remove);
    }

    public sealed record AddMemberRequest(string Phone, string Name, string Role);

    private static async Task<IResult> List(Guid orgId, PortSyncsDbContext db, CancellationToken ct)
    {
        var list = await (
                from m in db.Memberships
                join u in db.Users on m.UserId equals u.Id
                where m.OrganizationId == orgId && m.Status == "active" && m.Role != Roles.Driver
                orderby u.Name
                select new { membershipId = m.Id, userId = u.Id, u.Name, u.Phone, m.Role })
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> Add(Guid orgId, AddMemberRequest req, PortSyncsDbContext db, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return Results.NotFound();

        if (!AssignableRoles.TryGetValue(org.Type, out var allowed) || !allowed.Contains(req.Role))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = [$"Allowed roles: {string.Join(", ", allowed ?? Array.Empty<string>())}."] });
        if (!AuthEndpoints.IsPhone(req.Phone))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["phone"] = ["Use international format, e.g. +966501234567."] });
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == req.Phone, ct);
        if (user is null)
        {
            user = new User { Phone = req.Phone, Name = req.Name.Trim() };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        var existing = await db.Memberships.FirstOrDefaultAsync(
            m => m.UserId == user.Id && m.OrganizationId == orgId && m.Role == req.Role, ct);
        if (existing is null)
        {
            existing = new Membership { UserId = user.Id, OrganizationId = orgId, Role = req.Role };
            db.Memberships.Add(existing);
        }
        else
        {
            existing.Status = "active";
        }
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { membershipId = existing.Id, userId = user.Id, user.Name, user.Phone, existing.Role });
    }

    private static async Task<IResult> Remove(Guid orgId, Guid membershipId, HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        var m = await db.Memberships.FirstOrDefaultAsync(x => x.Id == membershipId && x.OrganizationId == orgId, ct);
        if (m is null) return Results.NotFound();

        // Never leave an organization without an admin.
        if (m.Role is Roles.CompanyAdmin or Roles.DepotAdmin)
        {
            var admins = await db.Memberships.CountAsync(
                x => x.OrganizationId == orgId && x.Role == m.Role && x.Status == "active", ct);
            if (admins <= 1) return Results.Conflict(new { error = "last_admin" });
        }

        m.Status = "removed";
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
