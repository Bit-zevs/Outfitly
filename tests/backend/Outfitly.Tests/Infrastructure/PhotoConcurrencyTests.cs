using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class PhotoConcurrencyTests
{
    [Fact]
    public async Task Concurrent_upload_and_stale_delete_do_not_overwrite_the_winning_photo()
    {
        await using var database = await TestDatabase.CreateAsync();
        await Verify(database.Open);
    }

    [PostgreSqlFact]
    public async Task PostgreSQL_migration_and_photo_concurrency_preserve_the_winning_photo()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await Verify(database.Open);
    }

    private static async Task Verify(Func<OutfitlyDbContext> open)
    {
        WardrobeData data;
        await using (var seed = open()) data = await WardrobeData.SeedAsync(seed);
        await using var winner = open();
        await using var loser = open();
        await using var staleDelete = open();
        var winnerItem = await winner.WardrobeItems.SingleAsync();
        var loserItem = await loser.WardrobeItems.SingleAsync();
        new WardrobeService(new EfWardrobeRepository(staleDelete)).GetItem(data.Owner, data.ItemId);
        winnerItem.SetUploadedPhoto($"{Guid.NewGuid():N}.png");
        loserItem.SetUploadedPhoto($"{Guid.NewGuid():N}.jpg");
        await winner.SaveChangesAsync();
        await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => loser.SaveChangesAsync());
        new WardrobeService(new EfWardrobeRepository(staleDelete)).DeleteItem(data.Owner, data.ItemId);
        await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => staleDelete.SaveChangesAsync());
        await using var read = open();
        Assert.Equal(winnerItem.PhotoStorageKey, (await read.WardrobeItems.SingleAsync()).PhotoStorageKey);
        Assert.Contains(data.ItemId, (await read.Outfits.SingleAsync()).ItemIds);
        Assert.All(await read.ShareLinks.ToArrayAsync(), link => Assert.True(link.IsActive));
    }
}
