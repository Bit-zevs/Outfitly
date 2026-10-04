using Outfitly.Domain;

namespace Outfitly.Tests.Domain;

public sealed class OutfitTests
{
    [Fact]
    public void An_empty_draft_cannot_be_published()
    {
        var outfit = new Outfit(Guid.NewGuid(), Guid.NewGuid(), "Draft");

        Assert.Throws<InvalidOperationException>(outfit.Publish);

        Assert.False(outfit.IsPublic);
        Assert.Empty(outfit.ItemIds);
    }

    [Fact]
    public void The_same_identifier_cannot_be_added_twice_even_as_another_object()
    {
        var owner = Guid.NewGuid();
        var item = new WardrobeItem(Guid.NewGuid(), owner, "Shirt", ClothingCategory.Top);
        var outfit = new Outfit(Guid.NewGuid(), owner, "Daily");
        outfit.AddItem(item);
        var duplicate = new WardrobeItem(item.Id, owner, "Another object", ClothingCategory.Bottom);

        Assert.Throws<InvalidOperationException>(() => outfit.AddItem(duplicate));

        Assert.Equal(item.Id, Assert.Single(outfit.ItemIds));
    }

    [Fact]
    public void A_foreign_item_is_rejected_without_changing_the_outfit()
    {
        var outfit = new Outfit(Guid.NewGuid(), Guid.NewGuid(), "Daily");
        var foreign = new WardrobeItem(Guid.NewGuid(), Guid.NewGuid(), "Foreign", ClothingCategory.Top);

        Assert.Throws<InvalidOperationException>(() => outfit.AddItem(foreign));

        Assert.Empty(outfit.ItemIds);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Removing_a_member_unpublishes_only_when_no_members_remain(int count, bool remainsPublic)
    {
        var owner = Guid.NewGuid();
        var outfit = new Outfit(Guid.NewGuid(), owner, "Daily");
        var first = new WardrobeItem(Guid.NewGuid(), owner, "Shirt", ClothingCategory.Top);
        outfit.AddItem(first);
        for (var i = 1; i < count; i++)
            outfit.AddItem(new WardrobeItem(Guid.NewGuid(), owner, "Shoes", ClothingCategory.Footwear));
        outfit.Publish();

        outfit.RemoveItem(first.Id);

        Assert.Equal(remainsPublic, outfit.IsPublic);
        Assert.DoesNotContain(first.Id, outfit.ItemIds);
        Assert.Equal(count - 1, outfit.ItemIds.Count);
    }

    [Fact]
    public void Removing_an_absent_member_preserves_a_populated_public_outfit()
    {
        var owner = Guid.NewGuid();
        var outfit = new Outfit(Guid.NewGuid(), owner, "Daily");
        var item = new WardrobeItem(Guid.NewGuid(), owner, "Shirt", ClothingCategory.Top);
        outfit.AddItem(item);
        outfit.Publish();

        outfit.RemoveItem(Guid.NewGuid());

        Assert.True(outfit.IsPublic);
        Assert.Equal(item.Id, Assert.Single(outfit.ItemIds));
    }

    [Fact]
    public void Invalid_outfit_edits_preserve_the_previous_name_and_description()
    {
        var outfit = new Outfit(Guid.NewGuid(), Guid.NewGuid(), "Daily", "Original description");

        Assert.Throws<ArgumentException>(() => outfit.Update(" \t", "Changed description"));

        Assert.Equal("Daily", outfit.Name);
        Assert.Equal("Original description", outfit.Description);
    }
}
