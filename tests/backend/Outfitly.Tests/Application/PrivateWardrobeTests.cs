using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure;

namespace Outfitly.Tests.Application;

public sealed class PrivateWardrobeTests
{
    [Fact]
    public void Personal_lists_include_private_resources_and_exclude_other_owners()
    {
        var service = new WardrobeService(new InMemoryWardrobeRepository());
        var owner = Guid.NewGuid();
        var item = service.CreateItem(owner, "Private shirt", ClothingCategory.Top);
        var outfit = service.CreateOutfit(owner, "Private draft");
        var other = Guid.NewGuid();
        service.CreateItem(other, "Foreign", ClothingCategory.Bottom).Publish();
        service.CreateOutfit(other, "Foreign draft");

        var items = service.GetMyItems(owner);
        var outfits = service.GetMyOutfits(owner);

        Assert.Equal(item.Id, Assert.Single(items).Id);
        Assert.False(Assert.Single(items).IsPublic);
        Assert.Equal(outfit.Id, Assert.Single(outfits).Id);
        Assert.Empty(Assert.Single(outfits).Items);
    }

    [Fact]
    public void Personal_reads_reject_an_empty_actor()
    {
        var service = new WardrobeService(new InMemoryWardrobeRepository());

        Assert.Throws<UnauthorizedAccessException>(() => service.GetMyItems(Guid.Empty));
        Assert.Throws<UnauthorizedAccessException>(() => service.GetMyOutfits(Guid.Empty));
    }

    [Fact]
    public void Knowing_a_public_resource_id_does_not_grant_access_to_its_owner_view()
    {
        var service = new WardrobeService(new InMemoryWardrobeRepository());
        var owner = Guid.NewGuid();
        var item = service.CreateItem(owner, "Shirt", ClothingCategory.Top);
        var outfit = service.CreateOutfit(owner, "Daily");
        service.AddItemToOutfit(owner, outfit.Id, item.Id);
        item.Publish();
        outfit.Publish();
        var other = Guid.NewGuid();

        Assert.Throws<UnauthorizedAccessException>(() => service.GetItem(other, item.Id));
        Assert.Throws<UnauthorizedAccessException>(() => service.GetOutfit(other, outfit.Id));
    }
}
