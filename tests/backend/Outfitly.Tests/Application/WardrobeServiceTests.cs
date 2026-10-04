using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure;

namespace Outfitly.Tests.Application;

public sealed class WardrobeServiceTests
{
    [Theory]
    [InlineData("update item", false)]
    [InlineData("update outfit", false)]
    [InlineData("publish item", false)]
    [InlineData("publish outfit", false)]
    [InlineData("share item", false)]
    [InlineData("share outfit", false)]
    [InlineData("revoke link", false)]
    [InlineData("add member", false)]
    [InlineData("remove member", false)]
    [InlineData("delete item", false)]
    [InlineData("delete outfit", false)]
    [InlineData("update item", true)]
    [InlineData("share outfit", true)]
    [InlineData("delete item", true)]
    public void Unauthorized_commands_leave_the_wardrobe_unchanged(string command, bool emptyActor)
    {
        var data = Arrange();
        var link = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, data.Outfit.Id);
        var actor = emptyActor ? Guid.Empty : Guid.NewGuid();
        var action = Command(data, link.Id, actor, command);

        Assert.Throws<UnauthorizedAccessException>(action);

        Assert.Equal("Shirt", data.Item.Name);
        Assert.Equal("Daily", data.Outfit.Name);
        Assert.False(data.Item.IsPublic);
        Assert.False(data.Outfit.IsPublic);
        Assert.Equal(data.Item.Id, Assert.Single(data.Outfit.ItemIds));
        Assert.True(link.IsActive);
        Assert.Single(data.Repository.GetAll());
        Assert.Single(data.Repository.GetOutfits());
        Assert.Single(data.Repository.GetLinks());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_outfits_cannot_be_published_or_shared(bool sharing)
    {
        var repository = new InMemoryWardrobeRepository();
        var service = new WardrobeService(repository);
        var owner = Guid.NewGuid();
        var draft = service.CreateOutfit(owner, "Draft");
        Action action = sharing
            ? () => service.CreateShareLink(owner, ShareTargetType.Outfit, draft.Id)
            : () => service.SetPublication(owner, ShareTargetType.Outfit, draft.Id, true);

        Assert.Throws<InvalidOperationException>(action);

        Assert.False(draft.IsPublic);
        Assert.Empty(repository.GetLinks());
    }

    [Fact]
    public void A_foreign_item_cannot_be_added_to_an_owned_outfit()
    {
        var data = Arrange();
        var foreign = data.Service.CreateItem(Guid.NewGuid(), "Foreign", ClothingCategory.Top);

        Assert.Throws<UnauthorizedAccessException>(() =>
            data.Service.AddItemToOutfit(data.Owner, data.Outfit.Id, foreign.Id));

        Assert.Equal(data.Item.Id, Assert.Single(data.Outfit.ItemIds));
    }

    [Fact]
    public void A_missing_item_cannot_be_added_to_an_outfit()
    {
        var data = Arrange();

        Assert.Throws<KeyNotFoundException>(() =>
            data.Service.AddItemToOutfit(data.Owner, data.Outfit.Id, Guid.NewGuid()));

        Assert.Equal(data.Item.Id, Assert.Single(data.Outfit.ItemIds));
    }

    [Theory]
    [InlineData(ShareTargetType.WardrobeItem)]
    [InlineData(ShareTargetType.Outfit)]
    public void Sharing_a_missing_target_does_not_create_an_orphan_link(ShareTargetType type)
    {
        var data = Arrange();

        Assert.Throws<KeyNotFoundException>(() => data.Service.CreateShareLink(data.Owner, type, Guid.NewGuid()));

        Assert.Empty(data.Repository.GetLinks());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Unknown_target_types_do_not_fall_through_to_outfit_commands(int type)
    {
        var data = Arrange();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            data.Service.SetPublication(data.Owner, (ShareTargetType)type, data.Outfit.Id, true));

        Assert.False(data.Outfit.IsPublic);
    }

    [Fact]
    public void Unpublishing_an_item_does_not_revoke_its_private_share_link()
    {
        var data = Arrange();
        data.Item.Publish();
        var link = data.Service.CreateShareLink(data.Owner, ShareTargetType.WardrobeItem, data.Item.Id);

        data.Service.SetPublication(data.Owner, ShareTargetType.WardrobeItem, data.Item.Id, false);

        Assert.Empty(data.Service.GetPublicItems());
        Assert.Equal(data.Item.Id, data.Service.GetSharedItem(link.Token)?.Id);
        Assert.True(link.IsActive);
    }

