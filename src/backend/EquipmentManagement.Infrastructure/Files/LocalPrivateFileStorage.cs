using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.Infrastructure.Files;

/// <summary>
/// Local-disk implementation of <see cref="IPrivateFileStorage"/> for
/// development and single-node deployments (docs/PROJECT_SPEC.md, section 17:
/// "object storage compatível com Azure Blob Storage/S3" is the target for
/// production; this is the local-dev equivalent behind the same interface — see ADR-033). Every <paramref name="storageKey"/> is
/// validated to resolve strictly inside <see cref="_rootPath"/> before any I/O,
/// closing off path traversal even though callers only ever pass
/// server-generated keys (see <c>EquipmentHandoverService.BuildStorageKey</c>).
/// </summary>
public sealed class LocalPrivateFileStorage : IPrivateFileStorage
{
    private readonly string _rootPath;

    public LocalPrivateFileStorage(IOptions<PrivateStorageOptions> options, IHostEnvironment environment)
    {
        var configuredRoot = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);

        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves <paramref name="storageKey"/> to an absolute path and verifies
    /// it lands inside <see cref="_rootPath"/> — a defense-in-depth check, since
    /// every real caller already builds the key from a GUID, never from
    /// user-supplied input (docs/PROJECT_SPEC.md, section 23: "nunca confiar no
    /// nome original para path").
    /// </summary>
    private string ResolvePath(string storageKey)
    {
        var combined = Path.GetFullPath(Path.Combine(_rootPath, storageKey));
        var normalizedRoot = Path.GetFullPath(_rootPath) + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved storage path escapes the private storage root.");
        }

        return combined;
    }
}
