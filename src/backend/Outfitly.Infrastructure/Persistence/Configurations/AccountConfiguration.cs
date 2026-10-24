using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Application.Authentication;
using Outfitly.Domain;

namespace Outfitly.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");
        builder.HasKey(account => account.UserId);
        builder.Property(account => account.UserId).ValueGeneratedNever();
        builder.Property(account => account.Email).HasMaxLength(254).IsRequired();
        builder.Property(account => account.NormalizedEmail).HasMaxLength(254).IsRequired().IsConcurrencyToken();
        builder.Property(account => account.SessionVersion).IsConcurrencyToken();
        builder.Property(account => account.PendingEmail).HasMaxLength(254);
        builder.Property(account => account.PendingNormalizedEmail).HasMaxLength(254);
        builder.Property(account => account.EmailChangeTokenHash).HasMaxLength(64).IsConcurrencyToken();
        builder.HasIndex(account => account.EmailChangeTokenHash).IsUnique();
        builder.Property(account => account.PasswordHash).IsRequired().IsConcurrencyToken();
        builder.Property(account => account.PasswordResetTokenHash).HasMaxLength(64).IsConcurrencyToken();
        builder.HasIndex(account => account.PasswordResetTokenHash).IsUnique();
        builder.Property(account => account.EmailConfirmed).HasDefaultValue(false);
        builder.Property(account => account.ConfirmationTokenHash).HasMaxLength(64).IsConcurrencyToken();
        builder.HasIndex(account => account.ConfirmationTokenHash).IsUnique();
        builder.HasIndex(account => account.NormalizedEmail).IsUnique();
        builder.HasOne<User>().WithOne().HasForeignKey<Account>(account => account.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LoginSessionConfiguration : IEntityTypeConfiguration<LoginSession>
{
    public void Configure(EntityTypeBuilder<LoginSession> builder)
    {
        builder.ToTable("LoginSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.HasOne<Account>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
