using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Persistence;
using static Outfitly.Tests.Infrastructure.EmailConfirmationPersistenceTests;

namespace Outfitly.Tests.Infrastructure;

public sealed class AccountSettingsPersistenceTests
{
    internal const string OldEmail = "settings@example.com";
    internal const string Password = "original settings password";
    internal const string NewEmail = "replacement@example.com";

    internal static async Task<Guid> Seed(Func<OutfitlyDbContext> open, TimeProvider? clock = null)
    {
        await using var db = open();
        var service = Service(db, clock);
        var registered = await service.RegisterAsync(OldEmail, Password, "Original", "original");
        await db.SaveChangesAsync();
        await service.ConfirmEmailAsync(registered.Confirmation.Token);
        await db.SaveChangesAsync();
        await service.LoginAsync(OldEmail, Password);
        await db.SaveChangesAsync();
        await service.LoginAsync(OldEmail, Password);
        await db.SaveChangesAsync();
        return registered.Id;
    }

    [Fact]
    public async Task Email_change_has_independent_tokens_cooldown_expiry_replacement_and_cancellation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new Clock();
        var id = await Seed(database.Open, clock);
        string original;
        await using (var request = database.Open())
        {
            original = (await Service(request, clock).RequestEmailChangeAsync(id, " Replacement@example.com ", Password)).Token;
            await request.SaveChangesAsync();
        }
        await using (var read = database.Open())
        {
            var account = await read.Accounts.SingleAsync();
            Assert.Equal(OldEmail, account.Email);
            Assert.True(account.EmailConfirmed);
            Assert.Equal("Replacement@example.com", (await Service(read, clock).GetUserAsync(id))!.PendingEmail);
            Assert.NotEqual(original, account.EmailChangeTokenHash);
            Assert.Equal(64, account.EmailChangeTokenHash!.Length);
            Assert.DoesNotContain(await read.LoginSessions.ToArrayAsync(), session => session.IsRevoked);
            await Assert.ThrowsAsync<ConfirmationTooFrequentException>(() => Service(read, clock).RequestEmailChangeAsync(id, NewEmail, Password));
            await Assert.ThrowsAsync<InvalidConfirmationTokenException>(() => Service(read, clock).ConfirmEmailAsync(original));
            await Assert.ThrowsAsync<InvalidPasswordResetTokenException>(() => Service(read, clock).ResetPasswordAsync(original, "replacement password"));
        }
        clock.Now = clock.Now.AddSeconds(60);
        string replacement;
        await using (var request = database.Open())
        {
            replacement = (await Service(request, clock).RequestEmailChangeAsync(id, NewEmail, Password)).Token;
            await request.SaveChangesAsync();
        }
        await using (var check = database.Open())
            await Assert.ThrowsAsync<InvalidEmailChangeTokenException>(() => Service(check, clock).ConfirmEmailChangeAsync(original));
        clock.Now = clock.Now.AddHours(24);
        await using (var expired = database.Open())
        {
            Assert.Null((await Service(expired, clock).GetUserAsync(id))!.PendingEmail);
            await Assert.ThrowsAsync<InvalidEmailChangeTokenException>(() => Service(expired, clock).ConfirmEmailChangeAsync(replacement));
            var next = await Service(expired, clock).RequestEmailChangeAsync(id, NewEmail, Password);
            await expired.SaveChangesAsync();
            replacement = next.Token;
        }
        await using (var cancel = database.Open())
        {
            await Service(cancel, clock).CancelEmailChangeAsync(id);
            await cancel.SaveChangesAsync();
        }
        await using var verify = database.Open();
        Assert.Null((await Service(verify, clock).GetUserAsync(id))!.PendingEmail);
        await Assert.ThrowsAsync<InvalidEmailChangeTokenException>(() => Service(verify, clock).ConfirmEmailChangeAsync(replacement));
        await Assert.ThrowsAsync<ConfirmationTooFrequentException>(() => Service(verify, clock).RequestEmailChangeAsync(id, NewEmail, Password));
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task Password_change_validates_length_and_only_success_revokes_sessions_and_tokens(int length, bool accepted)
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = await Seed(database.Open);
        string reset, emailToken;
        await using (var issue = database.Open())
        {
            reset = (await Service(issue).RequestPasswordResetAsync(OldEmail))!.Token;
            emailToken = (await Service(issue).RequestEmailChangeAsync(id, NewEmail, Password)).Token;
            await issue.SaveChangesAsync();
        }
        var nextPassword = new string('p', length);
        await using (var change = database.Open())
        {
            if (accepted)
            {
                await Service(change).ChangePasswordAsync(id, Password, nextPassword);
                await change.SaveChangesAsync();
            }
            else await Assert.ThrowsAsync<ArgumentException>(() => Service(change).ChangePasswordAsync(id, Password, nextPassword));
        }
        await using var read = database.Open();
        var account = await read.Accounts.SingleAsync();
        Assert.Equal(accepted, (await read.LoginSessions.ToArrayAsync()).All(session => session.IsRevoked));
        Assert.Equal(accepted, account.PasswordResetTokenHash is null);
        Assert.Equal(accepted, account.EmailChangeTokenHash is null);
        Assert.Equal(accepted, account.PendingEmail is null);
        Assert.NotNull(await Service(read).LoginAsync(OldEmail, accepted ? nextPassword : Password));
        if (accepted)
        {
            await Assert.ThrowsAsync<InvalidPasswordResetTokenException>(() => Service(read).ResetPasswordAsync(reset, "unused password"));
            await Assert.ThrowsAsync<InvalidEmailChangeTokenException>(() => Service(read).ConfirmEmailChangeAsync(emailToken));
        }
    }

    [Fact]
    public async Task Password_recovery_also_invalidates_a_pending_email_change()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = await Seed(database.Open);
        string pending;
        await using (var issue = database.Open())
        {
            pending = (await Service(issue).RequestEmailChangeAsync(id, NewEmail, Password)).Token;
            var reset = (await Service(issue).RequestPasswordResetAsync(OldEmail))!.Token;
            await issue.SaveChangesAsync();
            await Service(issue).ResetPasswordAsync(reset, "recovered password");
            await issue.SaveChangesAsync();
        }
        await using var read = database.Open();
        Assert.Null((await read.Accounts.SingleAsync()).PendingEmail);
        await Assert.ThrowsAsync<InvalidEmailChangeTokenException>(() => Service(read).ConfirmEmailChangeAsync(pending));
    }

    [Fact]
    public async Task Wrong_password_and_profile_conflict_do_not_change_saved_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = await Seed(database.Open);
        await using (var seed = database.Open())
        {
            await Service(seed).RegisterAsync("other@example.com", Password, "Other", "occupied");
            await seed.SaveChangesAsync();
        }
        await using (var change = database.Open())
        {
            await Assert.ThrowsAsync<InvalidCurrentPasswordException>(() => Service(change).ChangePasswordAsync(id, "wrong", "new password"));
            await Assert.ThrowsAsync<InvalidCurrentPasswordException>(() => Service(change).RequestEmailChangeAsync(id, NewEmail, "wrong"));
            await Assert.ThrowsAsync<UsernameConflictException>(() => Service(change).ChangeProfileAsync(id, "Changed", "occupied"));
            await Assert.ThrowsAsync<ArgumentException>(() => Service(change).ChangeProfileAsync(id, "  ", "available"));
        }
        await using var read = database.Open();
        var user = await read.Users.SingleAsync(user => user.Id == id);
        Assert.Equal("Original", user.DisplayName);
        Assert.Equal("original", user.Username);
        Assert.Null((await read.Accounts.SingleAsync(account => account.UserId == id)).PendingEmail);
        Assert.All(await read.LoginSessions.ToArrayAsync(), session => Assert.False(session.IsRevoked));
    }

    // Used against PostgreSQL as well as SQLite: each command reads its own snapshot.
    internal static async Task VerifyRace(Func<OutfitlyDbContext> open, string staleCommand, string winner)
    {
        var clock = new Clock();
        var id = await Seed(open, clock);
        string token;
        await using (var issue = open())
        {
            token = (await Service(issue, clock).RequestEmailChangeAsync(id, NewEmail, Password)).Token;
            await issue.SaveChangesAsync();
        }
        clock.Now = clock.Now.AddMinutes(1);
        await using (var stale = open())
        await using (var first = open())
        {
            await Prepare(Service(stale, clock), staleCommand, id, token);
            await Prepare(Service(first, clock), winner, id, token);
            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => stale.SaveChangesAsync());
        }
        await using var read = open();
        var account = await read.Accounts.SingleAsync();
        Assert.Equal(winner == "confirm" ? NewEmail : OldEmail, account.Email);
        Assert.Equal(winner is "confirm" or "password", (await read.LoginSessions.ToArrayAsync()).All(session => session.IsRevoked));
        if (winner is "confirm" or "password" or "cancel") Assert.Null(account.EmailChangeTokenHash);
        else Assert.NotNull(account.EmailChangeTokenHash);
    }

    private static async Task Prepare(AccountService service, string command, Guid id, string token)
    {
        switch (command)
        {
            case "confirm": await service.ConfirmEmailChangeAsync(token); break;
            case "request": await service.RequestEmailChangeAsync(id, "another@example.com", Password); break;
            case "cancel": await service.CancelEmailChangeAsync(id); break;
            case "password": await service.ChangePasswordAsync(id, Password, "new password"); break;
            case "login": await service.LoginAsync(OldEmail, Password); break;
            default: throw new ArgumentException(command);
        }
    }

    [Theory]
    [InlineData("confirm", "request")]
    [InlineData("request", "confirm")]
    [InlineData("confirm", "cancel")]
    [InlineData("cancel", "confirm")]
    [InlineData("confirm", "password")]
    [InlineData("password", "confirm")]
    [InlineData("login", "confirm")]
    [InlineData("login", "password")]
    public async Task Stale_commands_cannot_commit_over_credential_changes(string stale, string winner)
    {
        await using var database = await TestDatabase.CreateAsync();
        await VerifyRace(database.Open, stale, winner);
    }
}
