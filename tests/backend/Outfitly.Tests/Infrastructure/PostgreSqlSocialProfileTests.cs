using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Application.Authentication;
using Outfitly.Domain;
using Outfitly.Infrastructure.Authentication;
using Outfitly.Infrastructure.Persistence;
using Outfitly.Infrastructure.Photos;
using SkiaSharp;
namespace Outfitly.Tests.Infrastructure;

public sealed class PostgreSqlSocialProfileTests
{
    private static SocialProfiles Service(OutfitlyDbContext db) => new(new EfSocialProfileStore(db));
    internal static Guid Seed(OutfitlyDbContext db, string name, bool confirmed = true)
    {
        var id = Guid.NewGuid(); db.Users.Add(new User(id, name, name));
        db.Accounts.Add(new Account { UserId = id, Email = name + "@example.com", NormalizedEmail = name.ToUpperInvariant() + "@EXAMPLE.COM", PasswordHash = "test-only-hash", EmailConfirmed = confirmed });
        return id;
    }
    [PostgreSqlFact]
    public async Task Connections_are_paginated_public_confirmed_only_and_survive_renaming()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync(); Guid target; Guid first; Guid hidden;
        await using (var seed = database.Open())
        {
            target = Seed(seed, "target"); first = Seed(seed, "person00"); hidden = Seed(seed, "hidden", false);
            for (var i = 1; i <= 20; i++) { var follower = Seed(seed, $"person{i:D2}"); seed.Add(new UserFollow(follower, target)); }
            seed.Add(new UserFollow(first, target)); seed.Add(new UserFollow(hidden, target)); await seed.SaveChangesAsync();
        }
        await using (var read = database.Open())
        {
            var service = Service(read); var profile = await service.ProfileAsync("target", first, default);
            Assert.Equal(21, profile.FollowersCount); Assert.True(profile.IsFollowing);
            var one = await service.ConnectionsAsync("target", true, 1, default); var two = await service.ConnectionsAsync("target", true, 2, default);
            Assert.Equal(21, one.Total); Assert.Equal(20, one.Items.Count); Assert.Single(two.Items);
            Assert.Equal(Enumerable.Range(0, 21).Select(i => $"person{i:D2}"), one.Items.Concat(two.Items).Select(u => u.Username));
            Assert.Equal(1, (await service.ProfileAsync("person00", null, default)).FollowingCount);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ProfileAsync("hidden", first, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.FollowAsync(hidden, "target", false, default));
        }
        await using (var edit = database.Open()) { (await edit.Users.SingleAsync(u => u.Id == target)).ChangeUsername("renamed"); await edit.SaveChangesAsync(); }
        await using var fresh = database.Open(); Assert.True((await Service(fresh).ProfileAsync("renamed", first, default)).IsFollowing);
    }
    [PostgreSqlFact]
    public async Task Concurrent_follows_conflict_then_can_be_repeated_in_a_fresh_scope()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync(); Guid actor;
        await using (var seed = database.Open()) { actor = Seed(seed, "actor"); Seed(seed, "target"); await seed.SaveChangesAsync(); }
        await using (var first = database.Open())
        await using (var second = database.Open())
        {
            await Service(first).FollowAsync(actor, "target", false, default); await Service(second).FollowAsync(actor, "target", false, default);
            await first.SaveChangesAsync(); await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => second.SaveChangesAsync());
        }
        await using (var retry = database.Open()) { await Service(retry).FollowAsync(actor, "target", false, default); await retry.SaveChangesAsync(); }
        await using var fresh = database.Open(); Assert.Equal(1, (await Service(fresh).ProfileAsync("target", actor, default)).FollowersCount);
    }
    [PostgreSqlFact]
    public async Task Avatar_upload_and_delete_conflicts_preserve_database_and_files_in_both_commit_orders()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        foreach (var uploadWins in new[] { true, false })
        {
            var root = Path.Combine(Path.GetTempPath(), "outfitly-avatar-" + Guid.NewGuid().ToString("N"));
            var store = new LocalPhotoStore(root, Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalPhotoStore>.Instance);
            try
            {
                Guid actor; var oldKey = await store.WriteAsync([1, 2, 3], ".png", default);
                await using (var seed = database.Open()) { actor = Seed(seed, "actor_" + (uploadWins ? "one" : "two")); seed.Users.Local.Single(u => u.Id == actor).SetAvatar(oldKey); await seed.SaveChangesAsync(); }
                await using var upload = database.Open(); await using var deletion = database.Open();
                var uploadFiles = new PhotoChanges(store); var deleteFiles = new PhotoChanges(store);
                using var bitmap = new SKBitmap(20, 20); using var image = SKImage.FromBitmap(bitmap); using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
                await new AvatarService(new EfAccountStore(upload), store, new SkiaPhotoProcessor(), uploadFiles).UploadAsync(actor, new MemoryStream(bytes.ToArray()), "image/png", new(0, 0, 20), default);
                await new AvatarService(new EfAccountStore(deletion), store, new SkiaPhotoProcessor(), deleteFiles).DeleteAsync(actor, default);
                var winner = uploadWins ? upload : deletion; var loser = uploadWins ? deletion : upload;
                var winFiles = uploadWins ? uploadFiles : deleteFiles; var loseFiles = uploadWins ? deleteFiles : uploadFiles;
                await winner.SaveChangesAsync(); await winFiles.CompleteAsync(true);
                await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => loser.SaveChangesAsync()); await loseFiles.CompleteAsync(false);
                await using var fresh = database.Open(); var saved = await fresh.Users.SingleAsync(u => u.Id == actor);
                if (uploadWins) Assert.Equal(saved.AvatarStorageKey, Path.GetFileName(Assert.Single(Directory.GetFiles(root))));
                else { Assert.Null(saved.AvatarStorageKey); Assert.Empty(Directory.GetFiles(root)); }
                Assert.False(File.Exists(Path.Combine(root, oldKey)));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
