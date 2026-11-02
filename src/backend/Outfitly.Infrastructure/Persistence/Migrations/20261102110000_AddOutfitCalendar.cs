using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Outfitly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutfitCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutfitAssignments",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OutfitId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutfitAssignments", x => new { x.OwnerId, x.Date, x.OutfitId });
                    table.ForeignKey(
                        name: "FK_OutfitAssignments_Outfits_OutfitId_OwnerId",
                        columns: x => new { x.OutfitId, x.OwnerId },
                        principalTable: "Outfits",
                        principalColumns: new[] { "Id", "OwnerId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutfitAssignments_OutfitId_OwnerId",
                table: "OutfitAssignments",
                columns: new[] { "OutfitId", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutfitAssignments");
        }
    }
}
