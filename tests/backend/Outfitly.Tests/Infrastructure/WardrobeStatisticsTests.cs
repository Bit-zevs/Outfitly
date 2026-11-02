using Microsoft.EntityFrameworkCore;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class WardrobeStatisticsTests
{
    [Fact]
    public async Task Sqlite_statistics_aggregate_saved_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Verify(database.Open);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_statistics_aggregate_saved_data()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await Verify(database.Open);
    }

    private static async Task Verify(Func<OutfitlyDbContext> open)
    {
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await using (var seed = open())
        {
            seed.Users.AddRange(new User(owner, "Owner"), new User(other, "Other"));
            var first = new WardrobeItem(Guid.NewGuid(), owner, "First", ClothingCategory.Top, " White ", brand: " BRAND ");
            var second = new WardrobeItem(Guid.NewGuid(), owner, "Second", ClothingCategory.Bottom, "white", brand: "brand");
            var third = new WardrobeItem(Guid.NewGuid(), owner, "Third", ClothingCategory.Top);
            first.Publish(); second.Publish(); second.Unpublish();
            var foreign = new WardrobeItem(Guid.NewGuid(), other, "Foreign", ClothingCategory.Dress);
            seed.WardrobeItems.AddRange(first, second, third, foreign);
            var outfit = new Outfit(Guid.NewGuid(), owner, "One"); outfit.AddItem(first); outfit.Publish();
            var another = new Outfit(Guid.NewGuid(), owner, "Two"); another.AddItem(first);
            seed.Outfits.AddRange(outfit, another);
            seed.Set<PostInteraction>().AddRange(
                new PostInteraction(first.Id, false, other, "view", null, null, DateTimeOffset.UtcNow),
                new PostInteraction(second.Id, false, other, "view", null, null, DateTimeOffset.UtcNow),
                new PostInteraction(first.Id, false, other, "like", null, null, DateTimeOffset.UtcNow),
                new PostInteraction(outfit.Id, true, other, "view", null, null, DateTimeOffset.UtcNow),
                new PostInteraction(foreign.Id, false, owner, "like", null, null, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
        }
        await using var read = open();
        var result = await new EfWardrobeStatisticsQuery(read).GetAsync(owner, default);
        Assert.Equal(3, result.Items); Assert.Equal(2, result.Outfits);
        Assert.Equal(1, result.PublicItems); Assert.Equal(1, result.PublicOutfits);
        Assert.Equal(2, result.ItemsWithoutOutfits);
        Assert.Equal(6, result.Categories.Count);
        Assert.Equal(2, result.Categories.Single(x => x.Category == ClothingCategory.Top).Count);
        Assert.Equal(0, result.Categories.Single(x => x.Category == ClothingCategory.Dress).Count);
        Assert.Equal(2, result.Colors.Single(x => x.Label == "white").Count);
        Assert.Equal(1, result.Colors.Single(x => x.Label == null).Count);
        Assert.Equal(2, result.Brands.Single(x => x.Label == "brand").Count);
        Assert.Equal(2, result.ItemReactions.Views); Assert.Equal(1, result.ItemReactions.Likes);
        Assert.Equal(1, result.OutfitReactions.Views); Assert.Equal(0, result.OutfitReactions.Likes);
        Assert.Empty(read.ChangeTracker.Entries());
        var empty = await new EfWardrobeStatisticsQuery(read).GetAsync(Guid.NewGuid(), default);
        Assert.Equal(0, empty.Items); Assert.Equal(0, empty.Outfits);
        Assert.Empty(empty.Colors); Assert.All(empty.Categories, x => Assert.Equal(0, x.Count));
    }
}
