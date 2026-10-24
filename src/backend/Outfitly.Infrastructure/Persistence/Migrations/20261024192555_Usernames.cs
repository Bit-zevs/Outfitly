using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Outfitly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Usernames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // Two leading zero bits make 26 base32 digits from the canonical 128-bit UUID.
            // This is injective and agrees with Domain.Usernames.FromId, including leading zeros.
            migrationBuilder.Sql("""
                UPDATE "Users" AS u SET "Username" = 'u_' || (
                    SELECT string_agg(substr('abcdefghijklmnopqrstuvwxyz234567',
                        substring('00' || (('x' || replace(u."Id"::text, '-', ''))::bit(128))::text
                            FROM digit * 5 + 1 FOR 5)::bit(5)::integer + 1, 1), '' ORDER BY digit)
                    FROM generate_series(0, 25) AS digit
                );
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Username", table: "Users", type: "character varying(30)", maxLength: 30,
                nullable: false, oldClrType: typeof(string), oldType: "character varying(30)", oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Users");
        }
    }
}
