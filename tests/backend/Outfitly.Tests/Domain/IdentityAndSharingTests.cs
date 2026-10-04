using Outfitly.Domain;

namespace Outfitly.Tests.Domain;

public sealed class IdentityAndSharingTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("item")]
    [InlineData("item owner")]
    [InlineData("outfit")]
    [InlineData("outfit owner")]
    [InlineData("link")]
    [InlineData("link target")]
    public void Empty_identifiers_are_rejected_at_domain_entry_points(string entity)
    {
        var valid = Guid.NewGuid();
        Action create = entity switch
        {
            "user" => () => _ = new User(Guid.Empty, "Alice"),
            "item" => () => _ = new WardrobeItem(Guid.Empty, valid, "Shirt", ClothingCategory.Top),
            "item owner" => () => _ = new WardrobeItem(valid, Guid.Empty, "Shirt", ClothingCategory.Top),
            "outfit" => () => _ = new Outfit(Guid.Empty, valid, "Daily"),
            "outfit owner" => () => _ = new Outfit(valid, Guid.Empty, "Daily"),
            "link" => () => _ = new ShareLink(Guid.Empty, ShareTargetType.Outfit, valid),
            "link target" => () => _ = new ShareLink(valid, ShareTargetType.Outfit, Guid.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(entity))
        };

        Assert.Throws<ArgumentException>(create);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Invalid_renames_preserve_the_existing_user_name(string? name)
    {
        var user = new User(Guid.NewGuid(), "Alice");

        Assert.ThrowsAny<ArgumentException>(() => user.Rename(name!));

        Assert.Equal("Alice", user.DisplayName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Unknown_share_targets_cannot_produce_a_link(int target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ShareLink(Guid.NewGuid(), (ShareTargetType)target, Guid.NewGuid()));
    }

    [Fact]
    public void Revoking_one_link_leaves_another_link_to_the_same_target_active()
    {
        var target = Guid.NewGuid();
        var revoked = new ShareLink(Guid.NewGuid(), ShareTargetType.WardrobeItem, target);
        var active = new ShareLink(Guid.NewGuid(), ShareTargetType.WardrobeItem, target);
        var token = revoked.Token;

        revoked.Deactivate();
        revoked.Deactivate();

        Assert.False(revoked.IsActive);
        Assert.True(active.IsActive);
        Assert.Equal(token, revoked.Token);
        Assert.NotEqual(revoked.Token, active.Token);
        Assert.Matches("^[0-9a-f]{64}$", active.Token);
    }
}
