namespace SiteMonitor.Core.Models;

public enum CheckType
{
    /// <summary>URL → HTTP; bare host/IP → Ping, falling back to TCP 443/80.</summary>
    Auto,
    Ping,
    Tcp,
    Http
}

public sealed class MonitorTarget
{
    public const int MinIntervalSeconds = 10;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>IP address, host name, or URL (http:// or https://).</summary>
    public string Address { get; set; } = "";
    public CheckType CheckType { get; set; } = CheckType.Auto;
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between checks. Null = use global default.</summary>
    public int? IntervalSeconds { get; set; }
    /// <summary>Per-check timeout in milliseconds. Null = use global default.</summary>
    public int? TimeoutMs { get; set; }
    /// <summary>Consecutive failures before an alert is sent. Null = use global default.</summary>
    public int? FailThreshold { get; set; }

    /// <summary>TCP port for Tcp checks (also used to override Auto fallback).</summary>
    public int? Port { get; set; }
    /// <summary>HTTP: expected status code. Null = any 2xx/3xx counts as up.</summary>
    public int? ExpectedStatus { get; set; }
    /// <summary>HTTP: text that must appear in the response body (optional).</summary>
    public string? Keyword { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name;

    public int EffectiveInterval(GlobalSettings g) => Math.Max(MinIntervalSeconds, IntervalSeconds ?? g.DefaultIntervalSeconds);
    public int EffectiveTimeout(GlobalSettings g) => Math.Clamp(TimeoutMs ?? g.DefaultTimeoutMs, 500, 120_000);
    public int EffectiveThreshold(GlobalSettings g) => Math.Max(1, FailThreshold ?? g.DefaultFailThreshold);

    public MonitorTarget Clone() => (MonitorTarget)MemberwiseClone();

    /// <summary>Returns a list of validation problems; empty if valid.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Address)) errors.Add("Address is required.");
        if (IntervalSeconds is < MinIntervalSeconds) errors.Add($"Interval must be at least {MinIntervalSeconds} seconds.");
        if (FailThreshold is < 1) errors.Add("Failure threshold must be at least 1.");
        if (Port is < 1 or > 65535) errors.Add("Port must be between 1 and 65535.");
        if (CheckType == CheckType.Tcp && Port is null && !Checks.CheckFactory.TryGetUri(Address, out _))
            errors.Add("TCP checks need a port.");
        if (ExpectedStatus is < 100 or > 599) errors.Add("Expected status must be 100-599.");
        return errors;
    }
}
