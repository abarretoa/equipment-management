using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillInTransitForDispatchedDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only (Status is already a string column). Before this
            // release "Registar envio" left equipment Available; it now moves
            // it to InTransit. Backfill exactly the equipment whose persisted
            // state proves a registered, unconfirmed carrier dispatch: an item
            // of a Handover that has a dispatch row and is still Draft (not
            // Finalized), with the equipment still Available and no Active
            // assignment. Equipment Assigned elsewhere, finalized Handovers
            // and every other status are left untouched.
            migrationBuilder.Sql(
                """
                UPDATE "Equipment" AS e
                SET "Status" = 'InTransit', "UpdatedAtUtc" = now()
                WHERE e."Status" = 'Available'
                  AND EXISTS (
                      SELECT 1
                      FROM "EquipmentHandoverItems" AS i
                      JOIN "EquipmentHandovers" AS h ON h."Id" = i."EquipmentHandoverId"
                      JOIN "EquipmentDispatches" AS d ON d."EquipmentHandoverId" = h."Id"
                      WHERE i."EquipmentId" = e."Id" AND h."Status" = 'Draft')
                  AND NOT EXISTS (
                      SELECT 1 FROM "EquipmentAssignments" AS a
                      WHERE a."EquipmentId" = e."Id" AND a."Status" = 'Active');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous release has no InTransit: equipment shipped but not
            // yet confirmed was Available there. InTransit never has an Active
            // assignment, so this restores exactly that meaning.
            migrationBuilder.Sql(
                """
                UPDATE "Equipment"
                SET "Status" = 'Available', "UpdatedAtUtc" = now()
                WHERE "Status" = 'InTransit';
                """);
        }
    }
}
