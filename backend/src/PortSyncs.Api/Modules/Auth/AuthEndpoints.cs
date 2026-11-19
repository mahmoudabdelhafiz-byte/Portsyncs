using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PortSyncs.Api.Database;
using PortSyncs.Api.Infrastructure.Sms;

namespace PortSyncs.Api.Modules.Auth;

/// <summary>
/// T20: login with a one-time code sent by SMS. The same flow serves drivers, dispatchers,
/// company admins and depot staff; what each can do comes from their memberships.
/// </summary>
public static partial class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Auth").RequireRateLimiting("auth");
        auth.MapPost("/otp/request", RequestOtp);
        auth.MapPost("/otp/verify", VerifyOtp);
        auth.MapPost("/refresh", Refresh);
        auth.MapPost("/logout", Logout);

        var me = app.MapGroup("/api/v1/me").WithTags("Auth").RequireAuthorization();
        me.MapGet("/", GetMe);
        me.MapPatch("/", UpdateMe);
    }

    public sealed record OtpRequest(string Phone);
    public sealed record OtpVerify(string Phone, string Code, string? Name, string? DeviceName);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record UpdateMeRequest(string? Name, string? Locale);

    private static async Task<IResult> RequestOtp(
        OtpRequest req, HttpContext http, PortSyncsDbContext db, TokenService tokens,
        ISmsSender sms, AuthOptions options, CancellationToken ct)
    {
        if (!IsPhone(req.Phone))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["phone"] = ["Use international format, e.g. +966501234567."] });

        var now = DateTimeOffset.UtcNow;
        var since = now.AddMinutes(-options.OtpWindowMinutes);
        var recent = await db.OtpChallenges.CountAsync(c => c.Phone == req.Phone && c.CreatedAt > since, ct);
        if (recent >= options.OtpMaxPerWindow)
            return Results.Json(new { error = "too_many_codes", retryAfterMinutes = options.OtpWindowMinutes }, statusCode: 429);

        var code = TokenService.NewOtpCode();
        db.OtpChallenges.Add(new OtpChallenge
        {
            Phone = req.Phone,
            CodeHash = tokens.HashOtp(req.Phone, code),
            ExpiresAt = now.AddMinutes(options.OtpMinutes),
            ClientIp = http.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync(ct);

        await sms.SendAsync(req.Phone, $"Your PortSyncs code is {code}. It expires in {options.OtpMinutes} minutes.", ct);

        // Same answer whether or not the phone is registered, so the endpoint reveals nothing.
        return Results.Accepted(value: new { expiresInSeconds = options.OtpMinutes * 60 });
    }

    private static async Task<IResult> VerifyOtp(
        OtpVerify req, PortSyncsDbContext db, TokenService tokens, AuthOptions options, CancellationToken ct)
    {
        if (!IsPhone(req.Phone) || req.Code is not { Length: 6 } || !req.Code.All(char.IsAsciiDigit))
            return Invalid("invalid_code");

        var now = DateTimeOffset.UtcNow;
        var challenge = await db.OtpChallenges
            .Where(c => c.Phone == req.Phone && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (challenge is null) return Invalid("code_expired");

        if (challenge.Attempts >= options.OtpMaxAttempts)
        {
            challenge.ConsumedAt = now;
            await db.SaveChangesAsync(ct);
            return Invalid("too_many_attempts");
        }

        challenge.Attempts++;
        if (!tokens.OtpMatches(req.Phone, req.Code, challenge.CodeHash))
        {
            await db.SaveChangesAsync(ct);
            return Invalid("invalid_code");
        }
        challenge.ConsumedAt = now;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // First login creates the person. Drivers added by a company (T26) are linked by phone.
        var driverRecords = await db.Drivers
            .Where(d => d.Phone == req.Phone && d.Status == "active")
            .ToListAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == req.Phone, ct);
        var isNewUser = user is null;
        if (user is null)
        {
            var name = !string.IsNullOrWhiteSpace(req.Name) ? req.Name.Trim()
                : driverRecords.FirstOrDefault()?.Name ?? "";
            user = new User { Phone = req.Phone, Name = name };
            db.Users.Add(user);
        }
        await db.SaveChangesAsync(ct);

        foreach (var driver in driverRecords.Where(d => d.UserId is null))
            driver.UserId = user.Id;

        var driverOrgs = driverRecords.Select(d => d.OrganizationId).Distinct().ToList();
        var existing = await db.Memberships
            .Where(m => m.UserId == user.Id && m.Role == Roles.Driver && driverOrgs.Contains(m.OrganizationId))
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);
        foreach (var orgId in driverOrgs.Except(existing))
            db.Memberships.Add(new Membership { UserId = user.Id, OrganizationId = orgId, Role = Roles.Driver });

        var (access, accessExpires) = tokens.CreateAccessToken(user.Id, user.Phone);
        var refresh = TokenService.NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenService.HashRefreshToken(refresh),
            DeviceName = Trim(req.DeviceName, 100),
            ExpiresAt = now.AddDays(options.RefreshTokenDays),
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new
        {
            accessToken = access,
            accessTokenExpiresAt = accessExpires,
            refreshToken = refresh,
            isNewUser,
            user = new { user.Id, user.Name, user.Phone, user.Locale },
        });
    }

    private static async Task<IResult> Refresh(
        RefreshRequest req, PortSyncsDbContext db, TokenService tokens, AuthOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken)) return Results.Unauthorized();

        var hash = TokenService.HashRefreshToken(req.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return Results.Unauthorized();

        var now = DateTimeOffset.UtcNow;
        if (stored.RevokedAt is not null)
        {
            // A rotated token was used again: it may have been stolen. Sign the user out everywhere.
            await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            return Results.Unauthorized();
        }
        if (stored.ExpiresAt <= now) return Results.Unauthorized();

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == stored.UserId, ct);

        var newRefresh = TokenService.NewRefreshToken();
        var replacement = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenService.HashRefreshToken(newRefresh),
            DeviceName = stored.DeviceName,
            ExpiresAt = now.AddDays(options.RefreshTokenDays),
        };
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);

        stored.RevokedAt = now;
        stored.ReplacedBy = replacement.Id;
        await db.SaveChangesAsync(ct);

        var (access, accessExpires) = tokens.CreateAccessToken(user.Id, user.Phone);
        return Results.Ok(new { accessToken = access, accessTokenExpiresAt = accessExpires, refreshToken = newRefresh });
    }

    private static async Task<IResult> Logout(RefreshRequest req, PortSyncsDbContext db, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(req.RefreshToken))
        {
            var hash = TokenService.HashRefreshToken(req.RefreshToken);
            await db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
        }
        return Results.NoContent();
    }

    private static async Task<IResult> GetMe(HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.Unauthorized();

        var memberships = await (
                from m in db.Memberships
                join o in db.Organizations on m.OrganizationId equals o.Id
                where m.UserId == userId && m.Status == "active"
                select new { organizationId = o.Id, organizationName = o.Name, organizationType = o.Type, m.Role })
            .ToListAsync(ct);

        return Results.Ok(new { user.Id, user.Name, user.Phone, user.Email, user.Locale, memberships });
    }

    private static async Task<IResult> UpdateMe(UpdateMeRequest req, HttpContext http, PortSyncsDbContext db, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.Unauthorized();

        if (req.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Enter a name up to 100 characters."] });
            user.Name = req.Name.Trim();
        }
        if (req.Locale is not null)
        {
            if (!LocaleRegex().IsMatch(req.Locale))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["locale"] = ["Use a language code such as en or ar."] });
            user.Locale = req.Locale;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { user.Id, user.Name, user.Phone, user.Locale });
    }

    private static IResult Invalid(string error) => Results.BadRequest(new { error });

    private static string? Trim(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, max)];

    public static bool IsPhone(string? p) => p is not null && PhoneRegex().IsMatch(p);

    [GeneratedRegex(@"^\+[1-9][0-9]{6,14}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Z]{2})?$")]
    private static partial Regex LocaleRegex();
}
