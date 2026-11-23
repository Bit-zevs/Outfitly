using System.Threading.Channels;
using Outfitly.Application.Authentication;

namespace Outfitly.Api.Authentication;

public interface IAnonymousAccountMail
{
    ValueTask EnqueueAsync(ConfirmationEmail email, CancellationToken cancellationToken);
}

// Keep SMTP latency and delivery failures outside anonymous HTTP responses.
internal sealed class AnonymousAccountMail(IServiceScopeFactory scopes) : BackgroundService, IAnonymousAccountMail
{
    private readonly Channel<ConfirmationEmail> queue = Channel.CreateBounded<ConfirmationEmail>(
        new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public ValueTask EnqueueAsync(ConfirmationEmail email, CancellationToken cancellationToken)
    {
        queue.Writer.TryWrite(email);
        return ValueTask.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await scope.ServiceProvider.GetRequiredService<IConfirmationEmailSender>().SendAsync(email, timeout.Token);
            }
            catch (EmailDeliveryException) { }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }
    }
}
