using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Application.Authentication;
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
        var registered = await service.RegisterAsync(Email, Password, "Reset");
        await db.SaveChangesAsync();
        await service.ConfirmEmailAsync(registered.Confirmation.Token);
        await db.SaveChangesAsync();
        var email = await service.RequestPasswordResetAsync(Email);
        await db.SaveChangesAsync();
        return email!.Token;
    }

    [Fact]
    public async Task Replacement_invalidates_old_token_and_expiry_is_exactly_one_hour()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new Clock();
        var original = await Seed(database.Open, clock);
        clock.Now = clock.Now.AddMinutes(1);
        string replacement;
        await using (var issue = database.Open())
        {
            replacement = (await Service(issue, clock).RequestPasswordResetAsync(Email))!.Token;
            await issue.SaveChangesAsync();
        }
        await using (var check = database.Open())
        {
            await Assert.ThrowsAsync<InvalidPasswordResetTokenException>(() => Service(check, clock).ResetPasswordAsync(original, "new password"));
            await Assert.ThrowsAsync<InvalidConfirmationTokenException>(() => Service(check, clock).ConfirmEmailAsync(replacement));
        }
        clock.Now = clock.Now.AddHours(1);
        await using var expired = database.Open();
        await Assert.ThrowsAsync<InvalidPasswordResetTokenException>(() => Service(expired, clock).ResetPasswordAsync(replacement, "new password"));
        Assert.NotNull(await Service(expired, clock).LoginAsync(Email, Password));
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
