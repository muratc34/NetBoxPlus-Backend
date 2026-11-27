using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTitleRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TitleRatings",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleRatings", x => new { x.ProfileId, x.TitleId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_TitleRatings_ProfileId_RatedAt",
                table: "TitleRatings",
                columns: new[] { "ProfileId", "RatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TitleRatings_UserId",
                table: "TitleRatings",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TitleRatings");
        }
    }
}
