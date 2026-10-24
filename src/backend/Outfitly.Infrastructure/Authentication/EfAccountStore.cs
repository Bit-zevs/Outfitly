using Microsoft.EntityFrameworkCore;
using Outfitly.Application.Authentication;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Infrastructure.Authentication;

public sealed class EfAccountStore(OutfitlyDbContext context) : IAccountStore
{
    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<bool> UsernameExistsAsync(string username, Guid exceptUserId, CancellationToken cancellationToken) =>
        context.Users.AnyAsync(user => user.Username == username && user.Id != exceptUserId, cancellationToken);
    public void MarkCredentialsChecked(Account account) => context.Entry(account).Property(a => a.PasswordHash).IsModified = true;
    public Task<Account?> FindByPasswordResetHashAsync(string hash, CancellationToken cancellationToken) =>
        context.Accounts.SingleOrDefaultAsync(account => account.PasswordResetTokenHash == hash, cancellationToken);

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var session in await context.LoginSessions.Where(s => s.UserId == userId && !s.IsRevoked).ToListAsync(cancellationToken))
            session.IsRevoked = true;
    }

    public Task<Account?> FindByConfirmationHashAsync(string hash, CancellationToken cancellationToken) =>
        context.Accounts.SingleOrDefaultAsync(account => account.ConfirmationTokenHash == hash, cancellationToken);

    public Task<Account?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Accounts.SingleOrDefaultAsync(account => account.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<AccountView?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (from account in context.Accounts
         join user in context.Users on account.UserId equals user.Id
         where user.Id == userId && account.EmailConfirmed
         select new AccountView(user.Id, account.Email, user.DisplayName, user.Username)).SingleOrDefaultAsync(cancellationToken);

    public Task<LoginSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        context.LoginSessions.SingleOrDefaultAsync(session => session.Id == sessionId &&
            context.Accounts.Any(account => account.UserId == session.UserId && account.SessionVersion == session.SessionVersion), cancellationToken);

    public void Add(User user, Account account)
    {
        context.Users.Add(user);
        context.Accounts.Add(account);
    }

    public void Add(LoginSession session) => context.LoginSessions.Add(session);
}
