using System.Collections.Concurrent;
using Outfitly.Application.Authentication;

namespace Outfitly.Tests.Api;

internal sealed class RecordingConfirmationSender : IConfirmationEmailSender
{
    internal ConcurrentDictionary<string, ConfirmationEmail> Emails { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal bool Fail { get; set; }
    public Task SendAsync(ConfirmationEmail email, CancellationToken cancellationToken)
    {
        if (Fail) throw new EmailDeliveryException(new InvalidOperationException("Test delivery failure"));
        Emails[email.Email] = email;
        return Task.CompletedTask;
    }
}
