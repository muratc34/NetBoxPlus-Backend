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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MovieEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new GenreEntityTypeConfiguration());

            #region Genre and Movie Ids
            Guid genre1 = Guid.NewGuid();
            Guid genre2 = Guid.NewGuid();
            Guid genre3 = Guid.NewGuid();
            Guid genre4 = Guid.NewGuid();
            Guid genre5 = Guid.NewGuid();
            Guid genre6 = Guid.NewGuid();
            Guid genre7 = Guid.NewGuid();
            Guid genre8 = Guid.NewGuid();
            Guid genre9 = Guid.NewGuid();
            Guid genre10 = Guid.NewGuid();
            Guid genre11 = Guid.NewGuid();
            Guid genre12 = Guid.NewGuid();
            Guid genre13 = Guid.NewGuid();
            Guid genre14 = Guid.NewGuid();
            Guid genre15 = Guid.NewGuid();
            Guid genre16 = Guid.NewGuid();
            Guid genre17 = Guid.NewGuid();
            Guid genre18 = Guid.NewGuid();

            Guid movie1 = Guid.NewGuid();
            Guid movie2 = Guid.NewGuid();
            Guid movie3 = Guid.NewGuid();
            #endregion

            #region Genre Data
            List<Genre> genreSeed = new List<Genre>()
            {
                new() { Id = genre1, GenreTitle = "Aksiyon" , GenreCode = 1, MovieId = movie1},
                new() { Id = genre2, GenreTitle = "Aksiyon" , GenreCode = 1, MovieId = movie2},
                new() { Id = genre3, GenreTitle = "Macera", GenreCode = 2, MovieId = movie1},
                new() { Id = genre4, GenreTitle = "Macera", GenreCode = 2, MovieId = movie2},
                new() { Id = genre5, GenreTitle = "Macera", GenreCode = 2, MovieId = movie3},
                new() { Id = genre6, GenreTitle = "Komedi", GenreCode = 4, MovieId = movie3},
                new() { Id = genre7, GenreTitle = "Fantastik", GenreCode = 9, MovieId = movie3 },
                new() { Id = genre8, GenreTitle = "Bilim Kurgu", GenreCode = 15, MovieId = movie1 },
                new() { Id = genre9, GenreTitle = "Bilim Kurgu", GenreCode = 15, MovieId = movie2 },
                new() { Id = genre10, GenreTitle = "Batılı", GenreCode = 18, MovieId = movie3 }
            };
            #endregion

            #region Movie Data
            List<Movie> movieSeed = new List<Movie>()
            {
                new() {
                    Id = movie1,
                    MovieDescription = "Komutan Logar, halı tüccarı Arif'i zamanda bir milyon yıl geriye gönderir. Burada dinozorlarla karşılaşan Arif, sakalını tıraş etmenin bir yolunu da bulur.",
                    Title = "A.R.O.G",
                    PosterPath = "/images/7c176488167c4a389acf5eea903db08b.jpg",
                    BackdropPicPath = "/backdrops/2b0c25864b3142e38ef89e3818ac026b.jpg",
                    TrailerPath = "/trailers/881bc6d2b70047aea0da2bc39f437e85.mp4",
                    ReleaseYear = 2008,
                    MpaaRating = MpaaRatingType.P13,
                    MovieClickCount = 0,
                },
                new() {
                    Id = movie2,
                    MovieDescription = "Sahte UFO fotoğrafçısı ve halı tüccarı olan Arif uzaylılarca kaçırılır. Gora gezegeninde, yaklaşan göktaşından kurtulmak ve özgür kalmak için arkadaşlarına yardım eder.",
                    Title = "G.O.R.A",
                    PosterPath = "/images/3ebd25faf255400a94f923db9a020d0c.jpg",
                    BackdropPicPath = "/backdrops/92a1013355f641098dbfb8ada29d378d.jpg",
                    TrailerPath = "/trailers/21caaee63a934d40adbbac4c09fbe97d.mp4",
                    ReleaseYear = 2004,
                    MpaaRating = MpaaRatingType.P18,
                    MovieClickCount = 0,
                },
                new() {
                    Id = movie3,
                    MovieDescription = "İki Osmanlı yetkilisi, sultanlarının adına ABD başkanına elmas bir kolye teslim etmek üzere cesurca Vahşi Batı'ya gider. Ama yaşanan kaos yüzünden bu görev raydan çıkar.",
                    Title = "Yahşi Batı",
                    PosterPath = "/images/bb9434b7c54546488964dd2cc7687067.jpg",
                    BackdropPicPath = "/backdrops/8af11b4faa4645f986553222ae2af034.jpg",
                    TrailerPath = "/trailers/20a27c60c72e4f58a2cad022214de908.mp4",
                    ReleaseYear = 2009,
                    MpaaRating = MpaaRatingType.P18,
                    MovieClickCount = 0,
                }

            };
            #endregion

            modelBuilder.Entity<Movie>().HasData(movieSeed);
            modelBuilder.Entity<Genre>().HasData(genreSeed);
        }
    }
}
