using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
namespace Outfitly.Tests.Infrastructure;
public sealed class PersonalCatalogTests
{
    [Fact]
    public async Task Sqlite_personal_filters() { await using var db = await TestDatabase.CreateAsync(); await Verify(db.Open); }
    [PostgreSqlFact]
    public async Task PostgreSql_personal_filters() { await using var db = await PostgreSqlDatabase.CreateAsync(); await Verify(db.Open); }
    private static async Task Verify(Func<OutfitlyDbContext> open)
    {
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await using (var seed = open())
        {
            seed.Users.AddRange(new User(owner, "Owner"), new User(other, "Other"));
            var a = new WardrobeItem(Guid.NewGuid(), owner, "Alpha", ClothingCategory.Top, " White ", " M ", " бренд ", "лён 50%_\\"); a.Publish();
            var b = new WardrobeItem(Guid.NewGuid(), owner, "Beta", ClothingCategory.Top, "white", "m", "бренд");
            seed.WardrobeItems.AddRange(a, b, new WardrobeItem(Guid.NewGuid(), owner, "Empty", ClothingCategory.Bottom),
                new WardrobeItem(Guid.NewGuid(), other, "Foreign", ClothingCategory.Top, "foreign", "xxl", "foreign"));
            await seed.SaveChangesAsync();
        }
        await using var read = open(); var query = new EfItemCatalogQuery(read);
        var result = await query.GetAsync(owner, new ItemCatalogQuery(Search: " лЁн 50%_\\ ", Category: ClothingCategory.Top,
            Color: " WHITE ", Brand: " БРЕНД ", Size: " M ", Publication: "public"), default);
        Assert.Equal("Alpha", Assert.Single(result.Items).Name);
        Assert.Equal(1, result.Total);
        Assert.Equal(2, (await query.GetAsync(owner, new ItemCatalogQuery(Search: "бренд"), default)).Total);
        Assert.Equal(0, (await query.GetAsync(null, new ItemCatalogQuery(Search: "бренд"), default)).Total);
        Assert.Equal("Beta", Assert.Single((await query.GetAsync(owner, new ItemCatalogQuery(Brand: "бренд", Publication: "private"), default)).Items).Name);
        Assert.Equal("Empty", Assert.Single((await query.GetAsync(owner, new ItemCatalogQuery(ColorMissing: true, BrandMissing: true, SizeMissing: true), default)).Items).Name);
        var options = await query.GetFilterOptionsAsync(owner, default);
        Assert.Equal(new string?[] { null, "white" }, options.Colors);
        Assert.Equal(new string?[] { null, "бренд" }, options.Brands);
        Assert.Equal(new string?[] { null, "m" }, options.Sizes);
        var page = await query.GetAsync(owner, new ItemCatalogQuery(Brand: "бренд", Page: 2, PageSize: 1), default);
        Assert.Equal(2, page.Total); Assert.Equal("Beta", Assert.Single(page.Items).Name);
        Assert.Empty(read.ChangeTracker.Entries());
    }
}

