using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Outfitly.Application;
using Outfitly.Infrastructure.Persistence;
using Outfitly.Infrastructure.Photos;
namespace Outfitly.Tests.Infrastructure;

public sealed class ItemDraftConcurrencyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Finalization_rejects_a_metadata_write_after_the_base_version_check(bool synchronous, bool draftChangesName)
    {
        await using var database = await TestDatabase.CreateAsync();
        await ItemDraftConcurrencyScenario.Run(database.Open, synchronous, draftChangesName);
    }
}
public sealed class PostgreSqlItemDraftConcurrencyTests
{
    [PostgreSqlFact]
    public async Task Migrated_storage_rejects_the_same_metadata_race_for_both_save_overloads()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        foreach (var synchronous in new[] { false, true })
        foreach (var draftChangesName in new[] { false, true })
            await ItemDraftConcurrencyScenario.Run(database.Open, synchronous, draftChangesName);
    }
}
internal static class ItemDraftConcurrencyScenario
{
    internal static WorkspaceService Service(OutfitlyDbContext db)
    {
        var files = new LocalPhotoStore(Path.Combine(Path.GetTempPath(), "outfitly-item-race-unused"), NullLogger<LocalPhotoStore>.Instance);
        var changes = new PhotoChanges(files); var repo = new EfWardrobeRepository(db);
        return new(new EfWorkspaceStore(db), repo, new WardrobeService(repo, changes), files, new SkiaPhotoProcessor(), changes, TimeProvider.System);
    }
    internal static async Task Run(Func<OutfitlyDbContext> open, bool synchronous, bool draftChangesName)
    {
        WardrobeData data; DraftView draft; var draftId = Guid.NewGuid();
        await using (var seed = open())
        {
            data = await WardrobeData.SeedAsync(seed);
            var item = await seed.WardrobeItems.SingleAsync(x => x.Id == data.ItemId);
            draft = Service(seed).Update(data.Owner, draftId, new("items", item.Id, null, ResourceVersion.For(item),
                new(Name: draftChangesName ? "Draft name" : item.Name, Description: item.Description, Category: item.Category,
                    Color: item.Color, Size: item.Size, Brand: item.Brand, PhotoUrl: item.PhotoUrl, PhotoMode: "url")));
            await seed.SaveChangesAsync();
        }
        await using (var stale = open())
        {
            Service(stale).Save(data.Owner, draftId, draft.Version);
            await using (var winner = open())
            {
                var item = await winner.WardrobeItems.SingleAsync(x => x.Id == data.ItemId);
                new WardrobeService(new EfWardrobeRepository(winner)).UpdateItem(data.Owner, item.Id, "Other device", item.Category,
                    item.Color, item.Size, item.Brand, item.Description, item.PhotoUrl);
                if (synchronous) winner.SaveChanges(); else await winner.SaveChangesAsync();
            }
            if (synchronous) Assert.Throws<WardrobeConcurrencyException>(() => stale.SaveChanges());
            else await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => stale.SaveChangesAsync());
        }
        await using var fresh = open();
        Assert.Equal("Other device", (await fresh.WardrobeItems.SingleAsync(x => x.Id == data.ItemId)).Name);
        var retained = Service(fresh).Read(data.Owner, draftId);
        Assert.Null(retained.CompletedId); Assert.Equal(draft.Version, retained.Version); Assert.Equal(draft.Content, retained.Content);
    }
}
