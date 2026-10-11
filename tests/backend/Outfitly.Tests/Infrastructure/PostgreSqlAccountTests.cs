using Microsoft.EntityFrameworkCore;
using Outfitly.Application.Authentication;
using Outfitly.Domain;
using Outfitly.Infrastructure.Authentication;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class PostgreSqlAccountTests
{
    private static AccountService Service(OutfitlyDbContext context) =>
        new(new EfAccountStore(context), new AccountPasswordHasher(), TimeProvider.System);

    [PostgreSqlFact]
    public async Task Concurrent_registration_of_the_same_email_commits_one_complete_account()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var first = database.Open();
        await using var second = database.Open();
        var alice = await Service(first).RegisterAsync("Alice@example.com", "first long password", "Alice");
        var bob = await Service(second).RegisterAsync("alice@EXAMPLE.COM", "second long password", "Bob");
        // Both uniqueness checks finish before either save; the database must arbitrate the race.
        var results = await Task.WhenAll(Commit(first), Commit(second));
        Assert.Single(results, error => error is null);
        Assert.IsType<AccountConflictException>(Assert.Single(results, error => error is not null));

        await using var read = database.Open();
        var account = await read.Accounts.SingleAsync();
        var user = await read.Users.SingleAsync();
        Assert.Equal(account.UserId, user.Id);
        Assert.Equal(results[0] is null ? alice.Id : bob.Id, user.Id);
        Assert.Equal(results[0] is null ? "Alice" : "Bob", user.DisplayName);
        Assert.Empty(await read.Database.GetPendingMigrationsAsync());
        Assert.False(read.Database.HasPendingModelChanges());
    }

    [PostgreSqlFact]
    public async Task Sessions_survive_new_contexts_and_revocation_leaves_other_sessions_active()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        Guid owner;
        await using (var register = database.Open())
        {
            owner = (await Service(register).RegisterAsync("alice@example.com", "a long test password", "Alice")).Id;
            await register.SaveChangesAsync();
        }
        LoginResult first;
        LoginResult second;
        await using (var login = database.Open())
        {
            first = (await Service(login).LoginAsync("alice@example.com", "a long test password"))!;
            await login.SaveChangesAsync();
        }
        await using (var login = database.Open())
        {
            second = (await Service(login).LoginAsync("alice@example.com", "a long test password"))!;
            await login.SaveChangesAsync();
        }
        await using (var logout = database.Open())
        {
            Assert.NotNull(await Service(logout).ValidateSessionAsync(owner, first.Session.Id));
            await Service(logout).RevokeSessionAsync(owner, first.Session.Id);
            await logout.SaveChangesAsync();
        }
        await using var read = database.Open();
        Assert.Null(await Service(read).ValidateSessionAsync(owner, first.Session.Id));
        Assert.NotNull(await Service(read).ValidateSessionAsync(owner, second.Session.Id));
    }

    [PostgreSqlFact]
    public async Task A_failed_registration_commit_does_not_leave_a_user_or_credentials()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using (var write = database.Open())
        {
            await Service(write).RegisterAsync("alice@example.com", "a long test password", "Alice");
            write.WardrobeItems.Add(new WardrobeItem(Guid.NewGuid(), Guid.NewGuid(), "No owner", ClothingCategory.Top));
            await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
        }
        await using var read = database.Open();
        Assert.Empty(await read.Users.ToListAsync());
        Assert.Empty(await read.Accounts.ToListAsync());
    }

    private static async Task<Exception?> Commit(OutfitlyDbContext context)
    {
        try { await context.SaveChangesAsync(); return null; }
        catch (AccountConflictException error) { return error; }
    }
}
