using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5TightenEquipmentReturnAssignmentUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentReturns_EquipmentAssignmentId",
                table: "EquipmentReturns");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_EquipmentAssignmentId",
                table: "EquipmentReturns",
                column: "EquipmentAssignmentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentReturns_EquipmentAssignmentId",
                table: "EquipmentReturns");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_EquipmentAssignmentId",
                table: "EquipmentReturns",
                column: "EquipmentAssignmentId",
                unique: true,
                filter: "\"Status\" <> 'Completed'");
        }
    }
}
