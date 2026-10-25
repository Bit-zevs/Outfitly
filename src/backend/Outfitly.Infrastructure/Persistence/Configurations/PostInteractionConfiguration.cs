using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Domain;
namespace Outfitly.Infrastructure.Persistence.Configurations;
internal sealed class PostInteractionConfiguration : IEntityTypeConfiguration<PostInteraction>
{
    public void Configure(EntityTypeBuilder<PostInteraction> b)
    {
        b.ToTable("PostInteractions", t => {
            t.HasCheckConstraint("CK_PostInteractions_Target", "(\"ItemId\" IS NULL) <> (\"OutfitId\" IS NULL)");
            t.HasCheckConstraint("CK_PostInteractions_Kind", "\"Kind\" IN ('view', 'like', 'comment')");
        });
        b.Property(x => x.CreatedAt).HasConversion(x => x.UtcDateTime, x => new DateTimeOffset(DateTime.SpecifyKind(x, DateTimeKind.Utc)));
        b.Property(x => x.UpdatedAt).HasConversion(x => x.HasValue ? x.Value.UtcDateTime : (DateTime?)null, x => x.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(x.Value, DateTimeKind.Utc)) : null);
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasMaxLength(10); b.Property(x => x.Text).HasMaxLength(2000);
        b.Property(x => x.Text).IsConcurrencyToken(); b.Property(x => x.IsDeleted).IsConcurrencyToken();
        b.HasOne<WardrobeItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Outfit>().WithMany().HasForeignKey(x => x.OutfitId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PostInteraction>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ItemId, x.UserId, x.Kind }).IsUnique().HasFilter("\"Kind\" <> 'comment'");
        b.HasIndex(x => new { x.OutfitId, x.UserId, x.Kind }).IsUnique().HasFilter("\"Kind\" <> 'comment'");
        b.HasIndex(x => new { x.ItemId, x.ParentId, x.CreatedAt, x.Id });
        b.HasIndex(x => new { x.OutfitId, x.ParentId, x.CreatedAt, x.Id });
    }
}
