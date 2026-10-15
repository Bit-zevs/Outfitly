using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Authentication;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Tests.Infrastructure;

public sealed class EmailConfirmationPersistenceTests
{
    internal static AccountService Service(OutfitlyDbContext context, TimeProvider? clock = null) =>
        new(new EfAccountStore(context), new AccountPasswordHasher(), clock ?? TimeProvider.System);

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Stale_confirmation_or_resend_cannot_overwrite_a_committed_command(bool firstResend, bool secondResend)
    {
        await using var database = await TestDatabase.CreateAsync();
        await VerifyRace(database.Open, firstResend, secondResend);
    }

    internal static async Task VerifyRace(Func<OutfitlyDbContext> open, bool firstResend, bool secondResend)
    {
        var clock = new Clock();
        string token;
        await using (var register = open())
        {
            token = (await Service(register, clock).RegisterAsync("race@example.com", "a race test password", "Race")).Confirmation.Token;
            await register.SaveChangesAsync();
        }
        clock.Now = clock.Now.AddSeconds(60);
        await using var first = open();
        await using var second = open();
        var firstService = Service(first, clock);
        var secondService = Service(second, clock);
        ConfirmationEmail? replacement = null;
        if (firstResend) replacement = await firstService.ResendConfirmationAsync("race@example.com");
        else await firstService.ConfirmEmailAsync(token);
        if (secondResend) await secondService.ResendConfirmationAsync("race@example.com");
        else await secondService.ConfirmEmailAsync(token);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<WardrobeConcurrencyException>(() => second.SaveChangesAsync());
        await using var read = open();
        Assert.Equal(!firstResend, (await read.Accounts.SingleAsync()).EmailConfirmed);
        if (firstResend)
        {
            await Assert.ThrowsAsync<InvalidConfirmationTokenException>(() => Service(read, clock).ConfirmEmailAsync(token));
            await Service(read, clock).ConfirmEmailAsync(replacement!.Token);
            await read.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Token_expires_at_exactly_twenty_four_hours_and_unconfirmed_accounts_have_no_valid_session()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new Clock();
        RegisteredAccount account;
        await using (var register = database.Open())
        {
            account = await Service(register, clock).RegisterAsync("expiry@example.com", "an expiry test password", "Expiry");
            await register.SaveChangesAsync();
        }
        clock.Now = clock.Now.AddHours(24);
        await using (var expired = database.Open())
        {
            await Assert.ThrowsAsync<InvalidConfirmationTokenException>(() => Service(expired, clock).ConfirmEmailAsync(account.Confirmation.Token));
            var replacement = await Service(expired, clock).ResendConfirmationAsync(account.Email);
            await expired.SaveChangesAsync();
            await Service(expired, clock).ConfirmEmailAsync(replacement!.Token);
            await expired.SaveChangesAsync();
            var login = await Service(expired, clock).LoginAsync(account.Email, "an expiry test password");
            await expired.SaveChangesAsync();
            Assert.NotNull(await Service(expired, clock).ValidateSessionAsync(account.Id, login!.Session.Id));
            (await expired.Accounts.SingleAsync()).EmailConfirmed = false;
            await expired.SaveChangesAsync();
        }
        await using var read = database.Open();
        var session = await read.LoginSessions.SingleAsync();
        Assert.Null(await Service(read, clock).ValidateSessionAsync(account.Id, session.Id));
    }
}
