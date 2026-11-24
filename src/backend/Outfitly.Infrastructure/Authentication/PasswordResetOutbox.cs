using Microsoft.EntityFrameworkCore;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Infrastructure.Authentication;

internal sealed class PasswordResetDelivery
{
    public string TokenHash { get; init; } = null!;
    public string ProtectedToken { get; init; } = null!;
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime DueAtUtc { get; init; }
}

public sealed record PendingPasswordReset(string TokenHash, string Email, string ProtectedToken, DateTime DueAtUtc);

public sealed class PasswordResetOutbox(OutfitlyDbContext db)
{
    public void Stage(string hash, string protectedToken, DateTime now, DateTime expires) =>
        db.Add(new PasswordResetDelivery { TokenHash = hash, ProtectedToken = protectedToken, DueAtUtc = now, ExpiresAtUtc = expires });

    private IQueryable<PasswordResetDelivery> Deliveries => db.Set<PasswordResetDelivery>();

    public Task<int> RemoveExpiredAsync(DateTime now, CancellationToken ct) => Deliveries
        .Where(mail => mail.ExpiresAtUtc <= now)
        .ExecuteDeleteAsync(ct);

    public Task<PendingPasswordReset?> NextAsync(DateTime now, CancellationToken ct) =>
        (from mail in Deliveries.AsNoTracking()
         join account in db.Accounts.AsNoTracking() on mail.TokenHash equals account.PasswordResetTokenHash
         where mail.DueAtUtc <= now && mail.ExpiresAtUtc > now
         orderby mail.DueAtUtc, mail.TokenHash
         select new PendingPasswordReset(mail.TokenHash, account.Email, mail.ProtectedToken, mail.DueAtUtc))
        .FirstOrDefaultAsync(ct);

    public Task<int> DeferAsync(PendingPasswordReset mail, DateTime next, CancellationToken ct) => Deliveries
        .Where(item => item.TokenHash == mail.TokenHash && item.DueAtUtc == mail.DueAtUtc)
        .ExecuteUpdateAsync(update => update.SetProperty(item => item.DueAtUtc, next), ct);

    public Task<int> CompleteAsync(string hash, CancellationToken ct) => Deliveries
        .Where(mail => mail.TokenHash == hash).ExecuteDeleteAsync(ct);
}
