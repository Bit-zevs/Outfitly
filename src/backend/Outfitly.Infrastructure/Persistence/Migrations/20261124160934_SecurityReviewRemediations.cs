using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Outfitly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityReviewRemediations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PostInteractions_ItemId_ParentId_CreatedAt_Id",
                table: "PostInteractions");

            migrationBuilder.DropIndex(
                name: "IX_PostInteractions_OutfitId_ParentId_CreatedAt_Id",
                table: "PostInteractions");

            migrationBuilder.AddColumn<bool>(
                name: "HasCopiedPhoto",
                table: "WardrobeItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing receipts identify direct copies and copies still in their cloned outfit.
            migrationBuilder.Sql("""
                UPDATE "WardrobeItems" AS item
                SET "HasCopiedPhoto" = TRUE
                WHERE item."PhotoStorageKey" IS NOT NULL
                  AND EXISTS (
                    SELECT 1 FROM "CopyReceipts" AS receipt
                    WHERE receipt."UserId" = item."OwnerId"
                      AND (receipt."ResultId" = item."Id" OR EXISTS (
                        SELECT 1 FROM "OutfitItems" AS member
                        WHERE member."OutfitId" = receipt."ResultId"
                          AND member."WardrobeItemId" = item."Id"))
                  );
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "CopyRevision",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "PostInteractions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "owner");

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "PasswordResetDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "PreviousConfirmationTokens",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreviousConfirmationTokens", x => x.Hash);
                    table.ForeignKey(
                        name: "FK_PreviousConfirmationTokens_Accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "Accounts",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_ItemId_Audience_ParentId_CreatedAt_Id",
                table: "PostInteractions",
                columns: new[] { "ItemId", "Audience", "ParentId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_OutfitId_Audience_ParentId_CreatedAt_Id",
                table: "PostInteractions",
                columns: new[] { "OutfitId", "Audience", "ParentId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginSessions_ExpiresAtUtc",
                table: "LoginSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PreviousConfirmationTokens_ExpiresAtUtc",
                table: "PreviousConfirmationTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PreviousConfirmationTokens_UserId",
                table: "PreviousConfirmationTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PreviousConfirmationTokens");

            migrationBuilder.DropIndex(
                name: "IX_PostInteractions_ItemId_Audience_ParentId_CreatedAt_Id",
                table: "PostInteractions");

            migrationBuilder.DropIndex(
                name: "IX_PostInteractions_OutfitId_Audience_ParentId_CreatedAt_Id",
                table: "PostInteractions");

            migrationBuilder.DropIndex(
                name: "IX_LoginSessions_ExpiresAtUtc",
                table: "LoginSessions");

            migrationBuilder.DropColumn(
                name: "HasCopiedPhoto",
                table: "WardrobeItems");

            migrationBuilder.DropColumn(
                name: "CopyRevision",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "PostInteractions");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "PasswordResetDeliveries");

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_ItemId_ParentId_CreatedAt_Id",
                table: "PostInteractions",
                columns: new[] { "ItemId", "ParentId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_OutfitId_ParentId_CreatedAt_Id",
                table: "PostInteractions",
                columns: new[] { "OutfitId", "ParentId", "CreatedAt", "Id" });
        }
    }
}
