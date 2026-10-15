using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8ImportSessionRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RowsJson",
                table: "ImportSessions",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "ImportSessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill any pre-existing rows (Phase 8 retention review): derive
            // ExpiresAtUtc from CreatedAtUtc using the same 24h default as
            // ImportLimitsOptions.SessionValidityHours, rather than leaving the
            // AddColumn sentinel default in place. A Pending row backfilled this
            // way will simply read as already-expired on next access/confirm —
            // safe, since its PII snapshot is cleared lazily at that point
            // rather than silently treated as still valid.
            migrationBuilder.Sql(
                "UPDATE \"ImportSessions\" SET \"ExpiresAtUtc\" = \"CreatedAtUtc\" + INTERVAL '24 hours';");

            migrationBuilder.CreateIndex(
                name: "IX_ImportSessions_ExpiresAtUtc",
                table: "ImportSessions",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportSessions_ExpiresAtUtc",
                table: "ImportSessions");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "ImportSessions");

            migrationBuilder.AlterColumn<string>(
                name: "RowsJson",
                table: "ImportSessions",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }
    }
}
