using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Outfitly.Domain;

namespace Outfitly.Application.Authentication;

// Prepares changes only; the HTTP boundary commits before issuing a cookie.
public sealed class AccountService(IAccountStore store, IAccountPasswordHasher passwords, TimeProvider clock)
{
    public async Task<RegisteredAccount> RegisterAsync(string email, string password, string displayName, string username,
        CancellationToken cancellationToken = default)
    {
        var address = Email(email);
        if (password is null || password.Length is < 8 or > 128)
            throw new ArgumentException("Password must contain between 8 and 128 characters.");
        var user = new User(Guid.NewGuid(), displayName, Usernames.Normalize(username));
        if (await store.UsernameExistsAsync(user.Username, user.Id, cancellationToken))
            throw new UsernameConflictException();
        if (await store.FindByEmailAsync(address.ToUpperInvariant(), cancellationToken) is not null)
            throw new AccountConflictException();
        var account = new Account
        {
            UserId = user.Id, Email = address, NormalizedEmail = address.ToUpperInvariant(),
            PasswordHash = passwords.Hash(password)
        };
        var confirmation = IssueConfirmation(account);
        store.Add(user, account);
        return new RegisteredAccount(user.Id, address, user.DisplayName, user.Username, confirmation);
    }

    public async Task<ConfirmationEmail?> ResendConfirmationAsync(string email, CancellationToken cancellationToken = default)
    {
        var account = await store.FindByEmailAsync(Email(email).ToUpperInvariant(), cancellationToken);
        if (account is null || account.EmailConfirmed) return null;
        if (account.ConfirmationIssuedAtUtc is { } issued && issued.AddSeconds(60) > clock.GetUtcNow().UtcDateTime)
            throw new ConfirmationTooFrequentException();
        return IssueConfirmation(account);
    }

    public async Task ConfirmEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidConfirmationTokenException();
        var account = await store.FindByConfirmationHashAsync(TokenHash(token), cancellationToken);
        if (account is null || account.EmailConfirmed || account.ConfirmationExpiresAtUtc is not { } expires ||
            expires <= clock.GetUtcNow().UtcDateTime) throw new InvalidConfirmationTokenException();
        account.EmailConfirmed = true;
        account.ConfirmationTokenHash = null;
        account.ConfirmationExpiresAtUtc = null;
    }

    private ConfirmationEmail IssueConfirmation(Account account)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        account.ConfirmationTokenHash = TokenHash(token);
        account.ConfirmationIssuedAtUtc = clock.GetUtcNow().UtcDateTime;
        account.ConfirmationExpiresAtUtc = account.ConfirmationIssuedAtUtc.Value.AddHours(24);
        return new ConfirmationEmail(account.Email, token);
    }

    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<ConfirmationEmail?> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var account = await store.FindByEmailAsync(Email(email).ToUpperInvariant(), cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (account is null || !account.EmailConfirmed ||
            account.PasswordResetIssuedAtUtc is { } issued && issued.AddMinutes(1) > now) return null;
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        account.PasswordResetTokenHash = TokenHash(token);
        account.PasswordResetIssuedAtUtc = now;
        account.PasswordResetExpiresAtUtc = now.AddHours(1);
        return new ConfirmationEmail(account.Email, token, AccountEmailPurpose.PasswordReset);
    }

    public async Task ResetPasswordAsync(string token, string password, CancellationToken cancellationToken = default)
    {
        if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidPasswordResetTokenException();
        if (password is null || password.Length is < 8 or > 128)
            throw new ArgumentException("Password must contain between 8 and 128 characters.");
        var account = await store.FindByPasswordResetHashAsync(TokenHash(token), cancellationToken);
        if (account is null || !account.EmailConfirmed || account.PasswordResetExpiresAtUtc is not { } expires ||
            expires <= clock.GetUtcNow().UtcDateTime) throw new InvalidPasswordResetTokenException();
        account.PasswordHash = passwords.Hash(password);
        account.SessionVersion = Guid.NewGuid();
        account.PasswordResetTokenHash = null;
        account.PasswordResetExpiresAtUtc = null;
        await store.RevokeSessionsAsync(account.UserId, cancellationToken);
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
        if (!account.EmailConfirmed) throw new EmailNotConfirmedException();
        var user = await store.GetUserAsync(account.UserId, cancellationToken);
        if (user is null) return null;
        if (verification == PasswordCheck.RehashNeeded) account.PasswordHash = passwords.Hash(password);
        // Include the account in the commit so a concurrent reset cannot create a session using the old password.
        store.MarkCredentialsChecked(account);
        var session = new LoginSession
        {
            Id = Guid.NewGuid(), UserId = user.Id, ExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddHours(8),
            SessionVersion = account.SessionVersion
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

    public async Task<AccountView> ChangeUsernameAsync(Guid actorId, string username, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) throw new UnauthorizedAccessException();
        var user = await store.FindUserAsync(actorId, cancellationToken) ?? throw new KeyNotFoundException();
        var normalized = Usernames.Normalize(username);
        if (await store.UsernameExistsAsync(normalized, actorId, cancellationToken)) throw new UsernameConflictException();
        user.ChangeUsername(normalized);
        var account = await store.GetUserAsync(actorId, cancellationToken) ?? throw new KeyNotFoundException();
        return account with { Username = normalized };
    }

    private static string Email(string email)
    {
        var value = email?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 254 ||
            !MailAddress.TryCreate(value, out var parsed) || parsed.Address != value)
            throw new ArgumentException("A valid email address is required.");
        return value;
    }
}
