using System.Net;
using System.Net.Mail;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Authentication;

namespace Outfitly.Tests.Infrastructure;

public sealed class SmtpEmailTests
{
    private static SmtpConfirmationOptions Options() => new()
    {
        PublicUrl = "https://localhost:7043", Username = "test-login", Password = "test-secret"
    };

    [Theory]
    [InlineData(AccountEmailPurpose.Confirmation, "confirm-email", "24 часа")]
    [InlineData(AccountEmailPurpose.PasswordReset, "reset-password", "1 час")]
    [InlineData(AccountEmailPurpose.EmailChange, "confirm-email-change", "24 часа")]
    public async Task Sends_multipart_branded_email_using_configured_origin_and_authenticated_StartTls(
        AccountEmailPurpose purpose, string route, string lifetime)
    {
        var options = Options();
        using var cancellation = new CancellationTokenSource();
        var called = false;
        var sender = new SmtpConfirmationEmailSender(options, async (client, message, token) =>
        {
            called = true;
            Assert.Equal(cancellation.Token, token);
            Assert.Equal("connect.smtp.bz", client.Host);
            Assert.Equal(587, client.Port);
            Assert.True(client.EnableSsl);
            Assert.False(client.UseDefaultCredentials);
            var credentials = Assert.IsType<NetworkCredential>(client.Credentials);
            Assert.Equal("test-login", credentials.UserName);
            Assert.Equal("test-secret", credentials.Password);
            Assert.Equal("no-reply@outfitlyapp.ru", message.From!.Address);
            Assert.Equal("Outfitly", message.From.DisplayName);
            Assert.Equal("reader@example.com", Assert.Single(message.To).Address);
            Assert.False(message.IsBodyHtml);
            Assert.Contains($"https://localhost:7043/#/{route}?token=a%26%22%3C", message.Body);
            Assert.Contains(lifetime, message.Body);
            var html = Assert.Single(message.AlternateViews);
            Assert.Equal("text/html", html.ContentType.MediaType);
            using var reader = new StreamReader(html.ContentStream);
            var body = await reader.ReadToEndAsync(token);
            Assert.Contains("Outfitly", body);
            Assert.Contains("<a href=", body);
            Assert.Contains("проигнорируйте", body);
            Assert.DoesNotContain("<img", body);
            Assert.DoesNotContain("a&\"<", body);
        });
        await sender.SendAsync(new("reader@example.com", "a&\"<", purpose), cancellation.Token);
        Assert.True(called);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("username")]
    [InlineData("port")]
    [InlineData("url")]
    [InlineData("sender")]
    public async Task Invalid_configuration_fails_before_transport(string setting)
    {
        var options = Options();
        switch (setting)
        {
            case "password": options.Password = ""; break;
            case "username": options.Username = ""; break;
            case "port": options.Port = 465; break;
            case "url": options.PublicUrl = "http://example.com"; break;
            case "sender": options.From = "invalid"; break;
        }
        var called = false;
        var sender = new SmtpConfirmationEmailSender(options, (_, _, _) => { called = true; return Task.CompletedTask; });
        await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(new("reader@example.com", "token"), default));
        Assert.False(called);
    }

    [Theory]
    [InlineData(SmtpStatusCode.GeneralFailure)]
    [InlineData(SmtpStatusCode.ClientNotPermitted)]
    [InlineData(SmtpStatusCode.MailboxUnavailable)]
    public async Task Transport_errors_do_not_expose_sensitive_diagnostics(SmtpStatusCode status)
    {
        var sender = new SmtpConfirmationEmailSender(Options(), (_, _, _) => throw new SmtpException(status, "test-secret token reader@example.com"));
        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(new("reader@example.com", "token"), default));
        Assert.DoesNotContain("test-secret", error.ToString());
        Assert.DoesNotContain("reader@example.com", error.ToString());
    }

    [Fact]
    public async Task Cancellation_is_not_reported_as_delivery_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var sender = new SmtpConfirmationEmailSender(Options(), (_, _, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(new("reader@example.com", "token"), cancellation.Token));
    }
}
