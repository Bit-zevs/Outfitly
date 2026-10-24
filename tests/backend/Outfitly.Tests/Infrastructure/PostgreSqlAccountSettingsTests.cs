using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Outfitly.Application.Authentication;
using static Outfitly.Tests.Infrastructure.EmailConfirmationPersistenceTests;
using static Outfitly.Tests.Infrastructure.AccountSettingsPersistenceTests;

namespace Outfitly.Tests.Infrastructure;

public sealed class PostgreSqlAccountSettingsTests
{
    [PostgreSqlFact]
    public async Task Migration_preserves_existing_confirmed_accounts_passwords_and_sessions()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var id = await Seed(database.Open);
        string hash;
        await using (var migrate = database.Open())
        {
            hash = (await migrate.Accounts.SingleAsync()).PasswordHash;
            var migrator = migrate.GetService<IMigrator>();
            await migrator.MigrateAsync("20261024192555_Usernames");
            await migrator.MigrateAsync();
        }
        await using var read = database.Open();
        var account = await read.Accounts.SingleAsync();
        Assert.Equal(OldEmail, account.Email); Assert.Equal(hash, account.PasswordHash); Assert.True(account.EmailConfirmed);
        Assert.Null(account.PendingEmail); Assert.Null(account.PendingNormalizedEmail); Assert.Null(account.EmailChangeTokenHash);
        Assert.Null(account.EmailChangeIssuedAtUtc); Assert.Null(account.EmailChangeExpiresAtUtc);
        var sessions = await read.LoginSessions.ToArrayAsync();
        Assert.Equal(2, sessions.Length);
        foreach (var session in sessions) Assert.NotNull(await Service(read).ValidateSessionAsync(id, session.Id));
        Assert.False(read.Database.HasPendingModelChanges());
        Assert.Empty(await read.Database.GetPendingMigrationsAsync());
    }

    [PostgreSqlFact]
    public async Task Concurrent_confirmations_of_the_same_email_commit_one_and_rollback_the_loser_completely()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var firstId = await Seed(database.Open);
        Guid secondId;
        string firstToken, secondToken;
        await using (var seed = database.Open())
        {
            var second = await Service(seed).RegisterAsync("other@example.com", Password, "Other", "other");
            await seed.SaveChangesAsync();
            await Service(seed).ConfirmEmailAsync(second.Confirmation.Token);
            await seed.SaveChangesAsync();
            secondId = second.Id;
            await Service(seed).LoginAsync(second.Email, Password);
            await seed.SaveChangesAsync();
            firstToken = (await Service(seed).RequestEmailChangeAsync(firstId, NewEmail, Password)).Token;
            secondToken = (await Service(seed).RequestEmailChangeAsync(secondId, NewEmail, Password)).Token;
            await seed.SaveChangesAsync();
        }
        await using (var first = database.Open())
        await using (var second = database.Open())
        {
            await Service(first).ConfirmEmailChangeAsync(firstToken);
            await Service(second).ConfirmEmailChangeAsync(secondToken);
            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<AccountConflictException>(() => second.SaveChangesAsync());
        }
        await using var read = database.Open();
        var winner = await read.Accounts.SingleAsync(account => account.UserId == firstId);
        var loser = await read.Accounts.SingleAsync(account => account.UserId == secondId);
        Assert.Equal(NewEmail, winner.Email); Assert.Null(winner.PendingEmail);
        Assert.Equal("other@example.com", loser.Email); Assert.Equal(NewEmail, loser.PendingEmail);
        Assert.NotNull(loser.EmailChangeTokenHash);
        Assert.All(await read.LoginSessions.Where(session => session.UserId == firstId).ToArrayAsync(), session => Assert.True(session.IsRevoked));
        Assert.All(await read.LoginSessions.Where(session => session.UserId == secondId).ToArrayAsync(), session => Assert.False(session.IsRevoked));
    }

    [PostgreSqlFact]
    public async Task PostgreSQL_rejects_stale_email_password_and_login_commands()
    {
        foreach (var (stale, winner) in new[] { ("confirm", "request"), ("request", "confirm"), ("confirm", "cancel"),
            ("cancel", "confirm"), ("confirm", "password"), ("password", "confirm"), ("login", "confirm"), ("login", "password") })
        {
            await using var database = await PostgreSqlDatabase.CreateAsync();
            await VerifyRace(database.Open, stale, winner);
        }
    }
}
