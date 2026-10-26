using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Outfitly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersonalWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CopyReceipts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopyReceipts", x => new { x.UserId, x.OperationId });
                    table.ForeignKey(
                        name: "FK_CopyReceipts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditorDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveSlot = table.Column<string>(type: "text", nullable: true),
                    Data = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    BaseVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PhotoStorageKey = table.Column<string>(type: "text", nullable: true),
                    CompletedId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorDrafts", x => x.Id);
                    table.CheckConstraint("CK_EditorDrafts_Kind", "\"Kind\" IN ('items', 'outfits')");
                    table.ForeignKey(
                        name: "FK_EditorDrafts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => x.Id);
                    table.CheckConstraint("CK_Favorites_Kind", "\"Kind\" IN ('items', 'outfits')");
                    table.ForeignKey(
                        name: "FK_Favorites_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EditorDrafts_UserId_ActiveSlot",
                table: "EditorDrafts",
                columns: new[] { "UserId", "ActiveSlot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorDrafts_UserId_UpdatedAtUtc_Id",
                table: "EditorDrafts",
                columns: new[] { "UserId", "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId_Kind_CreatedAtUtc_Id",
                table: "Favorites",
                columns: new[] { "UserId", "Kind", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId_Kind_TargetId",
                table: "Favorites",
                columns: new[] { "UserId", "Kind", "TargetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopyReceipts");

            migrationBuilder.DropTable(
                name: "EditorDrafts");

            migrationBuilder.DropTable(
                name: "Favorites");
        }
    }
}
