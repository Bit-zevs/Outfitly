namespace Outfitly.Application.Authentication;

public enum AccountEmailPurpose { Confirmation, PasswordReset }
public sealed record ConfirmationEmail(string Email, string Token, AccountEmailPurpose Purpose = AccountEmailPurpose.Confirmation);
public sealed record RegisteredAccount(Guid Id, string Email, string DisplayName, string Username, ConfirmationEmail Confirmation);

public interface IConfirmationEmailSender
{
    Task SendAsync(ConfirmationEmail email, CancellationToken cancellationToken);
}

public sealed class EmailNotConfirmedException : Exception;
public sealed class InvalidConfirmationTokenException : Exception;
public sealed class InvalidPasswordResetTokenException : Exception;
public sealed class ConfirmationTooFrequentException : Exception;
public sealed class EmailDeliveryException(Exception inner) : Exception("Confirmation email delivery failed.", inner);
