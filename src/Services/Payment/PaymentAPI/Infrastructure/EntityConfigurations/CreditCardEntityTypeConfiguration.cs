using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentAPI.Model;

namespace PaymentAPI.Infrastructure.EntityConfigurations
{
    public class CreditCardEntityTypeConfiguration
        : IEntityTypeConfiguration<CreditCard>
    {
        public void Configure(EntityTypeBuilder<CreditCard> builder)
        {
            builder.ToTable("CreditCards");

            builder.HasKey(cc => cc.Id);

            builder.Property(cc => cc.CardBrand)
                .IsRequired();

            builder.Property(cc => cc.CardName)
                .IsRequired();

            builder.Property(cc => cc.CardNumber)
                .IsRequired();

            builder.Property(cc => cc.CVV)
                .IsRequired();

            builder.Property(cc => cc.ExpireMonth)
                .IsRequired();

            builder.Property(cc => cc.ExpireYear)
                .IsRequired();
        }
    }
}
