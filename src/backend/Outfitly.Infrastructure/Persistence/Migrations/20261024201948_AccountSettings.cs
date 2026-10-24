using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Outfitly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailChangeExpiresAtUtc",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailChangeIssuedAtUtc",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailChangeTokenHash",
                table: "Accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingEmail",
                table: "Accounts",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingNormalizedEmail",
                table: "Accounts",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_EmailChangeTokenHash",
                table: "Accounts",
                column: "EmailChangeTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_EmailChangeTokenHash",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "EmailChangeExpiresAtUtc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "EmailChangeIssuedAtUtc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "EmailChangeTokenHash",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PendingEmail",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PendingNormalizedEmail",
                table: "Accounts");
        }
    }
}
