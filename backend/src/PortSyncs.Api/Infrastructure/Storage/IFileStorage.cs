namespace PortSyncs.Api.Infrastructure.Storage;

/// <summary>
/// Evidence photos, EIR photos, proof-of-delivery and documents. H1 stores on the VPS disk;
/// Azure Blob or S3 replace it behind this interface (T75, T38).
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default);
    Task<Stream?> OpenAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public sealed class StorageOptions
{
    public string Provider { get; set; } = "Local";
    public string LocalRoot { get; set; } = "/data/files";
}

public sealed class LocalDiskFileStorage(StorageOptions options) : IFileStorage
{
    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{now:dd}/{Guid.NewGuid():N}";
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        var path = FullPath(key);
        Stream? stream = File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = FullPath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string FullPath(string key)
    {
        var root = Path.GetFullPath(options.LocalRoot);
        var full = Path.GetFullPath(Path.Combine(root, key));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        return full;
    }
}
