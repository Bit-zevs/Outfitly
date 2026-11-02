using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Domain;
namespace Outfitly.Infrastructure.Persistence.Configurations;
internal sealed class OutfitAssignmentConfiguration : IEntityTypeConfiguration<OutfitAssignment>
{
    public void Configure(EntityTypeBuilder<OutfitAssignment> builder)
    {
        builder.ToTable("OutfitAssignments");
        builder.HasKey(x => new { x.OwnerId, x.Date, x.OutfitId });
        builder.Property(x => x.Date).HasColumnType("date");
        builder.HasOne<Outfit>().WithMany().HasForeignKey(x => new { x.OutfitId, x.OwnerId })
            .HasPrincipalKey(x => new { x.Id, x.OwnerId }).OnDelete(DeleteBehavior.Cascade);
    }
}
