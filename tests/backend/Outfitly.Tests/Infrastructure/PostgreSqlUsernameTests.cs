using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Outfitly.Application;
using Outfitly.Application.Authentication;
using Outfitly.Domain;
using Outfitly.Infrastructure.Authentication;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class PostgreSqlUsernameTests
{
    private static AccountService Service(OutfitlyDbContext context) =>
        new(new EfAccountStore(context), new AccountPasswordHasher(), TimeProvider.System);

    [PostgreSqlFact]
    public async Task Migration_backfills_full_UUIDs_before_creating_the_unique_index()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var ids = new[] { Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), Guid.NewGuid(), Guid.NewGuid() };
        await using (var migrate = database.Open())
        {
            var migrator = migrate.GetService<IMigrator>();
            await migrator.MigrateAsync("20261015114640_PasswordReset");
            foreach (var id in ids)
                await migrate.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"Id\", \"DisplayName\") VALUES ({id}, 'Legacy')");
            await migrator.MigrateAsync();
        }
        await using var read = database.Open();
        var users = await read.Users.ToArrayAsync();
        Assert.Equal(ids.Length, users.Length);
        Assert.Equal(ids.Length, users.Select(user => user.Username).Distinct().Count());
        foreach (var user in users)
        {
            Assert.Equal(Usernames.FromId(user.Id), user.Username);
            Assert.Equal(user.Username, Usernames.Normalize(user.Username));
            Assert.Equal("Legacy", user.DisplayName);
        }
        Assert.False(read.Database.HasPendingModelChanges());
        Assert.Empty(await read.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlFact]
    public async Task Concurrent_registrations_of_case_variants_commit_one_complete_account()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var first = database.Open();
        await using var second = database.Open();
        await Service(first).RegisterAsync("first@example.com", "long password one", "First", " @ANNA ");
        await Service(second).RegisterAsync("second@example.com", "long password two", "Second", "anna");
        var results = await Task.WhenAll(Commit(first), Commit(second));
        Assert.Single(results, error => error is null);
        Assert.IsType<UsernameConflictException>(Assert.Single(results, error => error is not null));
        await using var read = database.Open();
        var user = await read.Users.SingleAsync();
        Assert.Equal("anna", user.Username);
        Assert.Equal(user.Id, (await read.Accounts.SingleAsync()).UserId);
    }

    [PostgreSqlFact]
    public async Task Rename_races_use_the_unique_index_and_released_names_can_be_reused()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        Guid firstId, secondId;
        await using (var seed = database.Open())
        {
            var first = await Service(seed).RegisterAsync("first@example.com", "long password one", "First", "first");
            var second = await Service(seed).RegisterAsync("second@example.com", "long password two", "Second", "second");
            firstId = first.Id; secondId = second.Id;
            await seed.SaveChangesAsync();
            await Service(seed).ConfirmEmailAsync(first.Confirmation.Token);
            await Service(seed).ConfirmEmailAsync(second.Confirmation.Token);
            await seed.SaveChangesAsync();
        }
        await using (var first = database.Open())
        await using (var second = database.Open())
        {
            await Service(first).ChangeUsernameAsync(firstId, "shared_name");
            await Service(second).ChangeUsernameAsync(secondId, "@SHARED_NAME");
            var results = await Task.WhenAll(Commit(first), Commit(second));
            Assert.Single(results, error => error is null);
            Assert.IsType<UsernameConflictException>(Assert.Single(results, error => error is not null));
        }
        await using (var read = database.Open())
        {
            var winner = await read.Users.SingleAsync(user => user.Username == "shared_name");
            var freed = winner.Id == firstId ? "first" : "second";
            var loser = winner.Id == firstId ? secondId : firstId;
            await Service(read).ChangeUsernameAsync(loser, freed);
            await read.SaveChangesAsync();
        }
        await using var verify = database.Open();
        Assert.Equal(2, await verify.Users.Select(user => user.Username).Distinct().CountAsync());
        Assert.Equal(2, await verify.Accounts.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Public_queries_use_literal_prefixes_and_return_only_published_resources()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        Guid ownerId, publicId, privateId, outfitId;
        await using (var seed = database.Open())
        {
            var owner = await Service(seed).RegisterAsync("owner@example.com", "long password one", "Анна", "anna_style");
            var other = await Service(seed).RegisterAsync("other@example.com", "long password two", "Other", "annaxstyle");
            await seed.SaveChangesAsync();
            await Service(seed).ConfirmEmailAsync(owner.Confirmation.Token);
            await Service(seed).ConfirmEmailAsync(other.Confirmation.Token);
            ownerId = owner.Id;
            var wardrobe = new WardrobeService(new EfWardrobeRepository(seed));
            publicId = wardrobe.CreateItem(ownerId, "Public", ClothingCategory.Top).Id;
            privateId = wardrobe.CreateItem(ownerId, "Private", ClothingCategory.Top).Id;
            wardrobe.SetPublication(ownerId, ShareTargetType.WardrobeItem, publicId, true);
            outfitId = wardrobe.CreateOutfit(ownerId, "Outfit").Id;
            wardrobe.AddItemToOutfit(ownerId, outfitId, privateId);
            wardrobe.SetPublication(ownerId, ShareTargetType.Outfit, outfitId, true);
            await seed.SaveChangesAsync();
        }
        await using var read = database.Open();
        var profiles = new PublicProfiles(new EfPublicProfileQuery(read));
        Assert.Equal("anna_style", Assert.Single((await profiles.SearchAsync("@ANNA_", 1, 20, default)).Items).Username);
        var items = await profiles.ItemsAsync("anna_style", 1, 20, default);
        Assert.Equal(publicId, Assert.Single(items.Items).Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profiles.ItemAsync("anna_style", privateId, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profiles.ItemAsync("annaxstyle", publicId, default));
        var outfit = await profiles.OutfitAsync("anna_style", outfitId, default);
        Assert.Equal(privateId, Assert.Single(outfit.Items).Id);
        Assert.Equal(ownerId, outfit.OwnerId);
        Assert.Empty(read.ChangeTracker.Entries());
    }

    private static async Task<Exception?> Commit(OutfitlyDbContext context)
    {
        try { await context.SaveChangesAsync(); return null; }
        catch (Exception exception) { return exception; }
    }
}
