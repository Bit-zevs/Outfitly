using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class OutfitConcurrencyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Concurrent_removals_conflict_and_a_fresh_retry_revokes_links(bool deleteItems, bool reverse)
    {
        await using var database = await TestDatabase.CreateAsync();
        await OutfitConcurrencyScenarios.Removals(database.Open, deleteItems, reverse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Link_creation_and_emptying_cannot_both_commit_stale_state(bool linkWins)
    {
        await using var database = await TestDatabase.CreateAsync();
        await OutfitConcurrencyScenarios.CreateLink(database.Open, linkWins);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_and_emptying_cannot_both_commit_stale_state(bool publicationWins)
    {
        await using var database = await TestDatabase.CreateAsync();
        await OutfitConcurrencyScenarios.Publish(database.Open, publicationWins);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Link_creation_and_outfit_deletion_do_not_leave_orphan_active_links(bool linkWins)
    {
        await using var database = await TestDatabase.CreateAsync();
        await OutfitConcurrencyScenarios.DeleteOutfit(database.Open, linkWins);
    }

    [Fact]
    public async Task Link_revocation_also_coordinates_with_a_stale_membership_command()
    {
        await using var database = await TestDatabase.CreateAsync();
        WardrobeData data;
        Guid linkId;
        await using (var seed = database.Open())
        {
            data = await WardrobeData.SeedAsync(seed);
            linkId = (await seed.ShareLinks.SingleAsync(x => x.Token == data.OutfitToken)).Id;
        }
        await using (var revoke = database.Open())
        await using (var remove = database.Open())
        {
            OutfitConcurrencyScenarios.Service(revoke).DeactivateShareLink(data.Owner, linkId);
            OutfitConcurrencyScenarios.Service(remove).RemoveItemFromOutfit(data.Owner, data.OutfitId, data.ItemId);
            await revoke.SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => remove.SaveChangesAsync());
        }
        await using var read = database.Open();
        Assert.Equal(data.ItemId, Assert.Single((await read.Outfits.SingleAsync()).ItemIds));
        Assert.False((await read.ShareLinks.SingleAsync(x => x.Id == linkId)).IsActive);
        Assert.True((await read.Outfits.SingleAsync()).IsPublic);
    }

    [Fact]
    public async Task Commands_on_different_outfits_can_commit_independently()
    {
        await using var database = await TestDatabase.CreateAsync();
        WardrobeData data;
        Guid otherId;
        await using (var seed = database.Open())
        {
            data = await WardrobeData.SeedAsync(seed);
            var service = OutfitConcurrencyScenarios.Service(seed);
            var other = service.CreateOutfit(data.Owner, "Other");
            otherId = other.Id;
            service.AddItemToOutfit(data.Owner, otherId, data.ItemId);
            await seed.SaveChangesAsync();
        }
        await using var first = database.Open();
        await using var second = database.Open();
        var firstLink = OutfitConcurrencyScenarios.Service(first).CreateShareLink(data.Owner, ShareTargetType.Outfit, data.OutfitId);
        var secondLink = OutfitConcurrencyScenarios.Service(second).CreateShareLink(data.Owner, ShareTargetType.Outfit, otherId);
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();
        await using var read = database.Open();
        Assert.NotNull(OutfitConcurrencyScenarios.Service(read).GetSharedOutfit(firstLink.Token));
        Assert.NotNull(OutfitConcurrencyScenarios.Service(read).GetSharedOutfit(secondLink.Token));
    }
}

// Every schedule reads both snapshots before either commit; no sleeps or shared DbContext.
// The same scenarios run on PostgreSQL with real migrations and separate connections.
internal static class OutfitConcurrencyScenarios
{
    internal static WardrobeService Service(OutfitlyDbContext context) => new(new EfWardrobeRepository(context));

    internal static async Task Removals(Func<OutfitlyDbContext> open, bool deleteItems, bool reverse)
    {
        WardrobeData data;
        Guid secondId;
        await using (var seed = open())
        {
            data = await WardrobeData.SeedAsync(seed);
            var service = Service(seed);
            secondId = service.CreateItem(data.Owner, "Second", ClothingCategory.Bottom).Id;
            service.AddItemToOutfit(data.Owner, data.OutfitId, secondId);
            await seed.SaveChangesAsync();
        }
        var winnerId = reverse ? secondId : data.ItemId;
        var loserId = reverse ? data.ItemId : secondId;
        await using (var winner = open())
        await using (var loser = open())
        {
            Remove(Service(winner), winnerId);
            Remove(Service(loser), loserId);
            await winner.SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => loser.SaveChangesAsync());
        }
        await using (var read = open())
        {
            var outfit = await read.Outfits.SingleAsync(x => x.Id == data.OutfitId);
            Assert.Equal(loserId, Assert.Single(outfit.ItemIds));
            Assert.True(outfit.IsPublic);
            Assert.NotNull(Service(read).GetSharedOutfit(data.OutfitToken));
            Assert.True(await read.WardrobeItems.AnyAsync(x => x.Id == loserId));
            Assert.Equal(!deleteItems, await read.WardrobeItems.AnyAsync(x => x.Id == winnerId));
        }
        await using (var retry = open())
        {
            Remove(Service(retry), loserId);
            await retry.SaveChangesAsync();
        }
        await AssertEmptyAndRefill(open, data);

        void Remove(WardrobeService service, Guid itemId)
        {
            if (deleteItems) service.DeleteItem(data.Owner, itemId);
            else service.RemoveItemFromOutfit(data.Owner, data.OutfitId, itemId);
        }
    }

    internal static async Task CreateLink(Func<OutfitlyDbContext> open, bool linkWins)
    {
        WardrobeData data;
        await using (var seed = open()) data = await WardrobeData.SeedAsync(seed);
        ShareLink link;
        await using (var create = open())
        await using (var remove = open())
        {
            link = Service(create).CreateShareLink(data.Owner, ShareTargetType.Outfit, data.OutfitId);
            Service(remove).RemoveItemFromOutfit(data.Owner, data.OutfitId, data.ItemId);
            await (linkWins ? create : remove).SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => (linkWins ? remove : create).SaveChangesAsync());
        }
        await using (var read = open())
            Assert.Equal(linkWins, await read.ShareLinks.AnyAsync(x => x.Id == link.Id));
        if (linkWins)
        {
            await using var retry = open();
            Service(retry).RemoveItemFromOutfit(data.Owner, data.OutfitId, data.ItemId);
            await retry.SaveChangesAsync();
        }
        await AssertEmptyAndRefill(open, data);
        await using var final = open();
        Assert.Null(Service(final).GetSharedOutfit(link.Token));
    }

    internal static async Task Publish(Func<OutfitlyDbContext> open, bool publicationWins)
    {
        WardrobeData data;
        await using (var seed = open())
        {
            data = await WardrobeData.SeedAsync(seed);
            Service(seed).SetPublication(data.Owner, ShareTargetType.Outfit, data.OutfitId, false);
            await seed.SaveChangesAsync();
        }
        await using (var publish = open())
        await using (var remove = open())
        {
            Service(publish).SetPublication(data.Owner, ShareTargetType.Outfit, data.OutfitId, true);
            Service(remove).RemoveItemFromOutfit(data.Owner, data.OutfitId, data.ItemId);
            await (publicationWins ? publish : remove).SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => (publicationWins ? remove : publish).SaveChangesAsync());
        }
        await using (var retry = open())
        {
            if (publicationWins)
            {
                Service(retry).RemoveItemFromOutfit(data.Owner, data.OutfitId, data.ItemId);
                await retry.SaveChangesAsync();
            }
            else
                Assert.Throws<InvalidOperationException>(() => Service(retry).SetPublication(data.Owner, ShareTargetType.Outfit, data.OutfitId, true));
        }
        await AssertEmptyAndRefill(open, data);
    }

    internal static async Task DeleteOutfit(Func<OutfitlyDbContext> open, bool linkWins)
    {
        WardrobeData data;
        await using (var seed = open()) data = await WardrobeData.SeedAsync(seed);
        ShareLink link;
        await using (var create = open())
        await using (var delete = open())
        {
            link = Service(create).CreateShareLink(data.Owner, ShareTargetType.Outfit, data.OutfitId);
            Service(delete).DeleteOutfit(data.Owner, data.OutfitId);
            await (linkWins ? create : delete).SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => (linkWins ? delete : create).SaveChangesAsync());
        }
        if (linkWins)
        {
            await using var retry = open();
            Service(retry).DeleteOutfit(data.Owner, data.OutfitId);
            await retry.SaveChangesAsync();
        }
        await using var read = open();
        Assert.False(await read.Outfits.AnyAsync(x => x.Id == data.OutfitId));
        Assert.False(await read.ShareLinks.AnyAsync(x => x.TargetId == data.OutfitId && x.IsActive));
        Assert.Null(Service(read).GetSharedOutfit(link.Token));
        Assert.True(await read.WardrobeItems.AnyAsync(x => x.Id == data.ItemId));
    }

    private static async Task AssertEmptyAndRefill(Func<OutfitlyDbContext> open, WardrobeData data)
    {
        await using (var read = open())
        {
            var outfit = await read.Outfits.SingleAsync(x => x.Id == data.OutfitId);
            Assert.Empty(outfit.ItemIds);
            Assert.False(outfit.IsPublic);
            Assert.False(await read.ShareLinks.AnyAsync(x => x.TargetId == data.OutfitId && x.IsActive));
        }
        await using (var refill = open())
        {
            var service = Service(refill);
            var item = service.CreateItem(data.Owner, "Refill", ClothingCategory.Top);
            service.AddItemToOutfit(data.Owner, data.OutfitId, item.Id);
            await refill.SaveChangesAsync();
        }
        await using var final = open();
        Assert.Null(Service(final).GetSharedOutfit(data.OutfitToken));
        Assert.False((await final.Outfits.SingleAsync(x => x.Id == data.OutfitId)).IsPublic);
    }
}
