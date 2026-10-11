using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class PostgreSqlCatalogTests
{
    [PostgreSqlFact]
    public async Task Filters_use_PostgreSQL_case_folding_literal_patterns_and_stable_pages()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ids = new[] { Guid.Parse("00000000-0000-0000-0000-000000000003"), Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002") };
        await using (var seed = database.Open())
        {
            seed.Users.AddRange(new User(owner, "Owner"), new User(other, "Other"));
            foreach (var id in ids)
            {
                var item = new WardrobeItem(id, owner, "Рубашка 50%_\\", ClothingCategory.Top, "БЕЛЫЙ");
                item.Publish();
                seed.WardrobeItems.Add(item);
            }
            seed.WardrobeItems.Add(new WardrobeItem(Guid.NewGuid(), owner, "Рубашка 500x", ClothingCategory.Bottom, "БЕЛЫЙ"));
            seed.WardrobeItems.Add(new WardrobeItem(Guid.NewGuid(), other, "Рубашка чужая", ClothingCategory.Top, "БЕЛЫЙ"));
            await seed.SaveChangesAsync();
        }
        await using var read = database.Open();
        var query = new EfItemCatalogQuery(read);
        var filter = new ItemCatalogQuery("  РУБАШКА 50%_\\  ", ClothingCategory.Top, " белый ", PageSize: 2);
        var first = await query.GetAsync(owner, filter, default);
        var second = await query.GetAsync(owner, filter with { Page = 2 }, default);
        Assert.Equal(3, first.Total);
        Assert.Equal(ids.Order().Take(2), first.Items.Select(item => item.Id));
        Assert.Equal(ids.Order().Skip(2), second.Items.Select(item => item.Id));
        var publicItems = await query.GetAsync(null, new ItemCatalogQuery(Search: "рубашка"), default);
        Assert.Equal(3, publicItems.Total);
        Assert.All(publicItems.Items, item => Assert.True(item.IsPublic));
        Assert.Empty(read.ChangeTracker.Entries<WardrobeItem>());
    }
}
