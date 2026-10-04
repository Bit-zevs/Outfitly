using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Domain;

namespace Outfitly.Infrastructure.Persistence.Configurations;

internal sealed class ShareLinkConfiguration : IEntityTypeConfiguration<ShareLink>
{
    public void Configure(EntityTypeBuilder<ShareLink> builder)
    {
        builder.ToTable("ShareLinks");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Id).ValueGeneratedNever();
        builder.Property(link => link.Token).HasMaxLength(64).IsRequired();
        builder.Property(link => link.TargetId);
        builder.Property(link => link.TargetType).HasConversion<string>().IsRequired();
        builder.Property(link => link.IsActive);
        builder.HasIndex(link => link.Token).IsUnique();
        builder.HasIndex(link => new { link.TargetType, link.TargetId });
        // The polymorphic target cannot have a single relational foreign key.
        // Retain revoked links after deletion; WardrobeService handles revocation.
    }
}
