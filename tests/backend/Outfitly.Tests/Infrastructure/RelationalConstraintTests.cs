using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Outfitly.Domain;

namespace Outfitly.Tests.Infrastructure;

public sealed class RelationalConstraintTests
{
    [Fact]
    public async Task Membership_cannot_be_duplicated_in_storage()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var data = await WardrobeData.SeedAsync(context);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"OutfitItems\" (\"OutfitId\", \"WardrobeItemId\", \"OwnerId\") VALUES ({data.OutfitId}, {data.ItemId}, {data.Owner})"));

        Assert.Equal(1555, exception.SqliteExtendedErrorCode); // SQLITE_CONSTRAINT_PRIMARYKEY
        await using var read = database.Open();
        Assert.Equal(data.ItemId, Assert.Single((await read.Outfits.SingleAsync()).ItemIds));
    }

    [Fact]
    public async Task Valid_but_foreign_items_cannot_be_attached_by_bypassing_domain_checks()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var data = await WardrobeData.SeedAsync(context);
        var other = Guid.NewGuid();
        var foreign = new WardrobeItem(Guid.NewGuid(), other, "Foreign", ClothingCategory.Bottom);
        context.Users.Add(new User(other, "Other"));
        context.WardrobeItems.Add(foreign);
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"OutfitItems\" (\"OutfitId\", \"WardrobeItemId\", \"OwnerId\") VALUES ({data.OutfitId}, {foreign.Id}, {data.Owner})"));

        Assert.Equal(787, exception.SqliteExtendedErrorCode); // SQLITE_CONSTRAINT_FOREIGNKEY
        await using var read = database.Open();
        Assert.Equal(data.ItemId, Assert.Single((await read.Outfits.SingleAsync()).ItemIds));
    }

    [Fact]
    public async Task Share_tokens_cannot_identify_two_different_links()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var data = await WardrobeData.SeedAsync(context);
        var newId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"ShareLinks\" (\"Id\", \"Token\", \"TargetType\", \"TargetId\", \"IsActive\") VALUES ({newId}, {data.ItemToken}, 'WardrobeItem', {data.ItemId}, 1)"));

        Assert.Equal(2067, exception.SqliteExtendedErrorCode); // SQLITE_CONSTRAINT_UNIQUE
        await using var read = database.Open();
        Assert.Equal(2, await read.ShareLinks.CountAsync());
    }

    [Fact]
    public async Task An_owner_with_a_wardrobe_cannot_be_deleted_accidentally()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var data = await WardrobeData.SeedAsync(context);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"Users\" WHERE \"Id\" = {data.Owner}"));

        Assert.Equal(1811, exception.SqliteExtendedErrorCode); // SQLite RESTRICT trigger
        await using var read = database.Open();
        Assert.Single(await read.Users.ToListAsync());
        Assert.Single(await read.WardrobeItems.ToListAsync());
        Assert.Equal(data.ItemId, Assert.Single((await read.Outfits.SingleAsync()).ItemIds));
    }
}
