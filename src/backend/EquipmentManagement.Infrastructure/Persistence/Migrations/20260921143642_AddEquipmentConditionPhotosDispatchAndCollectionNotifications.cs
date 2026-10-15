using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentConditionPhotosDispatchAndCollectionNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Snapshot_Imei",
                table: "ReturnInspections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_Kind",
                table: "ReturnInspections",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_MobileNumber",
                table: "ReturnInspections",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_Notes",
                table: "ReturnInspections",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_SimSerialNumber",
                table: "ReturnInspections",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EquipmentPhotoSetId",
                table: "EquipmentHandoverItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_Imei",
                table: "EquipmentHandoverItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_Kind",
                table: "EquipmentHandoverItems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_MobileNumber",
                table: "EquipmentHandoverItems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_Notes",
                table: "EquipmentHandoverItems",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot_SimSerialNumber",
                table: "EquipmentHandoverItems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpectedCollectionFromDate",
                table: "ConsultantAllocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpectedCollectionToDate",
                table: "ConsultantAllocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEndOfEngagement",
                table: "ConsultantAllocations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConsultantAllocationCollectionNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmailSnapshot = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultantAllocationCollectionNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsultantAllocationCollectionNotifications_ConsultantAlloc~",
                        column: x => x.ConsultantAllocationId,
                        principalTable: "ConsultantAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentHandoverId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedDeliveryFromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedDeliveryToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DispatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DispatchedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentDispatches_EquipmentHandovers_EquipmentHandoverId",
                        column: x => x.EquipmentHandoverId,
                        principalTable: "EquipmentHandovers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentPhotoSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EquipmentReturnId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentPhotoSets", x => x.Id);
                    table.CheckConstraint("CK_EquipmentPhotoSets_Source_ReturnId", "(\"Source\" = 'InitialRegistration' AND \"EquipmentReturnId\" IS NULL) OR (\"Source\" = 'Return' AND \"EquipmentReturnId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EquipmentPhotoSets_EquipmentReturns_EquipmentReturnId",
                        column: x => x.EquipmentReturnId,
                        principalTable: "EquipmentReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentPhotoSets_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MobilePhoneSpecifications",
                columns: table => new
                {
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Imei = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MobileNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SimSerialNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilePhoneSpecifications", x => x.EquipmentId);
                    table.ForeignKey(
                        name: "FK_MobilePhoneSpecifications_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentDispatchNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentDispatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmailSnapshot = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentDispatchNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentDispatchNotifications_EquipmentDispatches_Equipmen~",
                        column: x => x.EquipmentDispatchId,
                        principalTable: "EquipmentDispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentPhotoSetPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentPhotoSetId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_EquipmentPhotoSetPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentPhotoSetPhotos_EquipmentPhotoSets_EquipmentPhotoSe~",
                        column: x => x.EquipmentPhotoSetId,
                        principalTable: "EquipmentPhotoSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHandoverItems_EquipmentPhotoSetId",
                table: "EquipmentHandoverItems",
                column: "EquipmentPhotoSetId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultantAllocationCollectionNotifications_ConsultantAlloc~",
                table: "ConsultantAllocationCollectionNotifications",
                column: "ConsultantAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultantAllocationCollectionNotifications_Status",
                table: "ConsultantAllocationCollectionNotifications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDispatches_EquipmentHandoverId",
                table: "EquipmentDispatches",
                column: "EquipmentHandoverId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDispatchNotifications_EquipmentDispatchId",
                table: "EquipmentDispatchNotifications",
                column: "EquipmentDispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentDispatchNotifications_Status",
                table: "EquipmentDispatchNotifications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentPhotoSetPhotos_EquipmentPhotoSetId",
                table: "EquipmentPhotoSetPhotos",
                column: "EquipmentPhotoSetId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentPhotoSets_EquipmentId",
                table: "EquipmentPhotoSets",
                column: "EquipmentId",
                unique: true,
                filter: "\"Status\" = 'Current'");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentPhotoSets_EquipmentReturnId",
                table: "EquipmentPhotoSets",
                column: "EquipmentReturnId");

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentHandoverItems_EquipmentPhotoSets_EquipmentPhotoSet~",
                table: "EquipmentHandoverItems",
                column: "EquipmentPhotoSetId",
                principalTable: "EquipmentPhotoSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentHandoverItems_EquipmentPhotoSets_EquipmentPhotoSet~",
                table: "EquipmentHandoverItems");

            migrationBuilder.DropTable(
                name: "ConsultantAllocationCollectionNotifications");

            migrationBuilder.DropTable(
                name: "EquipmentDispatchNotifications");

            migrationBuilder.DropTable(
                name: "EquipmentPhotoSetPhotos");

            migrationBuilder.DropTable(
                name: "MobilePhoneSpecifications");

            migrationBuilder.DropTable(
                name: "EquipmentDispatches");

            migrationBuilder.DropTable(
                name: "EquipmentPhotoSets");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentHandoverItems_EquipmentPhotoSetId",
                table: "EquipmentHandoverItems");

            migrationBuilder.DropColumn(
                name: "Snapshot_Imei",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "Snapshot_Kind",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "Snapshot_MobileNumber",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "Snapshot_Notes",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "Snapshot_SimSerialNumber",
                table: "ReturnInspections");

            migrationBuilder.DropColumn(
                name: "EquipmentPhotoSetId",
                table: "EquipmentHandoverItems");

            migrationBuilder.DropColumn(
                name: "Snapshot_Imei",
                table: "EquipmentHandoverItems");

            migrationBuilder.DropColumn(
                name: "Snapshot_Kind",
                table: "EquipmentHandoverItems");

            migrationBuilder.DropColumn(
                name: "Snapshot_MobileNumber",
                table: "EquipmentHandoverItems");

            migrationBuilder.DropColumn(
                name: "Snapshot_Notes",
                table: "EquipmentHandoverItems");

            migrationBuilder.DropColumn(
                name: "Snapshot_SimSerialNumber",
                table: "EquipmentHandoverItems");

            migrationBuilder.DropColumn(
                name: "ExpectedCollectionFromDate",
                table: "ConsultantAllocations");

            migrationBuilder.DropColumn(
                name: "ExpectedCollectionToDate",
                table: "ConsultantAllocations");

            migrationBuilder.DropColumn(
                name: "IsEndOfEngagement",
                table: "ConsultantAllocations");
        }
    }
}
