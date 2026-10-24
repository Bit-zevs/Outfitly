using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Outfitly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmailConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationExpiresAtUtc",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationIssuedAtUtc",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationTokenHash",
                table: "Accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Preserve access for accounts created before email confirmation.
            migrationBuilder.Sql("UPDATE \"Accounts\" SET \"EmailConfirmed\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ConfirmationTokenHash",
                table: "Accounts",
                column: "ConfirmationTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_ConfirmationTokenHash",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ConfirmationExpiresAtUtc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ConfirmationIssuedAtUtc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ConfirmationTokenHash",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "EmailConfirmed",
                table: "Accounts");
        }
    }
}
