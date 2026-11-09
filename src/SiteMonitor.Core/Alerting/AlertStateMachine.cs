using System.Text;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Core.Alerting;

public enum AlertKind { None, Down, Reminder, Recovered }

/// <summary>
/// Per-target up/down state. Pure logic (no I/O) so it is unit-testable.
/// Flow: Process(result) → returns which alert (if any) to send → caller sends it →
/// caller calls MarkSent / MarkFailed. Failed sends are retried on the next check.
/// </summary>
public sealed class AlertStateMachine
{
    private const int MaxRecentFailures = 10;
    private readonly Queue<CheckResult> _recentFailures = new();
    private bool _downAlertSent;
    private bool _recoveryPending;
    private TimeSpan _lastOutageDuration;
    private int _sendFailures;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;

    /// <summary>Backoff between email retries: 1, 2, 4, 8, 16, then every 30 minutes.</summary>
    public static TimeSpan RetryDelay(int failures) =>
        TimeSpan.FromMinutes(Math.Min(30, Math.Pow(2, Math.Max(0, failures - 1))));

    public TargetStatus Status { get; }

    public AlertStateMachine(MonitorTarget target)
    {
        Status = new TargetStatus { TargetId = target.Id };
        UpdateTarget(target);
    }

    public void UpdateTarget(MonitorTarget target)
    {
        Status.Name = target.DisplayName;
        Status.Address = target.Address;
    }

    public IReadOnlyCollection<CheckResult> RecentFailures => _recentFailures;

    public AlertKind Process(CheckResult r, int threshold, GlobalSettings g)
    {
        Status.LastCheck = r.Timestamp;
        Status.LastSuccess = r.Success;
        Status.LastMessage = r.Message;
        Status.LatencyMs = r.LatencyMs;

        if (r.Success)
        {
            Status.ConsecutiveFailures = 0;
            _recentFailures.Clear();

            if (Status.IsDown)
            {
                _lastOutageDuration = r.Timestamp - (Status.DownSince ?? r.Timestamp);
                Status.IsDown = false;
                Status.DownSince = null;
                // Only announce recovery if we actually told someone it was down.
                _recoveryPending = _downAlertSent && g.SendRecoveryEmails;
                _downAlertSent = false;
                ResetRetry();
                return _recoveryPending ? AlertKind.Recovered : AlertKind.None;
            }
            return _recoveryPending && r.Timestamp >= _retryAfter ? AlertKind.Recovered : AlertKind.None;
        }

        // Failure
        if (_recoveryPending) ResetRetry();
        _recoveryPending = false;
        Status.ConsecutiveFailures++;
        _recentFailures.Enqueue(r);
        while (_recentFailures.Count > MaxRecentFailures) _recentFailures.Dequeue();

        if (!Status.IsDown)
        {
            if (Status.ConsecutiveFailures < threshold) return AlertKind.None;
            Status.IsDown = true;
            Status.DownSince = _recentFailures.ElementAt(Math.Max(0, _recentFailures.Count - threshold)).Timestamp;
            ResetRetry();
            return AlertKind.Down;
        }

        if (!_downAlertSent) // previous send failed → retry with backoff
            return r.Timestamp >= _retryAfter ? AlertKind.Down : AlertKind.None;

        if (g.ReminderHours > 0 && Status.LastAlertSent is { } last &&
            r.Timestamp - last >= TimeSpan.FromHours(g.ReminderHours))
            return AlertKind.Reminder;

        return AlertKind.None;
    }

    public void MarkSent(AlertKind kind, DateTimeOffset when)
    {
        Status.LastAlertSent = when;
        Status.LastAlertError = null;
        if (kind is AlertKind.Down or AlertKind.Reminder) _downAlertSent = true;
        if (kind == AlertKind.Recovered) _recoveryPending = false;
        ResetRetry();
    }

    public void MarkFailed(string error, DateTimeOffset when)
    {
        Status.LastAlertError = error;
        _sendFailures++;
        _retryAfter = when + RetryDelay(_sendFailures);
    }

    private void ResetRetry()
    {
        _sendFailures = 0;
        _retryAfter = DateTimeOffset.MinValue;
    }

    public (string Subject, string Body) BuildEmail(AlertKind kind, MonitorTarget t, string checkKind, int threshold)
    {
        var name = t.DisplayName;
        var sb = new StringBuilder();
        string subject;
        switch (kind)
        {
            case AlertKind.Recovered:
                subject = $"[RECOVERED] {name} is back up";
                sb.AppendLine($"{name} is responding again.");
                sb.AppendLine();
                sb.AppendLine($"Address:   {t.Address}");
                sb.AppendLine($"Check:     {checkKind}");
                sb.AppendLine($"Result:    {Status.LastMessage} ({Status.LatencyMs} ms)");
                sb.AppendLine($"Recovered: {Status.LastCheck:yyyy-MM-dd HH:mm:ss zzz}");
                sb.AppendLine($"Outage:    about {FormatDuration(_lastOutageDuration)}");
                break;

            default:
                subject = kind == AlertKind.Reminder ? $"[STILL DOWN] {name}" : $"[DOWN] {name} is not responding";
                sb.AppendLine(kind == AlertKind.Reminder
                    ? $"{name} is still down."
                    : $"{name} failed {threshold} consecutive checks and is considered DOWN.");
                sb.AppendLine();
                sb.AppendLine($"Address:    {t.Address}");
                sb.AppendLine($"Check:      {checkKind}");
                sb.AppendLine($"Down since: {Status.DownSince:yyyy-MM-dd HH:mm:ss zzz}");
                if (kind == AlertKind.Reminder && Status.DownSince is { } ds && Status.LastCheck is { } lc)
                    sb.AppendLine($"Duration:   {FormatDuration(lc - ds)}");
                sb.AppendLine();
                sb.AppendLine("Recent failures:");
                foreach (var f in _recentFailures.TakeLast(threshold))
                    sb.AppendLine($"  {f.Timestamp:yyyy-MM-dd HH:mm:ss}  {f.Message}");
                break;
        }
        sb.AppendLine();
        sb.AppendLine($"-- SiteMonitor on {Environment.MachineName}");
        return (subject, sb.ToString());
    }

    public static string FormatDuration(TimeSpan d) =>
        d.TotalDays >= 1 ? $"{(int)d.TotalDays}d {d.Hours}h {d.Minutes}m"
        : d.TotalHours >= 1 ? $"{(int)d.TotalHours}h {d.Minutes}m"
        : d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes}m {d.Seconds}s"
        : $"{Math.Max(0, (int)d.TotalSeconds)}s";
}
