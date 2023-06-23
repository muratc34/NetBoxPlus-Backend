using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MovieAPI.Migrations
{
    /// <inheritdoc />
    public partial class ratingformovietable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgeRating",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatingCode = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgeRating", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GenreCode = table.Column<int>(type: "integer", nullable: false),
                    GenreTitle = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MovieGenres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MovieId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenreId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieGenres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgeRatingId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.ForeignKey(
                        name: "FK_Movies_AgeRating_AgeRatingId",
                        column: x => x.AgeRatingId,
                        principalTable: "AgeRating",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Genres",
                columns: new[] { "Id", "GenreCode", "GenreTitle" },
                values: new object[,]
                {
                    { new Guid("0132c181-faa0-4dc2-9fcf-5c543b9ec61a"), 16, "Gerilim" },
                    { new Guid("0eba0bdf-3ad7-427a-b602-f46e83c61cb7"), 6, "Belgesel" },
                    { new Guid("2a0448ae-b398-4fca-bc86-4fc662092c7f"), 9, "Fantastik" },
                    { new Guid("2a448041-443d-420a-b0b1-8fdfac3a6466"), 3, "Animasyon" },
                    { new Guid("3abacc9e-6193-4ad0-9c86-63a580397be5"), 4, "Komedi" },
                    { new Guid("3f47a5cd-417f-4283-b6aa-58e01e1d6a17"), 11, "Korku" },
                    { new Guid("4702cd3a-7bcb-404e-b9ed-b063833540cc"), 2, "Macera" },
                    { new Guid("50821fe1-b324-4637-a78e-e73617ef009d"), 5, "Suç" },
                    { new Guid("5dfb8b50-0623-4014-b008-f8f18541f9cf"), 14, "Romantik" },
                    { new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), 1, "Aksiyon" },
                    { new Guid("74c0a32a-ea94-43a7-b18c-db5c086232d1"), 18, "Batılı" },
                    { new Guid("87eb8c63-8d0e-4838-add0-bf00b281c956"), 17, "Savaş" },
                    { new Guid("a022589b-b7e8-4e9f-9cfd-34ddb83e3338"), 10, "Tarih" },
                    { new Guid("a21d77f8-a390-417c-9e1d-fea1356628e8"), 8, "Aile" },
                    { new Guid("aa7845db-b450-47d4-ac47-ad74b9e74330"), 7, "Dram" },
                    { new Guid("acb3ce7c-f975-4dcd-928f-37c072aa75b2"), 12, "Müzikal" },
                    { new Guid("b1807636-77ba-4418-b7c7-b406aba8f4a5"), 15, "Bilim Kurgu" },
                    { new Guid("e9363cb0-f339-4987-9b13-55908b8c8f6c"), 13, "Gizem" }
                });

            migrationBuilder.InsertData(
                table: "MovieGenres",
                columns: new[] { "Id", "GenreId", "MovieId" },
                values: new object[,]
                {
                    { new Guid("001de4af-af68-4851-9188-5c58d877ce83"), new Guid("4702cd3a-7bcb-404e-b9ed-b063833540cc"), new Guid("c52b5792-3a9e-4dfb-9367-470c33f02c45") },
                    { new Guid("039fb776-9922-48a0-9017-271c47112569"), new Guid("aa7845db-b450-47d4-ac47-ad74b9e74330"), new Guid("91149f27-1f4d-4d24-b1fd-3758208403b6") },
                    { new Guid("05e81086-88af-4666-9bb5-ebf024a4866b"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("2f3abab2-f661-427e-997c-5c53b10fe80f") },
                    { new Guid("0a2d3e80-2e9f-4cf3-bc36-1021e6ab7346"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("6a68caa3-5bb5-431e-8186-febe68a587ce") },
                    { new Guid("18e9a9c0-352d-49e0-b842-4f2be9bf98c8"), new Guid("b1807636-77ba-4418-b7c7-b406aba8f4a5"), new Guid("e70d6db8-8be3-4bd8-966c-8cc1e23e432b") },
                    { new Guid("2f21d1c0-6597-4726-aadf-323f368a724a"), new Guid("aa7845db-b450-47d4-ac47-ad74b9e74330"), new Guid("87361bc9-2c0f-416c-886a-5096241c21ff") },
                    { new Guid("2f59285e-6bf3-4cb0-ba12-1d6f41c335d5"), new Guid("5dfb8b50-0623-4014-b008-f8f18541f9cf"), new Guid("2f3abab2-f661-427e-997c-5c53b10fe80f") },
                    { new Guid("2ff68010-0d70-4051-b61a-5130a2fa02fc"), new Guid("4702cd3a-7bcb-404e-b9ed-b063833540cc"), new Guid("1b597ee6-1b36-493e-b4e4-6795f7613bc6") },
                    { new Guid("3005f34e-9a9e-40c1-8c51-286dc976912f"), new Guid("50821fe1-b324-4637-a78e-e73617ef009d"), new Guid("9fa2a432-f244-44e1-9a88-cdab005e7d02") },
                    { new Guid("3dc823e5-8d72-4d43-9b95-0663d1f26446"), new Guid("0132c181-faa0-4dc2-9fcf-5c543b9ec61a"), new Guid("c2659c76-e004-4bb6-906e-09a2c298f406") },
                    { new Guid("46bd8e93-e399-4120-b624-a3a02fa4eaae"), new Guid("b1807636-77ba-4418-b7c7-b406aba8f4a5"), new Guid("c9c75601-c909-4d84-b61b-5768be2a94f5") },
                    { new Guid("545e9d63-8660-4ce4-b50f-4ae551d94bd8"), new Guid("4702cd3a-7bcb-404e-b9ed-b063833540cc"), new Guid("6a68caa3-5bb5-431e-8186-febe68a587ce") },
                    { new Guid("57e9fb01-8bd9-4cbb-8b44-4dc0c8155c93"), new Guid("4702cd3a-7bcb-404e-b9ed-b063833540cc"), new Guid("9aa37ea5-e2b9-4bc8-8117-b717c0b7b731") },
                    { new Guid("6269dbd0-fee2-48ce-a482-f6f7ef4155e6"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("c2659c76-e004-4bb6-906e-09a2c298f406") },
                    { new Guid("682ffe5f-efe8-4cfd-ba75-6b8b8a2b1c9b"), new Guid("50821fe1-b324-4637-a78e-e73617ef009d"), new Guid("c2659c76-e004-4bb6-906e-09a2c298f406") },
                    { new Guid("68c78804-9381-446d-8801-238a9cd49af1"), new Guid("b1807636-77ba-4418-b7c7-b406aba8f4a5"), new Guid("6a68caa3-5bb5-431e-8186-febe68a587ce") },
                    { new Guid("6b6d6067-3624-4d81-865e-2b98885a4f7d"), new Guid("b1807636-77ba-4418-b7c7-b406aba8f4a5"), new Guid("9aa37ea5-e2b9-4bc8-8117-b717c0b7b731") },
                    { new Guid("6c03edd1-949d-48f4-8930-35829a6da914"), new Guid("50821fe1-b324-4637-a78e-e73617ef009d"), new Guid("91149f27-1f4d-4d24-b1fd-3758208403b6") },
                    { new Guid("7dbd26dc-bb2f-49fe-a751-0ae5a77839a4"), new Guid("aa7845db-b450-47d4-ac47-ad74b9e74330"), new Guid("7c2df28b-269e-43dc-a37b-271b512e766c") },
                    { new Guid("8327f0eb-0292-453f-8616-d027994743de"), new Guid("2a0448ae-b398-4fca-bc86-4fc662092c7f"), new Guid("91149f27-1f4d-4d24-b1fd-3758208403b6") },
                    { new Guid("8f6b8bb5-d227-4f22-a1f6-b424bfb71e13"), new Guid("50821fe1-b324-4637-a78e-e73617ef009d"), new Guid("7c2df28b-269e-43dc-a37b-271b512e766c") },
                    { new Guid("93c4dcbb-62c8-44c3-9a86-d7420df48320"), new Guid("aa7845db-b450-47d4-ac47-ad74b9e74330"), new Guid("9fa2a432-f244-44e1-9a88-cdab005e7d02") },
                    { new Guid("981f7e93-35b9-460c-9af2-dbd70f744ce9"), new Guid("b1807636-77ba-4418-b7c7-b406aba8f4a5"), new Guid("64c1a1fa-1582-40cd-bc10-ca1aa0641b08") },
                    { new Guid("9af2cf25-8a93-472c-901b-e6bea2c65638"), new Guid("4702cd3a-7bcb-404e-b9ed-b063833540cc"), new Guid("64c1a1fa-1582-40cd-bc10-ca1aa0641b08") },
                    { new Guid("a09d97fb-86bd-487a-97f3-e83dfad65dbe"), new Guid("b1807636-77ba-4418-b7c7-b406aba8f4a5"), new Guid("8c392033-749a-4c37-9310-0fb1c27ea7b2") },
                    { new Guid("a515fde9-f26e-45b7-a602-17ccc85b34d2"), new Guid("3abacc9e-6193-4ad0-9c86-63a580397be5"), new Guid("1b597ee6-1b36-493e-b4e4-6795f7613bc6") },
                    { new Guid("a766f033-4c97-43bb-940a-ba0971e0cab8"), new Guid("0132c181-faa0-4dc2-9fcf-5c543b9ec61a"), new Guid("9fa2a432-f244-44e1-9a88-cdab005e7d02") },
                    { new Guid("ad522f7d-df58-4638-a193-10d413e076fb"), new Guid("50821fe1-b324-4637-a78e-e73617ef009d"), new Guid("8c82d2e4-ca04-41b9-84c2-3c48078fbe33") },
                    { new Guid("b884d4e1-9d97-4f34-99b3-0a39366ac1b6"), new Guid("74c0a32a-ea94-43a7-b18c-db5c086232d1"), new Guid("1b597ee6-1b36-493e-b4e4-6795f7613bc6") },
                    { new Guid("bae61358-beee-431c-bacb-b1f48b748c53"), new Guid("50821fe1-b324-4637-a78e-e73617ef009d"), new Guid("87361bc9-2c0f-416c-886a-5096241c21ff") },
                    { new Guid("c7adc98a-14ec-4dfd-9e36-4c25d4478e27"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("c9c75601-c909-4d84-b61b-5768be2a94f5") },
                    { new Guid("c9e09f7f-98cf-4fd5-8855-3a3ae70f52c1"), new Guid("aa7845db-b450-47d4-ac47-ad74b9e74330"), new Guid("c52b5792-3a9e-4dfb-9367-470c33f02c45") },
                    { new Guid("d5092eb3-3d93-43b2-b801-1e494593d341"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("87361bc9-2c0f-416c-886a-5096241c21ff") },
                    { new Guid("daa1028d-9646-4dbc-ae48-eb988ccca796"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("64c1a1fa-1582-40cd-bc10-ca1aa0641b08") },
                    { new Guid("daee7710-b6fc-4b7a-a6bb-b52f8a7d2f32"), new Guid("aa7845db-b450-47d4-ac47-ad74b9e74330"), new Guid("db620628-b7a8-4cb3-b08f-47273a69a82d") },
                    { new Guid("db503dac-cb83-48e1-9dc8-08e7eb9bb647"), new Guid("2a0448ae-b398-4fca-bc86-4fc662092c7f"), new Guid("1b597ee6-1b36-493e-b4e4-6795f7613bc6") },
                    { new Guid("de0c2c8e-2f04-4fc1-9d3e-ad9a4257b5b3"), new Guid("b1807636-77ba-4418-b7c7-b406aba8f4a5"), new Guid("c52b5792-3a9e-4dfb-9367-470c33f02c45") },
                    { new Guid("e6488127-fb83-4c39-a6b8-f6afcf6edcf0"), new Guid("aa7845db-b450-47d4-ac47-ad74b9e74330"), new Guid("2f3abab2-f661-427e-997c-5c53b10fe80f") },
                    { new Guid("e6a05a37-2b90-4d8e-b722-0a3f4efe422f"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("9aa37ea5-e2b9-4bc8-8117-b717c0b7b731") },
                    { new Guid("edeefae3-4fce-4622-917a-dc6df4613cc2"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("e70d6db8-8be3-4bd8-966c-8cc1e23e432b") },
                    { new Guid("f0621773-647e-49d6-babe-129288fd0961"), new Guid("0132c181-faa0-4dc2-9fcf-5c543b9ec61a"), new Guid("8c82d2e4-ca04-41b9-84c2-3c48078fbe33") },
                    { new Guid("f6458bcd-ce06-4de1-8d73-da5e0478cb07"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("8c82d2e4-ca04-41b9-84c2-3c48078fbe33") },
                    { new Guid("f86bcc8f-3a77-4082-bcbe-98700ffcfefd"), new Guid("60d9ba9a-3323-4f2d-8000-99be480cae99"), new Guid("8c392033-749a-4c37-9310-0fb1c27ea7b2") }
                });

            migrationBuilder.InsertData(
                table: "Movies",
                columns: new[] { "Id", "AgeRatingId", "BackdropPicPath", "MovieClickCount", "MovieDescription", "MoviePath", "PosterPath", "ReleaseYear", "Title", "TrailerPath" },
                values: new object[,]
                {
                    { new Guid("1b597ee6-1b36-493e-b4e4-6795f7613bc6"), null, "/backdrops/8af11b4faa4645f986553222ae2af034.jpg", 0, "İki Osmanlı yetkilisi, sultanlarının adına ABD başkanına elmas bir kolye teslim etmek üzere cesurca Vahşi Batı'ya gider. Ama yaşanan kaos yüzünden bu görev raydan çıkar.", null, "/images/bb9434b7c54546488964dd2cc7687067.jpg", 2009, "Yahşi Batı", "/trailers/20a27c60c72e4f58a2cad022214de908.mp4" },
                    { new Guid("2f3abab2-f661-427e-997c-5c53b10fe80f"), null, "/backdrops/b78f2077629f47f39034803f5bf88fe8.jpg", 0, "Nazik ve dost canlısı bir adam, 1960'lı ve 1970'li yılların önemli olaylarını bizzat yaşarken bitmez tükenmez iyimserliğiyle etrafındakilere ilham kaynağı olur.", null, "/images/c7855db8eaab468d8e409fe12ffd105c.jpg", 1994, "Forrest Gump", "/trailers/7e0ced5174454138a8ea0949a939e6de.mp4" },
                    { new Guid("64c1a1fa-1582-40cd-bc10-ca1aa0641b08"), null, "/backdrops/2b0c25864b3142e38ef89e3818ac026b.jpg", 0, "Komutan Logar, halı tüccarı Arif'i zamanda bir milyon yıl geriye gönderir. Burada dinozorlarla karşılaşan Arif, sakalını tıraş etmenin bir yolunu da bulur.", "/movies/22071065bc364d62ab51854b57a38cfd.mp4", "/images/7c176488167c4a389acf5eea903db08b.jpg", 2008, "A.R.O.G", "/trailers/881bc6d2b70047aea0da2bc39f437e85.mp4" },
                    { new Guid("6a68caa3-5bb5-431e-8186-febe68a587ce"), null, "/backdrops/92a1013355f641098dbfb8ada29d378d.jpg", 0, "Sahte UFO fotoğrafçısı ve halı tüccarı olan Arif uzaylılarca kaçırılır. Gora gezegeninde, yaklaşan göktaşından kurtulmak ve özgür kalmak için arkadaşlarına yardım eder.", null, "/images/3ebd25faf255400a94f923db9a020d0c.jpg", 2004, "G.O.R.A", "/trailers/21caaee63a934d40adbbac4c09fbe97d.mp4" },
                    { new Guid("7c2df28b-269e-43dc-a37b-271b512e766c"), null, "/backdrops/29db949ca81e411fb824d75b8e60bca6.jpg", 0, "New York'ta güçlü bir mafya babasının savaş kahramanı olan en küçük oğlu Michael Corleone, babasının suikasta hedef olmasının ardından aile işine girer.", null, "/images/9ad9332cb3e24b1fb78c9fd329893823.jpg", 1972, "Baba", "/trailers/0f643881fa6a41fe9ad967925a5714be.mp4" },
                    { new Guid("87361bc9-2c0f-416c-886a-5096241c21ff"), null, "/backdrops/47f6e637f87f4abc82dcb7953128f3c4.jpg", 0, "Batman, Teğmen Gordon ve Bölge Savcısı Harvey Dent, tüyler ürpertici makyajıyla Gotham şehrine korku saçan suç dehası Joker'e karşı mücadele eder.", null, "/images/5704860398744f049501cf7eaeec896f.jpg", 2008, "Kara Şövalye", "/trailers/50562a93803b4060a3bc60a60ce9b860.mp4" },
                    { new Guid("8c392033-749a-4c37-9310-0fb1c27ea7b2"), null, "/backdrops/839388eaa9a849ed934b00dd8b149b44.jpg", 0, "Matrix'in oluşumundan sorumlu olan makineler Zion şehrinin yerini tespit etmişlerdir. Zion insanların elinde kalan tek şehirdir ve direnmekten başka şansı yoktur.", null, "/images/62a07a9ae25549e2b94d0c15a124f4e5.jpg", 2003, "The Matrix Reloaded", "/trailers/55bceecd564f4aa490bf0c0c04d8c1d6.mp4" },
                    { new Guid("8c82d2e4-ca04-41b9-84c2-3c48078fbe33"), null, "/backdrops/a1199f3fce9d4d8aa3ca9ab9c3699a9e.jpg", 0, "Bir gangsterin oğlu arabasını çalıp köpeğini öldürünce, korkusuz eski tetikçi John Wick intikamını almak için tüm mafyayla karşı karşıya gelir.", null, "/images/59106ee0321e42b98effe3156ffc2782.jpg", 2014, "John Wick", "/trailers/6b5978da46c94ea089dd0bb9fc9f88c4.mp4" },
                    { new Guid("91149f27-1f4d-4d24-b1fd-3758208403b6"), null, "/backdrops/88ee3b025b934e899cb82b81f80ac15b.jpg", 0, "Bir gardiyan, bir idam mahkûmunun gizemli güçlere sahip olduğunu keşfettiğinde, mahkûmun infazını çaresizce engellemeye çalışır.", null, "/images/7ff4da6089f442a19cc98a1ad5967a8f.jpg", 1999, "Yeşil Yol", "/trailers/290e68d54c374deab90c3db04d105bad.mp4" },
                    { new Guid("9aa37ea5-e2b9-4bc8-8117-b717c0b7b731"), null, "/backdrops/7966e82616a04ac4b7aeadc36d8f3820.jpg", 0, "Genetiği değiştirilmiş bir örümcek tarafından ısırılan genç Peter Parker, adaletsizlikle ve intikam peşindeki kötücül bir süper kahramanla savaşmak için gereken güçlere kavuşur.", null, "/images/500b9cd389c6442fb059a9ca24caed02.jpg", 2002, "Örümcek Adam", "/trailers/a041210c03744649bb5f37f114ee09fd.mp4" },
                    { new Guid("9fa2a432-f244-44e1-9a88-cdab005e7d02"), null, "/backdrops/5b4ff97c4c4548679839077853d2b325.jpg", 0, "1981'de Gotham Şehri'nde akli dengesi bozuk olan başarısız komedyen Arthur Fleck, metroda uğradığı saldırının ardından karanlık ve dehşet saçan yeni bir kimliğe bürünür.", null, "/images/7f8da590ec304db28bc0e126ab1e70d0.jpg", 2019, "Joker", "/trailers/68f283c2b3754c3cab734b5c856695e6.mp4" },
                    { new Guid("c2659c76-e004-4bb6-906e-09a2c298f406"), null, "/backdrops/6c8db86487f846a7b16b950e5fda69a4.jpg", 0, "Amerika'da hapis cezasından kurtulmak için babasının yanına Tokyo'ya taşınan bir genç, drift yarışı dünyasında önemli bir yarışçı haline gelir.\r\n", null, "/images/0b10521a0b334e3d840a60a7ad2fc883.jpg", 2006, "Hızlı ve Öfkeli 3: Tokyo Yarışı", "/trailers/3f8665926ceb4225beda2712b32fbc3d.mp4" },
                    { new Guid("c52b5792-3a9e-4dfb-9367-470c33f02c45"), null, "/backdrops/19534cca191d4d61bc9e8ac0f21b75f9.jpg", 0, "İnsanlık yok olmanın eşiğindeyken bir grup astronot, yaşanabilir başka bir gezegen bulmak için bir solucan deliğinden geçerek tehlikeli bir yolculuğa çıkar.", null, "/images/76d1d5e9f01f4ba39c5d83b8de85ab46.jpg", 2014, "Interstellar", "/trailers/c8eaf842cbb64d33893fc6f479ff0bf7.mp4" },
                    { new Guid("c9c75601-c909-4d84-b61b-5768be2a94f5"), null, "/backdrops/7866355f27744b22bf64c7907c6151e3.jpg", 0, "Matrix nedir sorusunun yanıtını arayan Neo adlı bilgisayar korsanı, beyaz tavşanın peşine takılır ve yaşadığı dünyayla ilgili akıllara durgunluk veren gerçeği öğrenir.", null, "/images/8f1f529d982a47cba5ab51b8eadf0056.jpg", 1999, "Matrix", "/trailers/88d141076c8f42678a2675f6f61b32a1.mp4" },
                    { new Guid("db620628-b7a8-4cb3-b08f-47273a69a82d"), null, "/backdrops/45b52220f22242548eb1001f6ef6632d.jpg", 0, "Eşini ve onun âşığını öldürmekten hüküm giyen kendi hâlinde bir bankacı, hapishanede Red adında bir mahkûmla arkadaş olur ve umuda tutunarak hayatta kalmaya çalışır. ", null, "/images/43cced96ddbe4d12a31e159f25a219e2.jpg", 1994, "Esaretin Bedeli", "/trailers/7845e3bd601d4f8782f44799aaab37db.mp4" },
                    { new Guid("e70d6db8-8be3-4bd8-966c-8cc1e23e432b"), null, "/backdrops/da120d883e6c4c168eee41708732c841.jpg", 0, "Matrix üçlemesinin bu son bölümünde Neo bir metro istasyonunda hapsolmuş olarak gözlerini açar. Matrix ile makinelerin dünyası arasında bir alanda sıkışıp kalmıştır.", null, "/images/1bfb6ae7ecdd4e9f8a8ccdbab79c4ad0.jpg", 2003, "The Matrix Revolutions", "/trailers/3ae5367133a843b2baf4d816e4c0b6f5.mp4" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_AgeRatingId",
                table: "Movies",
                column: "AgeRatingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "MovieGenres");

            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.DropTable(
                name: "AgeRating");
        }
    }
}
