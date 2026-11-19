using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Streaming.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPopularity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyPlays",
                columns: table => new
                {
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Plays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyPlays", x => new { x.Day, x.TitleId });
                });

            migrationBuilder.CreateTable(
                name: "PopularityRankings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CountedThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PopularityRankings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PopularTitles",
                columns: table => new
                {
                    RankingId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PopularTitles", x => new { x.RankingId, x.TitleId });
                    table.ForeignKey(
                        name: "FK_PopularTitles_PopularityRankings_RankingId",
                        column: x => x.RankingId,
                        principalTable: "PopularityRankings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyPlays");

            migrationBuilder.DropTable(
                name: "PopularTitles");

            migrationBuilder.DropTable(
                name: "PopularityRankings");
        }
    }
}
