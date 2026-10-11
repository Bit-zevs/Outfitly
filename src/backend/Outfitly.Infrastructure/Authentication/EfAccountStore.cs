using Microsoft.EntityFrameworkCore;
using Outfitly.Application.Authentication;
using Outfitly.Domain;
using Outfitly.Infrastructure.Persistence;

namespace Outfitly.Infrastructure.Authentication;

public sealed class EfAccountStore(OutfitlyDbContext context) : IAccountStore
{
    public Task<Account?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Accounts.SingleOrDefaultAsync(account => account.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<AccountView?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (from account in context.Accounts
         join user in context.Users on account.UserId equals user.Id
         where user.Id == userId
         select new AccountView(user.Id, account.Email, user.DisplayName)).SingleOrDefaultAsync(cancellationToken);

    public Task<LoginSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        context.LoginSessions.SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public void Add(User user, Account account)
    {
        context.Users.Add(user);
        context.Accounts.Add(account);
    }

    public void Add(LoginSession session) => context.LoginSessions.Add(session);
}
