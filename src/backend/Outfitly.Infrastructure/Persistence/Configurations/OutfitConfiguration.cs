using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Domain;

namespace Outfitly.Infrastructure.Persistence.Configurations;

internal sealed class OutfitConfiguration : IEntityTypeConfiguration<Outfit>
{
    public void Configure(EntityTypeBuilder<Outfit> builder)
    {
        builder.ToTable("Outfits");
        builder.HasKey(outfit => outfit.Id);
        builder.Property(outfit => outfit.Id).ValueGeneratedNever();
        builder.Property(outfit => outfit.OwnerId);
        builder.Property(outfit => outfit.Name).IsRequired();
        builder.Property(outfit => outfit.Description);
        builder.Property(outfit => outfit.IsPublic);
        builder.Property<Guid>("Revision").IsConcurrencyToken();
        builder.Ignore(outfit => outfit.ItemIds);
        builder.HasAlternateKey(outfit => new { outfit.Id, outfit.OwnerId });
        builder.HasIndex(outfit => outfit.OwnerId);
        builder.HasOne<User>().WithMany().HasForeignKey(outfit => outfit.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Sharing OwnerId across both foreign keys also enforces ownership in the database.
        builder.HasMany<WardrobeItem>("_items").WithMany()
            .UsingEntity<Dictionary<string, object>>("OutfitItems",
                join => join.HasOne<WardrobeItem>().WithMany()
                    .HasForeignKey("WardrobeItemId", "OwnerId")
                    .HasPrincipalKey(item => new { item.Id, item.OwnerId })
                    .OnDelete(DeleteBehavior.Restrict),
                join => join.HasOne<Outfit>().WithMany()
                    .HasForeignKey("OutfitId", "OwnerId")
                    .HasPrincipalKey(outfit => new { outfit.Id, outfit.OwnerId })
                    .OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("OutfitItems");
                    join.HasKey("OutfitId", "WardrobeItemId");
                });
        builder.Navigation("_items").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}
