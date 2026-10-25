using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class EngagementPersistenceTests
{
    [Theory]
    [InlineData("view", false)]
    [InlineData("like", false)]
    [InlineData("comment", false)]
    [InlineData("view", true)]
    [InlineData("like", true)]
    [InlineData("comment", true)]
    public async Task Deleted_item_is_a_conflict_for_both_save_overloads(string kind, bool synchronous)
    {
        await using var database = await TestDatabase.CreateAsync();
        await EngagementDeletionScenario.Run(database.Open, kind, synchronous);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unrelated_foreign_key_errors_are_not_reported_as_target_concurrency(bool synchronous)
    {
        await using var database = await TestDatabase.CreateAsync();
        WardrobeData data;
        await using (var seed = database.Open()) data = await WardrobeData.SeedAsync(seed);
        await using (var db = database.Open())
        {
            db.Add(new PostInteraction(data.ItemId, false, Guid.NewGuid(), "comment", "Invalid author", null, DateTimeOffset.UtcNow));
            if (synchronous) Assert.Throws<DbUpdateException>(() => db.SaveChanges());
            else await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using var fresh = database.Open();
        Assert.Empty(await fresh.Set<PostInteraction>().ToArrayAsync());
        Assert.True(await fresh.WardrobeItems.AnyAsync(x => x.Id == data.ItemId));
    }
}

internal static class EngagementDeletionScenario
{
    internal static async Task Run(Func<OutfitlyDbContext> open, string kind, bool synchronous)
    {
        WardrobeData data; var visitor = Guid.NewGuid();
        await using (var seed = open())
        {
            data = await WardrobeData.SeedAsync(seed); seed.Users.Add(new User(visitor, "Visitor")); await seed.SaveChangesAsync();
        }
        await using (var stale = open())
        {
            var service = new PostEngagement(new EfWardrobeRepository(stale), new EfPostInteractionStore(stale), TimeProvider.System);
            if (kind == "comment") service.AddComment(data.ItemId, false, visitor, data.ItemToken, "Comment", null);
            else service.React(data.ItemId, false, visitor, data.ItemToken, kind);
            await using (var deletion = open())
            {
                new WardrobeService(new EfWardrobeRepository(deletion)).DeleteItem(data.Owner, data.ItemId);
                await deletion.SaveChangesAsync();
            }
            if (synchronous) Assert.Throws<WardrobeConcurrencyException>(() => stale.SaveChanges());
            else await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => stale.SaveChangesAsync());
        }
        await using var fresh = open();
        Assert.Empty(await fresh.Set<PostInteraction>().ToArrayAsync());
        Assert.False(await fresh.WardrobeItems.AnyAsync(x => x.Id == data.ItemId));
    }
}
