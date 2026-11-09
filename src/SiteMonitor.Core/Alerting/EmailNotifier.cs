using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SiteMonitor.Core.Config;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Core.Alerting;

public interface INotifier
{
    Task SendAsync(string subject, string body, CancellationToken ct);
}

public sealed class EmailNotifier : INotifier
{
    private readonly Func<SmtpSettings> _settings;

    /// <param name="settings">Called on every send so config reloads take effect immediately.</param>
    public EmailNotifier(Func<SmtpSettings> settings) => _settings = settings;

    public Task SendAsync(string subject, string body, CancellationToken ct) => SendAsync(_settings(), subject, body, ct);

    public static async Task SendAsync(SmtpSettings s, string subject, string body, CancellationToken ct)
    {
        if (!s.IsConfigured)
            throw new InvalidOperationException("SMTP is not configured (host, From and To are required).");

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(s.From));
        foreach (var to in s.Recipients()) msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient { Timeout = 30_000 };
        var options = s.Security switch
        {
            SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None
        };
        await client.ConnectAsync(s.Host, s.Port, options, ct).ConfigureAwait(false);

        var password = SecretProtector.Unprotect(s.ProtectedPassword);
        if (!string.IsNullOrEmpty(s.Username))
            await client.AuthenticateAsync(s.Username, password, ct).ConfigureAwait(false);

        await client.SendAsync(msg, ct).ConfigureAwait(false);
        await client.DisconnectAsync(true, ct).ConfigureAwait(false);
    }
}
