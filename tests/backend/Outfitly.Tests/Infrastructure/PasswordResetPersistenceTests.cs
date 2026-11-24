using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Authentication;
using Outfitly.Infrastructure.Persistence;
using static Outfitly.Tests.Infrastructure.EmailConfirmationPersistenceTests;

namespace Outfitly.Tests.Infrastructure;

public sealed class PasswordResetPersistenceTests
{
    private const string Email = "reset-race@example.com";
    private const string Password = "original password";

    private static async Task<string> Seed(Func<OutfitlyDbContext> open, Clock clock)
    {
        await using var db = open();
        var service = Service(db, clock);
        var registered = await service.RegisterAsync(Email, "Reset");
        await db.SaveChangesAsync();
        await service.ConfirmEmailAsync(registered.Confirmation.Token, Password, "reset");
        await db.SaveChangesAsync();
        var email = await service.RequestPasswordResetAsync(Email);
        await db.SaveChangesAsync();
        return email!.Token;
    }

    [PostgreSqlFact]
    public async Task Delivery_is_visible_after_commit_and_can_be_reclaimed_when_retry_is_due()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var clock = new Clock();
        await using (var seed = database.Open())
        {
            var service = Service(seed, clock);
            var registered = await service.RegisterAsync(Email, "Reset");
            await seed.SaveChangesAsync();
            await service.ConfirmEmailAsync(registered.Confirmation.Token, Password, "reset");
            await seed.SaveChangesAsync();
        }
        await using (var write = database.Open())
        {
            var reset = (await Service(write, clock).RequestPasswordResetAsync(Email))!;
            new AccountMailOutbox(write).Stage(reset.TokenHash, "protected fixture", reset.IssuedAtUtc, reset.ExpiresAtUtc);
            await write.SaveChangesAsync();
        }
        var now = clock.GetUtcNow().UtcDateTime;
        PendingAccountMail pending;
        await using (var first = database.Open())
        {
            var outbox = new AccountMailOutbox(first);
            pending = Assert.IsType<PendingAccountMail>(await outbox.NextAsync(now, default));
            Assert.Equal(1, await outbox.DeferAsync(pending, now.AddSeconds(30), default));
        }
        await using var next = database.Open();
        var reopened = new AccountMailOutbox(next);
        Assert.Equal(0, await reopened.DeferAsync(pending, now.AddSeconds(30), default));
        Assert.Null(await reopened.NextAsync(now, default));
        var retry = Assert.IsType<PendingAccountMail>(await reopened.NextAsync(now.AddSeconds(30), default));
        Assert.Equal(pending.TokenHash, retry.TokenHash);
        Assert.Equal(pending.ProtectedToken, retry.ProtectedToken);
        Assert.Equal(1, await reopened.CompleteAsync(retry.TokenHash, default));
        Assert.Null(await reopened.NextAsync(now.AddSeconds(31), default));
    }

    [Fact]
    public async Task Credential_cleanup_removes_only_expired_sessions_and_previous_tokens()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new Clock();
        var now = clock.Now.UtcDateTime;
        await Seed(database.Open, clock);
        Guid currentSession;
        await using (var seed = database.Open())
        {
            var actor = (await seed.Accounts.AsNoTracking().SingleAsync()).UserId;
            currentSession = Guid.NewGuid();
            seed.LoginSessions.AddRange(
                new LoginSession { Id = Guid.NewGuid(), UserId = actor, ExpiresAtUtc = now },
                new LoginSession { Id = currentSession, UserId = actor, ExpiresAtUtc = now.AddMinutes(1) });
            seed.AddRange(
                new PreviousConfirmationToken(new string('A', 64), actor, now),
                new PreviousConfirmationToken(new string('B', 64), actor, now.AddMinutes(1)));
            await seed.SaveChangesAsync();
        }

        await using (var maintenance = database.Open())
            await new AccountMailOutbox(maintenance).PruneCredentialsAsync(now, default);

        await using var read = database.Open();
        Assert.Equal(currentSession, (await read.LoginSessions.SingleAsync()).Id);
        Assert.Equal(new string('B', 64), (await read.Set<PreviousConfirmationToken>().SingleAsync()).Hash);
    }

    [Fact]
    public async Task Repeated_request_preserves_original_link_until_it_expires()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new Clock();
        var original = await Seed(database.Open, clock);
        clock.Now = clock.Now.AddMinutes(1);
        await using (var check = database.Open())
        {
            var service = Service(check, clock);
            Assert.Null(await service.RequestPasswordResetAsync(Email));
            await service.ResetPasswordAsync(original, "new password");
        }
        clock.Now = clock.Now.AddMinutes(59);
        await using (var issue = database.Open())
        {
            var service = Service(issue, clock);
            await Assert.ThrowsAsync<InvalidPasswordResetTokenException>(() => service.ResetPasswordAsync(original, "new password"));
            var replacement = await service.RequestPasswordResetAsync(Email);
            Assert.NotNull(replacement);
            Assert.NotEqual(original, replacement.Token);
            await issue.SaveChangesAsync();
            await service.ResetPasswordAsync(replacement.Token, "new password");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_reset_or_login_cannot_commit_after_reset(bool login)
    {
        await using var database = await TestDatabase.CreateAsync();
        await VerifyRace(database.Open, login);
    }

    internal static async Task VerifyRace(Func<OutfitlyDbContext> open, bool login)
    {
        var clock = new Clock();
        var token = await Seed(open, clock);
        await using var first = open();
        await using var second = open();
        await Service(first, clock).ResetPasswordAsync(token, "winning password");
        if (login) Assert.NotNull(await Service(second, clock).LoginAsync(Email, Password));
        else await Service(second, clock).ResetPasswordAsync(token, "losing password");
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => second.SaveChangesAsync());
        await using var read = open();
        Assert.Null((await read.Accounts.SingleAsync()).PasswordResetTokenHash);
        Assert.Empty(await read.LoginSessions.ToListAsync());
        Assert.Null(await Service(read, clock).LoginAsync(Email, Password));
        Assert.NotNull(await Service(read, clock).LoginAsync(Email, "winning password"));
    }

    [PostgreSqlFact]
    public async Task PostgreSQL_applies_reset_migration_and_rejects_stale_commands()
    {
        foreach (var login in new[] { false, true })
        {
            await using var database = await PostgreSqlDatabase.CreateAsync();
            await VerifyRace(database.Open, login);
        }
    }

    [Fact]
    public async Task Session_committed_after_reset_reads_sessions_is_still_invalidated()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new Clock();
        var token = await Seed(database.Open, clock);
        await using var reset = database.Open();
        await using var login = database.Open();
        await Service(reset, clock).ResetPasswordAsync(token, "replacement password");
        var result = await Service(login, clock).LoginAsync(Email, Password);
        await login.SaveChangesAsync();
        await reset.SaveChangesAsync();
        await using var read = database.Open();
        Assert.Null(await Service(read, clock).ValidateSessionAsync(result!.User.Id, result.Session.Id));
    }
}
