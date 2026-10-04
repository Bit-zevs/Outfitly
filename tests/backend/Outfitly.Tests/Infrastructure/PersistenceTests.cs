using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Deleting_a_shared_item_persists_changes_to_all_outfits_and_only_affected_links()
    {
        await using var database = await TestDatabase.CreateAsync();
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
        Assert.Equal(remainingId, (await read.WardrobeItems.SingleAsync()).Id);
        var empty = await read.Outfits.SingleAsync(outfit => outfit.Id == data.OutfitId);
        Assert.Empty(empty.ItemIds);
        Assert.False(empty.IsPublic);
        Assert.Equal(3, await read.ShareLinks.CountAsync());
    }

    [Fact]
    public async Task A_fresh_context_restores_all_characteristics_membership_and_share_tokens()
    {
        await using var database = await TestDatabase.CreateAsync();
        WardrobeData data;
        await using (var write = database.Open())
            data = await WardrobeData.SeedAsync(write);

        await using var read = database.Open();
        var service = new WardrobeService(new EfWardrobeRepository(read));
        var item = service.GetSharedItem(data.ItemToken);
        var outfit = service.GetSharedOutfit(data.OutfitToken);

        Assert.Equal(new ItemView(data.ItemId, data.Owner, "Shirt", ClothingCategory.Top, "White", "M",
            "Brand", "Cotton", "https://example.com/shirt.jpg", false), item);
        Assert.NotNull(outfit);
        Assert.Equal(data.OutfitId, outfit.Id);
        Assert.Equal(data.Owner, outfit.OwnerId);
        Assert.Equal("Daily", outfit.Name);
        Assert.Equal("Summer outfit", outfit.Description);
        Assert.True(outfit.IsPublic);
        Assert.Equal(item, Assert.Single(outfit.Items));
        Assert.Equal("Alice", (await read.Users.SingleAsync()).DisplayName);
        Assert.Equal(new[] { data.ItemToken, data.OutfitToken }.Order(),
            (await read.ShareLinks.ToListAsync()).Select(link => link.Token).Order());
    }

    [Fact]
    public async Task Edits_and_cleared_optional_values_survive_a_new_context()
    {
        await using var database = await TestDatabase.CreateAsync();
        WardrobeData data;
        await using (var write = database.Open())
        {
            data = await WardrobeData.SeedAsync(write);
            (await write.Users.SingleAsync()).Rename("Bob");
            var service = new WardrobeService(new EfWardrobeRepository(write));
            service.UpdateItem(data.Owner, data.ItemId, "Updated", ClothingCategory.Accessory);
            service.UpdateOutfit(data.Owner, data.OutfitId, "Winter", null);
            service.SetPublication(data.Owner, ShareTargetType.WardrobeItem, data.ItemId, true);
            await write.SaveChangesAsync();
        }

        await using var read = database.Open();
        var serviceAfterReload = new WardrobeService(new EfWardrobeRepository(read));

        Assert.Equal(new ItemView(data.ItemId, data.Owner, "Updated", ClothingCategory.Accessory,
            null, null, null, null, null, true), serviceAfterReload.GetSharedItem(data.ItemToken));
        Assert.Equal("Bob", (await read.Users.SingleAsync()).DisplayName);
        var outfit = serviceAfterReload.GetSharedOutfit(data.OutfitToken);
        Assert.NotNull(outfit);
        Assert.Equal("Winter", outfit.Name);
        Assert.Null(outfit.Description);
    }

    [Fact]
    public async Task Removing_the_last_member_persists_revocation_even_after_refilling()
    {
        await using var database = await TestDatabase.CreateAsync();
        WardrobeData data;
        await using (var write = database.Open())
        {
            data = await WardrobeData.SeedAsync(write);
            new WardrobeService(new EfWardrobeRepository(write))
                .RemoveItemFromOutfit(data.Owner, data.OutfitId, data.ItemId);
            await write.SaveChangesAsync();
        }
        await using (var refill = database.Open())
        {
            new WardrobeService(new EfWardrobeRepository(refill))
                .AddItemToOutfit(data.Owner, data.OutfitId, data.ItemId);
            await refill.SaveChangesAsync();
        }

        await using var read = database.Open();
        var service = new WardrobeService(new EfWardrobeRepository(read));

        Assert.Null(service.GetSharedOutfit(data.OutfitToken));
        Assert.NotNull(service.GetSharedItem(data.ItemToken));
        Assert.Empty(service.GetPublicOutfits());
        Assert.Equal(data.ItemId, Assert.Single((await read.Outfits.SingleAsync()).ItemIds));
        Assert.False((await read.ShareLinks.SingleAsync(link => link.Token == data.OutfitToken)).IsActive);
    }

    [Fact]
    public async Task A_use_case_can_read_its_pending_additions_and_deletions_before_commit()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open();
        var owner = Guid.NewGuid();
        context.Users.Add(new User(owner, "Alice"));
        var service = new WardrobeService(new EfWardrobeRepository(context));
        var item = service.CreateItem(owner, "Shirt", ClothingCategory.Top);
        var outfit = service.CreateOutfit(owner, "Daily");
        service.AddItemToOutfit(owner, outfit.Id, item.Id);
        var link = service.CreateShareLink(owner, ShareTargetType.Outfit, outfit.Id);

        service.DeleteItem(owner, item.Id);

        Assert.Null(service.GetSharedOutfit(link.Token));
        Assert.Empty(new EfWardrobeRepository(context).GetAll());
        Assert.Empty(outfit.ItemIds);
        await context.SaveChangesAsync();
        await using var read = database.Open();
        Assert.Empty(await read.WardrobeItems.ToListAsync());
        Assert.Empty((await read.Outfits.SingleAsync()).ItemIds);
    }

    [Fact]
    public async Task A_failed_commit_cannot_leave_a_partly_deleted_wardrobe()
    {
        await using var database = await TestDatabase.CreateAsync();
        WardrobeData data;
        await using (var write = database.Open())
        {
            data = await WardrobeData.SeedAsync(write);
            new WardrobeService(new EfWardrobeRepository(write)).DeleteItem(data.Owner, data.ItemId);
            write.WardrobeItems.Add(new WardrobeItem(Guid.NewGuid(), Guid.NewGuid(), "Missing owner", ClothingCategory.Top));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());

            Assert.Equal(787, Assert.IsType<SqliteException>(exception.InnerException).SqliteExtendedErrorCode);
        }
        await using var read = database.Open();
        var service = new WardrobeService(new EfWardrobeRepository(read));
        Assert.NotNull(service.GetSharedItem(data.ItemToken));
        var outfit = Assert.IsType<OutfitView>(service.GetSharedOutfit(data.OutfitToken));
        Assert.True(outfit.IsPublic);
        Assert.Equal(data.ItemId, Assert.Single(outfit.Items).Id);
        Assert.Single(await read.WardrobeItems.ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_populated_outfit_preserves_its_items_and_their_share_links()
    {
        await using var database = await TestDatabase.CreateAsync();
        WardrobeData data;
        await using (var write = database.Open())
        {
            data = await WardrobeData.SeedAsync(write);

            new WardrobeService(new EfWardrobeRepository(write)).DeleteOutfit(data.Owner, data.OutfitId);
            await write.SaveChangesAsync();
        }

        await using var read = database.Open();
        var service = new WardrobeService(new EfWardrobeRepository(read));
        Assert.Empty(await read.Outfits.ToListAsync());
        Assert.Equal(data.ItemId, service.GetSharedItem(data.ItemToken)?.Id);
        Assert.Null(service.GetSharedOutfit(data.OutfitToken));
        Assert.False((await read.ShareLinks.SingleAsync(link => link.Token == data.OutfitToken)).IsActive);
        Assert.Equal(2, await read.ShareLinks.CountAsync());
    }
}
