using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5EquipmentReturnDamageMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquipmentReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExpectedReturnDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentReturns_Consultants_ConsultantId",
                        column: x => x.ConsultantId,
                        principalTable: "Consultants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentReturns_EquipmentAssignments_EquipmentAssignmentId",
                        column: x => x.EquipmentAssignmentId,
                        principalTable: "EquipmentAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentReturns_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    HasDamage = table.Column<bool>(type: "boolean", nullable: false),
                    DamageDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InspectedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    InspectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_ReturnInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnInspections_EquipmentReturns_EquipmentReturnId",
                        column: x => x.EquipmentReturnId,
                        principalTable: "EquipmentReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DamagePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnInspectionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_DamagePhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DamagePhotos_ReturnInspections_ReturnInspectionId",
                        column: x => x.ReturnInspectionId,
                        principalTable: "ReturnInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Damages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnInspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Damages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Damages_EquipmentReturns_EquipmentReturnId",
                        column: x => x.EquipmentReturnId,
                        principalTable: "EquipmentReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Damages_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Damages_ReturnInspections_ReturnInspectionId",
                        column: x => x.ReturnInspectionId,
                        principalTable: "ReturnInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnInspectionPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnInspectionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_ReturnInspectionPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnInspectionPhotos_ReturnInspections_ReturnInspectionId",
                        column: x => x.ReturnInspectionId,
                        principalTable: "ReturnInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DamageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecords_Damages_DamageId",
                        column: x => x.DamageId,
                        principalTable: "Damages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecords_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DamagePhotos_ReturnInspectionId",
                table: "DamagePhotos",
                column: "ReturnInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Damages_EquipmentId",
                table: "Damages",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Damages_EquipmentReturnId",
                table: "Damages",
                column: "EquipmentReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_Damages_ReturnInspectionId",
                table: "Damages",
                column: "ReturnInspectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Damages_Status",
                table: "Damages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_ConsultantId",
                table: "EquipmentReturns",
                column: "ConsultantId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_CreatedAtUtc",
                table: "EquipmentReturns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_EquipmentAssignmentId",
                table: "EquipmentReturns",
                column: "EquipmentAssignmentId",
                unique: true,
                filter: "\"Status\" <> 'Completed'");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_EquipmentId",
                table: "EquipmentReturns",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReturns_Status",
                table: "EquipmentReturns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_DamageId",
                table: "MaintenanceRecords",
                column: "DamageId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_EquipmentId",
                table: "MaintenanceRecords",
                column: "EquipmentId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceRecords_Status",
                table: "MaintenanceRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspectionPhotos_ReturnInspectionId",
                table: "ReturnInspectionPhotos",
                column: "ReturnInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInspections_EquipmentReturnId",
                table: "ReturnInspections",
                column: "EquipmentReturnId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DamagePhotos");

            migrationBuilder.DropTable(
                name: "MaintenanceRecords");

            migrationBuilder.DropTable(
                name: "ReturnInspectionPhotos");

            migrationBuilder.DropTable(
                name: "Damages");

            migrationBuilder.DropTable(
                name: "ReturnInspections");

            migrationBuilder.DropTable(
                name: "EquipmentReturns");
        }
    }
}
