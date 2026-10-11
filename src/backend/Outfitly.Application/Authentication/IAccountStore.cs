using Outfitly.Domain;

namespace Outfitly.Application.Authentication;

public interface IAccountStore
{
    Task<Account?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<AccountView?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<LoginSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);
    void Add(User user, Account account);
    void Add(LoginSession session);
}

public enum PasswordCheck { Failed, Success, RehashNeeded }

public interface IAccountPasswordHasher
{
    string Hash(string password);
    PasswordCheck Verify(string? hash, string password);
}