    [Fact]
    public void Revoking_a_link_preserves_publication_and_other_links()
    {
        var data = Arrange();
        data.Outfit.Publish();
        var first = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, data.Outfit.Id);
        var second = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, data.Outfit.Id);

        data.Service.DeactivateShareLink(data.Owner, first.Id);

        Assert.Null(data.Service.GetSharedOutfit(first.Token));
        Assert.Equal(data.Outfit.Id, data.Service.GetSharedOutfit(second.Token)?.Id);
        Assert.Equal(data.Outfit.Id, Assert.Single(data.Service.GetPublicOutfits()).Id);
    }

    [Fact]
    public void Published_outfits_show_private_members_without_publishing_them_separately()
    {
        var data = Arrange();
        data.Service.CreateItem(data.Owner, "Unrelated private item", ClothingCategory.Bottom);

        data.Service.SetPublication(data.Owner, ShareTargetType.Outfit, data.Outfit.Id, true);

        var outfit = Assert.Single(data.Service.GetPublicOutfits());
        Assert.Equal(data.Item.Id, Assert.Single(outfit.Items).Id);
        Assert.False(Assert.Single(outfit.Items).IsPublic);
        Assert.Empty(data.Service.GetPublicItems());
    }

    [Fact]
    public void Shared_outfits_show_private_members_without_appearing_in_public_lists()
    {
        var data = Arrange();

        var link = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, data.Outfit.Id);

        var view = Assert.IsType<OutfitView>(data.Service.GetSharedOutfit(link.Token));
        Assert.Equal(data.Item.Id, Assert.Single(view.Items).Id);
        Assert.Empty(data.Service.GetPublicOutfits());
        Assert.Empty(data.Service.GetPublicItems());
        Assert.Null(data.Service.GetSharedItem(link.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void Unrecognized_tokens_never_grant_access(string? token)
    {
        var data = Arrange();

        var item = data.Service.GetSharedItem(token!);
        var outfit = data.Service.GetSharedOutfit(token!);

        Assert.Null(item);
        Assert.Null(outfit);
    }

    [Fact]
    public void Deleting_a_member_updates_all_outfits_and_revokes_only_affected_links()
    {
        var data = Arrange();
        var remaining = data.Service.CreateItem(data.Owner, "Shoes", ClothingCategory.Footwear);
        var populated = data.Service.CreateOutfit(data.Owner, "Still populated");
        data.Service.AddItemToOutfit(data.Owner, populated.Id, data.Item.Id);
        data.Service.AddItemToOutfit(data.Owner, populated.Id, remaining.Id);
        data.Outfit.Publish();
        populated.Publish();
        var itemLink = data.Service.CreateShareLink(data.Owner, ShareTargetType.WardrobeItem, data.Item.Id);
        var itemLink2 = data.Service.CreateShareLink(data.Owner, ShareTargetType.WardrobeItem, data.Item.Id);
        var emptyLink = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, data.Outfit.Id);
        var emptyLink2 = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, data.Outfit.Id);
        var surviving = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, populated.Id);
        var survivingItem = data.Service.CreateShareLink(data.Owner, ShareTargetType.WardrobeItem, remaining.Id);

        data.Service.DeleteItem(data.Owner, data.Item.Id);

        Assert.Equal(remaining.Id, Assert.Single(data.Repository.GetAll()).Id);
        Assert.Empty(data.Outfit.ItemIds);
        Assert.False(data.Outfit.IsPublic);
        Assert.False(itemLink.IsActive);
        Assert.False(itemLink2.IsActive);
        Assert.False(emptyLink.IsActive);
        Assert.False(emptyLink2.IsActive);
        Assert.True(populated.IsPublic);
        Assert.Equal(remaining.Id, Assert.Single(populated.ItemIds));
        Assert.Equal(remaining.Id, Assert.Single(data.Service.GetSharedOutfit(surviving.Token)!.Items).Id);
        Assert.Equal(remaining.Id, data.Service.GetSharedItem(survivingItem.Token)?.Id);
        Assert.Null(data.Service.GetSharedItem(itemLink.Token));
        Assert.Null(data.Service.GetSharedOutfit(emptyLink.Token));
    }

    [Fact]
    public void Refilling_an_emptied_outfit_does_not_reactivate_its_old_links()
    {
        var data = Arrange();
        data.Outfit.Publish();
        var link = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, data.Outfit.Id);
        data.Service.RemoveItemFromOutfit(data.Owner, data.Outfit.Id, data.Item.Id);

        data.Service.AddItemToOutfit(data.Owner, data.Outfit.Id, data.Item.Id);

        Assert.False(data.Outfit.IsPublic);
        Assert.False(link.IsActive);
        Assert.Null(data.Service.GetSharedOutfit(link.Token));
        Assert.Equal(data.Item.Id, Assert.Single(data.Repository.GetAll()).Id);
    }

    [Fact]
    public void Deleting_an_outfit_preserves_clothes_and_item_links()
    {
        var data = Arrange();
        var first = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, data.Outfit.Id);
        var second = data.Service.CreateShareLink(data.Owner, ShareTargetType.Outfit, data.Outfit.Id);
        var itemLink = data.Service.CreateShareLink(data.Owner, ShareTargetType.WardrobeItem, data.Item.Id);

        data.Service.DeleteOutfit(data.Owner, data.Outfit.Id);

        Assert.Empty(data.Repository.GetOutfits());
        Assert.Equal(data.Item.Id, Assert.Single(data.Repository.GetAll()).Id);
        Assert.False(first.IsActive);
        Assert.False(second.IsActive);
        Assert.Equal(data.Item.Id, data.Service.GetSharedItem(itemLink.Token)?.Id);
        Assert.Null(data.Service.GetSharedOutfit(first.Token));
    }

    [Fact]
    public void Previously_returned_views_do_not_change_when_the_wardrobe_is_edited()
    {
        var data = Arrange();
        data.Outfit.Publish();
        var before = Assert.Single(data.Service.GetPublicOutfits());

        data.Service.UpdateItem(data.Owner, data.Item.Id, "New shirt", ClothingCategory.Bottom);

        Assert.Equal("Shirt", Assert.Single(before.Items).Name);
        Assert.Equal(ClothingCategory.Top, Assert.Single(before.Items).Category);
        Assert.Equal("New shirt", Assert.Single(Assert.Single(data.Service.GetPublicOutfits()).Items).Name);
    }

    private static Action Command(Data data, Guid linkId, Guid actor, string command) => command switch
    {
        "update item" => () => data.Service.UpdateItem(actor, data.Item.Id, "Changed", ClothingCategory.Bottom),
        "update outfit" => () => data.Service.UpdateOutfit(actor, data.Outfit.Id, "Changed"),
        "publish item" => () => data.Service.SetPublication(actor, ShareTargetType.WardrobeItem, data.Item.Id, true),
        "publish outfit" => () => data.Service.SetPublication(actor, ShareTargetType.Outfit, data.Outfit.Id, true),
        "share item" => () => data.Service.CreateShareLink(actor, ShareTargetType.WardrobeItem, data.Item.Id),
        "share outfit" => () => data.Service.CreateShareLink(actor, ShareTargetType.Outfit, data.Outfit.Id),
        "revoke link" => () => data.Service.DeactivateShareLink(actor, linkId),
        "add member" => () => data.Service.AddItemToOutfit(actor, data.Outfit.Id, data.Item.Id),
        "remove member" => () => data.Service.RemoveItemFromOutfit(actor, data.Outfit.Id, data.Item.Id),
        "delete item" => () => data.Service.DeleteItem(actor, data.Item.Id),
        "delete outfit" => () => data.Service.DeleteOutfit(actor, data.Outfit.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private static Data Arrange()
    {
        var repository = new InMemoryWardrobeRepository();
        var service = new WardrobeService(repository);
        var owner = Guid.NewGuid();
        var item = service.CreateItem(owner, "Shirt", ClothingCategory.Top);
        var outfit = service.CreateOutfit(owner, "Daily");
        service.AddItemToOutfit(owner, outfit.Id, item.Id);
        return new Data(repository, service, owner, item, outfit);
    }

    private sealed record Data(InMemoryWardrobeRepository Repository, WardrobeService Service,
        Guid Owner, WardrobeItem Item, Outfit Outfit);
}
