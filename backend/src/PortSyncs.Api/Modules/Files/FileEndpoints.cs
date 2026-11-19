using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PortSyncs.Api.Database;
using PortSyncs.Api.Infrastructure.Storage;
using PortSyncs.Api.Modules.Auth;
using PortSyncs.Api.Modules.Eirs;

namespace PortSyncs.Api.Modules.Files;

/// <summary>
/// Evidence files (T38): EIR photos now; proof-of-delivery photos (T82) and documents (T86) next.
/// Files are checked by their actual bytes (not the name the phone gives them), limited in size,
/// stored behind IFileStorage, and served only to people allowed to see them.
/// </summary>
public static class FileEndpoints
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public static void MapFiles(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1/organizations/{orgId:guid}/files").WithTags("Files")
            .RequireOrgRole(Roles.CompanyAdmin, Roles.Dispatcher, Roles.Driver, Roles.DepotAdmin, Roles.GateOfficer)
            .MapPost("/", Upload)
            .DisableAntiforgery(); // bearer-token API, no cookies

        app.MapGet("/api/v1/files/{fileId:guid}", Download).WithTags("Files").RequireAuthorization();
    }

    private static async Task<IResult> Upload(
        Guid orgId, IFormFile file, HttpContext http, PortSyncsDbContext db, IFileStorage storage, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();
        if (file.Length is 0 or > MaxBytes)
            return Results.BadRequest(new { error = "file_size", maxBytes = MaxBytes });

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        var contentType = Sniff(bytes);
        if (contentType is null)
            return Results.BadRequest(new { error = "file_type", allowed = new[] { "jpeg", "png", "webp", "heic", "pdf" } });

        var key = await storage.SaveAsync(new MemoryStream(bytes, writable: false), contentType, ct);
        var record = new FileRecord
        {
            OrganizationId = orgId,
            StorageKey = key,
            ContentType = contentType,
            SizeBytes = bytes.LongLength,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            UploadedBy = userId,
        };
        db.Files.Add(record);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/files/{record.Id}", new { fileId = record.Id, record.ContentType, record.SizeBytes, record.Sha256 });
    }

    private static async Task<IResult> Download(
        Guid fileId, HttpContext http, PortSyncsDbContext db, IFileStorage storage, CancellationToken ct)
    {
        if (http.User.UserId() is not { } userId) return Results.Unauthorized();

        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null || !await CanViewAsync(db, userId, file, ct)) return Results.NotFound();

        var stream = await storage.OpenAsync(file.StorageKey, ct);
        if (stream is null) return Results.NotFound();

        http.Response.Headers.CacheControl = "private, max-age=3600";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Stream(stream, file.ContentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// A file is visible to members of the organization that uploaded it, and to anyone who
    /// can see an EIR it is attached to (the depot, the trucking company, the driver).
    /// </summary>
    private static async Task<bool> CanViewAsync(PortSyncsDbContext db, Guid userId, FileRecord file, CancellationToken ct)
    {
        if (file.UploadedBy == userId) return true;
        if (file.OrganizationId is { } orgId &&
            await db.Memberships.AnyAsync(m => m.UserId == userId && m.OrganizationId == orgId && m.Status == "active", ct))
            return true;

        var eirIds = await db.EirPhotos.Where(p => p.FileId == file.Id).Select(p => p.EirId).ToListAsync(ct);
        foreach (var eirId in eirIds)
        {
            var eir = await db.Eirs.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eirId, ct);
            if (eir is not null && await EirAccess.CanViewAsync(db, userId, eir, ct)) return true;
        }
        return false;
    }

    /// <summary>Detects the real file type from its first bytes.</summary>
    public static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (b.Length >= 12 && Ascii(b[..4]) == "RIFF" && Ascii(b[8..12]) == "WEBP") return "image/webp";
        if (b.Length >= 12 && Ascii(b[4..8]) == "ftyp" && Ascii(b[8..12]) is "heic" or "heix" or "hevc" or "mif1" or "msf1") return "image/heic";
        if (b.Length >= 5 && Ascii(b[..5]) == "%PDF-") return "application/pdf";
        return null;
    }

    private static string Ascii(ReadOnlySpan<byte> b) => Encoding.ASCII.GetString(b);
}
