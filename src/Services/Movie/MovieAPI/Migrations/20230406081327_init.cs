using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MovieAPI.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MovieDescription = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    PosterPath = table.Column<string>(type: "text", nullable: true),
                    BackdropPicPath = table.Column<string>(type: "text", nullable: true),
                    MoviePath = table.Column<string>(type: "text", nullable: true),
                    TrailerPath = table.Column<string>(type: "text", nullable: true),
                    MovieClickCount = table.Column<int>(type: "integer", nullable: false),
                    ReleaseYear = table.Column<int>(type: "integer", maxLength: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgeRating",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MovieId = table.Column<Guid>(type: "uuid", nullable: false),
                    RatingCode = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgeRating", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgeRating_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MovieId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenreCode = table.Column<int>(type: "integer", nullable: false),
                    GenreTitle = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Genres_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Movies",
                columns: new[] { "Id", "BackdropPicPath", "MovieClickCount", "MovieDescription", "MoviePath", "PosterPath", "ReleaseYear", "Title", "TrailerPath" },
                values: new object[,]
                {
                    { new Guid("aad9fa7b-ed42-4578-8bd4-9d11300c5aa7"), "/backdrops/2b0c25864b3142e38ef89e3818ac026b.jpg", 0, "Komutan Logar, halı tüccarı Arif'i zamanda bir milyon yıl geriye gönderir. Burada dinozorlarla karşılaşan Arif, sakalını tıraş etmenin bir yolunu da bulur.", null, "/images/7c176488167c4a389acf5eea903db08b.jpg", 2008, "A.R.O.G", "/trailers/881bc6d2b70047aea0da2bc39f437e85.mp4" },
                    { new Guid("c8542199-bc95-4d19-8ef9-cbf85134a113"), "/backdrops/8af11b4faa4645f986553222ae2af034.jpg", 0, "İki Osmanlı yetkilisi, sultanlarının adına ABD başkanına elmas bir kolye teslim etmek üzere cesurca Vahşi Batı'ya gider. Ama yaşanan kaos yüzünden bu görev raydan çıkar.", null, "/images/bb9434b7c54546488964dd2cc7687067.jpg", 2009, "Yahşi Batı", "/trailers/20a27c60c72e4f58a2cad022214de908.mp4" },
                    { new Guid("e381968a-6868-4d18-8a5e-d0b689526786"), "/backdrops/92a1013355f641098dbfb8ada29d378d.jpg", 0, "Sahte UFO fotoğrafçısı ve halı tüccarı olan Arif uzaylılarca kaçırılır. Gora gezegeninde, yaklaşan göktaşından kurtulmak ve özgür kalmak için arkadaşlarına yardım eder.", null, "/images/3ebd25faf255400a94f923db9a020d0c.jpg", 2004, "G.O.R.A", "/trailers/21caaee63a934d40adbbac4c09fbe97d.mp4" }
                });

            migrationBuilder.InsertData(
                table: "AgeRating",
                columns: new[] { "Id", "MovieId", "Rating", "RatingCode" },
                values: new object[,]
                {
                    { new Guid("235a753a-7069-41c2-9184-d74bc518e025"), new Guid("c8542199-bc95-4d19-8ef9-cbf85134a113"), "13+", 3 },
                    { new Guid("4511ce42-49af-4172-899c-66d90b740cbf"), new Guid("e381968a-6868-4d18-8a5e-d0b689526786"), "18+", 5 },
                    { new Guid("86f4410a-834d-41f0-ab03-9f7f423ffa68"), new Guid("aad9fa7b-ed42-4578-8bd4-9d11300c5aa7"), "13+", 3 }
                });

            migrationBuilder.InsertData(
                table: "Genres",
                columns: new[] { "Id", "GenreCode", "GenreTitle", "MovieId" },
                values: new object[,]
                {
                    { new Guid("25ef1df0-a2ea-45d0-940d-43af27713381"), 1, "Aksiyon", new Guid("aad9fa7b-ed42-4578-8bd4-9d11300c5aa7") },
                    { new Guid("3932bbf2-d6ee-4cfd-af39-23b737713046"), 4, "Komedi", new Guid("c8542199-bc95-4d19-8ef9-cbf85134a113") },
                    { new Guid("7da6cb20-2174-44d9-937c-4f8cdadd0ff6"), 1, "Aksiyon", new Guid("e381968a-6868-4d18-8a5e-d0b689526786") },
                    { new Guid("8d648e87-0387-44f7-a3b5-46e21a1bb2d0"), 18, "Batılı", new Guid("c8542199-bc95-4d19-8ef9-cbf85134a113") },
                    { new Guid("945bb268-f789-4ba4-a38d-ae25a31dfbf0"), 2, "Macera", new Guid("e381968a-6868-4d18-8a5e-d0b689526786") },
                    { new Guid("9477c56a-c7c3-4eaa-9b6c-c5b900c3cc68"), 2, "Macera", new Guid("aad9fa7b-ed42-4578-8bd4-9d11300c5aa7") },
                    { new Guid("cb8dd30d-453d-4042-98a6-03cb3aab3a21"), 2, "Macera", new Guid("c8542199-bc95-4d19-8ef9-cbf85134a113") },
                    { new Guid("cbce449f-aacd-45b9-876d-fe5ff9a19549"), 15, "Bilim Kurgu", new Guid("e381968a-6868-4d18-8a5e-d0b689526786") },
                    { new Guid("dfe037b0-1590-40ed-b27e-c8bfa0d8b312"), 9, "Fantastik", new Guid("c8542199-bc95-4d19-8ef9-cbf85134a113") },
                    { new Guid("e4768910-5cd2-4b18-b00b-3e42b6be2689"), 15, "Bilim Kurgu", new Guid("aad9fa7b-ed42-4578-8bd4-9d11300c5aa7") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgeRating_MovieId",
                table: "AgeRating",
                column: "MovieId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Genres_MovieId",
                table: "Genres",
                column: "MovieId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgeRating");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Movies");
        }
    }
}
