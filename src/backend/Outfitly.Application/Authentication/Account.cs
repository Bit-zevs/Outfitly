namespace Outfitly.Application.Authentication;

public sealed class Account
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = null!;
    public string NormalizedEmail { get; init; } = null!;
    public string PasswordHash { get; set; } = null!;
    public bool EmailConfirmed { get; set; }
    public string? ConfirmationTokenHash { get; set; }
    public DateTime? ConfirmationExpiresAtUtc { get; set; }
    public DateTime? ConfirmationIssuedAtUtc { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetExpiresAtUtc { get; set; }
    public DateTime? PasswordResetIssuedAtUtc { get; set; }
    public Guid SessionVersion { get; set; }
}

public sealed class LoginSession
{
    public Guid SessionVersion { get; init; }
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public bool IsRevoked { get; set; }
}

public sealed record AccountView(Guid Id, string Email, string DisplayName);
public sealed record LoginResult(AccountView User, LoginSession Session);

public sealed class AccountConflictException(Exception? inner = null)
    : Exception("An account with this email already exists.", inner);
