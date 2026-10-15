using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Outfitly.Application.Authentication;

namespace Outfitly.Infrastructure.Authentication;

public sealed class SmtpConfirmationOptions
{
    public string PublicUrl { get; set; } = "";
    public string Host { get; set; } = "connect.smtp.bz";
    public int Port { get; set; } = 587;
    public string From { get; set; } = "no-reply@outfitlyapp.ru";
    public string FromName { get; set; } = "Outfitly";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public void Validate()
    {
        if (!Uri.TryCreate(PublicUrl, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0 ||
            Uri.CheckHostName(Host) == UriHostNameType.Unknown || Port != 587 ||
            !MailAddress.TryCreate(From, out var address) || address.Address != From ||
            string.IsNullOrWhiteSpace(FromName) || FromName.Contains('\r') || FromName.Contains('\n') ||
            string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            throw new InvalidOperationException("Configure EmailConfirmation HTTPS origin, SMTP host, port 587, sender and credentials.");
    }
}

public sealed class SmtpConfirmationEmailSender : IConfirmationEmailSender
{
    private readonly SmtpConfirmationOptions options;
    private readonly Func<SmtpClient, MailMessage, CancellationToken, Task> deliver;

    public SmtpConfirmationEmailSender(SmtpConfirmationOptions options)
        : this(options, (client, message, token) => client.SendMailAsync(message, token)) { }

    internal SmtpConfirmationEmailSender(SmtpConfirmationOptions options,
        Func<SmtpClient, MailMessage, CancellationToken, Task> deliver)
    {
        this.options = options;
        this.deliver = deliver;
    }

    public async Task SendAsync(ConfirmationEmail email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            options.Validate();
            var origin = new Uri(options.PublicUrl);
            var reset = email.Purpose == AccountEmailPurpose.PasswordReset;
            var route = reset ? "reset-password" : "confirm-email";
            var link = $"{origin.GetLeftPart(UriPartial.Authority)}/#/{route}?token={Uri.EscapeDataString(email.Token)}";
            var title = reset ? "Сброс пароля" : "Подтверждение почты";
            var action = reset ? "Сбросить пароль" : "Подтвердить почту";
            var notice = reset
                ? "Ссылка действует 1 час. Если вы не запрашивали сброс пароля, проигнорируйте письмо."
                : "Ссылка действует 24 часа. Если вы не регистрировались, проигнорируйте письмо.";
            var safeLink = WebUtility.HtmlEncode(link);
            using var message = new MailMessage
            {
                From = new MailAddress(options.From, options.FromName, Encoding.UTF8),
                Subject = $"{title} Outfitly",
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                Body = $"Outfitly\n{title}\n\n{action}, открыв ссылку:\n{link}\n\n{notice}"
            };
            message.To.Add(new MailAddress(email.Email));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString($"""
                <!doctype html><html lang="ru"><body style="font-family:Arial,sans-serif;color:#252525;background:#faf9f6;padding:24px">
                <main style="max-width:520px;margin:auto"><p style="font-size:24px">Outfitly</p>
                <h1>{title}</h1><p>Для продолжения откройте ссылку и выполните действие на сайте.</p>
                <p style="margin:32px 0"><a href="{safeLink}" style="background:#252525;color:#fff;padding:14px 22px;text-decoration:none;border-radius:6px">{action}</a></p>
                <p>Если кнопка не работает, скопируйте ссылку в браузер:</p><p style="overflow-wrap:anywhere"><a href="{safeLink}">{safeLink}</a></p>
                <p>{notice}</p></main></body></html>
                """, Encoding.UTF8, MediaTypeNames.Text.Html));
            // EnableSsl requires STARTTLS; default platform certificate validation remains enabled.
            using var client = new SmtpClient(options.Host, options.Port)
            {
                EnableSsl = true, UseDefaultCredentials = false,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = new NetworkCredential(options.Username, options.Password)
            };
            await deliver(client, message, cancellationToken);
        }
        catch (Exception error) when (error is SmtpException or ArgumentException or InvalidOperationException)
        {
            // Do not retain SMTP diagnostics: they can contain addresses, credentials or message contents.
            throw new EmailDeliveryException(new InvalidOperationException("SMTP delivery unavailable."));
        }
    }
}
