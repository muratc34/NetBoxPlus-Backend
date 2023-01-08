using Microsoft.EntityFrameworkCore;
using MovieAPI.Infrastructure.EntityConfigurations;
using MovieAPI.Model;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace MovieAPI.Infrastructure
{
    public class MovieContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(@"Server=(localdb)\MSSQLLocalDB;Database=MovieDb;Trusted_Connection=true");
        }

        public DbSet<Movie> Movies { get; set; }
        public DbSet<Genre> Genres { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MovieEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new GenreEntityTypeConfiguration());

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

            List<Genre> genreSeed = new List<Genre>()
            {
                new() { Id = genre1, GenreTitle = "Aksiyon" , GenreCode = 1 ,  },
                new() { Id = genre2, GenreTitle = "Macera", GenreCode = 2},
                new() { Id = genre3, GenreTitle = "Animasyon", GenreCode = 3},
                new() { Id = genre4, GenreTitle = "Komedi", GenreCode = 4},
                new() { Id = genre5, GenreTitle = "Suç" , GenreCode = 5},
                new() { Id = genre6, GenreTitle = "Belgesel", GenreCode = 6 },
                new() { Id = genre7, GenreTitle = "Dram", GenreCode = 7 },
                new() { Id = genre8, GenreTitle = "Aile", GenreCode = 8 },
                new() { Id = genre9, GenreTitle = "Fantastik", GenreCode = 9 },
                new() { Id = genre10, GenreTitle = "Tarih", GenreCode = 10 },
                new() { Id = genre11, GenreTitle = "Korku", GenreCode = 11 },
                new() { Id = genre12, GenreTitle = "Müzikal", GenreCode = 12 },
                new() { Id = genre13, GenreTitle = "Gizem", GenreCode = 13 },
                new() { Id = genre14, GenreTitle = "Romantik", GenreCode = 14 },
                new() { Id = genre15, GenreTitle = "Bilim Kurgu", GenreCode = 15 },
                new() { Id = genre16, GenreTitle = "Gerilim", GenreCode = 16 },
                new() { Id = genre17, GenreTitle = "Savaş", GenreCode = 17 },
                new() { Id = genre18, GenreTitle = "Batılı", GenreCode = 18 },
            };

            modelBuilder.Entity<Genre>().HasData(genreSeed);
        }
    }
}
