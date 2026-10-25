namespace Outfitly.Application.Authentication;

public sealed class Account
{
    public Guid UserId { get; init; }
    public string Email { get; set; } = null!;
    public string NormalizedEmail { get; set; } = null!;
    public string? PendingEmail { get; set; }
    public string? PendingNormalizedEmail { get; set; }
    public string? EmailChangeTokenHash { get; set; }
    public DateTime? EmailChangeIssuedAtUtc { get; set; }
    public DateTime? EmailChangeExpiresAtUtc { get; set; }
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

public sealed record AccountView(Guid Id, string Email, string DisplayName, string Username, string? PendingEmail = null, string? Bio = null, string? AvatarUrl = null);
public sealed record LoginResult(AccountView User, LoginSession Session);

public sealed class AccountConflictException(Exception? inner = null)
    : Exception("An account with this email already exists.", inner);

public sealed class UsernameConflictException(Exception? inner = null)
    : Exception("This username is already taken.", inner);

public sealed class InvalidCurrentPasswordException : Exception;
public sealed class InvalidEmailChangeTokenException : Exception;
