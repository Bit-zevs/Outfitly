namespace Outfitly.Application.Authentication;

public sealed class Account
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = null!;
    public string NormalizedEmail { get; init; } = null!;
    public string PasswordHash { get; set; } = null!;
}

public sealed class LoginSession
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public bool IsRevoked { get; set; }
}

public sealed record AccountView(Guid Id, string Email, string DisplayName);
public sealed record LoginResult(AccountView User, LoginSession Session);

public sealed class AccountConflictException(Exception? inner = null)
    : Exception("An account with this email already exists.", inner);
