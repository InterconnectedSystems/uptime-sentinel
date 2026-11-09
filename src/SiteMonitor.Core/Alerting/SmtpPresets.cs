using SiteMonitor.Core.Models;

namespace SiteMonitor.Core.Alerting;

/// <summary>
/// Preset SMTP providers for the dropdown. There is no legitimate free *anonymous* open relay;
/// these are providers with free tiers — the user still supplies their own account credentials.
/// Limits verified Nov 2026; providers change them, so the GUI shows them as guidance only.
/// </summary>
public sealed record SmtpPreset(
    string Name,
    string Host,
    int Port,
    SmtpSecurity Security,
    string FreeTier,
    string Notes,
    string SignupUrl);

public static class SmtpPresets
{
    public const string CustomName = "Custom";

    public static readonly IReadOnlyList<SmtpPreset> All = new[]
    {
        new SmtpPreset(CustomName, "", 587, SmtpSecurity.StartTls, "",
            "Enter your own SMTP server (e.g. your ISP, company mail server, or a relay on your network).", ""),

        new SmtpPreset("SMTP2GO (free tier)", "mail.smtp2go.com", 587, SmtpSecurity.StartTls,
            "1,000 emails/month (200/day)",
            "Create an SMTP user in the SMTP2GO dashboard and verify your sender address/domain. Port 2525 also works if 587 is blocked.",
            "https://www.smtp2go.com/"),

        new SmtpPreset("Brevo (free tier)", "smtp-relay.brevo.com", 587, SmtpSecurity.StartTls,
            "300 emails/day",
            "Username is your Brevo login email; password is an SMTP key (Settings → SMTP & API), not your account password.",
            "https://www.brevo.com/free-smtp-server/"),

        new SmtpPreset("Mailjet (free tier)", "in-v3.mailjet.com", 587, SmtpSecurity.StartTls,
            "200 emails/day (6,000/month)",
            "Username = API key, password = secret key. The From address must be a validated sender.",
            "https://www.mailjet.com/"),

        new SmtpPreset("Gmail", "smtp.gmail.com", 587, SmtpSecurity.StartTls,
            "Free with a Google account (~500/day)",
            "Requires 2-Step Verification and an App Password (myaccount.google.com → Security → App passwords). Your normal password will not work.",
            "https://myaccount.google.com/apppasswords"),

        new SmtpPreset("Zoho Mail", "smtp.zoho.com", 465, SmtpSecurity.SslOnConnect,
            "Free plan (custom domain)",
            "Use an app-specific password if 2FA is enabled. EU accounts use smtp.zoho.eu.",
            "https://www.zoho.com/mail/"),

        new SmtpPreset("Microsoft 365 / Exchange Online", "smtp.office365.com", 587, SmtpSecurity.StartTls,
            "Included with a paid Microsoft 365 mailbox",
            "Basic-auth SMTP must be enabled for the mailbox. Microsoft is disabling it by default for tenants from Dec 2026, " +
            "and personal Outlook.com/Hotmail accounts no longer support password SMTP. Prefer another provider if this fails.",
            "https://learn.microsoft.com/exchange/clients-and-mobile-in-exchange-online/authenticated-client-smtp-submission"),
    };

    public static SmtpPreset? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Copies server settings from a preset onto settings (credentials/addresses untouched).</summary>
    public static void Apply(SmtpPreset preset, SmtpSettings settings)
    {
        settings.Preset = preset.Name;
        if (preset.Name == CustomName) return;
        settings.Host = preset.Host;
        settings.Port = preset.Port;
        settings.Security = preset.Security;
    }
}
