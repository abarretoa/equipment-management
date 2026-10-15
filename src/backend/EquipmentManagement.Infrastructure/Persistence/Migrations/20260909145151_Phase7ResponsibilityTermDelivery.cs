using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7ResponsibilityTermDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResponsibilityTermDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsibilityTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmailSnapshot = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    RecipientOverridden = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SentByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponsibilityTermDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResponsibilityTermDeliveries_ResponsibilityTerms_Responsibi~",
                        column: x => x.ResponsibilityTermId,
                        principalTable: "ResponsibilityTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResponsibilityTermSignedArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsibilityTermId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SignedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponsibilityTermSignedArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResponsibilityTermSignedArtifacts_ResponsibilityTerms_Respo~",
                        column: x => x.ResponsibilityTermId,
                        principalTable: "ResponsibilityTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTermDeliveries_ResponsibilityTermId",
                table: "ResponsibilityTermDeliveries",
                column: "ResponsibilityTermId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTermDeliveries_SentAtUtc",
                table: "ResponsibilityTermDeliveries",
                column: "SentAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTermDeliveries_Status",
                table: "ResponsibilityTermDeliveries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTermSignedArtifacts_ResponsibilityTermId",
                table: "ResponsibilityTermSignedArtifacts",
                column: "ResponsibilityTermId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResponsibilityTermDeliveries");

            migrationBuilder.DropTable(
                name: "ResponsibilityTermSignedArtifacts");
        }
    }
}
