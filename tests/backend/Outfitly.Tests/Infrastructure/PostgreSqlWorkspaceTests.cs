using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Outfitly.Application;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;
using Outfitly.Infrastructure.Photos;
namespace Outfitly.Tests.Infrastructure;
public sealed class PostgreSqlWorkspaceTests
{
    private static (WorkspaceService Service, PhotoChanges Changes) Scope(OutfitlyDbContext db, string root)
    {
        var files = new LocalPhotoStore(root, NullLogger<LocalPhotoStore>.Instance); var changes = new PhotoChanges(files); var repo = new EfWardrobeRepository(db);
        return (new WorkspaceService(new EfWorkspaceStore(db), repo, new WardrobeService(repo, changes), files, new SkiaPhotoProcessor(), changes, TimeProvider.System), changes);
    }
    [PostgreSqlFact]
    public async Task Favorites_active_slots_and_draft_versions_are_protected_under_concurrency()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync(); WardrobeData data; var actor = Guid.NewGuid();
        var root = Path.Combine(Path.GetTempPath(), "outfitly-workspace-" + Guid.NewGuid().ToString("N"));
        await using (var seed = database.Open()) { data = await WardrobeData.SeedAsync(seed); seed.Users.Add(new User(actor, "Reader")); await seed.SaveChangesAsync(); }
        await using (var one = database.Open())
        await using (var two = database.Open())
        {
            Scope(one, root).Service.Favorite(actor, "items", data.ItemId, data.ItemToken, false); Scope(two, root).Service.Favorite(actor, "items", data.ItemId, data.ItemToken, false);
            await one.SaveChangesAsync(); await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => two.SaveChangesAsync());
        }
        Guid draftId = Guid.NewGuid(); DraftView draft;
        await using (var one = database.Open())
        await using (var two = database.Open())
        {
            draft = Scope(one, root).Service.Update(actor, draftId, new("items", null, null, null, new(Name: "First")));
            Scope(two, root).Service.Update(actor, Guid.NewGuid(), new("items", null, null, null, new(Name: "Other device")));
            await one.SaveChangesAsync(); await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => two.SaveChangesAsync());
        }
        await using (var one = database.Open())
        await using (var two = database.Open())
        {
            Scope(one, root).Service.Update(actor, draftId, new("items", null, draft.Version, null, new(Name: "Winner")));
            Scope(two, root).Service.Update(actor, draftId, new("items", null, draft.Version, null, new(Name: "Loser")));
            await one.SaveChangesAsync(); await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => two.SaveChangesAsync());
        }
        await using (var seed = database.Open())
        {
            for (var i = 0; i < 21; i++)
            {
                var item = new WardrobeItem(Guid.NewGuid(), data.Owner, $"Saved {i}", ClothingCategory.Top);
                seed.WardrobeItems.Add(item);
                seed.Add(new Favorite(data.Owner, "items", item.Id, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i)));
            }
            await seed.SaveChangesAsync();
        }
        await using var read = database.Open(); var service = Scope(read, root).Service;
        Assert.Equal("Winner", service.Read(actor, draftId).Content.Name); Assert.Equal(1, service.Favorites(actor, "items", 1).Total);
        var firstPage = service.Favorites(data.Owner, "items", 1); var secondPage = service.Favorites(data.Owner, "items", 2);
        Assert.Equal(21, firstPage.Total); Assert.Equal(20, firstPage.Items.Count);
        Assert.Equal("Saved 20", firstPage.Items.First().Name); Assert.Equal("Saved 0", Assert.Single(secondPage.Items).Name);
    }
    [PostgreSqlFact]
    public async Task A_copy_losing_to_source_changes_rolls_back_every_row_and_its_new_files()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync(); WardrobeData data; var actor = Guid.NewGuid();
        var root = Path.Combine(Path.GetTempPath(), "outfitly-copy-" + Guid.NewGuid().ToString("N"));
        var files = new LocalPhotoStore(root, NullLogger<LocalPhotoStore>.Instance);
        try
        {
            var originalKey = await files.WriteAsync([1, 2, 3], ".png", default);
            await using (var seed = database.Open()) { data = await WardrobeData.SeedAsync(seed); (await seed.WardrobeItems.SingleAsync(x => x.Id == data.ItemId)).SetUploadedPhoto(originalKey); seed.Users.Add(new User(actor, "Reader")); await seed.SaveChangesAsync(); }
            await using (var copying = database.Open())
            {
                var scope = Scope(copying, root); await scope.Service.CopyAsync(actor, data.OutfitId, Guid.NewGuid(), null, default);
                await using (var other = database.Open()) { new WardrobeService(new EfWardrobeRepository(other)).UpdateOutfit(data.Owner, data.OutfitId, "Changed"); await other.SaveChangesAsync(); }
                await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => copying.SaveChangesAsync()); await scope.Changes.CompleteAsync(false);
            }
            await using var fresh = database.Open();
            Assert.False(await fresh.Outfits.AnyAsync(x => x.OwnerId == actor)); Assert.False(await fresh.WardrobeItems.AnyAsync(x => x.OwnerId == actor)); Assert.Empty(await fresh.Set<CopyReceipt>().ToArrayAsync());
            Assert.Equal(originalKey, Path.GetFileName(Assert.Single(Directory.GetFiles(root))));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [PostgreSqlFact]
    public async Task A_losing_draft_photo_upload_keeps_the_winning_content_and_cleans_its_file()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync(); var actor = Guid.NewGuid(); var id = Guid.NewGuid(); DraftView draft;
        var root = Path.Combine(Path.GetTempPath(), "outfitly-draft-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var seed = database.Open()) { seed.Users.Add(new User(actor, "Owner")); await seed.SaveChangesAsync(); draft = Scope(seed, root).Service.Update(actor, id, new("items", null, null, null, new(Name: "Initial"))); await seed.SaveChangesAsync(); }
            await using (var uploading = database.Open())
            await using (var editing = database.Open())
            {
                var scope = Scope(uploading, root);
                using var bitmap = new SkiaSharp.SKBitmap(4, 4); using var image = SkiaSharp.SKImage.FromBitmap(bitmap); using var bytes = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                await scope.Service.UploadAsync(actor, id, draft.Version, new MemoryStream(bytes.ToArray()), "image/png", default);
                Scope(editing, root).Service.Update(actor, id, new("items", null, draft.Version, null, new(Name: "Other device")));
                await editing.SaveChangesAsync(); await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => uploading.SaveChangesAsync()); await scope.Changes.CompleteAsync(false);
            }
            await using var fresh = database.Open(); var saved = Scope(fresh, root).Service.Read(actor, id);
            Assert.Equal("Other device", saved.Content.Name); Assert.Null(saved.PhotoUrl); Assert.Empty(Directory.GetFiles(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
