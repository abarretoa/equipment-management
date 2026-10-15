using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceNormalizedImeiFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Never pad, trim or otherwise rewrite an existing IMEI to make it
            // fit: a malformed one must be corrected deliberately (known case:
            // the demo seed's 14-digit synthetic IMEI, fixed at promotion).
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE malformed integer;
                BEGIN
                    SELECT count(*) INTO malformed FROM "MobilePhoneSpecifications"
                    WHERE "NormalizedImei" !~ '^[0-9]{15}$';
                    IF malformed > 0 THEN
                        RAISE EXCEPTION 'EnforceNormalizedImeiFormat: % mobile phone(s) have an IMEI that is not exactly 15 digits. Correct those IMEIs (or reset the disposable database) before applying this migration.', malformed;
                    END IF;
                END $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MobilePhoneSpecifications_NormalizedImei_Format",
                table: "MobilePhoneSpecifications",
                sql: "\"NormalizedImei\" ~ '^[0-9]{15}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MobilePhoneSpecifications_NormalizedImei_Format",
                table: "MobilePhoneSpecifications");
        }
    }
}
