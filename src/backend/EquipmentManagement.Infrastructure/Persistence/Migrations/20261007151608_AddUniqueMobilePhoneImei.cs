using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueMobilePhoneImei : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedImei",
                table: "MobilePhoneSpecifications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Same normalization as MobilePhoneSpecificationRules.NormalizeImei:
            // whitespace and hyphens removed, upper-cased.
            migrationBuilder.Sql(
                """
                UPDATE "MobilePhoneSpecifications"
                SET "NormalizedImei" = upper(regexp_replace("Imei", '[[:space:]-]', '', 'g'));
                """);

            // The '' default above exists only so AddColumn succeeds on existing
            // rows; it is backfilled immediately, and new rows must always carry
            // a real value, so it is dropped (same as Origin on EquipmentAssignments).
            migrationBuilder.Sql("ALTER TABLE \"MobilePhoneSpecifications\" ALTER COLUMN \"NormalizedImei\" DROP DEFAULT;");

            // IMEI uniqueness is introduced before any production data exists
            // (only synthetic dev/test/demo data). Fail loudly and explain,
            // rather than letting the unique index fail opaquely or silently
            // deleting/renaming anyone's records: duplicate synthetic phones
            // must be cleaned up (or the database reset) deliberately.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "MobilePhoneSpecifications"
                        GROUP BY "NormalizedImei" HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'AddUniqueMobilePhoneImei: duplicate IMEIs exist in MobilePhoneSpecifications. Clean up the duplicate (synthetic) mobile phones or reset the database before applying this migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MobilePhoneSpecifications_NormalizedImei",
                table: "MobilePhoneSpecifications",
                column: "NormalizedImei",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MobilePhoneSpecifications_NormalizedImei",
                table: "MobilePhoneSpecifications");

            migrationBuilder.DropColumn(
                name: "NormalizedImei",
                table: "MobilePhoneSpecifications");
        }
    }
}
