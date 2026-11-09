namespace SiteMonitor.Core.Models;

public sealed record CheckResult(bool Success, string Message, long LatencyMs, DateTimeOffset Timestamp)
{
    public static CheckResult Ok(string msg, long ms) => new(true, msg, ms, DateTimeOffset.Now);
    public static CheckResult Fail(string msg, long ms = 0) => new(false, msg, ms, DateTimeOffset.Now);
}

/// <summary>Snapshot of a target's state, written by the service to status.json for the GUI.</summary>
public sealed class TargetStatus
{
    public Guid TargetId { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public bool IsDown { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastCheck { get; set; }
    public bool? LastSuccess { get; set; }
    public string LastMessage { get; set; } = "";
    public long LatencyMs { get; set; }
    public DateTimeOffset? DownSince { get; set; }
    public DateTimeOffset? LastAlertSent { get; set; }
    public string? LastAlertError { get; set; }
}

public sealed class ServiceStatus
{
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ServiceStarted { get; set; }
    public List<TargetStatus> Targets { get; set; } = new();
}
