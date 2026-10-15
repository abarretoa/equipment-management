using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistResponsibilityTermSnapshotsPerVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SnapshotUnavailable",
                table: "ResponsibilityTerms",
                type: "boolean",
                nullable: false,
                defaultValue: true); // every existing version starts "unavailable"; the backfill below clears it only where proven

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_CompanyName",
                table: "ResponsibilityTerms",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_ConsultantAddress",
                table: "ResponsibilityTerms",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_ConsultantEmail",
                table: "ResponsibilityTerms",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_ConsultantFullName",
                table: "ResponsibilityTerms",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_ConsultantNif",
                table: "ResponsibilityTerms",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_ConsultantPhone",
                table: "ResponsibilityTerms",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_EngagementType",
                table: "ResponsibilityTerms",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_RoleOrFunction",
                table: "ResponsibilityTerms",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_WorkforceType",
                table: "ResponsibilityTerms",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ResponsibilityTermItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsibilityTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Snapshot_AssetTag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Snapshot_EquipmentTypeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Snapshot_Manufacturer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_Processor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_Memory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Snapshot_Storage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Snapshot_Imei = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Snapshot_MobileNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Snapshot_SimSerialNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Snapshot_Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EquipmentPhotoSetId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponsibilityTermItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResponsibilityTermItems_EquipmentPhotoSets_EquipmentPhotoSe~",
                        column: x => x.EquipmentPhotoSetId,
                        principalTable: "EquipmentPhotoSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResponsibilityTermItems_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResponsibilityTermItems_ResponsibilityTerms_ResponsibilityT~",
                        column: x => x.ResponsibilityTermId,
                        principalTable: "ResponsibilityTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ResponsibilityTerms_Snapshot_PresentOrUnavailable",
                table: "ResponsibilityTerms",
                sql: "\"SnapshotUnavailable\" OR \"Snapshot_ConsultantFullName\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTermItems_EquipmentId",
                table: "ResponsibilityTermItems",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTermItems_EquipmentPhotoSetId",
                table: "ResponsibilityTermItems",
                column: "EquipmentPhotoSetId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTermItems_ResponsibilityTermId_EquipmentId",
                table: "ResponsibilityTermItems",
                columns: new[] { "ResponsibilityTermId", "EquipmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTermItems_ResponsibilityTermId_SortOrder",
                table: "ResponsibilityTermItems",
                columns: new[] { "ResponsibilityTermId", "SortOrder" },
                unique: true);

            migrationBuilder.Sql(BackfillSql);

            // New versions always carry their own snapshot (the application
            // writes SnapshotUnavailable = false explicitly; this only keeps
            // the column default truthful).
            migrationBuilder.Sql("""ALTER TABLE "ResponsibilityTerms" ALTER COLUMN "SnapshotUnavailable" SET DEFAULT false;""");
        }

        /// <summary>
        /// Gives each existing term version its own snapshot ONLY where the old
        /// shared model provably still holds the exact data that version was
        /// rendered from; every other version stays <c>SnapshotUnavailable</c>
        /// (its PDF and SHA-256 remain the evidence). Nothing is fabricated.
        /// Under the old model the Handover's fields were written by (a) a
        /// carrier term prepared before delivery and (b) Confirmar entrega
        /// when no prepared term existed; they never changed after
        /// finalization, and "Anular envio" cleared them. So a version T of
        /// Handover H is recoverable iff H's fields are present for H and all
        /// its items, no dispatch of H was cancelled after T, and either
        ///   - T was generated after H's finalization (rendered from the
        ///     then-immutable delivery record), or
        ///   - T is the latest version generated before finalization (or the
        ///     latest at all, for a Draft): the one whose preparation the
        ///     fields still hold.
        /// Photo-set links are copied only for template-v2 versions (v1 PDFs
        /// show no photos). A recovered Draft's stale preparation copy is
        /// then cleared: a Draft has no delivery record, and the version now
        /// owns those values. Idempotent: only still-unavailable versions with
        /// no snapshot are ever considered.
        /// </summary>
        public const string BackfillSql =
            """
            CREATE TEMP TABLE "_rt_recoverable" ON COMMIT DROP AS
            SELECT t."Id" AS "TermId", t."EquipmentHandoverId" AS "HandoverId", t."TemplateVersion", h."Status" AS "HandoverStatus"
            FROM "ResponsibilityTerms" t
            JOIN "EquipmentHandovers" h ON h."Id" = t."EquipmentHandoverId"
            WHERE t."SnapshotUnavailable"
              AND t."Snapshot_ConsultantFullName" IS NULL
              AND NOT EXISTS (SELECT 1 FROM "ResponsibilityTermItems" x WHERE x."ResponsibilityTermId" = t."Id")
              AND h."Snapshot_ConsultantFullName" IS NOT NULL
              AND EXISTS (SELECT 1 FROM "EquipmentHandoverItems" i WHERE i."EquipmentHandoverId" = h."Id")
              AND NOT EXISTS (SELECT 1 FROM "EquipmentHandoverItems" i WHERE i."EquipmentHandoverId" = h."Id" AND i."Snapshot_AssetTag" IS NULL)
              AND NOT EXISTS (SELECT 1 FROM "EquipmentDispatches" d WHERE d."EquipmentHandoverId" = h."Id" AND d."CancelledAtUtc" > t."GeneratedAtUtc")
              AND (
                    (h."FinalizedAtUtc" IS NOT NULL AND t."GeneratedAtUtc" >= h."FinalizedAtUtc")
                 OR NOT EXISTS (
                        SELECT 1 FROM "ResponsibilityTerms" t2
                        WHERE t2."EquipmentHandoverId" = h."Id"
                          AND t2."GeneratedAtUtc" > t."GeneratedAtUtc"
                          AND (h."FinalizedAtUtc" IS NULL OR t2."GeneratedAtUtc" < h."FinalizedAtUtc"))
                  );

            UPDATE "ResponsibilityTerms" t SET
                "Snapshot_ConsultantFullName" = h."Snapshot_ConsultantFullName",
                "Snapshot_ConsultantNif" = h."Snapshot_ConsultantNif",
                "Snapshot_ConsultantAddress" = h."Snapshot_ConsultantAddress",
                "Snapshot_ConsultantPhone" = h."Snapshot_ConsultantPhone",
                "Snapshot_ConsultantEmail" = h."Snapshot_ConsultantEmail",
                "Snapshot_WorkforceType" = h."Snapshot_WorkforceType",
                "Snapshot_EngagementType" = h."Snapshot_EngagementType",
                "Snapshot_RoleOrFunction" = h."Snapshot_RoleOrFunction",
                "Snapshot_CompanyName" = h."Snapshot_CompanyName",
                "SnapshotUnavailable" = false
            FROM "_rt_recoverable" r
            JOIN "EquipmentHandovers" h ON h."Id" = r."HandoverId"
            WHERE t."Id" = r."TermId";

            INSERT INTO "ResponsibilityTermItems" (
                "Id", "ResponsibilityTermId", "EquipmentId", "SortOrder",
                "Snapshot_AssetTag", "Snapshot_EquipmentTypeName", "Snapshot_Manufacturer", "Snapshot_Model", "Snapshot_SerialNumber",
                "Snapshot_Processor", "Snapshot_Memory", "Snapshot_Storage", "Snapshot_Kind", "Snapshot_Imei",
                "Snapshot_MobileNumber", "Snapshot_SimSerialNumber", "Snapshot_Notes", "EquipmentPhotoSetId")
            SELECT gen_random_uuid(), r."TermId", i."EquipmentId",
                (row_number() OVER (PARTITION BY r."TermId" ORDER BY i."CreatedAtUtc", i."Id") - 1)::integer,
                i."Snapshot_AssetTag", i."Snapshot_EquipmentTypeName", i."Snapshot_Manufacturer", i."Snapshot_Model", i."Snapshot_SerialNumber",
                i."Snapshot_Processor", i."Snapshot_Memory", i."Snapshot_Storage", i."Snapshot_Kind", i."Snapshot_Imei",
                i."Snapshot_MobileNumber", i."Snapshot_SimSerialNumber", i."Snapshot_Notes",
                CASE WHEN r."TemplateVersion" >= 2 THEN i."EquipmentPhotoSetId" END
            FROM "_rt_recoverable" r
            JOIN "EquipmentHandoverItems" i ON i."EquipmentHandoverId" = r."HandoverId";

            UPDATE "EquipmentHandovers" h SET
                "Snapshot_ConsultantFullName" = NULL, "Snapshot_ConsultantNif" = NULL, "Snapshot_ConsultantAddress" = NULL,
                "Snapshot_ConsultantPhone" = NULL, "Snapshot_ConsultantEmail" = NULL, "Snapshot_WorkforceType" = NULL,
                "Snapshot_EngagementType" = NULL, "Snapshot_RoleOrFunction" = NULL, "Snapshot_CompanyName" = NULL
            WHERE h."Status" = 'Draft'
              AND EXISTS (SELECT 1 FROM "_rt_recoverable" r WHERE r."HandoverId" = h."Id");

            UPDATE "EquipmentHandoverItems" i SET
                "Snapshot_AssetTag" = NULL, "Snapshot_EquipmentTypeName" = NULL, "Snapshot_Manufacturer" = NULL, "Snapshot_Model" = NULL,
                "Snapshot_SerialNumber" = NULL, "Snapshot_Processor" = NULL, "Snapshot_Memory" = NULL, "Snapshot_Storage" = NULL,
                "Snapshot_Kind" = NULL, "Snapshot_Imei" = NULL, "Snapshot_MobileNumber" = NULL, "Snapshot_SimSerialNumber" = NULL,
                "Snapshot_Notes" = NULL, "EquipmentPhotoSetId" = NULL
            FROM "_rt_recoverable" r
            WHERE r."HandoverId" = i."EquipmentHandoverId" AND r."HandoverStatus" = 'Draft';
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old model has one shared snapshot per Handover: independent
            // per-version snapshots cannot be folded back into it without
            // losing history. Refuse rather than drop it silently.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "ResponsibilityTerms" WHERE NOT "SnapshotUnavailable")
                       OR EXISTS (SELECT 1 FROM "ResponsibilityTermItems") THEN
                        RAISE EXCEPTION 'PersistResponsibilityTermSnapshotsPerVersion: per-version Responsibility Term snapshots exist. Rolling back would delete them; restore from backup instead.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "ResponsibilityTermItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ResponsibilityTerms_Snapshot_PresentOrUnavailable",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "SnapshotUnavailable",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "Snapshot_CompanyName",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "Snapshot_ConsultantAddress",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "Snapshot_ConsultantEmail",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "Snapshot_ConsultantFullName",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "Snapshot_ConsultantNif",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "Snapshot_ConsultantPhone",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "Snapshot_EngagementType",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "Snapshot_RoleOrFunction",
                table: "ResponsibilityTerms");

            migrationBuilder.DropColumn(
                name: "Snapshot_WorkforceType",
                table: "ResponsibilityTerms");
        }
    }
}
