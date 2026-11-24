using System.Security.Cryptography;
using System.Text;
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
    public async Task Migration_releases_legacy_pending_nicks_without_changing_verified_profiles_or_tokens()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var context = database.Open();
        const string previous = "20261123154006_CatalogCreationDates";
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        const string password = "a long confirmation password";
        var pendingId = Guid.NewGuid();
        var verifiedId = Guid.NewGuid();
        const string pendingToken = "legacy_token_padded_to_forty_three_chars_ab";
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pendingToken)));
        var now = DateTime.UtcNow;
        var passwordHash = new AccountPasswordHasher().Hash(password);
        // Use the historical schema for fixture writes before the migration under test.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Users" ("Id", "DisplayName", "Username")
            VALUES ({pendingId}, 'Pending', 'legacy_pending'), ({verifiedId}, 'Verified', 'verified')
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Accounts" ("UserId", "Email", "NormalizedEmail", "PasswordHash", "EmailConfirmed",
                "ConfirmationTokenHash", "ConfirmationIssuedAtUtc", "ConfirmationExpiresAtUtc", "SessionVersion")
            VALUES
                ({pendingId}, 'pending@example.com', 'PENDING@EXAMPLE.COM', '', false,
                    {tokenHash}, {now}, {now.AddHours(24)}, {Guid.Empty}),
                ({verifiedId}, 'verified@example.com', 'VERIFIED@EXAMPLE.COM', {passwordHash}, true,
                    NULL, NULL, NULL, {Guid.Empty})
            """);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        var temporary = Assert.IsType<User>(await context.Users.FindAsync(pendingId)).Username;
        Assert.StartsWith("~", temporary);
        Assert.False(await context.Users.AnyAsync(user => user.Username == "legacy_pending"));
        Assert.Equal("verified", (await context.Users.FindAsync(verifiedId))!.Username);
        await migrator.MigrateAsync(previous);
        context.ChangeTracker.Clear();
        var rollback = await context.Database.SqlQuery<string>($"""
            SELECT "Username" AS "Value" FROM "Users" WHERE "Id" = {pendingId}
            """).SingleAsync();
        Assert.Equal(rollback, Usernames.Normalize(rollback));
        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(temporary, (await context.Users.FindAsync(pendingId))!.Username);
        await Service(context).ConfirmEmailAsync(pendingToken, password, "legacy_pending");
        await context.SaveChangesAsync();
        Assert.Equal(pendingId, (await context.Users.SingleAsync(user => user.Username == "legacy_pending")).Id);
    }

    [PostgreSqlFact]
    public async Task Concurrent_confirmations_of_case_variants_claim_the_nick_once()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var first = database.Open();
        await using var second = database.Open();
        var a = await Service(first).RegisterAsync("first@example.com", "First");
        var b = await Service(second).RegisterAsync("second@example.com", "Second");
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();
        await Service(first).ConfirmEmailAsync(a.Confirmation.Token, "a long confirmation password", " @ANNA ");
        await Service(second).ConfirmEmailAsync(b.Confirmation.Token, "a long confirmation password", "anna");
        var results = await Task.WhenAll(Commit(first), Commit(second));
        Assert.Single(results, error => error is null);
        Assert.IsType<UsernameConflictException>(Assert.Single(results, error => error is not null));
        await using var read = database.Open();
        var user = await read.Users.SingleAsync(user => user.Username == "anna");
        Assert.Equal(user.Id, (await read.Accounts.SingleAsync(account => account.EmailConfirmed)).UserId);
        Assert.Equal(2, await read.Accounts.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Rename_races_use_the_unique_index_and_released_names_can_be_reused()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        Guid firstId, secondId;
        await using (var seed = database.Open())
        {
            var first = await Service(seed).RegisterAsync("first@example.com", "First");
            var second = await Service(seed).RegisterAsync("second@example.com", "Second");
            firstId = first.Id; secondId = second.Id;
            await seed.SaveChangesAsync();
            await Service(seed).ConfirmEmailAsync(first.Confirmation.Token, "long password one", "first");
            await Service(seed).ConfirmEmailAsync(second.Confirmation.Token, "long password two", "second");
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
            var owner = await Service(seed).RegisterAsync("owner@example.com", "Анна");
            var other = await Service(seed).RegisterAsync("other@example.com", "Other");
            await seed.SaveChangesAsync();
            await Service(seed).ConfirmEmailAsync(owner.Confirmation.Token, "long password one", "anna_style");
            await Service(seed).ConfirmEmailAsync(other.Confirmation.Token, "long password two", "annaxstyle");
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
