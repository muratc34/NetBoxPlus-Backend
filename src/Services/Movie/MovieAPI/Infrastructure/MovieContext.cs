using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MovieAPI.Infrastructure.EntityConfigurations;
using MovieAPI.Model;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace MovieAPI.Infrastructure
{
    public class MovieContext : DbContext
    {

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(@"Server=localhost;Port=5432;Database=MovieDb;Search Path=public;User Id=postgres;Password=12345");
        }

        public DbSet<Movie> Movies { get; set; }
        public DbSet<Genre> Genres { get; set; }
        public DbSet<MovieGenre> MovieGenres { get; set; }
        public DbSet<AgeRating> AgeRatings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MovieEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new GenreEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new AgeRatingEntityTypeConfiguration());

            //#region Define Ids
            //Guid genre1 = Guid.NewGuid();
            //Guid genre2 = Guid.NewGuid();
            //Guid genre3 = Guid.NewGuid();
            //Guid genre4 = Guid.NewGuid();
            //Guid genre5 = Guid.NewGuid();
            //Guid genre6 = Guid.NewGuid();
            //Guid genre7 = Guid.NewGuid();
            //Guid genre8 = Guid.NewGuid();
            //Guid genre9 = Guid.NewGuid();
            //Guid genre10 = Guid.NewGuid();
            //Guid genre11 = Guid.NewGuid();
            //Guid genre12 = Guid.NewGuid();
            //Guid genre13 = Guid.NewGuid();
            //Guid genre14 = Guid.NewGuid();
            //Guid genre15 = Guid.NewGuid();
            //Guid genre16 = Guid.NewGuid();
            //Guid genre17 = Guid.NewGuid();
            //Guid genre18 = Guid.NewGuid();

            //Guid movie1 = Guid.NewGuid();
            //Guid movie2 = Guid.NewGuid();
            //Guid movie3 = Guid.NewGuid();
            //Guid movie4 = Guid.NewGuid();
            //Guid movie5 = Guid.NewGuid();
            //Guid movie6 = Guid.NewGuid();
            //Guid movie7 = Guid.NewGuid();
            //Guid movie8 = Guid.NewGuid();
            //Guid movie9 = Guid.NewGuid();
            //Guid movie10 = Guid.NewGuid();
            //Guid movie11 = Guid.NewGuid();
            //Guid movie12 = Guid.NewGuid();
            //Guid movie13 = Guid.NewGuid();
            //Guid movie14 = Guid.NewGuid();
            //Guid movie15 = Guid.NewGuid();
            //Guid movie16 = Guid.NewGuid();

            //Guid movieGenre1 = Guid.NewGuid();
            //Guid movieGenre2 = Guid.NewGuid();
            //Guid movieGenre3 = Guid.NewGuid();
            //Guid movieGenre4 = Guid.NewGuid();
            //Guid movieGenre5 = Guid.NewGuid();
            //Guid movieGenre6 = Guid.NewGuid();
            //Guid movieGenre7 = Guid.NewGuid();
            //Guid movieGenre8 = Guid.NewGuid();
            //Guid movieGenre9 = Guid.NewGuid();
            //Guid movieGenre10 = Guid.NewGuid();
            //Guid movieGenre11 = Guid.NewGuid();
            //Guid movieGenre12 = Guid.NewGuid();
            //Guid movieGenre13 = Guid.NewGuid();
            //Guid movieGenre14 = Guid.NewGuid();
            //Guid movieGenre15 = Guid.NewGuid();
            //Guid movieGenre16 = Guid.NewGuid();
            //Guid movieGenre17 = Guid.NewGuid();
            //Guid movieGenre18 = Guid.NewGuid();
            //Guid movieGenre19 = Guid.NewGuid();
            //Guid movieGenre20 = Guid.NewGuid();
            //Guid movieGenre21 = Guid.NewGuid();
            //Guid movieGenre22 = Guid.NewGuid();
            //Guid movieGenre23 = Guid.NewGuid();
            //Guid movieGenre24 = Guid.NewGuid();
            //Guid movieGenre25 = Guid.NewGuid();
            //Guid movieGenre26 = Guid.NewGuid();
            //Guid movieGenre27 = Guid.NewGuid();
            //Guid movieGenre28 = Guid.NewGuid();
            //Guid movieGenre29 = Guid.NewGuid();
            //Guid movieGenre30 = Guid.NewGuid();
            //Guid movieGenre31 = Guid.NewGuid();
            //Guid movieGenre32 = Guid.NewGuid();
            //Guid movieGenre33 = Guid.NewGuid();
            //Guid movieGenre34 = Guid.NewGuid();
            //Guid movieGenre35 = Guid.NewGuid();
            //Guid movieGenre36 = Guid.NewGuid();
            //Guid movieGenre37 = Guid.NewGuid();
            //Guid movieGenre38 = Guid.NewGuid();
            //Guid movieGenre39 = Guid.NewGuid();
            //Guid movieGenre40 = Guid.NewGuid();
            //Guid movieGenre41 = Guid.NewGuid();
            //Guid movieGenre42 = Guid.NewGuid();
            //Guid movieGenre43 = Guid.NewGuid();

            ////Guid age1 = Guid.NewGuid();
            ////Guid age2 = Guid.NewGuid();
            ////Guid age3 = Guid.NewGuid();
            ////Guid age4 = Guid.NewGuid();
            ////Guid age5 = Guid.NewGuid();
            ////Guid age6 = Guid.NewGuid();
            //#endregion

            //#region Genre Data
            //List<Genre> genreSeed = new List<Genre>()
            //{
            //    new() { Id = genre1, GenreTitle = "Aksiyon" , GenreCode = 1},
            //    new() { Id = genre2, GenreTitle = "Macera" , GenreCode = 2},
            //    new() { Id = genre3, GenreTitle = "Animasyon", GenreCode = 3},
            //    new() { Id = genre4, GenreTitle = "Komedi", GenreCode = 4},
            //    new() { Id = genre5, GenreTitle = "Suç", GenreCode = 5},
            //    new() { Id = genre6, GenreTitle = "Belgesel", GenreCode = 6},
            //    new() { Id = genre7, GenreTitle = "Dram", GenreCode = 7},
            //    new() { Id = genre8, GenreTitle = "Aile", GenreCode = 8},
            //    new() { Id = genre9, GenreTitle = "Fantastik", GenreCode = 9},
            //    new() { Id = genre10, GenreTitle = "Tarih", GenreCode = 10},
            //    new() { Id = genre11, GenreTitle = "Korku", GenreCode = 11},
            //    new() { Id = genre12, GenreTitle = "Müzikal", GenreCode = 12},
            //    new() { Id = genre13, GenreTitle = "Gizem", GenreCode = 13},
            //    new() { Id = genre14, GenreTitle = "Romantik", GenreCode = 14},
            //    new() { Id = genre15, GenreTitle = "Bilim Kurgu", GenreCode = 15},
            //    new() { Id = genre16, GenreTitle = "Gerilim", GenreCode = 16},
            //    new() { Id = genre17, GenreTitle = "Savaş", GenreCode = 17},
            //    new() { Id = genre18, GenreTitle = "Batılı", GenreCode = 18},
            //};
            //#endregion

            //#region AgeRatingData
            ////List<AgeRating> ageRatingSeed = new List<AgeRating>()
            ////{
            ////    new() { Id = age1, Rating = "7+", RatingCode= 1, },
            ////    new() { Id = age2, Rating = "10+", RatingCode= 2, },
            ////    new() { Id = age3, Rating = "13+", RatingCode= 3, },
            ////    new() { Id = age4, Rating = "16+", RatingCode= 4, },
            ////    new() { Id = age5, Rating = "18+", RatingCode= 5, },
            ////    new() { Id = age6, Rating = "Genel İzleyici", RatingCode= 6, }
            ////};
            //#endregion

            //#region MovieGenre Data
            //List<MovieGenre> movieGenreSeed = new List<MovieGenre>()
            //{
            //    new(){ Id = movieGenre1, MovieId = movie1, GenreId = genre1 },
            //    new(){ Id = movieGenre2, MovieId = movie1, GenreId = genre5 },
            //    new(){ Id = movieGenre3, MovieId = movie1, GenreId = genre7 },
            //    new(){ Id = movieGenre4, MovieId = movie2, GenreId = genre1 },
            //    new(){ Id = movieGenre5, MovieId = movie2, GenreId = genre5 },
            //    new(){ Id = movieGenre6, MovieId = movie2, GenreId = genre16 },
            //    new(){ Id = movieGenre7, MovieId = movie3, GenreId = genre1 },
            //    new(){ Id = movieGenre8, MovieId = movie3, GenreId = genre15 },
            //    new(){ Id = movieGenre9, MovieId = movie4, GenreId = genre5},
            //    new(){ Id = movieGenre10, MovieId = movie4, GenreId = genre7},
            //    new(){ Id = movieGenre11, MovieId = movie4, GenreId = genre9},
            //    new(){ Id = movieGenre12, MovieId= movie5, GenreId= genre1},
            //    new(){ Id = movieGenre13, MovieId= movie5, GenreId= genre2},
            //    new(){ Id = movieGenre14, MovieId= movie5, GenreId= genre15},
            //    new(){ Id = movieGenre15, MovieId= movie6, GenreId= genre2},
            //    new(){ Id = movieGenre16, MovieId= movie6, GenreId= genre7},
            //    new(){ Id = movieGenre17, MovieId= movie6, GenreId= genre15},
            //    new(){ Id = movieGenre18, MovieId= movie7, GenreId= genre5},
            //    new(){ Id = movieGenre19, MovieId= movie7, GenreId= genre7},
            //    new(){ Id = movieGenre20, MovieId= movie8, GenreId= genre1},
            //    new(){ Id = movieGenre21, MovieId= movie8, GenreId= genre15},
            //    new(){ Id = movieGenre22, MovieId= movie9, GenreId= genre1},
            //    new(){ Id = movieGenre23, MovieId= movie9, GenreId= genre2},
            //    new(){ Id = movieGenre24, MovieId= movie9, GenreId= genre15},
            //    new(){ Id = movieGenre25, MovieId= movie10, GenreId= genre7},
            //    new(){ Id = movieGenre26, MovieId= movie11, GenreId= genre1},
            //    new(){ Id = movieGenre27, MovieId= movie11, GenreId= genre7},
            //    new(){ Id = movieGenre28, MovieId= movie11, GenreId= genre14},
            //    new(){ Id = movieGenre29, MovieId= movie12, GenreId= genre1},
            //    new(){ Id = movieGenre30, MovieId= movie12, GenreId= genre15},
            //    new(){ Id = movieGenre31, MovieId= movie13, GenreId= genre1},
            //    new(){ Id = movieGenre32, MovieId= movie13, GenreId= genre5},
            //    new(){ Id = movieGenre33, MovieId= movie13, GenreId= genre16},
            //    new(){ Id = movieGenre34, MovieId= movie14, GenreId= genre2},
            //    new(){ Id = movieGenre35, MovieId= movie14, GenreId= genre4},
            //    new(){ Id = movieGenre36, MovieId= movie14, GenreId= genre9},
            //    new(){ Id = movieGenre37, MovieId= movie14, GenreId= genre18},
            //    new(){ Id = movieGenre38, MovieId= movie15, GenreId= genre5},
            //    new(){ Id = movieGenre39, MovieId= movie15, GenreId= genre7},
            //    new(){ Id = movieGenre40, MovieId= movie15, GenreId= genre16},
            //    new(){ Id = movieGenre41, MovieId= movie16, GenreId= genre1},
            //    new(){ Id = movieGenre42, MovieId= movie16, GenreId= genre2},
            //    new(){ Id = movieGenre43, MovieId= movie16, GenreId= genre15}

            //};
            //#endregion

            //#region Movie Data
            //List<Movie> movieSeed = new List<Movie>()
            //{
            //    new() {
            //        Id = movie1,
            //        MovieDescription = "Batman, Teğmen Gordon ve Bölge Savcısı Harvey Dent, tüyler ürpertici makyajıyla Gotham şehrine korku saçan suç dehası Joker'e karşı mücadele eder.",
            //        Title = "Kara Şövalye",
            //        PosterPath = "/images/5704860398744f049501cf7eaeec896f.jpg",
            //        BackdropPicPath = "/backdrops/47f6e637f87f4abc82dcb7953128f3c4.jpg",
            //        TrailerPath = "/trailers/50562a93803b4060a3bc60a60ce9b860.mp4",
            //        ReleaseYear = 2008,
            //        MovieClickCount = 0
            //    },
            //    new() {
            //        Id = movie2,
            //        MovieDescription = "Bir gangsterin oğlu arabasını çalıp köpeğini öldürünce, korkusuz eski tetikçi John Wick intikamını almak için tüm mafyayla karşı karşıya gelir.",
            //        Title = "John Wick",
            //        PosterPath = "/images/59106ee0321e42b98effe3156ffc2782.jpg",
            //        BackdropPicPath = "/backdrops/a1199f3fce9d4d8aa3ca9ab9c3699a9e.jpg",
            //        TrailerPath = "/trailers/6b5978da46c94ea089dd0bb9fc9f88c4.mp4",
            //        ReleaseYear = 2014,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie3,
            //        MovieDescription = "Matrix üçlemesinin bu son bölümünde Neo bir metro istasyonunda hapsolmuş olarak gözlerini açar. Matrix ile makinelerin dünyası arasında bir alanda sıkışıp kalmıştır.",
            //        Title = "The Matrix Revolutions",
            //        PosterPath = "/images/1bfb6ae7ecdd4e9f8a8ccdbab79c4ad0.jpg",
            //        BackdropPicPath = "/backdrops/da120d883e6c4c168eee41708732c841.jpg",
            //        TrailerPath = "/trailers/3ae5367133a843b2baf4d816e4c0b6f5.mp4",
            //        ReleaseYear = 2003,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie4,
            //        MovieDescription = "Bir gardiyan, bir idam mahkûmunun gizemli güçlere sahip olduğunu keşfettiğinde, mahkûmun infazını çaresizce engellemeye çalışır.",
            //        Title = "Yeşil Yol",
            //        PosterPath = "/images/7ff4da6089f442a19cc98a1ad5967a8f.jpg",
            //        BackdropPicPath = "/backdrops/88ee3b025b934e899cb82b81f80ac15b.jpg",
            //        TrailerPath = "/trailers/290e68d54c374deab90c3db04d105bad.mp4",
            //        ReleaseYear = 1999,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie5,
            //        MovieDescription = "Genetiği değiştirilmiş bir örümcek tarafından ısırılan genç Peter Parker, adaletsizlikle ve intikam peşindeki kötücül bir süper kahramanla savaşmak için gereken güçlere kavuşur.",
            //        Title = "Örümcek Adam",
            //        PosterPath = "/images/500b9cd389c6442fb059a9ca24caed02.jpg",
            //        BackdropPicPath = "/backdrops/7966e82616a04ac4b7aeadc36d8f3820.jpg",
            //        TrailerPath = "/trailers/a041210c03744649bb5f37f114ee09fd.mp4",
            //        ReleaseYear = 2002,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie6,
            //        MovieDescription = "İnsanlık yok olmanın eşiğindeyken bir grup astronot, yaşanabilir başka bir gezegen bulmak için bir solucan deliğinden geçerek tehlikeli bir yolculuğa çıkar.",
            //        Title = "Interstellar",
            //        PosterPath = "/images/76d1d5e9f01f4ba39c5d83b8de85ab46.jpg",
            //        BackdropPicPath = "/backdrops/19534cca191d4d61bc9e8ac0f21b75f9.jpg",
            //        TrailerPath = "/trailers/c8eaf842cbb64d33893fc6f479ff0bf7.mp4",
            //        ReleaseYear = 2014,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie7,
            //        MovieDescription = "New York'ta güçlü bir mafya babasının savaş kahramanı olan en küçük oğlu Michael Corleone, babasının suikasta hedef olmasının ardından aile işine girer.",
            //        Title = "Baba",
            //        PosterPath = "/images/9ad9332cb3e24b1fb78c9fd329893823.jpg",
            //        BackdropPicPath = "/backdrops/29db949ca81e411fb824d75b8e60bca6.jpg",
            //        TrailerPath = "/trailers/0f643881fa6a41fe9ad967925a5714be.mp4",
            //        ReleaseYear = 1972,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie8,
            //        MovieDescription = "Matrix nedir sorusunun yanıtını arayan Neo adlı bilgisayar korsanı, beyaz tavşanın peşine takılır ve yaşadığı dünyayla ilgili akıllara durgunluk veren gerçeği öğrenir.",
            //        Title = "Matrix",
            //        PosterPath = "/images/8f1f529d982a47cba5ab51b8eadf0056.jpg",
            //        BackdropPicPath = "/backdrops/7866355f27744b22bf64c7907c6151e3.jpg",
            //        TrailerPath = "/trailers/88d141076c8f42678a2675f6f61b32a1.mp4",
            //        ReleaseYear = 1999,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie9,
            //        MovieDescription = "Komutan Logar, halı tüccarı Arif'i zamanda bir milyon yıl geriye gönderir. Burada dinozorlarla karşılaşan Arif, sakalını tıraş etmenin bir yolunu da bulur.",
            //        Title = "A.R.O.G",
            //        PosterPath = "/images/7c176488167c4a389acf5eea903db08b.jpg",
            //        BackdropPicPath = "/backdrops/2b0c25864b3142e38ef89e3818ac026b.jpg",
            //        MoviePath = "/movies/22071065bc364d62ab51854b57a38cfd.mp4",
            //        TrailerPath = "/trailers/881bc6d2b70047aea0da2bc39f437e85.mp4",
            //        ReleaseYear = 2008,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie10,
            //        MovieDescription = "Eşini ve onun âşığını öldürmekten hüküm giyen kendi hâlinde bir bankacı, hapishanede Red adında bir mahkûmla arkadaş olur ve umuda tutunarak hayatta kalmaya çalışır. ",
            //        Title = "Esaretin Bedeli",
            //        PosterPath = "/images/43cced96ddbe4d12a31e159f25a219e2.jpg",
            //        BackdropPicPath = "/backdrops/45b52220f22242548eb1001f6ef6632d.jpg",
            //        TrailerPath = "/trailers/7845e3bd601d4f8782f44799aaab37db.mp4",
            //        ReleaseYear = 1994,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie11,
            //        MovieDescription = "Nazik ve dost canlısı bir adam, 1960'lı ve 1970'li yılların önemli olaylarını bizzat yaşarken bitmez tükenmez iyimserliğiyle etrafındakilere ilham kaynağı olur.",
            //        Title = "Forrest Gump",
            //        PosterPath = "/images/c7855db8eaab468d8e409fe12ffd105c.jpg",
            //        BackdropPicPath = "/backdrops/b78f2077629f47f39034803f5bf88fe8.jpg",
            //        TrailerPath = "/trailers/7e0ced5174454138a8ea0949a939e6de.mp4",
            //        ReleaseYear = 1994,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie12,
            //        MovieDescription = "Matrix'in oluşumundan sorumlu olan makineler Zion şehrinin yerini tespit etmişlerdir. Zion insanların elinde kalan tek şehirdir ve direnmekten başka şansı yoktur.",
            //        Title = "The Matrix Reloaded",
            //        PosterPath = "/images/62a07a9ae25549e2b94d0c15a124f4e5.jpg",
            //        BackdropPicPath = "/backdrops/839388eaa9a849ed934b00dd8b149b44.jpg",
            //        TrailerPath = "/trailers/55bceecd564f4aa490bf0c0c04d8c1d6.mp4",
            //        ReleaseYear = 2003,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie13,
            //        MovieDescription = "Amerika'da hapis cezasından kurtulmak için babasının yanına Tokyo'ya taşınan bir genç, drift yarışı dünyasında önemli bir yarışçı haline gelir.\r\n",
            //        Title = "Hızlı ve Öfkeli 3: Tokyo Yarışı",
            //        PosterPath = "/images/0b10521a0b334e3d840a60a7ad2fc883.jpg",
            //        BackdropPicPath = "/backdrops/6c8db86487f846a7b16b950e5fda69a4.jpg",
            //        TrailerPath = "/trailers/3f8665926ceb4225beda2712b32fbc3d.mp4",
            //        ReleaseYear = 2006,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie14,
            //        MovieDescription = "İki Osmanlı yetkilisi, sultanlarının adına ABD başkanına elmas bir kolye teslim etmek üzere cesurca Vahşi Batı'ya gider. Ama yaşanan kaos yüzünden bu görev raydan çıkar.",
            //        Title = "Yahşi Batı",
            //        PosterPath = "/images/bb9434b7c54546488964dd2cc7687067.jpg",
            //        BackdropPicPath = "/backdrops/8af11b4faa4645f986553222ae2af034.jpg",
            //        TrailerPath = "/trailers/20a27c60c72e4f58a2cad022214de908.mp4",
            //        ReleaseYear = 2009,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie15,
            //        MovieDescription = "1981'de Gotham Şehri'nde akli dengesi bozuk olan başarısız komedyen Arthur Fleck, metroda uğradığı saldırının ardından karanlık ve dehşet saçan yeni bir kimliğe bürünür.",
            //        Title = "Joker",
            //        PosterPath = "/images/7f8da590ec304db28bc0e126ab1e70d0.jpg",
            //        BackdropPicPath = "/backdrops/5b4ff97c4c4548679839077853d2b325.jpg",
            //        TrailerPath = "/trailers/68f283c2b3754c3cab734b5c856695e6.mp4",
            //        ReleaseYear = 2019,
            //        MovieClickCount = 0,
            //    },
            //    new() {
            //        Id = movie16,
            //        MovieDescription = "Sahte UFO fotoğrafçısı ve halı tüccarı olan Arif uzaylılarca kaçırılır. Gora gezegeninde, yaklaşan göktaşından kurtulmak ve özgür kalmak için arkadaşlarına yardım eder.",
            //        Title = "G.O.R.A",
            //        PosterPath = "/images/3ebd25faf255400a94f923db9a020d0c.jpg",
            //        BackdropPicPath = "/backdrops/92a1013355f641098dbfb8ada29d378d.jpg",
            //        TrailerPath = "/trailers/21caaee63a934d40adbbac4c09fbe97d.mp4",
            //        ReleaseYear = 2004,
            //        MovieClickCount = 0,
            //    }

            //};
            //#endregion

            //modelBuilder.Entity<AgeRating>().HasData(ageRatingSeed);
            //modelBuilder.Entity<Movie>().HasData(movieSeed);
            //modelBuilder.Entity<Genre>().HasData(genreSeed);
            //modelBuilder.Entity<MovieGenre>().HasData(movieGenreSeed);
            
        }
    }
}
