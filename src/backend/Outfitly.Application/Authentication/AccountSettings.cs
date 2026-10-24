using System.Security.Cryptography;
using Outfitly.Domain;

namespace Outfitly.Application.Authentication;

public sealed partial class AccountService
{
    public async Task<AccountView> ChangeProfileAsync(Guid actorId, string displayName, string username,
        CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty) throw new UnauthorizedAccessException();
        // Validate both fields and uniqueness before changing the tracked user.
        var validated = new User(actorId, displayName, Usernames.Normalize(username));
        var user = await store.FindUserAsync(actorId, cancellationToken) ?? throw new KeyNotFoundException();
        var account = await GetUserAsync(actorId, cancellationToken) ?? throw new KeyNotFoundException();
        if (await store.UsernameExistsAsync(validated.Username, actorId, cancellationToken)) throw new UsernameConflictException();
        user.Rename(validated.DisplayName);
        user.ChangeUsername(validated.Username);
        return account with { DisplayName = user.DisplayName, Username = user.Username };
    }

    public async Task ChangePasswordAsync(Guid actorId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        var account = await RequirePasswordAsync(actorId, currentPassword, cancellationToken);
        if (newPassword is null || newPassword.Length is < 8 or > 128)
            throw new ArgumentException("Password must contain between 8 and 128 characters.");
        account.PasswordHash = passwords.Hash(newPassword);
        await RevokeCredentialsAsync(account, cancellationToken);
    }

    public async Task<ConfirmationEmail> RequestEmailChangeAsync(Guid actorId, string email, string currentPassword,
        CancellationToken cancellationToken = default)
    {
        var account = await RequirePasswordAsync(actorId, currentPassword, cancellationToken);
        var address = Email(email);
        var normalized = address.ToUpperInvariant();
        if (normalized == account.NormalizedEmail) throw new ArgumentException("Choose a different email address.");
        if (await store.FindByEmailAsync(normalized, cancellationToken) is not null) throw new AccountConflictException();
        var now = clock.GetUtcNow().UtcDateTime;
        if (account.EmailChangeIssuedAtUtc is { } issued && issued.AddSeconds(60) > now)
            throw new ConfirmationTooFrequentException();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        account.PendingEmail = address;
        account.PendingNormalizedEmail = normalized;
        account.EmailChangeTokenHash = TokenHash(token);
        account.EmailChangeIssuedAtUtc = now;
        account.EmailChangeExpiresAtUtc = now.AddHours(24);
        return new ConfirmationEmail(address, token, AccountEmailPurpose.EmailChange);
    }

    public async Task<Guid> ConfirmEmailChangeAsync(string token, CancellationToken cancellationToken = default)
    {
        if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidEmailChangeTokenException();
        var account = await store.FindByEmailChangeHashAsync(TokenHash(token), cancellationToken);
        if (account is null || !account.EmailConfirmed || account.PendingEmail is null || account.PendingNormalizedEmail is null ||
            account.EmailChangeExpiresAtUtc is not { } expires || expires <= clock.GetUtcNow().UtcDateTime)
            throw new InvalidEmailChangeTokenException();
        if (await store.FindByEmailAsync(account.PendingNormalizedEmail, cancellationToken) is not null)
            throw new AccountConflictException();
        account.Email = account.PendingEmail;
        account.NormalizedEmail = account.PendingNormalizedEmail;
        await RevokeCredentialsAsync(account, cancellationToken);
        return account.UserId;
    }

    public async Task CancelEmailChangeAsync(Guid actorId, CancellationToken cancellationToken = default)
    {
        var account = await RequireAccountAsync(actorId, cancellationToken);
        // Force an UPDATE even for an empty request so concurrent credential changes are checked.
        store.MarkCredentialsChecked(account);
        ClearEmailChange(account);
    }

    private async Task<Account> RequireAccountAsync(Guid actorId, CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty) throw new UnauthorizedAccessException();
        var account = await store.FindAccountAsync(actorId, cancellationToken) ?? throw new KeyNotFoundException();
        if (!account.EmailConfirmed) throw new UnauthorizedAccessException();
        return account;
    }

    private async Task<Account> RequirePasswordAsync(Guid actorId, string password, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(actorId, cancellationToken);
        if (password is null || password.Length > 128 || passwords.Verify(account.PasswordHash, password) == PasswordCheck.Failed)
            throw new InvalidCurrentPasswordException();
        store.MarkCredentialsChecked(account);
        return account;
    }

    private async Task RevokeCredentialsAsync(Account account, CancellationToken cancellationToken)
    {
        account.SessionVersion = Guid.NewGuid();
        account.PasswordResetTokenHash = null;
        account.PasswordResetExpiresAtUtc = null;
        account.PasswordResetIssuedAtUtc = null;
        ClearEmailChange(account);
        await store.RevokeSessionsAsync(account.UserId, cancellationToken);
    }

    private static void ClearEmailChange(Account account)
    {
        account.PendingEmail = null;
        account.PendingNormalizedEmail = null;
        account.EmailChangeTokenHash = null;
        account.EmailChangeExpiresAtUtc = null;
        // Keep issued-at so cancelling cannot bypass the resend cooldown.
    }
}
