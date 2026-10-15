using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentAssignmentOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "EquipmentHandoverItemId",
                table: "EquipmentAssignments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "EquipmentHandoverId",
                table: "EquipmentAssignments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "EquipmentAssignments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Handover");

            // Every pre-existing assignment came from a finalized Handover;
            // the default above exists only to backfill them. New rows must
            // always state their origin explicitly, so it is dropped again.
            migrationBuilder.Sql("ALTER TABLE \"EquipmentAssignments\" ALTER COLUMN \"Origin\" DROP DEFAULT;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentAssignments_Origin_HandoverReferences",
                table: "EquipmentAssignments",
                sql: "(\"Origin\" = 'Handover' AND \"EquipmentHandoverId\" IS NOT NULL AND \"EquipmentHandoverItemId\" IS NOT NULL) OR (\"Origin\" = 'CurrentInventoryImport' AND \"EquipmentHandoverId\" IS NULL AND \"EquipmentHandoverItemId\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Current Inventory import custody has no Handover to point at;
            // reverting would have to invent one or zero-fill the foreign keys.
            // Refuse instead of corrupting real custody records.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "EquipmentAssignments" WHERE "Origin" <> 'Handover') THEN
                        RAISE EXCEPTION 'AddEquipmentAssignmentOrigin (down): Current Inventory import assignments exist and cannot be represented without a Handover.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentAssignments_Origin_HandoverReferences",
                table: "EquipmentAssignments");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "EquipmentAssignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "EquipmentHandoverItemId",
                table: "EquipmentAssignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "EquipmentHandoverId",
                table: "EquipmentAssignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
