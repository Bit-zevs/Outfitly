using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Infrastructure.Authentication;

namespace Outfitly.Infrastructure.Persistence.Configurations;

internal sealed class PasswordResetDeliveryConfiguration : IEntityTypeConfiguration<PasswordResetDelivery>
{
    public void Configure(EntityTypeBuilder<PasswordResetDelivery> builder)
    {
        builder.ToTable("PasswordResetDeliveries");
        builder.HasKey(mail => mail.TokenHash);
        builder.Property(mail => mail.TokenHash).HasMaxLength(64);
        builder.HasIndex(mail => mail.DueAtUtc);
        builder.HasIndex(mail => mail.ExpiresAtUtc);
    }
}
