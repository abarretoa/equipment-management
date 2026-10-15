using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentDispatchCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentDispatches_EquipmentHandoverId",
                table: "EquipmentDispatches");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "EquipmentDispatches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "EquipmentDispatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByDisplayNameSnapshot",
                table: "EquipmentDispatches",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "EquipmentDispatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDispatches_EquipmentHandoverId",
                table: "EquipmentDispatches",
                column: "EquipmentHandoverId",
                unique: true,
                filter: "\"CancelledAtUtc\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentDispatches_Cancellation_Complete",
                table: "EquipmentDispatches",
                sql: "(\"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND \"CancelledByDisplayNameSnapshot\" IS NULL AND \"CancellationReason\" IS NULL) OR (\"CancelledAtUtc\" IS NOT NULL AND \"CancelledByUserId\" IS NOT NULL AND \"CancelledByDisplayNameSnapshot\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back would drop cancellation history (and the old
            // one-row-per-handover index cannot hold a cancelled attempt plus a
            // re-dispatch). Never lose that history silently.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE cancelled integer;
                BEGIN
                    SELECT count(*) INTO cancelled FROM "EquipmentDispatches" WHERE "CancelledAtUtc" IS NOT NULL;
                    IF cancelled > 0 THEN
                        RAISE EXCEPTION 'AddEquipmentDispatchCancellation: % cancelled dispatch(es) exist. Rolling back would delete their history; restore from backup instead.', cancelled;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_EquipmentDispatches_EquipmentHandoverId",
                table: "EquipmentDispatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentDispatches_Cancellation_Complete",
                table: "EquipmentDispatches");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "EquipmentDispatches");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "EquipmentDispatches");

            migrationBuilder.DropColumn(
                name: "CancelledByDisplayNameSnapshot",
                table: "EquipmentDispatches");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "EquipmentDispatches");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDispatches_EquipmentHandoverId",
                table: "EquipmentDispatches",
                column: "EquipmentHandoverId",
                unique: true);
        }
    }
}
