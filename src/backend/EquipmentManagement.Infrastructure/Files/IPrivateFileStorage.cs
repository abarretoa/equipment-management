namespace EquipmentManagement.Infrastructure.Files;

/// <summary>
/// Decouples the domain/application from where private binary evidence
/// (delivery inspection photos, and future PDF terms) actually lives —
/// filesystem today, Azure Blob/S3-compatible object storage in production
/// (docs/PROJECT_SPEC.md, sections 17, 23). No implementation of this
/// interface ever trusts a caller-supplied name as a storage path — see
/// <see cref="LocalPrivateFileStorage"/>.
/// </summary>
public interface IPrivateFileStorage
{
    /// <summary>Persists <paramref name="content"/> under a server-generated <paramref name="storageKey"/>, never the caller's original filename.</summary>
    Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Returns null if no object exists at <paramref name="storageKey"/>.</summary>
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Best-effort: deleting an object that doesn't exist is not an error.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
