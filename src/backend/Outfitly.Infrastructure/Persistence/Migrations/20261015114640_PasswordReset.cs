using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Outfitly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "SessionVersion", table: "Accounts",
                type: "uuid", nullable: false, defaultValue: Guid.Empty);
            migrationBuilder.AddColumn<Guid>(name: "SessionVersion", table: "LoginSessions",
                type: "uuid", nullable: false, defaultValue: Guid.Empty);
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetExpiresAtUtc",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetIssuedAtUtc",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "Accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_PasswordResetTokenHash",
                table: "Accounts",
                column: "PasswordResetTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SessionVersion", table: "Accounts");
            migrationBuilder.DropColumn(name: "SessionVersion", table: "LoginSessions");
            migrationBuilder.DropIndex(
                name: "IX_Accounts_PasswordResetTokenHash",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PasswordResetExpiresAtUtc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PasswordResetIssuedAtUtc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "Accounts");
        }
    }
}
