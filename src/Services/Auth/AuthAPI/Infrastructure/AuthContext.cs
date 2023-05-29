using AuthAPI.Infrastructure.EntityConfigurations;
using AuthAPI.Model;
using AuthAPI.Model.Contracts;
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
        public DbSet<UserOperationClaim> OperationClaimsUser { get; set; }
        public DbSet<Profile> Profiles { get; set; }

        //Contracts
        public DbSet<CreditCard> CreditCards { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new UserEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new OperationClaimEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new ProfileEntityTypeConfiguration());

            Guid userId1 = Guid.NewGuid();
            Guid userId2 = Guid.NewGuid();

            Guid claimId1 = Guid.NewGuid();
            Guid claimId2 = Guid.NewGuid();

            Guid userOperationClaimId1 = Guid.NewGuid();
            Guid userOperationClaimId2 = Guid.NewGuid();

            Guid pId1 = Guid.NewGuid();
            Guid pId2 = Guid.NewGuid();

            Guid profId1 = Guid.NewGuid();
            Guid profId2 = Guid.NewGuid();

            var hmac = new System.Security.Cryptography.HMACSHA512();

            List<User> userSeed = new List<User>()
            {
                new() { Id= userId1, PaymentId = pId1, FirstName="Admin", LastName = "Admin", Email = "admin@admin.com", PasswordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes("12345")), PasswordSalt = hmac.Key },
                new() { Id= userId2, PaymentId = pId2, FirstName="Murat", LastName = "Cinek", Email = "murat@murat.com", PasswordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes("test123")), PasswordSalt = hmac.Key  }

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

            List<Profile> profileSeed = new List<Profile>()
            {
                new () { Id = profId1, UserId=userId2, ProfileName="Default", PinHash = hmac.ComputeHash(Encoding.UTF8.GetBytes("1234")), PinSalt= hmac.Key  },
                new () { Id = profId2, UserId=userId2, ProfileName="Test", PinHash= hmac.ComputeHash(Encoding.UTF8.GetBytes("4321")), PinSalt= hmac.Key  }
            };

            modelBuilder.Entity<User>().HasData(userSeed);
            modelBuilder.Entity<OperationClaim>().HasData(operationClaimSeed);
            modelBuilder.Entity<UserOperationClaim>().HasData(userOperationClaimSeed);

            modelBuilder.Entity<Profile>().HasData(profileSeed);
        }
    }
}
