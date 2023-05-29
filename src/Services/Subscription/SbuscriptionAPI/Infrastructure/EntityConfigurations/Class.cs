using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using SubscriptionAPI.Model;

namespace SubscriptionAPI.Infrastructure.EntityConfigurations
{
    public class PlanEntityTypeConfiguration
        : IEntityTypeConfiguration<Plan>
    {
        public void Configure(EntityTypeBuilder<Plan> builder)
        {
            builder.ToTable("Plans");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.Quality)
                .IsRequired();

            builder.Property(p => p.ProfileCount)
                .IsRequired();
        }
    }
}
