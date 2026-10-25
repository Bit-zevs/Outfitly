using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Domain;
namespace Outfitly.Infrastructure.Persistence.Configurations;
internal sealed class UserFollowConfiguration : IEntityTypeConfiguration<UserFollow>
{
    public void Configure(EntityTypeBuilder<UserFollow> b)
    {
        b.ToTable("UserFollows", t => t.HasCheckConstraint("CK_UserFollows_NotSelf", "\"FollowerId\" <> \"FollowedId\""));
        b.HasKey(f => new { f.FollowerId, f.FollowedId });
        b.HasOne<User>().WithMany().HasForeignKey(f => f.FollowerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(f => f.FollowedId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(f => new { f.FollowedId, f.FollowerId });
    }
}
