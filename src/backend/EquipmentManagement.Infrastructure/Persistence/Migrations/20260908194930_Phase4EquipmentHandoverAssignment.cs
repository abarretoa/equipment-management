using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4EquipmentHandoverAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentHandovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeliveredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Snapshot_ConsultantFullName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Snapshot_ConsultantNif = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Snapshot_ConsultantAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Snapshot_ConsultantPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Snapshot_ConsultantEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Snapshot_WorkforceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Snapshot_EngagementType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Snapshot_RoleOrFunction = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentHandovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentHandovers_Consultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "Consultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentHandoverItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentHandoverId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Snapshot_AssetTag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Snapshot_EquipmentTypeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_Manufacturer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_SerialNumber = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_Processor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Snapshot_Memory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Snapshot_Storage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentHandoverItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentHandoverItems_EquipmentHandovers_EquipmentHandover~",
                        column: x => x.EquipmentHandoverId,
                        principalTable: "EquipmentHandovers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentHandoverItems_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentHandoverItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InspectedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    InspectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryInspections_EquipmentHandoverItems_EquipmentHandove~",
                        column: x => x.EquipmentHandoverItemId,
                        principalTable: "EquipmentHandoverItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentHandoverId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentHandoverItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentAssignments_Consultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "Consultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentAssignments_EquipmentHandoverItems_EquipmentHandov~",
                        column: x => x.EquipmentHandoverItemId,
                        principalTable: "EquipmentHandoverItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentAssignments_EquipmentHandovers_EquipmentHandoverId",
                        column: x => x.EquipmentHandoverId,
                        principalTable: "EquipmentHandovers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentAssignments_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryInspectionPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryInspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryInspectionPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryInspectionPhotos_DeliveryInspections_DeliveryInspec~",
                        column: x => x.DeliveryInspectionId,
                        principalTable: "DeliveryInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInspectionPhotos_DeliveryInspectionId",
                table: "DeliveryInspectionPhotos",
                column: "DeliveryInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryInspections_EquipmentHandoverItemId",
                table: "DeliveryInspections",
                column: "EquipmentHandoverItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignments_ConsultantId",
                table: "EquipmentAssignments",
                column: "ConsultantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignments_EquipmentHandoverId",
                table: "EquipmentAssignments",
                column: "EquipmentHandoverId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignments_EquipmentHandoverItemId",
                table: "EquipmentAssignments",
                column: "EquipmentHandoverItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignments_EquipmentId",
                table: "EquipmentAssignments",
                column: "EquipmentId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignments_StartedAtUtc",
                table: "EquipmentAssignments",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssignments_Status",
                table: "EquipmentAssignments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHandoverItems_EquipmentHandoverId",
                table: "EquipmentHandoverItems",
                column: "EquipmentHandoverId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHandoverItems_EquipmentId",
                table: "EquipmentHandoverItems",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHandovers_ConsultantId",
                table: "EquipmentHandovers",
                column: "ConsultantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHandovers_CreatedAtUtc",
                table: "EquipmentHandovers",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHandovers_Status",
                table: "EquipmentHandovers",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryInspectionPhotos");

            migrationBuilder.DropTable(
                name: "EquipmentAssignments");

            migrationBuilder.DropTable(
                name: "DeliveryInspections");

            migrationBuilder.DropTable(
                name: "EquipmentHandoverItems");

            migrationBuilder.DropTable(
                name: "EquipmentHandovers");
        }
    }
}
