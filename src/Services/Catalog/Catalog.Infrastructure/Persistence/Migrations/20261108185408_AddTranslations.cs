using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Genres_NormalizedName",
                table: "Genres");

            migrationBuilder.CreateTable(
                name: "EpisodeTranslations",
                columns: table => new
                {
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Synopsis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodeTranslations", x => new { x.ContentId, x.Language });
                    table.ForeignKey(
                        name: "FK_EpisodeTranslations_Episodes_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GenreTranslations",
                columns: table => new
                {
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    GenreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenreTranslations", x => new { x.GenreId, x.Language });
                    table.ForeignKey(
                        name: "FK_GenreTranslations_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovieTranslations",
                columns: table => new
                {
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Synopsis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieTranslations", x => new { x.ContentId, x.Language });
                    table.ForeignKey(
                        name: "FK_MovieTranslations_Movies_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonTranslations",
                columns: table => new
                {
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Biography = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonTranslations", x => new { x.PersonId, x.Language });
                    table.ForeignKey(
                        name: "FK_PersonTranslations_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeasonTranslations",
                columns: table => new
                {
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonTranslations", x => new { x.SeasonId, x.Language });
                    table.ForeignKey(
                        name: "FK_SeasonTranslations_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesTranslations",
                columns: table => new
                {
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Synopsis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesTranslations", x => new { x.ContentId, x.Language });
                    table.ForeignKey(
                        name: "FK_SeriesTranslations_Series_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GenreTranslations_Language_NormalizedName",
                table: "GenreTranslations",
                columns: new[] { "Language", "NormalizedName" },
                unique: true);

            // Existing text was written in the default language; it moves before the old columns go.
            migrationBuilder.Sql("""
                INSERT INTO "MovieTranslations" ("ContentId", "Language", "Title", "Synopsis") SELECT "Id", 'tr', "Title", "Synopsis" FROM "Movies";
                INSERT INTO "SeriesTranslations" ("ContentId", "Language", "Title", "Synopsis") SELECT "Id", 'tr', "Title", "Synopsis" FROM "Series";
                INSERT INTO "EpisodeTranslations" ("ContentId", "Language", "Title", "Synopsis") SELECT "Id", 'tr', "Title", "Synopsis" FROM "Episodes";
                INSERT INTO "SeasonTranslations" ("SeasonId", "Language", "Title") SELECT "Id", 'tr', "Title" FROM "Seasons" WHERE "Title" IS NOT NULL;
                INSERT INTO "GenreTranslations" ("GenreId", "Language", "Name", "NormalizedName") SELECT "Id", 'tr', "Name", "NormalizedName" FROM "Genres";
                INSERT INTO "PersonTranslations" ("PersonId", "Language", "Biography") SELECT "Id", 'tr', "Biography" FROM "People" WHERE "Biography" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "Synopsis",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "Biography",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Synopsis",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "Synopsis",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Episodes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpisodeTranslations");

            migrationBuilder.DropTable(
                name: "GenreTranslations");

            migrationBuilder.DropTable(
                name: "MovieTranslations");

            migrationBuilder.DropTable(
                name: "PersonTranslations");

            migrationBuilder.DropTable(
                name: "SeasonTranslations");

            migrationBuilder.DropTable(
                name: "SeriesTranslations");

            migrationBuilder.AddColumn<string>(
                name: "Synopsis",
                table: "Series",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Series",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Seasons",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Biography",
                table: "People",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Synopsis",
                table: "Movies",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Movies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Genres",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Genres",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Synopsis",
                table: "Episodes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Episodes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Genres_NormalizedName",
                table: "Genres",
                column: "NormalizedName",
                unique: true);
        }
    }
}
