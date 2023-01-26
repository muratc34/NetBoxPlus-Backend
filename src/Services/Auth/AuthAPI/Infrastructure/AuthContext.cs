using AuthAPI.Infrastructure.EntityConfiguration;
using AuthAPI.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AuthAPI.Infrastructure
{
    public class AuthContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(@"Server=localhost;Port=5432;Database=AuthDb;Search Path=public;User Id=postgres;Password=12345");
        }

        public DbSet<User> Users { get; set; }
        public DbSet<OperationClaim> OperationClaims { get; set; }
        public DbSet<UserOperationClaim> UserOperationClaims { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new UserEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new OperationClaimEntityTypeConfiguration());

            Guid userId1 = Guid.NewGuid();
            Guid userId2 = Guid.NewGuid();

            Guid claimId1 = Guid.NewGuid();
            Guid claimId2 = Guid.NewGuid();

            Guid userOperationClaimId1 = Guid.NewGuid();
            Guid userOperationClaimId2 = Guid.NewGuid();

            var hmac = new System.Security.Cryptography.HMACSHA512();

            List<User> userSeed = new List<User>()
            {
                new() { Id= userId1, FirstName="Admin", LastName = "Admin", Email = "admin@admin.com", PasswordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes("12345")), PasswordSalt = hmac.Key },
                new() { Id= userId2, FirstName="Murat", LastName = "Cinek", Email = "murat@murat.com", PasswordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes("test123")), PasswordSalt = hmac.Key  }

            };

            List<OperationClaim> operationClaimSeed = new List<OperationClaim>()
            {
                new() { Id = claimId1, Name = "Admin"},
                new() { Id = claimId2, Name = "Kullanıcı"}
            };

            List<UserOperationClaim> userOperationClaimSeed = new List<UserOperationClaim>()
            {
                new() { Id = userOperationClaimId1, UserId = userId1, OperationClaimId = claimId1 },
                new() { Id = userOperationClaimId2, UserId = userId2, OperationClaimId = claimId2 }
            };

            modelBuilder.Entity<User>().HasData(userSeed);
            modelBuilder.Entity<OperationClaim>().HasData(operationClaimSeed);
            modelBuilder.Entity<UserOperationClaim>().HasData(userOperationClaimSeed);
        }
    }
}
