using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5EquipmentReturnCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentReturns_EquipmentAssignmentId",
                table: "EquipmentReturns");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "EquipmentReturns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "EquipmentReturns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByDisplayNameSnapshot",
                table: "EquipmentReturns",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "EquipmentReturns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_EquipmentAssignmentId",
                table: "EquipmentReturns",
                column: "EquipmentAssignmentId",
                unique: true,
                filter: "\"Status\" <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentReturns_EquipmentAssignmentId",
                table: "EquipmentReturns");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "EquipmentReturns");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "EquipmentReturns");

            migrationBuilder.DropColumn(
                name: "CancelledByDisplayNameSnapshot",
                table: "EquipmentReturns");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "EquipmentReturns");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_EquipmentAssignmentId",
                table: "EquipmentReturns",
                column: "EquipmentAssignmentId",
                unique: true);
        }
    }
}
