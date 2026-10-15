using System.Security.Cryptography;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Files;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Infrastructure.Auditing;
using EquipmentManagement.Infrastructure.Common;
using EquipmentManagement.Infrastructure.Files;
using EquipmentManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EquipmentManagement.Infrastructure.ResponsibilityTerms;

/// <summary>
/// Registers the externally-signed PDF HR received back for exactly one
/// <see cref="ResponsibilityTerm"/> version (Phase 7 scope,
/// 42-49). Never touches <see cref="ResponsibilityTerm.StorageKey"/> — the
/// original generated PDF is never replaced. Recording a signed artifact
/// means only "an authorized internal user registered this PDF as the signed
/// returned document" — never a claim of cryptographic signature
/// verification (Phase 7 scope).
/// </summary>
public sealed class ResponsibilityTermSignedArtifactService(
    ApplicationDbContext dbContext,
    AuditService auditService,
    IPrivateFileStorage fileStorage,
    IOptions<PrivateStorageOptions> storageOptions,
    TimeProvider timeProvider)
{
    public async Task<ResponsibilityTermSignedArtifactRecord?> GetAsync(Guid termId, CancellationToken cancellationToken = default)
    {
        var artifact = await dbContext.ResponsibilityTermSignedArtifacts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ResponsibilityTermId == termId, cancellationToken);
        return artifact is null ? null : ToRecord(artifact);
    }

    /// <summary>Streams the private signed PDF and audits the access — same discipline as <c>ResponsibilityTermService.GetContentAsync</c> for the original (Phase 7 scope).</summary>
    public async Task<(Stream Content, string ContentType, string FileName)?> GetContentAsync(
        Guid termId, Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var artifact = await dbContext.ResponsibilityTermSignedArtifacts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ResponsibilityTermId == termId, cancellationToken);
        if (artifact is null)
        {
            return null;
        }

        var stream = await fileStorage.OpenReadAsync(artifact.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        await auditService.RecordAsync(new AuditEntry(
            AuditActions.ResponsibilityTerm.SignedDocumentDownloaded,
            EntityType: "ResponsibilityTerm",
            EntityId: termId.ToString(),
            ActorUserId: actorUserId,
            ActorDisplayNameSnapshot: actorDisplayName,
            NewValues: new { SignedArtifactId = artifact.Id }), cancellationToken);

        return (stream, artifact.ContentType, artifact.FileName);
    }

    /// <summary>
    /// Uploads and registers the signed PDF. Allowed against any existing
    /// term — current or Superseded (Phase 7 scope: a late,
    /// truthful signed return for a superseded version must not be rejected
    /// or silently reattached to a newer one). Only PDF, validated by magic
    /// bytes, is accepted (Phase 7 scope).
    /// </summary>
    public async Task<ServiceResult<ResponsibilityTermSignedArtifactRecord>> UploadAsync(
        Guid termId, Stream content, string declaredContentType, DateOnly? signedOn,
        Guid actorUserId, string actorDisplayName, CancellationToken cancellationToken = default)
    {
        var term = await dbContext.ResponsibilityTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, cancellationToken);
        if (term is null)
        {
            return ServiceResult.Fail<ResponsibilityTermSignedArtifactRecord>(ServiceErrorCode.NotFound, "Responsibility Term not found.");
        }

        // A carrier term may be prepared after dispatch, but its signed copy
        // is only registered once physical receipt is confirmed.
        var handoverStatus = await dbContext.EquipmentHandovers.AsNoTracking()
            .Where(h => h.Id == term.EquipmentHandoverId)
            .Select(h => h.Status)
            .FirstAsync(cancellationToken);
        if (!ResponsibilityTermRules.AllowsSignatureWorkflow(handoverStatus))
        {
            return ServiceResult.Fail<ResponsibilityTermSignedArtifactRecord>(
                ServiceErrorCode.HandoverNotFinalized, "O termo assinado só pode ser carregado depois de confirmada a entrega dos equipamentos.");
        }

        var alreadyExists = await dbContext.ResponsibilityTermSignedArtifacts.AsNoTracking()
            .AnyAsync(a => a.ResponsibilityTermId == termId, cancellationToken);
        if (!ResponsibilityTermDeliveryRules.CanUploadSignedArtifact(alreadyExists))
        {
            return ServiceResult.Fail<ResponsibilityTermSignedArtifactRecord>(
                ServiceErrorCode.SignedArtifactAlreadyExists, "A signed document has already been uploaded for this Responsibility Term version.");
        }

        if (!string.Equals(declaredContentType.Trim(), "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult.Fail<ResponsibilityTermSignedArtifactRecord>(
                ServiceErrorCode.InvalidFile, "Only PDF files are accepted as a signed Responsibility Term.");
        }

        var maxSize = storageOptions.Value.MaxSignedDocumentSizeBytes;
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0 || buffer.Length > maxSize)
        {
            return ServiceResult.Fail<ResponsibilityTermSignedArtifactRecord>(
                ServiceErrorCode.FileTooLarge, $"Signed document must be between 1 byte and {maxSize} bytes.");
        }

        var bytes = buffer.ToArray();
        if (!PdfSignatureDetector.IsPdf(bytes))
        {
            return ServiceResult.Fail<ResponsibilityTermSignedArtifactRecord>(
                ServiceErrorCode.InvalidFile, "The uploaded file is not a genuine PDF.");
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var storageKey = BuildStorageKey(termId);

        buffer.Position = 0;
        await fileStorage.SaveAsync(storageKey, buffer, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var artifact = new ResponsibilityTermSignedArtifact
        {
            Id = Guid.NewGuid(),
            ResponsibilityTermId = termId,
            StorageKey = storageKey,
            FileName = BuildFileName(term.DocumentNumber, term.Version),
            ContentType = "application/pdf",
            SizeBytes = bytes.LongLength,
            Sha256 = sha256,
            SignedOn = signedOn,
            UploadedAtUtc = now,
            UploadedByUserId = actorUserId,
            UploadedByDisplayNameSnapshot = actorDisplayName,
            CreatedAtUtc = now,
        };

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionIfNoneAsync(cancellationToken);
            dbContext.ResponsibilityTermSignedArtifacts.Add(artifact);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.RecordAsync(new AuditEntry(
                AuditActions.ResponsibilityTerm.SignedDocumentUploaded,
                EntityType: "ResponsibilityTerm",
                EntityId: termId.ToString(),
                ActorUserId: actorUserId,
                ActorDisplayNameSnapshot: actorDisplayName,
                NewValues: new { term.DocumentNumber, term.Version, SignedArtifactId = artifact.Id }), cancellationToken);
            await transaction.CommitIfOwnedAsync(cancellationToken);
        }
        catch (Exception ex) when (PostgresConflictClassifier.IsUniqueViolation(ex))
        {
            // A concurrent upload for the same term won the race between our
            // pre-check and this insert — the unique index is the real
            // backstop (Phase 7 scope). Clean up the file we
            // already wrote so it never becomes an orphan (ADR-033).
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            return ServiceResult.Fail<ResponsibilityTermSignedArtifactRecord>(
                ServiceErrorCode.SignedArtifactAlreadyExists, "A signed document has already been uploaded for this Responsibility Term version.");
        }
        catch
        {
            await fileStorage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }

        return ServiceResult.Ok(ToRecord(artifact));
    }

    private static string BuildStorageKey(Guid termId) => $"responsibility-terms/{termId:N}/signed/{Guid.NewGuid():N}.pdf";

    private static string BuildFileName(string documentNumber, int version) => $"responsibility-term-{documentNumber}-v{version}-signed.pdf";

    private static ResponsibilityTermSignedArtifactRecord ToRecord(ResponsibilityTermSignedArtifact a) => new(
        a.Id, a.ResponsibilityTermId, a.FileName, a.ContentType, a.SizeBytes, a.Sha256, a.SignedOn, a.UploadedAtUtc, a.UploadedByDisplayNameSnapshot);
}
