using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using SubscriptionAPI.Model;

namespace SubscriptionAPI.Infrastructure.EntityConfigurations
{
    public class SubscriptionEntityTypeConfiguration
        : IEntityTypeConfiguration<Subscription>
    {
        public void Configure(EntityTypeBuilder<Subscription> builder)
        {
            builder.ToTable("Subscriptions");

            builder.HasKey(s => s.Id);

            builder.Property(s => s.PlanId)
                .IsRequired();

            builder.Property(s => s.SubscriptionStatus)
                .IsRequired();

        }
    }
}
