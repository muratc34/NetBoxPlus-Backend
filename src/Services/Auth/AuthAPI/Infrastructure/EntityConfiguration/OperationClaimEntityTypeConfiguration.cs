using AuthAPI.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthAPI.Infrastructure.EntityConfiguration
{
    public class OperationClaimEntityTypeConfiguration:IEntityTypeConfiguration<OperationClaim>
    {
        public void Configure(EntityTypeBuilder<OperationClaim> builder)
        {
            builder.ToTable("OperationClaims");

            builder.HasKey(x => x.Id);

        }
    }
}
