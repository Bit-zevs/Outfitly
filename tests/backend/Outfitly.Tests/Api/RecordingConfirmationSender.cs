using System.Collections.Concurrent;
using Outfitly.Application.Authentication;

namespace Outfitly.Tests.Api;

internal sealed class RecordingConfirmationSender : IConfirmationEmailSender
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

    internal async Task<ConfirmationEmail> WaitForAsync(string address, string? previousToken = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!Emails.TryGetValue(address, out var email) || email.Token == previousToken)
            await Task.Delay(25, timeout.Token);
        return Emails[address];
    }
}
