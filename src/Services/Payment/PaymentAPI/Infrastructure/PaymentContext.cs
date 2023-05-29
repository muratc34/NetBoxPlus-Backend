using Microsoft.EntityFrameworkCore;
using PaymentAPI.Infrastructure.EntityConfigurations;
using PaymentAPI.Model;

namespace PaymentAPI.Infrastructure
{
    public class PaymentContext: DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(@"Server=localhost;Port=5432;Database=PaymentDb;Search Path=public;User Id=postgres;Password=12345");
        }

        public DbSet<CreditCard> CreditCards { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CreditCardEntityTypeConfiguration());
        }
    }
}
