using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentPhotoSortOrderConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_EquipmentPhotoSetPhotos_EquipmentPhotoSetId_SortOrder",
                table: "EquipmentPhotoSetPhotos",
                columns: new[] { "EquipmentPhotoSetId", "SortOrder" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_EquipmentPhotoSetPhotos_SortOrder_Range",
                table: "EquipmentPhotoSetPhotos",
                sql: "\"SortOrder\" >= 1 AND \"SortOrder\" <= 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentPhotoSetPhotos_EquipmentPhotoSetId_SortOrder",
                table: "EquipmentPhotoSetPhotos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EquipmentPhotoSetPhotos_SortOrder_Range",
                table: "EquipmentPhotoSetPhotos");
        }
    }
}
