using Microsoft.EntityFrameworkCore;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Infrastructure.Authentication;

internal sealed class AccountMailDelivery
{
    public string TokenHash { get; init; } = null!;
    public string ProtectedToken { get; init; } = null!;
    public AccountEmailPurpose Purpose { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime DueAtUtc { get; init; }
}

public sealed record PendingAccountMail(string TokenHash, string Email, string ProtectedToken, DateTime DueAtUtc, AccountEmailPurpose Purpose);

public sealed class AccountMailOutbox(OutfitlyDbContext db)
{
    public void Stage(string hash, string protectedToken, DateTime now, DateTime expires,
        AccountEmailPurpose purpose = AccountEmailPurpose.PasswordReset) =>
        db.Add(new AccountMailDelivery
        {
            TokenHash = hash, ProtectedToken = protectedToken, DueAtUtc = now, ExpiresAtUtc = expires, Purpose = purpose
        });

    private IQueryable<AccountMailDelivery> Deliveries => db.Set<AccountMailDelivery>();

    public Task<int> RemoveExpiredAsync(DateTime now, CancellationToken ct) => Deliveries
        .Where(mail => mail.ExpiresAtUtc <= now).ExecuteDeleteAsync(ct);

    public async Task PruneCredentialsAsync(DateTime now, CancellationToken ct)
    {
        const int batchSize = 1000;
        var sessions = db.LoginSessions.Where(session => session.ExpiresAtUtc <= now);
        var expiredSessions = sessions.OrderBy(session => session.ExpiresAtUtc).ThenBy(session => session.Id)
            .Take(batchSize).Select(session => session.Id);
        await sessions.Where(session => expiredSessions.Contains(session.Id)).ExecuteDeleteAsync(ct);

        var tokens = db.Set<PreviousConfirmationToken>().Where(token => token.ExpiresAtUtc <= now);
        var expiredTokens = tokens.OrderBy(token => token.ExpiresAtUtc).ThenBy(token => token.Hash)
            .Take(batchSize).Select(token => token.Hash);
        await tokens.Where(token => expiredTokens.Contains(token.Hash)).ExecuteDeleteAsync(ct);
    }

    public Task<PendingAccountMail?> NextAsync(DateTime now, CancellationToken ct)
    {
        var due = Deliveries.AsNoTracking().Where(mail => mail.DueAtUtc <= now && mail.ExpiresAtUtc > now);
        var accounts = db.Accounts.AsNoTracking();
        var resets = from mail in due
                     join account in accounts on mail.TokenHash equals account.PasswordResetTokenHash
                     where mail.Purpose == AccountEmailPurpose.PasswordReset && account.EmailConfirmed
                     select new { Mail = mail, account.Email };
        var changes = from mail in due
                      join account in accounts on mail.TokenHash equals account.EmailChangeTokenHash
                      where mail.Purpose == AccountEmailPurpose.EmailChange && account.EmailConfirmed && account.PendingEmail != null
                      select new { Mail = mail, Email = account.PendingEmail! };
        var confirmations = from mail in due
                            join account in accounts on mail.TokenHash equals account.ConfirmationTokenHash
                            where mail.Purpose == AccountEmailPurpose.Confirmation && !account.EmailConfirmed
                            select new { Mail = mail, account.Email };
        var previous = from mail in due
                       join token in db.Set<PreviousConfirmationToken>() on mail.TokenHash equals token.Hash
                       join account in accounts on token.UserId equals account.UserId
                       where mail.Purpose == AccountEmailPurpose.Confirmation && !account.EmailConfirmed && token.ExpiresAtUtc > now
                       select new { Mail = mail, account.Email };
        return resets.Concat(changes).Concat(confirmations).Concat(previous)
            .OrderBy(row => row.Mail.DueAtUtc).ThenBy(row => row.Mail.TokenHash)
            .Select(row => new PendingAccountMail(row.Mail.TokenHash, row.Email, row.Mail.ProtectedToken, row.Mail.DueAtUtc, row.Mail.Purpose))
            .FirstOrDefaultAsync(ct);
    }

    public Task<int> DeferAsync(PendingAccountMail mail, DateTime next, CancellationToken ct) => Deliveries
        .Where(item => item.TokenHash == mail.TokenHash && item.DueAtUtc == mail.DueAtUtc)
        .ExecuteUpdateAsync(update => update.SetProperty(item => item.DueAtUtc, next), ct);

    public Task<int> CompleteAsync(string hash, CancellationToken ct) => Deliveries
        .Where(mail => mail.TokenHash == hash).ExecuteDeleteAsync(ct);
}
