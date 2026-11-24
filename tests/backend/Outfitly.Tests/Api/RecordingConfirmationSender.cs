using System.Collections.Concurrent;
using Outfitly.Application.Authentication;
using Outfitly.Api.Authentication;

namespace Outfitly.Tests.Api;

internal sealed class RecordingConfirmationSender : IConfirmationEmailSender, IAnonymousAccountMail
{
    internal ConcurrentDictionary<string, ConfirmationEmail> Emails { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal bool Fail { get; set; }
    internal Func<CancellationToken, Task>? BeforeDelivery { get; set; }
    internal Action<ConfirmationEmail>? ObserveDelivery { get; set; }
    public async Task SendAsync(ConfirmationEmail email, CancellationToken cancellationToken)
    {
        ObserveDelivery?.Invoke(email);
        if (Fail) throw new EmailDeliveryException(new InvalidOperationException("Test delivery failure"));
        if (BeforeDelivery is not null) await BeforeDelivery(cancellationToken);
        Emails[email.Email] = email;
    }

    public async ValueTask EnqueueAsync(ConfirmationEmail email, CancellationToken cancellationToken)
    {
        try { await SendAsync(email, cancellationToken); }
        catch (EmailDeliveryException) { }
    }
}
