using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PortSyncs.Api.Database;

namespace PortSyncs.Api.Modules.Auth;

/// <summary>
/// Tenant authorization (T16): every endpoint under /{orgId} checks that the signed-in user
/// has an active membership with one of the allowed roles in that organization.
/// </summary>
public static class OrgAccess
{
    public static Guid? UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null;

    public static Task<bool> HasRoleAsync(
        PortSyncsDbContext db, Guid userId, Guid orgId, IReadOnlyCollection<string> roles, CancellationToken ct) =>
        db.Memberships.AnyAsync(m =>
            m.UserId == userId && m.OrganizationId == orgId && m.Status == "active" && roles.Contains(m.Role), ct);

    /// <summary>Adds a filter that reads {orgId} from the route and requires one of <paramref name="roles"/>.</summary>
    public static RouteGroupBuilder RequireOrgRole(this RouteGroupBuilder group, params string[] roles)
    {
        group.RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (http.User.UserId() is not { } userId) return Results.Unauthorized();
            if (!Guid.TryParse(http.Request.RouteValues["orgId"]?.ToString(), out var orgId)) return Results.NotFound();

            var db = http.RequestServices.GetRequiredService<PortSyncsDbContext>();
            return await HasRoleAsync(db, userId, orgId, roles, http.RequestAborted)
                ? await next(ctx)
                : Results.Forbid();
        });
        return group;
    }
}
