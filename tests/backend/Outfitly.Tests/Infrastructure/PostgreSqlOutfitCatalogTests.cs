using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
namespace Outfitly.Tests.Infrastructure;
public sealed class PostgreSqlOutfitCatalogTests
{
    [PostgreSqlFact]
    public async Task Search_treats_wildcards_literally_and_pages_before_loading_members()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var owner = Guid.NewGuid();
        await using (var seed = database.Open())
        {
            seed.Users.Add(new User(owner, "Owner"));
            var item = new WardrobeItem(Guid.NewGuid(), owner, "Shirt", ClothingCategory.Top, "White", "M", "Studio");
            var otherItem = new WardrobeItem(Guid.NewGuid(), owner, "Coat", ClothingCategory.Outerwear, "Blue", "L", "Other");
            seed.WardrobeItems.AddRange(item, otherItem);
            foreach (var name in new[] { "Look 100% B", "Look 100% A", "Look 1000 C", "Private 100%" })
            {
                var outfit = new Outfit(Guid.NewGuid(), owner, name); outfit.AddItem(item);
                if (!name.StartsWith("Private", StringComparison.Ordinal)) outfit.Publish();
                seed.Outfits.Add(outfit);
            }
            var nonMatching = new Outfit(Guid.NewGuid(), owner, "Look 100% C");
            nonMatching.AddItem(otherItem);
            nonMatching.Publish();
            seed.Outfits.Add(nonMatching);
            await seed.SaveChangesAsync();
        }
        await using var read = database.Open();
        var query = new EfPublicProfileQuery(read);
        var page = await query.AllOutfitsAsync(new ItemCatalogQuery(Search: "LOOK 100%", Sort: "nameDesc", Page: 2, PageSize: 1,
            Category: ClothingCategory.Top, Color: "WHITE", Size: "m", Brand: "studio"), CancellationToken.None);
        Assert.Equal(2, page.Total);
        var result = Assert.Single(page.Items);
        Assert.Equal("Look 100% A", result.Name);
        Assert.Single(result.Items);
        var options = await new EfItemCatalogQuery(read).GetPublicOutfitFilterOptionsAsync(CancellationToken.None);
        Assert.Equal(new[] { "other", "studio" }, options.Brands);
    }
}
