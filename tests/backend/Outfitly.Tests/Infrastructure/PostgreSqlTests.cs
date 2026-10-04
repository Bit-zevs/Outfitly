using Microsoft.EntityFrameworkCore;
using Npgsql;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class PostgreSqlTests
{
    [PostgreSqlFact]
    public async Task Deleting_an_item_persists_changes_to_empty_and_populated_outfits_in_one_commit()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        WardrobeData data;
        Guid remainingId;
        string survivingToken;
        await using (var write = database.Open())
        {
            data = await WardrobeData.SeedAsync(write);
            var service = new WardrobeService(new EfWardrobeRepository(write));
            var remaining = service.CreateItem(data.Owner, "Shoes", ClothingCategory.Footwear);
            remainingId = remaining.Id;
            var populated = service.CreateOutfit(data.Owner, "Still populated");
            service.AddItemToOutfit(data.Owner, populated.Id, data.ItemId);
            service.AddItemToOutfit(data.Owner, populated.Id, remaining.Id);
            populated.Publish();
            survivingToken = service.CreateShareLink(data.Owner, ShareTargetType.Outfit, populated.Id).Token;
            await write.SaveChangesAsync();
        }
        await using (var delete = database.Open())
        {
            new WardrobeService(new EfWardrobeRepository(delete)).DeleteItem(data.Owner, data.ItemId);
            await delete.SaveChangesAsync();
        }

        await using var read = database.Open();
        var serviceAfterCommit = new WardrobeService(new EfWardrobeRepository(read));
        Assert.Null(serviceAfterCommit.GetSharedItem(data.ItemToken));
        Assert.Null(serviceAfterCommit.GetSharedOutfit(data.OutfitToken));
        Assert.Equal(remainingId, Assert.Single(serviceAfterCommit.GetSharedOutfit(survivingToken)!.Items).Id);
        Assert.Equal(remainingId, Assert.Single(Assert.Single(serviceAfterCommit.GetPublicOutfits()).Items).Id);
        var empty = await read.Outfits.SingleAsync(outfit => outfit.Id == data.OutfitId);
        Assert.Empty(empty.ItemIds);
        Assert.False(empty.IsPublic);
        Assert.Equal(3, await read.ShareLinks.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Actual_migrations_support_saving_and_reloading_a_shared_wardrobe()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        WardrobeData data;
        await using (var write = database.Open())
            data = await WardrobeData.SeedAsync(write);

        await using var read = database.Open();
        var service = new WardrobeService(new EfWardrobeRepository(read));
        var outfit = service.GetSharedOutfit(data.OutfitToken);

        Assert.NotNull(outfit);
        Assert.True(outfit.IsPublic);
        Assert.Equal(data.ItemId, Assert.Single(outfit.Items).Id);
        Assert.Equal(new ItemView(data.ItemId, data.Owner, "Shirt", ClothingCategory.Top, "White", "M",
            "Brand", "Cotton", "https://example.com/shirt.jpg", false), service.GetSharedItem(data.ItemToken));
        Assert.Equal("Alice", (await read.Users.SingleAsync()).DisplayName);
        Assert.False(read.Database.HasPendingModelChanges());
        Assert.Empty(await read.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlFact]
    public async Task The_database_rejects_a_second_copy_of_an_outfit_member()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var context = database.Open();
        var data = await WardrobeData.SeedAsync(context);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"OutfitItems\" (\"OutfitId\", \"WardrobeItemId\", \"OwnerId\") VALUES ({data.OutfitId}, {data.ItemId}, {data.Owner})"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        await using var read = database.Open();
        Assert.Equal(data.ItemId, Assert.Single((await read.Outfits.SingleAsync()).ItemIds));
    }

    [PostgreSqlFact]
    public async Task The_database_rejects_membership_across_owners_even_when_domain_checks_are_bypassed()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var context = database.Open();
        var data = await WardrobeData.SeedAsync(context);
        var other = Guid.NewGuid();
        var foreign = new WardrobeItem(Guid.NewGuid(), other, "Foreign", ClothingCategory.Bottom);
        context.Users.Add(new User(other, "Other"));
        context.WardrobeItems.Add(foreign);
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"OutfitItems\" (\"OutfitId\", \"WardrobeItemId\", \"OwnerId\") VALUES ({data.OutfitId}, {foreign.Id}, {data.Owner})"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        await using var read = database.Open();
        Assert.Equal(data.ItemId, Assert.Single((await read.Outfits.SingleAsync()).ItemIds));
    }

    [PostgreSqlFact]
    public async Task The_database_rejects_a_duplicate_share_token()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var context = database.Open();
        var data = await WardrobeData.SeedAsync(context);
        var newId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"ShareLinks\" (\"Id\", \"Token\", \"TargetType\", \"TargetId\", \"IsActive\") VALUES ({newId}, {data.ItemToken}, 'WardrobeItem', {data.ItemId}, true)"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal(2, await context.ShareLinks.CountAsync());
    }

    [PostgreSqlFact]
    public async Task A_failed_commit_rolls_back_item_deletion_membership_and_revocation_together()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        WardrobeData data;
        await using (var write = database.Open())
        {
            data = await WardrobeData.SeedAsync(write);
            new WardrobeService(new EfWardrobeRepository(write)).DeleteItem(data.Owner, data.ItemId);
            write.WardrobeItems.Add(new WardrobeItem(Guid.NewGuid(), Guid.NewGuid(), "Missing owner", ClothingCategory.Top));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());

            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        }

        await using var read = database.Open();
        var service = new WardrobeService(new EfWardrobeRepository(read));
        Assert.NotNull(service.GetSharedItem(data.ItemToken));
        var outfit = Assert.IsType<OutfitView>(service.GetSharedOutfit(data.OutfitToken));
        Assert.True(outfit.IsPublic);
        Assert.Equal(data.ItemId, Assert.Single(outfit.Items).Id);
        Assert.Single(await read.WardrobeItems.ToListAsync());
    }
}
