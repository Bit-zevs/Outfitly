using Outfitly.Domain;
namespace Outfitly.Tests.Domain;
public sealed class ProfileTests
{
    [Fact]
    public void Bio_is_optional_trimmed_and_limited_to_500_characters()
    {
        var user = new User(Guid.NewGuid(), "Anna");
        Assert.Null(user.Bio); Assert.Null(user.AvatarStorageKey);
        user.SetBio("  First\nSecond  "); Assert.Equal("First\nSecond", user.Bio);
        user.SetBio(new string('a', 500)); Assert.Equal(500, user.Bio!.Length);
        Assert.Throws<ArgumentException>(() => user.SetBio(new string('a', 501)));
        Assert.Equal(500, user.Bio.Length);
        user.SetBio(" \n "); Assert.Null(user.Bio);
        user.SetBio(null); Assert.Null(user.Bio);
    }
    [Fact]
    public void Following_yourself_is_not_allowed()
    {
        var id = Guid.NewGuid(); Assert.Throws<ArgumentException>(() => new UserFollow(id, id));
        var target = Guid.NewGuid(); var follow = new UserFollow(id, target);
        Assert.Equal(id, follow.FollowerId); Assert.Equal(target, follow.FollowedId);
    }
}
