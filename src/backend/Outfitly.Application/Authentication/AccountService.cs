using System.Net.Mail;
using Outfitly.Domain;

namespace Outfitly.Application.Authentication;

// Prepares changes only; the HTTP boundary commits before issuing a cookie.
public sealed class AccountService(IAccountStore store, IAccountPasswordHasher passwords, TimeProvider clock)
{
    public async Task<AccountView> RegisterAsync(string email, string password, string displayName,
        CancellationToken cancellationToken = default)
    {
        var address = Email(email);
        if (password is null || password.Length is < 15 or > 128)
            throw new ArgumentException("Password must contain between 15 and 128 characters.");
        var user = new User(Guid.NewGuid(), displayName);
        if (await store.FindByEmailAsync(address.ToUpperInvariant(), cancellationToken) is not null)
            throw new AccountConflictException();
        store.Add(user, new Account
        {
            UserId = user.Id, Email = address, NormalizedEmail = address.ToUpperInvariant(),
            PasswordHash = passwords.Hash(password)
        });
        return new AccountView(user.Id, address, user.DisplayName);
    }

    public async Task<LoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var address = Email(email);
        if (password is null || password.Length > 128)
            return null;
        var account = await store.FindByEmailAsync(address.ToUpperInvariant(), cancellationToken);
        var verification = passwords.Verify(account?.PasswordHash, password);
        if (account is null || verification == PasswordCheck.Failed)
            return null;
        var user = await store.GetUserAsync(account.UserId, cancellationToken);
        if (user is null) return null;
        if (verification == PasswordCheck.RehashNeeded) account.PasswordHash = passwords.Hash(password);
        var session = new LoginSession
        {
            Id = Guid.NewGuid(), UserId = user.Id, ExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddHours(8)
        };
        store.Add(session);
        return new LoginResult(user, session);
    }

    public async Task<AccountView?> ValidateSessionAsync(Guid userId, Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await store.FindSessionAsync(sessionId, cancellationToken);
        return session is null || session.UserId != userId || session.IsRevoked ||
            session.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime
            ? null : await store.GetUserAsync(userId, cancellationToken);
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await store.FindSessionAsync(sessionId, cancellationToken);
        if (session is not null && session.UserId == userId) session.IsRevoked = true;
    }

    public Task<AccountView?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        store.GetUserAsync(userId, cancellationToken);

    private static string Email(string email)
    {
        var value = email?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 254 ||
            !MailAddress.TryCreate(value, out var parsed) || parsed.Address != value)
            throw new ArgumentException("A valid email address is required.");
        return value;
    }
}
