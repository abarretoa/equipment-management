using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentReturnGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReturnGroupId",
                table: "EquipmentReturns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EquipmentReturnGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantAllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentReturnGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentReturnGroups_ConsultantAllocations_ConsultantAlloc~",
                        column: x => x.ConsultantAllocationId,
                        principalTable: "ConsultantAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentReturnGroups_Consultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "Consultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_ReturnGroupId",
                table: "EquipmentReturns",
                column: "ReturnGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturnGroups_ConsultantAllocationId",
                table: "EquipmentReturnGroups",
                column: "ConsultantAllocationId",
                unique: true,
                filter: "\"ConsultantAllocationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturnGroups_ConsultantId",
                table: "EquipmentReturnGroups",
                column: "ConsultantId");

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentReturns_EquipmentReturnGroups_ReturnGroupId",
                table: "EquipmentReturns",
                column: "ReturnGroupId",
                principalTable: "EquipmentReturnGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentReturns_EquipmentReturnGroups_ReturnGroupId",
                table: "EquipmentReturns");

            migrationBuilder.DropTable(
                name: "EquipmentReturnGroups");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentReturns_ReturnGroupId",
                table: "EquipmentReturns");

            migrationBuilder.DropColumn(
                name: "ReturnGroupId",
                table: "EquipmentReturns");
        }
    }
}
