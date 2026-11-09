using SiteMonitor.Core.Alerting;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Core.Tests;

public class AlertStateMachineTests
{
    private static readonly MonitorTarget Target = new() { Name = "Example", Address = "https://example.com" };
    private static readonly GlobalSettings Global = new();
    private static DateTimeOffset _t = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static CheckResult Fail(string msg = "timeout") => new(false, msg, 0, _t = _t.AddMinutes(1));
    private static CheckResult Ok() => new(true, "HTTP 200", 42, _t = _t.AddMinutes(1));

    [Fact]
    public void Sends_exactly_one_down_alert_on_third_consecutive_failure()
    {
        var sm = new AlertStateMachine(Target);
        Assert.Equal(AlertKind.None, sm.Process(Fail(), 3, Global));
        Assert.Equal(AlertKind.None, sm.Process(Fail(), 3, Global));
        Assert.Equal(AlertKind.Down, sm.Process(Fail(), 3, Global));
        sm.MarkSent(AlertKind.Down, _t);
        Assert.True(sm.Status.IsDown);

        for (int i = 0; i < 20; i++)
            Assert.Equal(AlertKind.None, sm.Process(Fail(), 3, Global));
    }

    [Fact]
    public void Success_resets_the_failure_counter()
    {
        var sm = new AlertStateMachine(Target);
        sm.Process(Fail(), 3, Global);
        sm.Process(Fail(), 3, Global);
        sm.Process(Ok(), 3, Global);
        Assert.Equal(0, sm.Status.ConsecutiveFailures);
        Assert.Equal(AlertKind.None, sm.Process(Fail(), 3, Global));
        Assert.Equal(AlertKind.None, sm.Process(Fail(), 3, Global));
        Assert.False(sm.Status.IsDown);
    }

    [Fact]
    public void Down_since_is_time_of_first_failure_in_the_run()
    {
        var sm = new AlertStateMachine(Target);
        sm.Process(Ok(), 3, Global);
        var first = Fail();
        sm.Process(first, 3, Global);
        sm.Process(Fail(), 3, Global);
        sm.Process(Fail(), 3, Global);
        Assert.Equal(first.Timestamp, sm.Status.DownSince);
    }

    [Fact]
    public void Sends_recovery_after_down_alert_was_sent()
    {
        var sm = new AlertStateMachine(Target);
        for (int i = 0; i < 3; i++) sm.Process(Fail(), 3, Global);
        sm.MarkSent(AlertKind.Down, _t);
        Assert.Equal(AlertKind.Recovered, sm.Process(Ok(), 3, Global));
        sm.MarkSent(AlertKind.Recovered, _t);
        Assert.False(sm.Status.IsDown);
        Assert.Equal(AlertKind.None, sm.Process(Ok(), 3, Global));
    }

    [Fact]
    public void No_recovery_email_when_disabled()
    {
        var g = new GlobalSettings { SendRecoveryEmails = false };
        var sm = new AlertStateMachine(Target);
        for (int i = 0; i < 3; i++) sm.Process(Fail(), 3, g);
        sm.MarkSent(AlertKind.Down, _t);
        Assert.Equal(AlertKind.None, sm.Process(Ok(), 3, g));
    }

    [Fact]
    public void Failed_down_email_is_retried_on_next_check()
    {
        var sm = new AlertStateMachine(Target);
        for (int i = 0; i < 2; i++) sm.Process(Fail(), 3, Global);
        Assert.Equal(AlertKind.Down, sm.Process(Fail(), 3, Global));
        sm.MarkFailed("SMTP auth failed", _t);
        Assert.Equal("SMTP auth failed", sm.Status.LastAlertError);
        Assert.Equal(AlertKind.Down, sm.Process(Fail(), 3, Global));
        sm.MarkSent(AlertKind.Down, _t);
        Assert.Null(sm.Status.LastAlertError);
        Assert.Equal(AlertKind.None, sm.Process(Fail(), 3, Global));
    }

