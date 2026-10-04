using Microsoft.Extensions.Options;
using Saakh.Api.Configuration;

namespace Saakh.Api.Services;

public record StoredFile(string StorageKey, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// Evidence-document storage behind an interface so the local Docker volume can be swapped
/// for a managed provider without touching controller code (tech-stack.md).
/// </summary>
public interface IFileStorageService
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}

public class LocalDiskFileStorage : IFileStorageService
{
    private readonly string _root;
    private readonly ILogger<LocalDiskFileStorage> _log;

    public LocalDiskFileStorage(IOptions<StorageOptions> options, IHostEnvironment env,
        ILogger<LocalDiskFileStorage> log)
    {
        _log = log;
        var configured = options.Value.EvidenceRoot;
        _root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType,
        CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        // Date-partitioned so a directory never grows past a browsable size.
        var folder = DateTimeOffset.UtcNow.ToString("yyyy/MM");
        var key = $"{folder}/{Guid.NewGuid():N}{extension}".Replace('\\', '/');

        var absolute = ResolveOrThrow(key);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        await using var file = File.Create(absolute);
        await content.CopyToAsync(file, ct);

        _log.LogInformation("Stored evidence document {Key} ({Size} bytes)", key, file.Length);
        return new StoredFile(key, Path.GetFileName(fileName), contentType, file.Length);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var absolute = ResolveOrThrow(storageKey);
        if (!File.Exists(absolute))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(File.OpenRead(absolute));
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var absolute = ResolveOrThrow(storageKey);
        if (File.Exists(absolute))
        {
            File.Delete(absolute);
        }

        return Task.CompletedTask;
    }

    /// <summary>Keeps a stored key from escaping the evidence root via traversal segments.</summary>
    private string ResolveOrThrow(string storageKey)
    {
        var absolute = Path.GetFullPath(Path.Combine(_root, storageKey));
        var rootFull = Path.GetFullPath(_root);

        if (!absolute.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key resolves outside the evidence root.");
        }

        return absolute;
    }
}
