using Microsoft.EntityFrameworkCore;
using SubscriptionAPI.Infrastructure.EntityConfigurations;
using SubscriptionAPI.Model;

namespace SubscriptionAPI.Infrastructure
{
    public class SubscriptionContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(@"Server=localhost;Port=5432;Database=SubscriptionDb;Search Path=public;User Id=postgres;Password=12345");
        }

        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<Plan> Plans { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            modelBuilder.ApplyConfiguration(new SubscriptionEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new PlanEntityTypeConfiguration());

            Guid subsId1 = Guid.NewGuid();

            Guid planId1 = Guid.NewGuid();
            Guid planId2 = Guid.NewGuid();
            Guid planId3 = Guid.NewGuid();

            List<Plan> planSeed = new List<Plan>()
            {
                new () {Id= planId1, Quality="HD", ProfileCount = 1},
                new () {Id= planId2, Quality="FHD", ProfileCount = 2},
                new () {Id= planId3, Quality="UHD", ProfileCount = 3}
            };

            List<Subscription> subscriptionSeed = new List<Subscription>()
            {
                new () { Id= subsId1, PlanId = planId1, SubscriptionExpiration = DateTime.Now.AddDays(30), SubscriptionStartDate= DateTime.Now, SubscriptionStatus= true}
            };


            modelBuilder.Entity<Plan>().HasData(planSeed);
            modelBuilder.Entity<Subscription>().HasData(subscriptionSeed);
        }
    }
}
