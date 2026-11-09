using SiteMonitor.Core.Alerting;
using SiteMonitor.Core.Checks;
using SiteMonitor.Core.Config;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Service;

/// <summary>
/// Runs one lightweight async loop per enabled target. Reloads config.json automatically when it
/// changes (the GUI saves it), keeping each target's up/down state across reloads.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ConfigStore _store;
    private readonly ILogger<Worker> _log;
    private readonly Dictionary<Guid, AlertStateMachine> _states = new();
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _emailGate = new(1, 1);
    private readonly SemaphoreSlim _reloadSignal = new(0, 1);
    private readonly DateTimeOffset _started = DateTimeOffset.Now;
    private volatile AppConfig _config = new();
    private volatile bool _statusDirty = true;

    public Worker(ConfigStore store, ILogger<Worker> log)
    {
        _store = store;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("SiteMonitor service starting. Data folder: {Dir}", _store.DataDirectory);
        Directory.CreateDirectory(_store.DataDirectory);

        using var watcher = new FileSystemWatcher(_store.DataDirectory, "config.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler onChange = (_, _) => SignalReload();
        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.Renamed += (_, _) => SignalReload();

        var statusTask = StatusWriterLoop(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var runners = StartRunners(runCts.Token);

            try
            {
                await _reloadSignal.WaitAsync(stoppingToken);
                // Debounce: editors/atomic renames fire several events.
                await Task.Delay(1000, stoppingToken);
                while (_reloadSignal.CurrentCount > 0) _reloadSignal.Wait(0);
                _log.LogInformation("config.json changed; reloading.");
            }
            catch (OperationCanceledException) { }

            runCts.Cancel();
            try { await Task.WhenAll(runners); } catch (OperationCanceledException) { }
        }

        await statusTask.ConfigureAwait(false);
        _log.LogInformation("SiteMonitor service stopped.");
    }

    private void SignalReload()
    {
        try { _reloadSignal.Release(); } catch (SemaphoreFullException) { }
    }

    private List<Task> StartRunners(CancellationToken ct)
    {
        try
        {
            _config = _store.LoadConfig();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not read config.json; keeping previous configuration.");
        }

        var cfg = _config;
        var enabled = cfg.Targets.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Address)).ToList();

        lock (_stateLock)
        {
            // Drop state for removed/disabled targets, keep it for the rest.
            foreach (var id in _states.Keys.Except(enabled.Select(t => t.Id)).ToList()) _states.Remove(id);
            foreach (var t in enabled)
            {
                if (_states.TryGetValue(t.Id, out var sm)) sm.UpdateTarget(t);
                else _states[t.Id] = new AlertStateMachine(t);
            }
        }
        _statusDirty = true;

        if (!cfg.Smtp.IsConfigured)
            _log.LogWarning("SMTP is not configured; outages will be logged but no emails will be sent.");
        _log.LogInformation("Monitoring {Count} target(s).", enabled.Count);

        return enabled.Select(t => RunTargetAsync(t, ct)).ToList();
    }

    private async Task RunTargetAsync(MonitorTarget target, CancellationToken ct)
    {
        var g = _config.Global;
        var interval = TimeSpan.FromSeconds(target.EffectiveInterval(g));
        var timeout = target.EffectiveTimeout(g);
        var threshold = target.EffectiveThreshold(g);
        var resolved = CheckFactory.Create(target);
        AlertStateMachine sm;
        lock (_stateLock) sm = _states[target.Id];

        // Spread first checks out so many targets don't fire at once.
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Math.Min(5000, interval.TotalMilliseconds / 2)));
        try { await Task.Delay(jitter, ct); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                var result = await resolved.Check.RunAsync(resolved.Endpoint, timeout, ct);
                AlertKind alert;
                bool wasDown;
                lock (_stateLock)
                {
                    wasDown = sm.Status.IsDown;
                    alert = sm.Process(result, threshold, _config.Global);
                }
                _statusDirty = true;

                // Log transitions and the failures leading up to DOWN; stay quiet while it remains down.
                if (!wasDown && sm.Status.IsDown)
                    _log.LogWarning("{Name} is DOWN after {T} consecutive failures: {Msg}", target.DisplayName, threshold, result.Message);
                else if (wasDown && !sm.Status.IsDown)
                    _log.LogWarning("{Name} has RECOVERED.", target.DisplayName);
                else if (!result.Success && !sm.Status.IsDown)
                    _log.LogInformation("{Name}: FAIL {N}/{T} {Msg}", target.DisplayName, sm.Status.ConsecutiveFailures, threshold, result.Message);

                if (alert != AlertKind.None)
                    await SendAlertAsync(sm, alert, target, resolved.Check.Kind, threshold, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error checking {Name}", target.DisplayName);
            }
        }
        while (await WaitNext(timer, ct));
    }

    private static async Task<bool> WaitNext(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task SendAlertAsync(AlertStateMachine sm, AlertKind kind, MonitorTarget t, string checkKind, int threshold, CancellationToken ct)
    {
        string subject, body;
        lock (_stateLock) (subject, body) = sm.BuildEmail(kind, t, checkKind, threshold);


        var smtp = _config.Smtp;
        if (!smtp.IsConfigured)
        {
            lock (_stateLock) sm.MarkFailed("SMTP not configured", DateTimeOffset.Now);
            return;
        }

        await _emailGate.WaitAsync(ct);
        try
        {
            await EmailNotifier.SendAsync(smtp, subject, body, ct);
            lock (_stateLock) sm.MarkSent(kind, DateTimeOffset.Now);
            _log.LogInformation("Sent email: {Subject}", subject);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            lock (_stateLock) sm.MarkFailed(ex.Message, DateTimeOffset.Now);
            _log.LogError("Failed to send email \"{Subject}\": {Error}. Will retry later.", subject, ex.Message);
        }
        finally
        {
            _emailGate.Release();
            _statusDirty = true;
        }
    }

    private async Task StatusWriterLoop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            if (!_statusDirty) continue;
            _statusDirty = false;
            try
            {
                ServiceStatus snapshot;
                lock (_stateLock)
                {
                    snapshot = new ServiceStatus
                    {
                        UpdatedAt = DateTimeOffset.Now,
                        ServiceStarted = _started,
                        Targets = _states.Values.Select(s => Copy(s.Status)).ToList(),
                    };
                }
                _store.SaveStatus(snapshot);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not write status.json: {Error}", ex.Message);
            }
        }
        while (await WaitNext(timer, ct));
    }

    private static TargetStatus Copy(TargetStatus s) => new()
    {
        TargetId = s.TargetId, Name = s.Name, Address = s.Address, IsDown = s.IsDown,
        ConsecutiveFailures = s.ConsecutiveFailures, LastCheck = s.LastCheck, LastSuccess = s.LastSuccess,
        LastMessage = s.LastMessage, LatencyMs = s.LatencyMs, DownSince = s.DownSince,
        LastAlertSent = s.LastAlertSent, LastAlertError = s.LastAlertError,
    };
}
