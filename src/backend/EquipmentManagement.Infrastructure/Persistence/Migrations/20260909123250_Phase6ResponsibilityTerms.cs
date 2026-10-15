using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6ResponsibilityTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "responsibility_term_document_number_seq");

            migrationBuilder.CreateTable(
                name: "ResponsibilityTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentHandoverId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedByDisplayNameSnapshot = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponsibilityTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResponsibilityTerms_EquipmentHandovers_EquipmentHandoverId",
                        column: x => x.EquipmentHandoverId,
                        principalTable: "EquipmentHandovers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTerms_DocumentNumber",
                table: "ResponsibilityTerms",
                column: "DocumentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTerms_EquipmentHandoverId",
                table: "ResponsibilityTerms",
                column: "EquipmentHandoverId",
                unique: true,
                filter: "\"Status\" = 'Generated'");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTerms_EquipmentHandoverId_Version",
                table: "ResponsibilityTerms",
                columns: new[] { "EquipmentHandoverId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTerms_GeneratedAtUtc",
                table: "ResponsibilityTerms",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityTerms_Status",
                table: "ResponsibilityTerms",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResponsibilityTerms");

            migrationBuilder.DropSequence(
                name: "responsibility_term_document_number_seq");
        }
    }
}