    [Fact]
    public void Failed_recovery_email_is_retried_on_next_success()
    {
        var sm = new AlertStateMachine(Target);
        for (int i = 0; i < 3; i++) sm.Process(Fail(), 3, Global);
        sm.MarkSent(AlertKind.Down, _t);
        Assert.Equal(AlertKind.Recovered, sm.Process(Ok(), 3, Global));
        sm.MarkFailed("network", _t);
        Assert.Equal(AlertKind.Recovered, sm.Process(Ok(), 3, Global));
        sm.MarkSent(AlertKind.Recovered, _t);
        Assert.Equal(AlertKind.None, sm.Process(Ok(), 3, Global));
    }

    [Fact]
    public void No_recovery_email_if_down_alert_never_went_out()
    {
        var sm = new AlertStateMachine(Target);
        for (int i = 0; i < 3; i++) sm.Process(Fail(), 3, Global);
        sm.MarkFailed("SMTP not configured", _t);
        Assert.Equal(AlertKind.None, sm.Process(Ok(), 3, Global));
    }

    [Fact]
    public void Reminder_sent_after_configured_hours()
    {
        var g = new GlobalSettings { ReminderHours = 1 };
        var sm = new AlertStateMachine(Target);
        for (int i = 0; i < 3; i++) sm.Process(Fail(), 3, g);
        sm.MarkSent(AlertKind.Down, _t);
        for (int i = 0; i < 59; i++) Assert.Equal(AlertKind.None, sm.Process(Fail(), 3, g));
        Assert.Equal(AlertKind.Reminder, sm.Process(Fail(), 3, g));
        sm.MarkSent(AlertKind.Reminder, _t);
        Assert.Equal(AlertKind.None, sm.Process(Fail(), 3, g));
    }

    [Fact]
    public void Email_retries_back_off_exponentially()
    {
        var sm = new AlertStateMachine(Target);
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        CheckResult FailAt(double sec) => new(false, "x", 0, t0.AddSeconds(sec));

        sm.Process(FailAt(0), 3, Global);
        sm.Process(FailAt(10), 3, Global);
        Assert.Equal(AlertKind.Down, sm.Process(FailAt(20), 3, Global));
        sm.MarkFailed("smtp down", t0.AddSeconds(20));                       // retry after 1 min
        Assert.Equal(AlertKind.None, sm.Process(FailAt(30), 3, Global));
        Assert.Equal(AlertKind.None, sm.Process(FailAt(70), 3, Global));
        Assert.Equal(AlertKind.Down, sm.Process(FailAt(80), 3, Global));
        sm.MarkFailed("smtp down", t0.AddSeconds(80));                       // retry after 2 min
        Assert.Equal(AlertKind.None, sm.Process(FailAt(190), 3, Global));
        Assert.Equal(AlertKind.Down, sm.Process(FailAt(200), 3, Global));
        sm.MarkSent(AlertKind.Down, t0.AddSeconds(200));
        Assert.Equal(AlertKind.None, sm.Process(FailAt(210), 3, Global));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(50, 30)]
    public void Retry_delay_is_capped_at_30_minutes(int failures, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), AlertStateMachine.RetryDelay(failures));

    [Fact]
    public void Threshold_of_one_alerts_immediately()
    {
        var sm = new AlertStateMachine(Target);
        Assert.Equal(AlertKind.Down, sm.Process(Fail(), 1, Global));
    }

    [Fact]
    public void Down_email_lists_failures()
    {
        var sm = new AlertStateMachine(Target);
        sm.Process(Fail("err-a"), 3, Global);
        sm.Process(Fail("err-b"), 3, Global);
        sm.Process(Fail("err-c"), 3, Global);
        var (subject, body) = sm.BuildEmail(AlertKind.Down, Target, "HTTP", 3);
        Assert.Equal("[DOWN] Example is not responding", subject);
        Assert.Contains("err-a", body);
        Assert.Contains("err-c", body);
        Assert.Contains("https://example.com", body);
    }
}
