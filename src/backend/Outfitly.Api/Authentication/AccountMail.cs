using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Authentication;

namespace Outfitly.Api.Authentication;

internal sealed class AccountMail(AccountMailOutbox outbox, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("Outfitly.PasswordResetMail.v1");

    public void Stage(IssuedPasswordReset reset) =>
        outbox.Stage(reset.TokenHash, protector.Protect(reset.Token), reset.IssuedAtUtc, reset.ExpiresAtUtc);

    public void Stage(ConfirmationEmail email)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Token)));
        outbox.Stage(hash, protector.Protect(email.Token), email.IssuedAtUtc, email.ExpiresAtUtc, email.Purpose);
    }

    public ConfirmationEmail Message(PendingAccountMail pending) =>
        new(pending.Email, protector.Unprotect(pending.ProtectedToken), pending.Purpose);
}

internal sealed class AccountMailWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<AccountMailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var poll = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var cleanupAfter = DateTime.MinValue;
        while (await poll.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;
                var outbox = services.GetRequiredService<AccountMailOutbox>();
                var now = clock.GetUtcNow().UtcDateTime;
                if (now >= cleanupAfter)
                {
                    await outbox.RemoveExpiredAsync(now, stoppingToken);
                    await outbox.PruneCredentialsAsync(now, stoppingToken);
                    cleanupAfter = now.AddMinutes(1);
                }
                var pending = await outbox.NextAsync(now, stoppingToken);
                // Persist the next attempt before calling SMTP so termination also leaves a retry.
                if (pending is null || await outbox.DeferAsync(pending, now.AddSeconds(30), stoppingToken) == 0)
                    continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var message = services.GetRequiredService<AccountMail>().Message(pending);
                await services.GetRequiredService<IConfirmationEmailSender>().SendAsync(message, timeout.Token);
                await outbox.CompleteAsync(pending.TokenHash, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // Do not log the recipient, reset token or SMTP/provider diagnostics.
                logger.LogWarning("Account mail worker will retry after {ErrorType}.", error.GetType().Name);
            }
        }
    }
}
