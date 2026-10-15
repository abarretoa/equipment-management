using EquipmentManagement.Domain.Allocations;
using EquipmentManagement.Domain.Auditing;
using EquipmentManagement.Domain.Companies;
using EquipmentManagement.Domain.Consultants;
using EquipmentManagement.Domain.DataImports;
using EquipmentManagement.Domain.Damages;
using EquipmentManagement.Domain.Equipments;
using EquipmentManagement.Domain.Handovers;
using EquipmentManagement.Domain.Maintenance;
using EquipmentManagement.Domain.ResponsibilityTerms;
using EquipmentManagement.Domain.Returns;
using EquipmentManagement.Domain.Users;
using EquipmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EquipmentManagement.Infrastructure.Persistence;

/// <summary>
/// Identity tables, the audit log (Phase 1), the Company/Consultant/
/// ConsultantAllocation business domain (Phase 2), the EquipmentType/
/// Equipment/LaptopSpecification registry (Phase 3), the
/// EquipmentHandover/EquipmentAssignment/DeliveryInspection/
/// DeliveryInspectionPhoto custody workflow (Phase 4), and the
/// EquipmentReturn/ReturnInspection/Damage/MaintenanceRecord return-and-repair
/// workflow (Phase 5), and the ResponsibilityTerm document-generation
/// workflow (Phase 6).
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    /// <summary>
    /// Backs <c>AssetTagGenerator</c> — a single Postgres sequence shared by
    /// every equipment type, atomically incremented via <c>nextval()</c> (never
    /// <c>MAX(...)+1</c>). See ADR-026.
    /// </summary>
    public const string AssetTagSequenceName = "equipment_asset_tag_seq";

    /// <summary>Backs <c>DocumentNumberGenerator</c> — one global sequence for every Responsibility Term, same rationale as <see cref="AssetTagSequenceName"/>.</summary>
    public const string ResponsibilityTermDocumentNumberSequenceName = "responsibility_term_document_number_seq";

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Consultant> Consultants => Set<Consultant>();

    public DbSet<ConsultantAllocation> ConsultantAllocations => Set<ConsultantAllocation>();

    public DbSet<EquipmentType> EquipmentTypes => Set<EquipmentType>();

    public DbSet<Equipment> Equipment => Set<Equipment>();

    public DbSet<LaptopSpecification> LaptopSpecifications => Set<LaptopSpecification>();

    public DbSet<MobilePhoneSpecification> MobilePhoneSpecifications => Set<MobilePhoneSpecification>();

    public DbSet<EquipmentPhotoSet> EquipmentPhotoSets => Set<EquipmentPhotoSet>();

    public DbSet<EquipmentPhotoSetPhoto> EquipmentPhotoSetPhotos => Set<EquipmentPhotoSetPhoto>();

    public DbSet<EquipmentHandover> EquipmentHandovers => Set<EquipmentHandover>();

    public DbSet<EquipmentHandoverItem> EquipmentHandoverItems => Set<EquipmentHandoverItem>();

    public DbSet<DeliveryInspection> DeliveryInspections => Set<DeliveryInspection>();

    public DbSet<DeliveryInspectionPhoto> DeliveryInspectionPhotos => Set<DeliveryInspectionPhoto>();

    public DbSet<EquipmentAssignment> EquipmentAssignments => Set<EquipmentAssignment>();

    public DbSet<EquipmentReturn> EquipmentReturns => Set<EquipmentReturn>();

    public DbSet<EquipmentReturnGroup> EquipmentReturnGroups => Set<EquipmentReturnGroup>();

    public DbSet<ReturnInspection> ReturnInspections => Set<ReturnInspection>();

    public DbSet<ReturnInspectionPhoto> ReturnInspectionPhotos => Set<ReturnInspectionPhoto>();

    public DbSet<DamagePhoto> DamagePhotos => Set<DamagePhoto>();

    public DbSet<Damage> Damages => Set<Damage>();

    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();

    public DbSet<EquipmentRetirementRequest> EquipmentRetirementRequests => Set<EquipmentRetirementRequest>();

    public DbSet<ResponsibilityTerm> ResponsibilityTerms => Set<ResponsibilityTerm>();

    public DbSet<ResponsibilityTermItem> ResponsibilityTermItems => Set<ResponsibilityTermItem>();

    public DbSet<ResponsibilityTermDelivery> ResponsibilityTermDeliveries => Set<ResponsibilityTermDelivery>();

    public DbSet<ResponsibilityTermSignedArtifact> ResponsibilityTermSignedArtifacts => Set<ResponsibilityTermSignedArtifact>();

    public DbSet<ImportSession> ImportSessions => Set<ImportSession>();

    public DbSet<EquipmentDispatch> EquipmentDispatches => Set<EquipmentDispatch>();

    public DbSet<EquipmentDispatchNotification> EquipmentDispatchNotifications => Set<EquipmentDispatchNotification>();

    public DbSet<ConsultantAllocationCollectionNotification> ConsultantAllocationCollectionNotifications => Set<ConsultantAllocationCollectionNotification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(u => u.FirstName).HasMaxLength(UserProfileRules.MaxNameLength).IsRequired();
            entity.Property(u => u.LastName).HasMaxLength(UserProfileRules.MaxNameLength).IsRequired();
            entity.Property(u => u.Address).HasMaxLength(UserProfileRules.MaxAddressLength);
        });

        builder.Entity<ApplicationRole>(entity => entity.ToTable("Roles"));
        builder.Entity<IdentityUserRole<Guid>>(entity => entity.ToTable("UserRoles"));
        builder.Entity<IdentityUserClaim<Guid>>(entity => entity.ToTable("UserClaims"));
        builder.Entity<IdentityUserLogin<Guid>>(entity => entity.ToTable("UserLogins"));
        builder.Entity<IdentityRoleClaim<Guid>>(entity => entity.ToTable("RoleClaims"));
        builder.Entity<IdentityUserToken<Guid>>(entity => entity.ToTable("UserTokens"));

        builder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Action).HasMaxLength(200).IsRequired();
            entity.Property(a => a.EntityType).HasMaxLength(200).IsRequired();
            entity.Property(a => a.EntityId).HasMaxLength(200);
            entity.Property(a => a.ActorDisplayNameSnapshot).HasMaxLength(400);
            entity.Property(a => a.CorrelationId).HasMaxLength(100);
            entity.Property(a => a.IpAddress).HasMaxLength(64);
            entity.Property(a => a.OldValuesJson).HasColumnType("jsonb");
            entity.Property(a => a.NewValuesJson).HasColumnType("jsonb");
            entity.Property(a => a.MetadataJson).HasColumnType("jsonb");

            // Every filter the audit query API (docs/PROJECT_SPEC.md, section 15) supports, indexed:
            // newest-first sort, actor, action, and entity lookup.
            entity.HasIndex(a => a.OccurredAtUtc);
            entity.HasIndex(a => a.ActorUserId);
            entity.HasIndex(a => a.Action);
            entity.HasIndex(a => new { a.EntityType, a.EntityId });
        });

        builder.Entity<Company>(entity =>
        {
            entity.ToTable("Companies");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.NormalizedName).HasMaxLength(200).IsRequired();

            // The actual duplicate-prevention mechanism (Phase 2 scope): the application pre-checks for
            // a friendly error, but this unique index is what makes the
            // invariant hold under concurrent creation (Infrastructure catches
            // Postgres 23505 as a fallback — see CompanyService).
            entity.HasIndex(c => c.NormalizedName).IsUnique();
        });

        builder.Entity<Consultant>(entity =>
        {
            entity.ToTable("Consultants");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.FullName).HasMaxLength(300).IsRequired();
            entity.Property(c => c.Nif).HasMaxLength(20).IsRequired();
            entity.Property(c => c.Email).HasMaxLength(320).IsRequired();
            entity.Property(c => c.Address).HasMaxLength(500).IsRequired();
            entity.Property(c => c.Phone).HasMaxLength(50).IsRequired();

            // Global uniqueness (active AND archived) per docs/PROJECT_SPEC.md
            // invariant #14 — a real person's tax id must not become reusable
            // just because an old record was archived. See ConsultantService
            // for the matching Postgres 23505 fallback.
            entity.HasIndex(c => c.Nif).IsUnique();
            entity.HasIndex(c => c.IsActive);
        });

        builder.Entity<ConsultantAllocation>(entity =>
        {
            entity.ToTable("ConsultantAllocations");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.RoleOrFunction).HasMaxLength(200).IsRequired();
            entity.Property(a => a.WorkforceType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(a => a.EngagementType).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(a => a.StartDate).HasColumnType("date").IsRequired();
            entity.Property(a => a.EndDate).HasColumnType("date");
            entity.Property(a => a.ExpectedCollectionFromDate).HasColumnType("date");
            entity.Property(a => a.ExpectedCollectionToDate).HasColumnType("date");

            // No navigation properties (see the entity's own remarks) — plain
            // FK constraints only, added via HasOne/WithMany so delete
            // behavior can be pinned to Restrict (Phase 2 scope: history must never disappear as a side
            // effect of deleting the consultant/company it references — not
            // that either is ever hard-deleted through the API anyway).
            entity.HasOne<Consultant>().WithMany().HasForeignKey(a => a.ConsultantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>().WithMany().HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => a.ConsultantId);
            // One Active allocation per Consultant, whatever its dates — the
            // DB-level backstop for AllocationRules.CanCreateActiveAllocation,
            // same partial-unique-index pattern as EquipmentAssignment(EquipmentId)
            // WHERE Status = 'Active'. Named explicitly because a plain index on
            // ConsultantId already exists.
            entity.HasIndex(a => a.ConsultantId, "IX_ConsultantAllocations_ConsultantId_Active")
                .IsUnique()
                .HasFilter("\"Status\" = 'Active'");
            entity.HasIndex(a => a.CompanyId);
            entity.HasIndex(a => a.Status);
            entity.HasIndex(a => a.StartDate);
            entity.HasIndex(a => a.EndDate);

            // The exact shape of the overlap-check and "current allocation"
            // queries (Status = Active AND ConsultantId = X) — see
            // ConsultantAllocationService.
            entity.HasIndex(a => new { a.ConsultantId, a.Status });
        });

        builder.Entity<ConsultantAllocationCollectionNotification>(entity =>
        {
            entity.ToTable("ConsultantAllocationCollectionNotifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.RecipientEmailSnapshot).HasMaxLength(320).IsRequired();
            entity.Property(n => n.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(n => n.FailureCode).HasConversion<string>().HasMaxLength(30);

            // Send-attempt history is permanent, never hard-deleted — same
            // discipline as ResponsibilityTermDelivery.
            entity.HasOne<ConsultantAllocation>().WithMany().HasForeignKey(n => n.ConsultantAllocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(n => n.ConsultantAllocationId);
            entity.HasIndex(n => n.Status);
        });

        // AssetTagGenerator calls nextval() on this directly via raw SQL —
        // see ADR-026 for why a native Postgres
        // sequence (not an app-level counter, and never MAX(...)+1) is the
        // concurrency-safe primitive here.
        builder.HasSequence<long>(AssetTagSequenceName).StartsAt(1).IncrementsBy(1);

        builder.Entity<EquipmentType>(entity =>
        {
            entity.ToTable("EquipmentTypes");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
            entity.Property(t => t.NormalizedName).HasMaxLength(200).IsRequired();
            entity.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(t => t.AssetTagPrefix).HasMaxLength(6).IsRequired();

            // Duplicate-prevention mechanism identical in shape to Company's
            // (application pre-check + this index as the concurrency backstop).
            entity.HasIndex(t => t.NormalizedName).IsUnique();
        });

        builder.Entity<Equipment>(entity =>
        {
            entity.ToTable("Equipment");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AssetTag).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Manufacturer).HasMaxLength(200);
            entity.Property(e => e.Model).HasMaxLength(200);
            entity.Property(e => e.SerialNumber).HasMaxLength(200);
            entity.Property(e => e.NormalizedSerialNumber).HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.PurchaseDate).HasColumnType("date");
            entity.Property(e => e.InvoiceNumber).HasMaxLength(100);
            entity.Property(e => e.PurchaseVendor).HasMaxLength(200);

            // EquipmentType is intentionally not hard-deletable and this FK is
            // Restrict (Phase 3 scope) — deactivating a type never touches existing equipment.
            entity.HasOne<EquipmentType>().WithMany().HasForeignKey(e => e.EquipmentTypeId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.AssetTag).IsUnique();
            entity.HasIndex(e => e.EquipmentTypeId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAtUtc);

            // Partial unique index: only non-null serial numbers must be
            // unique (Phase 3 scope)
            // — most non-laptop equipment may legitimately have none.
            entity.HasIndex(e => e.NormalizedSerialNumber)
                .IsUnique()
                .HasFilter("\"NormalizedSerialNumber\" IS NOT NULL");
        });

        builder.Entity<LaptopSpecification>(entity =>
        {
            entity.ToTable("LaptopSpecifications");
            entity.HasKey(s => s.EquipmentId);
            entity.Property(s => s.Processor).HasMaxLength(200).IsRequired();
            entity.Property(s => s.Memory).HasMaxLength(100).IsRequired();
            entity.Property(s => s.Storage).HasMaxLength(200).IsRequired();

            // A true 1:0..1 extension of the Equipment aggregate (not a
            // separately-managed entity) — Cascade here only deletes the spec
            // row alongside its Equipment row, and Equipment itself is never
            // hard-deleted through any exposed operation, so this never
            // silently destroys equipment history (Phase 3 scope).
            entity.HasOne<Equipment>().WithOne().HasForeignKey<LaptopSpecification>(s => s.EquipmentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MobilePhoneSpecification>(entity =>
        {
            // Defense in depth for MobilePhoneSpecificationRules.IsValidImei:
            // the canonical IMEI is exactly 15 ASCII digits (no check-digit rule).
            entity.ToTable("MobilePhoneSpecifications", tb => tb.HasCheckConstraint(
                "CK_MobilePhoneSpecifications_NormalizedImei_Format",
                "\"NormalizedImei\" ~ '^[0-9]{15}$'"));
            entity.HasKey(s => s.EquipmentId);
            entity.Property(s => s.Imei).HasMaxLength(32).IsRequired();
            entity.Property(s => s.NormalizedImei).HasMaxLength(32).IsRequired();
            entity.Property(s => s.MobileNumber).HasMaxLength(30);

            // One IMEI identifies exactly one mobile phone, product-wide —
            // the database backstop for EquipmentService's own check and the
            // Current Inventory import's (same pattern as
            // IX_Equipment_NormalizedSerialNumber).
            entity.HasIndex(s => s.NormalizedImei).IsUnique();
            entity.Property(s => s.SimSerialNumber).HasMaxLength(30);

            // Same 1:0..1 extension shape as LaptopSpecification.
            entity.HasOne<Equipment>().WithOne().HasForeignKey<MobilePhoneSpecification>(s => s.EquipmentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EquipmentPhotoSet>(entity =>
        {
            entity.ToTable("EquipmentPhotoSets", tb => tb.HasCheckConstraint(
                "CK_EquipmentPhotoSets_Source_ReturnId",
                "(\"Source\" = 'InitialRegistration' AND \"EquipmentReturnId\" IS NULL) OR (\"Source\" = 'Return' AND \"EquipmentReturnId\" IS NOT NULL)"));
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(s => s.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(s => s.CreatedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();

            // Equipment is never hard-deleted (same discipline as every other
            // reference to it) — Restrict.
            entity.HasOne<Equipment>().WithMany().HasForeignKey(s => s.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EquipmentReturn>().WithMany().HasForeignKey(s => s.EquipmentReturnId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(s => s.EquipmentId);
            entity.HasIndex(s => s.EquipmentReturnId);

            // At most one Current set per equipment — identical pattern to
            // ResponsibilityTerm/EquipmentAssignment/MaintenanceRecord's
            // partial unique indexes.
            entity.HasIndex(s => s.EquipmentId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Current'");
        });

        builder.Entity<EquipmentPhotoSetPhoto>(entity =>
        {
            entity.ToTable("EquipmentPhotoSetPhotos", tb => tb.HasCheckConstraint(
                "CK_EquipmentPhotoSetPhotos_SortOrder_Range", "\"SortOrder\" >= 1 AND \"SortOrder\" <= 3"));
            entity.HasKey(p => p.Id);
            entity.Property(p => p.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(p => p.OriginalFileName).HasMaxLength(300).IsRequired();
            entity.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Sha256Hash).HasMaxLength(64).IsRequired();

            entity.HasOne<EquipmentPhotoSet>().WithMany().HasForeignKey(p => p.EquipmentPhotoSetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(p => p.EquipmentPhotoSetId);
            entity.HasIndex(p => new { p.EquipmentPhotoSetId, p.SortOrder }).IsUnique();
        });

        builder.Entity<EquipmentHandover>(entity =>
        {
            entity.ToTable("EquipmentHandovers");
            entity.HasKey(h => h.Id);
            entity.Property(h => h.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(h => h.DeliveredOn).HasColumnType("date");
            // Nullable on purpose: pre-Round-3 handovers have no known method
            // and are never backfilled (see EquipmentHandover.DeliveryMethod).
            entity.Property(h => h.DeliveryMethod).HasConversion<string>().HasMaxLength(20);
            entity.Property(h => h.Notes).HasMaxLength(2000);
            entity.Property(h => h.CreatedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();

            // Consultant is never hard-deleted (docs/PROJECT_SPEC.md, section
            // 16), so Restrict here only ever matters as a defensive guard.
            entity.HasOne<Consultant>().WithMany().HasForeignKey(h => h.ConsultantId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(h => h.ConsultantId);
            entity.HasIndex(h => h.Status);
            entity.HasIndex(h => h.CreatedAtUtc);

            // A true 1:0..1 extension of the handover row (populated only at
            // finalization, see the entity's remarks) — stored as columns on
            // this table, not a separate joined table, since it's always read
            // together with the handover.
            entity.OwnsOne(h => h.ConsultantSnapshot, snapshot =>
            {
                snapshot.Property(s => s.FullName).HasColumnName("Snapshot_ConsultantFullName").HasMaxLength(300).IsRequired();
                snapshot.Property(s => s.Nif).HasColumnName("Snapshot_ConsultantNif").HasMaxLength(20).IsRequired();
                snapshot.Property(s => s.Address).HasColumnName("Snapshot_ConsultantAddress").HasMaxLength(500).IsRequired();
                snapshot.Property(s => s.Phone).HasColumnName("Snapshot_ConsultantPhone").HasMaxLength(50).IsRequired();
                snapshot.Property(s => s.Email).HasColumnName("Snapshot_ConsultantEmail").HasMaxLength(320).IsRequired();
            });

            entity.OwnsOne(h => h.AllocationSnapshot, snapshot =>
            {
                snapshot.Property(s => s.WorkforceType).HasColumnName("Snapshot_WorkforceType").HasMaxLength(20).IsRequired();
                snapshot.Property(s => s.EngagementType).HasColumnName("Snapshot_EngagementType").HasMaxLength(30).IsRequired();
                snapshot.Property(s => s.RoleOrFunction).HasColumnName("Snapshot_RoleOrFunction").HasMaxLength(200).IsRequired();
                snapshot.Property(s => s.CompanyName).HasColumnName("Snapshot_CompanyName").HasMaxLength(200);
            });
        });

        builder.Entity<EquipmentHandoverItem>(entity =>
        {
            entity.ToTable("EquipmentHandoverItems");
            entity.HasKey(i => i.Id);

            entity.HasOne<EquipmentHandover>().WithMany().HasForeignKey(i => i.EquipmentHandoverId).OnDelete(DeleteBehavior.Restrict);

            // Equipment is never hard-deleted, and an equipment can legitimately
            // appear in many handovers over its lifetime (docs/PROJECT_SPEC.md,
            // section 20) — Restrict, not Cascade.
            entity.HasOne<Equipment>().WithMany().HasForeignKey(i => i.EquipmentId).OnDelete(DeleteBehavior.Restrict);

            // The photo set is permanent evidence, never hard-deleted —
            // Restrict, same discipline as every other reference to a
            // permanent historical row. Null for non-photo-required kinds and
            // for any Handover finalized before this column existed.
            entity.HasOne<EquipmentPhotoSet>().WithMany().HasForeignKey(i => i.EquipmentPhotoSetId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(i => i.EquipmentHandoverId);
            entity.HasIndex(i => i.EquipmentId);
            entity.HasIndex(i => i.EquipmentPhotoSetId);

            entity.OwnsOne(i => i.EquipmentSnapshot, snapshot =>
            {
                snapshot.Property(s => s.AssetTag).HasColumnName("Snapshot_AssetTag").HasMaxLength(20).IsRequired();
                snapshot.Property(s => s.EquipmentTypeName).HasColumnName("Snapshot_EquipmentTypeName").HasMaxLength(200).IsRequired();
                snapshot.Property(s => s.Manufacturer).HasColumnName("Snapshot_Manufacturer").HasMaxLength(200);
                snapshot.Property(s => s.Model).HasColumnName("Snapshot_Model").HasMaxLength(200);
                snapshot.Property(s => s.SerialNumber).HasColumnName("Snapshot_SerialNumber").HasMaxLength(200);
                snapshot.Property(s => s.Processor).HasColumnName("Snapshot_Processor").HasMaxLength(200);
                snapshot.Property(s => s.Memory).HasColumnName("Snapshot_Memory").HasMaxLength(100);
                snapshot.Property(s => s.Storage).HasColumnName("Snapshot_Storage").HasMaxLength(200);
                snapshot.Property(s => s.Kind).HasColumnName("Snapshot_Kind").HasMaxLength(20);
                snapshot.Property(s => s.Imei).HasColumnName("Snapshot_Imei").HasMaxLength(32);
                snapshot.Property(s => s.MobileNumber).HasColumnName("Snapshot_MobileNumber").HasMaxLength(30);
                snapshot.Property(s => s.SimSerialNumber).HasColumnName("Snapshot_SimSerialNumber").HasMaxLength(30);
                snapshot.Property(s => s.Notes).HasColumnName("Snapshot_Notes").HasMaxLength(2000);
            });
        });

        builder.Entity<DeliveryInspection>(entity =>
        {
            entity.ToTable("DeliveryInspections");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Condition).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(i => i.Notes).HasMaxLength(2000);
            entity.Property(i => i.InspectedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();

            // At most one inspection per item (docs/PROJECT_SPEC.md, section
            // 10) — Cascade so removing a still-draft item cleans up its
            // inspection and photos without a separate manual delete.
            entity.HasOne<EquipmentHandoverItem>().WithOne().HasForeignKey<DeliveryInspection>(i => i.EquipmentHandoverItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(i => i.EquipmentHandoverItemId).IsUnique();
        });

        builder.Entity<DeliveryInspectionPhoto>(entity =>
        {
            entity.ToTable("DeliveryInspectionPhotos");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(p => p.OriginalFileName).HasMaxLength(300).IsRequired();
            entity.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Sha256Hash).HasMaxLength(64).IsRequired();

            entity.HasOne<DeliveryInspection>().WithMany().HasForeignKey(p => p.DeliveryInspectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(p => p.DeliveryInspectionId);
        });

        builder.Entity<EquipmentAssignment>(entity =>
        {
            // A Handover-origin assignment always references its Handover and
            // HandoverItem; a Current Inventory import assignment never does
            // (no delivery happened, and none is fabricated) — see
            // EquipmentAssignmentOrigin.
            entity.ToTable("EquipmentAssignments", tb => tb.HasCheckConstraint(
                "CK_EquipmentAssignments_Origin_HandoverReferences",
                "(\"Origin\" = 'Handover' AND \"EquipmentHandoverId\" IS NOT NULL AND \"EquipmentHandoverItemId\" IS NOT NULL) OR " +
                "(\"Origin\" = 'CurrentInventoryImport' AND \"EquipmentHandoverId\" IS NULL AND \"EquipmentHandoverItemId\" IS NULL)"));
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(a => a.Origin).HasConversion<string>().HasMaxLength(30).IsRequired();

            entity.HasOne<Equipment>().WithMany().HasForeignKey(a => a.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Consultant>().WithMany().HasForeignKey(a => a.ConsultantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EquipmentHandover>().WithMany().HasForeignKey(a => a.EquipmentHandoverId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EquipmentHandoverItem>().WithOne().HasForeignKey<EquipmentAssignment>(a => a.EquipmentHandoverItemId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => a.EquipmentHandoverItemId).IsUnique();
            entity.HasIndex(a => a.ConsultantId);
            entity.HasIndex(a => a.Status);
            entity.HasIndex(a => a.StartedAtUtc);

            // THE database-enforced backstop for docs/PROJECT_SPEC.md invariant
            // #1 ("um equipamento não pode estar atribuído simultaneamente a
            // dois consultores"): at most one Active assignment row may exist
            // per equipment, no matter what application-level checks miss under
            // concurrency. Mirrors the partial-unique-index pattern already
            // used for Equipment.NormalizedSerialNumber (Phase 3).
            entity.HasIndex(a => a.EquipmentId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Active'");
        });

        builder.Entity<EquipmentDispatch>(entity =>
        {
            // A cancellation is recorded whole or not at all.
            entity.ToTable("EquipmentDispatches", tb => tb.HasCheckConstraint(
                "CK_EquipmentDispatches_Cancellation_Complete",
                "(\"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND \"CancelledByDisplayNameSnapshot\" IS NULL AND \"CancellationReason\" IS NULL) OR " +
                "(\"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CancelledByDisplayNameSnapshot\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL)"));
            entity.HasKey(d => d.Id);
            entity.Property(d => d.ExpectedDeliveryFromDate).HasColumnType("date").IsRequired();
            entity.Property(d => d.ExpectedDeliveryToDate).HasColumnType("date");
            entity.Property(d => d.DispatchedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();
            entity.Property(d => d.CancelledByDisplayNameSnapshot).HasMaxLength(400);
            entity.Property(d => d.CancellationReason).HasMaxLength(EquipmentDispatch.MaxCancellationReasonLength);

            entity.HasOne<EquipmentHandover>().WithMany().HasForeignKey(d => d.EquipmentHandoverId).OnDelete(DeleteBehavior.Restrict);

            // At most one ACTIVE dispatch per handover — an active dispatch
            // cannot be registered twice (ServiceErrorCode.DispatchAlreadyRegistered).
            // Cancelled attempts ("Anular envio") stay as history, so a later
            // dispatch of the same handover is a new row.
            entity.HasIndex(d => d.EquipmentHandoverId).IsUnique().HasFilter("\"CancelledAtUtc\" IS NULL");
        });

        builder.Entity<EquipmentDispatchNotification>(entity =>
        {
            entity.ToTable("EquipmentDispatchNotifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.RecipientEmailSnapshot).HasMaxLength(320).IsRequired();
            entity.Property(n => n.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(n => n.FailureCode).HasConversion<string>().HasMaxLength(30);

            entity.HasOne<EquipmentDispatch>().WithMany().HasForeignKey(n => n.EquipmentDispatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(n => n.EquipmentDispatchId);
            entity.HasIndex(n => n.Status);
        });

        builder.Entity<EquipmentReturn>(entity =>
        {
            entity.ToTable("EquipmentReturns");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(r => r.Reason).HasMaxLength(500);
            entity.Property(r => r.ExpectedReturnDate).HasColumnType("date");
            entity.Property(r => r.ReceivedOn).HasColumnType("date");
            entity.Property(r => r.ReceivedByDisplayNameSnapshot).HasMaxLength(400);
            entity.Property(r => r.CreatedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();
            entity.Property(r => r.CancelledByDisplayNameSnapshot).HasMaxLength(400);
            entity.Property(r => r.CancellationReason).HasMaxLength(500);

            // Equipment/Consultant are never hard-deleted (docs/PROJECT_SPEC.md,
            // section 16); the assignment being returned is itself permanent
            // history (Phase 4) — Restrict everywhere, same discipline as
            // EquipmentAssignment's own FKs.
            entity.HasOne<EquipmentAssignment>().WithMany().HasForeignKey(r => r.EquipmentAssignmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Equipment>().WithMany().HasForeignKey(r => r.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Consultant>().WithMany().HasForeignKey(r => r.ConsultantId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => r.EquipmentId);
            entity.HasIndex(r => r.ConsultantId);
            entity.HasIndex(r => r.Status);
            entity.HasIndex(r => r.CreatedAtUtc);

            // THE database-enforced backstop for "EquipmentAssignment 1 -> 0..1
            // *non-Cancelled* EquipmentReturn" (Phase 5 scope;
            // amended by the cancellation feature — see the corresponding
            // ADRs). An assignment represents one uninterrupted custody
            // period, ends permanently at physical receipt, and is never
            // reused (EquipmentReturnService.ReceiveAsync is the only place
            // Status becomes Ended) — so Pending/Received/Completed all
            // permanently occupy the assignment's one legitimate return slot.
            // Cancelling a return never disturbs custody, though (the
            // assignment stays Active — see ReturnRules.CanCancel and
            // EquipmentReturnService.CancelAsync), so a Cancelled row must NOT
            // count toward this uniqueness — excluded via the partial filter
            // below, which is what actually lets a new EquipmentReturn be
            // initiated after a cancellation. Do not widen this back to a
            // full (non-partial) unique index (that would make cancellation
            // pointless, since a cancelled attempt would still permanently
            // block re-initiation) and do not revert it to the original
            // Phase 5 predicate "Status <> 'Completed'" (that would treat a
            // Cancelled row as still blocking, the opposite of what
            // cancellation is for).
            entity.HasIndex(r => r.EquipmentAssignmentId)
                .IsUnique()
                .HasFilter("\"Status\" <> 'Cancelled'");

            // Nullable: returns created before grouping existed have no group
            // and are never backfilled. Restrict, like every other reference
            // here — nothing deletes a group, and deleting one must never be
            // able to take return history with it.
            entity.HasOne<EquipmentReturnGroup>().WithMany().HasForeignKey(r => r.ReturnGroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(r => r.ReturnGroupId);
        });

        builder.Entity<EquipmentReturnGroup>(entity =>
        {
            entity.ToTable("EquipmentReturnGroups");
            entity.HasKey(g => g.Id);
            entity.Property(g => g.CreatedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();

            entity.HasOne<Consultant>().WithMany().HasForeignKey(g => g.ConsultantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ConsultantAllocation>().WithMany().HasForeignKey(g => g.ConsultantAllocationId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(g => g.ConsultantId);

            // An allocation is ended once, so it can have created at most one
            // group. Manual groups (no allocation) are unconstrained.
            entity.HasIndex(g => g.ConsultantAllocationId)
                .IsUnique()
                .HasFilter("\"ConsultantAllocationId\" IS NOT NULL");
        });

        builder.Entity<ReturnInspection>(entity =>
        {
            entity.ToTable("ReturnInspections");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Condition).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(i => i.Notes).HasMaxLength(2000);
            entity.Property(i => i.DamageDescription).HasMaxLength(2000);
            entity.Property(i => i.InspectedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();

            // At most one inspection per return (Phase 5 scope) —
            // Restrict, not Cascade: unlike a draft handover item, nothing ever
            // deletes an EquipmentReturn (no hard-delete surface exists), so
            // there is no legitimate cleanup cascade to support here.
            entity.HasOne<EquipmentReturn>().WithOne().HasForeignKey<ReturnInspection>(i => i.EquipmentReturnId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(i => i.EquipmentReturnId).IsUnique();

            entity.OwnsOne(i => i.EquipmentSnapshot, snapshot =>
            {
                snapshot.Property(s => s.AssetTag).HasColumnName("Snapshot_AssetTag").HasMaxLength(20).IsRequired();
                snapshot.Property(s => s.EquipmentTypeName).HasColumnName("Snapshot_EquipmentTypeName").HasMaxLength(200).IsRequired();
                snapshot.Property(s => s.Manufacturer).HasColumnName("Snapshot_Manufacturer").HasMaxLength(200);
                snapshot.Property(s => s.Model).HasColumnName("Snapshot_Model").HasMaxLength(200);
                snapshot.Property(s => s.SerialNumber).HasColumnName("Snapshot_SerialNumber").HasMaxLength(200);
                snapshot.Property(s => s.Processor).HasColumnName("Snapshot_Processor").HasMaxLength(200);
                snapshot.Property(s => s.Memory).HasColumnName("Snapshot_Memory").HasMaxLength(100);
                snapshot.Property(s => s.Storage).HasColumnName("Snapshot_Storage").HasMaxLength(200);
                snapshot.Property(s => s.Kind).HasColumnName("Snapshot_Kind").HasMaxLength(20);
                snapshot.Property(s => s.Imei).HasColumnName("Snapshot_Imei").HasMaxLength(32);
                snapshot.Property(s => s.MobileNumber).HasColumnName("Snapshot_MobileNumber").HasMaxLength(30);
                snapshot.Property(s => s.SimSerialNumber).HasColumnName("Snapshot_SimSerialNumber").HasMaxLength(30);
                snapshot.Property(s => s.Notes).HasColumnName("Snapshot_Notes").HasMaxLength(2000);
            });
        });

        builder.Entity<ReturnInspectionPhoto>(entity =>
        {
            entity.ToTable("ReturnInspectionPhotos");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(p => p.OriginalFileName).HasMaxLength(300).IsRequired();
            entity.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Sha256Hash).HasMaxLength(64).IsRequired();

            entity.HasOne<ReturnInspection>().WithMany().HasForeignKey(p => p.ReturnInspectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(p => p.ReturnInspectionId);
        });

        builder.Entity<DamagePhoto>(entity =>
        {
            entity.ToTable("DamagePhotos");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(p => p.OriginalFileName).HasMaxLength(300).IsRequired();
            entity.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Sha256Hash).HasMaxLength(64).IsRequired();

            // Attached to the inspection, not to Damage directly — Damage is
            // only materialized at finalization, after evidence photos already
            // need to exist. See Damage's own remarks.
            entity.HasOne<ReturnInspection>().WithMany().HasForeignKey(p => p.ReturnInspectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(p => p.ReturnInspectionId);
        });

        builder.Entity<Damage>(entity =>
        {
            entity.ToTable("Damages");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Description).HasMaxLength(2000).IsRequired();
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(d => d.ReportedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();

            entity.HasOne<Equipment>().WithMany().HasForeignKey(d => d.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EquipmentReturn>().WithMany().HasForeignKey(d => d.EquipmentReturnId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ReturnInspection>().WithOne().HasForeignKey<Damage>(d => d.ReturnInspectionId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(d => d.EquipmentId);
            entity.HasIndex(d => d.Status);

            // At most one Damage per return inspection for this phase's
            // workflow (see the entity's remarks on why — widenable later
            // without a breaking change).
            entity.HasIndex(d => d.ReturnInspectionId).IsUnique();
        });

        builder.Entity<MaintenanceRecord>(entity =>
        {
            entity.ToTable("MaintenanceRecords");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(m => m.Notes).HasMaxLength(2000);
            entity.Property(m => m.Outcome).HasConversion<string>().HasMaxLength(20);
            entity.Property(m => m.CreatedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();

            entity.HasOne<Equipment>().WithMany().HasForeignKey(m => m.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Damage>().WithMany().HasForeignKey(m => m.DamageId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(m => m.EquipmentId);
            entity.HasIndex(m => m.Status);

            // THE database-enforced backstop for "at most one active
            // maintenance record per equipment" (Phase 5 scope) —
            // identical pattern to EquipmentAssignment(EquipmentId).
            entity.HasIndex(m => m.EquipmentId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Active'");
        });

        builder.Entity<EquipmentRetirementRequest>(entity =>
        {
            entity.ToTable("EquipmentRetirementRequests");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.RequestedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();
            entity.Property(r => r.RequestReason).HasMaxLength(2000).IsRequired();
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(r => r.DecidedByDisplayNameSnapshot).HasMaxLength(400);
            entity.Property(r => r.DecisionReason).HasMaxLength(2000);

            entity.HasOne<Equipment>().WithMany().HasForeignKey(r => r.EquipmentId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => r.EquipmentId);
            entity.HasIndex(r => r.Status);

            // THE database-enforced backstop for "at most one Pending
            // retirement request per equipment" — identical pattern to
            // MaintenanceRecord(EquipmentId) WHERE Status = 'Active'.
            entity.HasIndex(r => r.EquipmentId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Pending'");
        });

        // DocumentNumberGenerator calls nextval() on this directly via raw
        // SQL — see ADR-048 for why a native Postgres
        // sequence (never MAX(...)+1) is the concurrency-safe primitive here,
        // mirroring AssetTagSequenceName's exact rationale.
        builder.HasSequence<long>(ResponsibilityTermDocumentNumberSequenceName).StartsAt(1).IncrementsBy(1);

        builder.Entity<ResponsibilityTerm>(entity =>
        {
            entity.ToTable("ResponsibilityTerms");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.DocumentNumber).HasMaxLength(20).IsRequired();
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(t => t.GeneratedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();
            entity.Property(t => t.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(t => t.FileName).HasMaxLength(300).IsRequired();
            entity.Property(t => t.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(t => t.Sha256).HasMaxLength(64).IsRequired();

            // The handover is itself permanent, never hard-deleted history
            // (Phase 4) — Restrict, same discipline as every other reference
            // to it (e.g. EquipmentAssignment.EquipmentHandoverId).
            entity.HasOne<EquipmentHandover>().WithMany().HasForeignKey(t => t.EquipmentHandoverId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(t => t.EquipmentHandoverId);
            entity.HasIndex(t => t.GeneratedAtUtc);
            entity.HasIndex(t => t.Status);

            // The document number is the document's actual legal identity —
            // globally unique regardless of which handover it belongs to.
            entity.HasIndex(t => t.DocumentNumber).IsUnique();

            // Monotonic per-handover versioning: THE database-enforced
            // backstop against two concurrent generations both computing the
            // same "next version" (Phase 6 scope).
            entity.HasIndex(t => new { t.EquipmentHandoverId, t.Version }).IsUnique();

            // At most one current (non-superseded) term per handover —
            // identical pattern to EquipmentAssignment/MaintenanceRecord's
            // partial unique indexes (Phase 6 scope).
            entity.HasIndex(t => t.EquipmentHandoverId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Generated'");

            // The data THIS version was rendered from, frozen with it: same
            // columns as the Handover's delivery record, but owned by the
            // version, so no later version or cancellation can touch it.
            entity.OwnsOne(t => t.ConsultantSnapshot, snapshot =>
            {
                snapshot.Property(s => s.FullName).HasColumnName("Snapshot_ConsultantFullName").HasMaxLength(300).IsRequired();
                snapshot.Property(s => s.Nif).HasColumnName("Snapshot_ConsultantNif").HasMaxLength(20).IsRequired();
                snapshot.Property(s => s.Address).HasColumnName("Snapshot_ConsultantAddress").HasMaxLength(500).IsRequired();
                snapshot.Property(s => s.Phone).HasColumnName("Snapshot_ConsultantPhone").HasMaxLength(50).IsRequired();
                snapshot.Property(s => s.Email).HasColumnName("Snapshot_ConsultantEmail").HasMaxLength(320).IsRequired();
            });

            entity.OwnsOne(t => t.AllocationSnapshot, snapshot =>
            {
                snapshot.Property(s => s.WorkforceType).HasColumnName("Snapshot_WorkforceType").HasMaxLength(20).IsRequired();
                snapshot.Property(s => s.EngagementType).HasColumnName("Snapshot_EngagementType").HasMaxLength(30).IsRequired();
                snapshot.Property(s => s.RoleOrFunction).HasColumnName("Snapshot_RoleOrFunction").HasMaxLength(200).IsRequired();
                snapshot.Property(s => s.CompanyName).HasColumnName("Snapshot_CompanyName").HasMaxLength(200);
            });

            // Every version has its Consultant snapshot, except a legacy one
            // explicitly marked as unrecoverable — never fabricated.
            entity.ToTable(tb => tb.HasCheckConstraint(
                "CK_ResponsibilityTerms_Snapshot_PresentOrUnavailable",
                "\"SnapshotUnavailable\" OR \"Snapshot_ConsultantFullName\" IS NOT NULL"));
        });

        builder.Entity<ResponsibilityTermItem>(entity =>
        {
            entity.ToTable("ResponsibilityTermItems");
            entity.HasKey(i => i.Id);

            // Terms, equipment and photo sets are all permanent evidence that
            // is never hard-deleted: Restrict everywhere, no cascade can ever
            // remove a version's frozen items.
            entity.HasOne<ResponsibilityTerm>().WithMany().HasForeignKey(i => i.ResponsibilityTermId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Equipment>().WithMany().HasForeignKey(i => i.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<EquipmentPhotoSet>().WithMany().HasForeignKey(i => i.EquipmentPhotoSetId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(i => new { i.ResponsibilityTermId, i.EquipmentId }).IsUnique();
            entity.HasIndex(i => new { i.ResponsibilityTermId, i.SortOrder }).IsUnique();
            entity.HasIndex(i => i.EquipmentId);
            entity.HasIndex(i => i.EquipmentPhotoSetId);

            entity.OwnsOne(i => i.EquipmentSnapshot, snapshot =>
            {
                snapshot.Property(s => s.AssetTag).HasColumnName("Snapshot_AssetTag").HasMaxLength(20).IsRequired();
                snapshot.Property(s => s.EquipmentTypeName).HasColumnName("Snapshot_EquipmentTypeName").HasMaxLength(200).IsRequired();
                snapshot.Property(s => s.Manufacturer).HasColumnName("Snapshot_Manufacturer").HasMaxLength(200);
                snapshot.Property(s => s.Model).HasColumnName("Snapshot_Model").HasMaxLength(200);
                snapshot.Property(s => s.SerialNumber).HasColumnName("Snapshot_SerialNumber").HasMaxLength(200);
                snapshot.Property(s => s.Processor).HasColumnName("Snapshot_Processor").HasMaxLength(200);
                snapshot.Property(s => s.Memory).HasColumnName("Snapshot_Memory").HasMaxLength(100);
                snapshot.Property(s => s.Storage).HasColumnName("Snapshot_Storage").HasMaxLength(200);
                snapshot.Property(s => s.Kind).HasColumnName("Snapshot_Kind").HasMaxLength(20);
                snapshot.Property(s => s.Imei).HasColumnName("Snapshot_Imei").HasMaxLength(32);
                snapshot.Property(s => s.MobileNumber).HasColumnName("Snapshot_MobileNumber").HasMaxLength(30);
                snapshot.Property(s => s.SimSerialNumber).HasColumnName("Snapshot_SimSerialNumber").HasMaxLength(30);
                snapshot.Property(s => s.Notes).HasColumnName("Snapshot_Notes").HasMaxLength(2000);
            });
            entity.Navigation(i => i.EquipmentSnapshot).IsRequired();
        });

        // --- Phase 7: delivery/signature workflow ---------------------------

        builder.Entity<ResponsibilityTermDelivery>(entity =>
        {
            entity.ToTable("ResponsibilityTermDeliveries");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.RecipientEmailSnapshot).HasMaxLength(320).IsRequired();
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(d => d.FailureCode).HasConversion<string>().HasMaxLength(30);
            entity.Property(d => d.SentByDisplayNameSnapshot).HasMaxLength(400).IsRequired();

            // Deliveries are permanent send-attempt history, never hard-deleted
            // (Phase 7 scope) — Restrict, same discipline as every
            // other reference to a permanent historical row.
            entity.HasOne<ResponsibilityTerm>().WithMany().HasForeignKey(d => d.ResponsibilityTermId).OnDelete(DeleteBehavior.Restrict);

            // Deliberately no uniqueness on ResponsibilityTermId — a term may
            // be resent any number of times (Phase 7 scope).
            entity.HasIndex(d => d.ResponsibilityTermId);
            entity.HasIndex(d => d.Status);
            entity.HasIndex(d => d.SentAtUtc);
        });

        builder.Entity<ResponsibilityTermSignedArtifact>(entity =>
        {
            entity.ToTable("ResponsibilityTermSignedArtifacts");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(a => a.FileName).HasMaxLength(300).IsRequired();
            entity.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(a => a.UploadedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();

            entity.HasOne<ResponsibilityTerm>().WithMany().HasForeignKey(a => a.ResponsibilityTermId).OnDelete(DeleteBehavior.Restrict);

            // Structurally enforces ResponsibilityTerm 1 -> 0..1 SignedArtifact
            // (Phase 7 scope) — the actual concurrency backstop for
            // the double-upload race, beneath the application-level pre-check
            // in ResponsibilityTermSignedArtifactService.
            entity.HasIndex(a => a.ResponsibilityTermId).IsUnique();
        });

        // --- Phase 8: Excel import/export ------------------------------------

        builder.Entity<ImportSession>(entity =>
        {
            entity.ToTable("ImportSessions");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.ImportType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(s => s.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(s => s.FileSha256).HasMaxLength(64).IsRequired();

            // Typed-DTO JSON, never polymorphic (Phase 8 scope) —
            // the concrete row-snapshot type is fixed by ImportType and known
            // to the service that reads it back, never deserialized generically.
            // Nullable (Phase 8 retention review): set to null the moment the
            // detailed PII snapshot is no longer operationally needed — see
            // ImportSession's remarks and ADR-060.
            entity.Property(s => s.RowsJson).HasColumnType("jsonb");

            entity.Property(s => s.CreatedByDisplayNameSnapshot).HasMaxLength(400).IsRequired();
            entity.Property(s => s.ConfirmedByDisplayNameSnapshot).HasMaxLength(400);

            entity.HasIndex(s => new { s.ImportType, s.Status });
            entity.HasIndex(s => s.CreatedByUserId);
            entity.HasIndex(s => s.CreatedAtUtc);
            entity.HasIndex(s => s.ExpiresAtUtc);
        });
    }
}
