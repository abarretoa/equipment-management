using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EquipmentManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentPurchaseInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "Equipment",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PurchaseDate",
                table: "Equipment",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseVendor",
                table: "Equipment",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "Equipment");

            migrationBuilder.DropColumn(
                name: "PurchaseDate",
                table: "Equipment");

            migrationBuilder.DropColumn(
                name: "PurchaseVendor",
                table: "Equipment");
        }
    }
}
