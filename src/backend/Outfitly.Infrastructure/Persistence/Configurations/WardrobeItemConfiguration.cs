using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Domain;

namespace Outfitly.Infrastructure.Persistence.Configurations;

internal sealed class WardrobeItemConfiguration : IEntityTypeConfiguration<WardrobeItem>
{
    public void Configure(EntityTypeBuilder<WardrobeItem> builder)
    {
        builder.ToTable("WardrobeItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.OwnerId);
        builder.Property(item => item.Name).IsRequired();
        builder.Property(item => item.Category).HasConversion<string>().IsRequired();
        builder.Property(item => item.Color);
        builder.Property(item => item.Size);
        builder.Property(item => item.Brand);
        builder.Property(item => item.Description);
        builder.Property(item => item.PhotoUrl);
        builder.Property(item => item.IsPublic);
        builder.HasAlternateKey(item => new { item.Id, item.OwnerId });
        builder.HasIndex(item => new { item.OwnerId, item.Category });
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
