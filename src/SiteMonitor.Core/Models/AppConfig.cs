namespace SiteMonitor.Core.Models;

public sealed class AppConfig
{
    public GlobalSettings Global { get; set; } = new();
    public SmtpSettings Smtp { get; set; } = new();
    public List<MonitorTarget> Targets { get; set; } = new();
}

public sealed class GlobalSettings
{
    public int DefaultIntervalSeconds { get; set; } = 60;
    public int DefaultTimeoutMs { get; set; } = 5000;
    public int DefaultFailThreshold { get; set; } = 3;
    /// <summary>Send an email when a down target comes back up.</summary>
    public bool SendRecoveryEmails { get; set; } = true;
    /// <summary>Re-send a DOWN reminder every N hours while still down. 0 = never.</summary>
    public int ReminderHours { get; set; } = 0;
}

public enum SmtpSecurity
{
    None,
    StartTls,
    SslOnConnect
}

public sealed class SmtpSettings
{
    public string Preset { get; set; } = "Custom";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
    public string Username { get; set; } = "";
    /// <summary>Encrypted (DPAPI) password. Use SecretProtector to read/write.</summary>
    public string ProtectedPassword { get; set; } = "";
    public string From { get; set; } = "";
    /// <summary>Comma/semicolon separated recipient list.</summary>
    public string To { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From) && Recipients().Count > 0;

    public List<string> Recipients() =>
        To.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
