using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
namespace Outfitly.Tests.Infrastructure;
public sealed class PostgreSqlEngagementTests
{
    private static PostEngagement Service(OutfitlyDbContext db) => new(new EfWardrobeRepository(db), new EfPostInteractionStore(db), TimeProvider.System);
    [PostgreSqlFact]
    public async Task Migrated_storage_rejects_duplicate_reactions_and_fresh_commands_are_idempotent()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        WardrobeData data; var viewer = Guid.NewGuid();
        await using (var db = database.Open())
        {
            data = await WardrobeData.SeedAsync(db); db.Users.Add(new User(viewer, "Viewer")); await db.SaveChangesAsync();
        }
        foreach (var kind in new[] { "view", "like" })
        {
            await using var first = database.Open(); await using var second = database.Open();
            Service(first).React(data.ItemId, false, viewer, data.ItemToken, kind);
            Service(second).React(data.ItemId, false, viewer, data.ItemToken, kind);
            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => second.SaveChangesAsync());
            await using var retry = database.Open(); Service(retry).React(data.ItemId, false, viewer, data.ItemToken, kind); await retry.SaveChangesAsync();
        }
        await using (var read = database.Open())
        {
            var stats = Service(read).Read(data.ItemId, false, viewer, data.ItemToken);
            Assert.Equal(1, stats.Views); Assert.Equal(1, stats.Likes);
        }
    }
    [PostgreSqlFact]
    public async Task Outfit_commands_coordinate_revisions_and_target_deletion_cascades_through_replies()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        WardrobeData data; var viewer = Guid.NewGuid();
        await using (var db = database.Open())
        {
            data = await WardrobeData.SeedAsync(db); db.Users.Add(new User(viewer, "Viewer")); await db.SaveChangesAsync();
        }
        await using (var first = database.Open())
        await using (var stale = database.Open())
        {
            Service(first).React(data.OutfitId, true, viewer, null, "view");
            Service(stale).React(data.OutfitId, true, viewer, null, "like");
            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => stale.SaveChangesAsync());
        }
        Guid root;
        await using (var db = database.Open())
        {
            root = Service(db).AddComment(data.OutfitId, true, viewer, null, "Root", null); await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
        {
            Service(db).AddComment(data.OutfitId, true, data.Owner, null, "Reply", root); await db.SaveChangesAsync();
        }
        await using (var db = database.Open())
        {
            new WardrobeService(new EfWardrobeRepository(db)).DeleteOutfit(data.Owner, data.OutfitId); await db.SaveChangesAsync();
        }
        await using var fresh = database.Open(); Assert.Empty(await fresh.Set<PostInteraction>().ToArrayAsync());
    }
}

public sealed class PostgreSqlEngagementDeletionTests
{
    [PostgreSqlFact]
    public async Task Deleted_item_is_a_conflict_in_migrated_PostgreSQL()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        foreach (var kind in new[] { "view", "like", "comment" })
        foreach (var synchronous in new[] { false, true })
            await EngagementDeletionScenario.Run(database.Open, kind, synchronous);
    }
}
