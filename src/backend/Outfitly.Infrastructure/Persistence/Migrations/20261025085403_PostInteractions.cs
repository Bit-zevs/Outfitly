using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Outfitly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PostInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PostInteractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutfitId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostInteractions", x => x.Id);
                    table.CheckConstraint("CK_PostInteractions_Kind", "\"Kind\" IN ('view', 'like', 'comment')");
                    table.CheckConstraint("CK_PostInteractions_Target", "(\"ItemId\" IS NULL) <> (\"OutfitId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_PostInteractions_Outfits_OutfitId",
                        column: x => x.OutfitId,
                        principalTable: "Outfits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostInteractions_PostInteractions_ParentId",
                        column: x => x.ParentId,
                        principalTable: "PostInteractions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostInteractions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostInteractions_WardrobeItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "WardrobeItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_ItemId_ParentId_CreatedAt_Id",
                table: "PostInteractions",
                columns: new[] { "ItemId", "ParentId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_ItemId_UserId_Kind",
                table: "PostInteractions",
                columns: new[] { "ItemId", "UserId", "Kind" },
                unique: true,
                filter: "\"Kind\" <> 'comment'");

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_OutfitId_ParentId_CreatedAt_Id",
                table: "PostInteractions",
                columns: new[] { "OutfitId", "ParentId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_OutfitId_UserId_Kind",
                table: "PostInteractions",
                columns: new[] { "OutfitId", "UserId", "Kind" },
                unique: true,
                filter: "\"Kind\" <> 'comment'");

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_ParentId",
                table: "PostInteractions",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_PostInteractions_UserId",
                table: "PostInteractions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostInteractions");
        }
    }
}
