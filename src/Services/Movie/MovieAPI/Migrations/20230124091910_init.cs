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
                    ReleaseYear = table.Column<int>(type: "integer", maxLength: 4, nullable: false),
                    MpaaRating = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
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
                columns: new[] { "Id", "BackdropPicPath", "MovieClickCount", "MovieDescription", "MoviePath", "MpaaRating", "PosterPath", "ReleaseYear", "Title", "TrailerPath" },
                values: new object[,]
                {
                    { new Guid("0d26828a-e2d4-4e1e-99e8-c850b6b84cbf"), "/backdrops/92a1013355f641098dbfb8ada29d378d.jpg", 0, "Sahte UFO fotoğrafçısı ve halı tüccarı olan Arif uzaylılarca kaçırılır. Gora gezegeninde, yaklaşan göktaşından kurtulmak ve özgür kalmak için arkadaşlarına yardım eder.", null, "P18", "/images/3ebd25faf255400a94f923db9a020d0c.jpg", 2004, "G.O.R.A", "/trailers/21caaee63a934d40adbbac4c09fbe97d.mp4" },
                    { new Guid("14b24a0e-6c17-4bda-802a-a0c31325cd16"), "/backdrops/8af11b4faa4645f986553222ae2af034.jpg", 0, "İki Osmanlı yetkilisi, sultanlarının adına ABD başkanına elmas bir kolye teslim etmek üzere cesurca Vahşi Batı'ya gider. Ama yaşanan kaos yüzünden bu görev raydan çıkar.", null, "P18", "/images/bb9434b7c54546488964dd2cc7687067.jpg", 2009, "Yahşi Batı", "/trailers/20a27c60c72e4f58a2cad022214de908.mp4" },
                    { new Guid("43e3434b-c570-4c6c-bc16-7b62c183cde9"), "/backdrops/2b0c25864b3142e38ef89e3818ac026b.jpg", 0, "Komutan Logar, halı tüccarı Arif'i zamanda bir milyon yıl geriye gönderir. Burada dinozorlarla karşılaşan Arif, sakalını tıraş etmenin bir yolunu da bulur.", null, "P13", "/images/7c176488167c4a389acf5eea903db08b.jpg", 2008, "A.R.O.G", "/trailers/881bc6d2b70047aea0da2bc39f437e85.mp4" }
                });

            migrationBuilder.InsertData(
                table: "Genres",
                columns: new[] { "Id", "GenreCode", "GenreTitle", "MovieId" },
                values: new object[,]
                {
                    { new Guid("070ecd47-2425-47c8-b51c-2661a80a5931"), 15, "Bilim Kurgu", new Guid("0d26828a-e2d4-4e1e-99e8-c850b6b84cbf") },
                    { new Guid("4055ee8f-def7-4d20-88eb-c70ec0456839"), 18, "Batılı", new Guid("14b24a0e-6c17-4bda-802a-a0c31325cd16") },
                    { new Guid("40a295f6-3dcd-46ec-93e7-c98f8311cad3"), 2, "Macera", new Guid("14b24a0e-6c17-4bda-802a-a0c31325cd16") },
                    { new Guid("8dc1695a-5f22-4246-98f9-c0a08f76bd83"), 1, "Aksiyon", new Guid("0d26828a-e2d4-4e1e-99e8-c850b6b84cbf") },
                    { new Guid("907af036-93e8-4b2c-bacf-84bdf3414a2f"), 15, "Bilim Kurgu", new Guid("43e3434b-c570-4c6c-bc16-7b62c183cde9") },
                    { new Guid("98c27043-4f59-42b1-b9ad-5ab6330b8bfe"), 4, "Komedi", new Guid("14b24a0e-6c17-4bda-802a-a0c31325cd16") },
                    { new Guid("d8c38547-2721-41fe-93a2-47693ab00300"), 2, "Macera", new Guid("43e3434b-c570-4c6c-bc16-7b62c183cde9") },
                    { new Guid("deb36810-6e9d-4e8f-9f7e-b4856509fe42"), 1, "Aksiyon", new Guid("43e3434b-c570-4c6c-bc16-7b62c183cde9") },
                    { new Guid("e0265f7c-3a1c-4ba6-960f-c04ccb271efa"), 9, "Fantastik", new Guid("14b24a0e-6c17-4bda-802a-a0c31325cd16") },
                    { new Guid("fefbffe2-bd9d-44cf-b78a-8871783ae509"), 2, "Macera", new Guid("0d26828a-e2d4-4e1e-99e8-c850b6b84cbf") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Genres_MovieId",
                table: "Genres",
                column: "MovieId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Movies");
        }
    }
}
