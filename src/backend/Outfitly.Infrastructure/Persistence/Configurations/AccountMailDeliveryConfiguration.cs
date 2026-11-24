using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Infrastructure.Authentication;
using Outfitly.Application.Authentication;

namespace Outfitly.Infrastructure.Persistence.Configurations;

internal sealed class AccountMailDeliveryConfiguration : IEntityTypeConfiguration<AccountMailDelivery>
{
    public void Configure(EntityTypeBuilder<AccountMailDelivery> builder)
    {
        builder.ToTable("PasswordResetDeliveries");
        builder.HasKey(mail => mail.TokenHash);
        builder.Property(mail => mail.TokenHash).HasMaxLength(64);
        builder.Property(mail => mail.Purpose).HasDefaultValue(AccountEmailPurpose.PasswordReset).HasSentinel(AccountEmailPurpose.PasswordReset);
        builder.HasIndex(mail => mail.DueAtUtc);
        builder.HasIndex(mail => mail.ExpiresAtUtc);
    }
}
