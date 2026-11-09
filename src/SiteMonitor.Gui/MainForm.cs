using System.ServiceProcess;
using SiteMonitor.Core.Checks;
using SiteMonitor.Core.Config;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Gui;

internal sealed class MainForm : Form
{
    private readonly ConfigStore _store;
    private AppConfig _config;
    private readonly DataGridView _grid = new();
    private readonly Label _serviceLabel = new() { AutoSize = true, Margin = new Padding(3, 8, 12, 3), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
    private readonly Label _smtpLabel = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
    private readonly Button _install = new() { Text = "Install service", AutoSize = true };
    private readonly Button _start = new() { Text = "Start", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true };
    private readonly Button _uninstall = new() { Text = "Uninstall", AutoSize = true };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 5000 };

    public MainForm(ConfigStore store)
    {
        _store = store;
        _config = LoadConfigSafe();

        Text = "SiteMonitor — Uptime monitoring";
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1050, 520);
        MinimumSize = new Size(760, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        // Toolbar
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        tools.Items.Add(new ToolStripButton("➕ Add monitor", null, (_, _) => AddTarget()));
        tools.Items.Add(new ToolStripButton("✎ Edit", null, (_, _) => EditSelected()));
        tools.Items.Add(new ToolStripButton("✖ Delete", null, (_, _) => DeleteSelected()));
        tools.Items.Add(new ToolStripSeparator());
        tools.Items.Add(new ToolStripButton("✉ Email (SMTP) settings", null, (_, _) => EditSmtp()));
        tools.Items.Add(new ToolStripButton("⚙ Default settings", null, (_, _) => EditGlobal()));
        tools.Items.Add(new ToolStripSeparator());
        tools.Items.Add(new ToolStripButton("📄 Open log folder", null, (_, _) => OpenLogs()));

        // Grid
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "On", HeaderText = "On", FillWeight = 22, ReadOnly = false });
        AddText("Name", 90); AddText("Address", 140); AddText("Check", 60); AddText("Every", 40);
        AddText("Alert at", 40); AddText("Status", 55); AddText("Last check", 70); AddText("Details", 170);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellValueChanged += OnEnabledToggled;
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) DeleteSelected(); };

        // Bottom bar: service control + SMTP state
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 4, 8, 6), WrapContents = true };
        bottom.Controls.Add(new Label { Text = "Background service:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        bottom.Controls.AddRange(new Control[] { _serviceLabel, _install, _start, _stop, _uninstall, _smtpLabel });
        _install.Click += (_, _) => ServiceAction(ServiceManager.Install, "installed and started");
        _start.Click += (_, _) => ServiceAction(ServiceManager.Start, "started");
        _stop.Click += (_, _) => ServiceAction(ServiceManager.Stop, "stopped");
        _uninstall.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Remove the background service? Monitoring will stop. Your settings are kept.",
                    "SiteMonitor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                ServiceAction(ServiceManager.Uninstall, "removed");
        };

        Controls.Add(_grid);
        Controls.Add(tools);
        Controls.Add(bottom);

        _refresh.Tick += (_, _) => RefreshStatus();
        _refresh.Start();
        RebuildGrid();
        RefreshStatus();

        Shown += (_, _) =>
        {
            if (!_config.Smtp.IsConfigured)
                MessageBox.Show(this, "Welcome! Set up Email (SMTP) settings so SiteMonitor can email you when a site goes down, " +
                    "then add the sites or machines to watch and click \"Install service\".", "SiteMonitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
    }

    private void AddText(string name, int weight) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = name, FillWeight = weight, ReadOnly = true });

    private AppConfig LoadConfigSafe()
    {
        try { return _store.LoadConfig(); }
        catch (Exception ex)
        {
            FormHelpers.Error(this, $"Could not read {_store.ConfigPath}:\n{ex.Message}\n\nStarting with an empty configuration.");
            return new AppConfig();
        }
    }

    private void SaveConfig()
    {
        try { _store.SaveConfig(_config); }
        catch (Exception ex) { FormHelpers.Error(this, "Could not save settings:\n" + ex.Message); }
        RebuildGrid();
        RefreshStatus();
    }

    private MonitorTarget? Selected =>
        _grid.SelectedRows.Count > 0 && _grid.SelectedRows[0].Tag is Guid id ? _config.Targets.FirstOrDefault(t => t.Id == id) : null;

    private void AddTarget()
    {
        using var dlg = new TargetDialog(new MonitorTarget(), _config.Global, isNew: true);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _config.Targets.Add(dlg.Result);
        SaveConfig();
    }

    private void EditSelected()
    {
        if (Selected is not { } t) return;
        using var dlg = new TargetDialog(t, _config.Global, isNew: false);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var i = _config.Targets.IndexOf(t);
        _config.Targets[i] = dlg.Result;
        SaveConfig();
    }

    private void DeleteSelected()
    {
        if (Selected is not { } t) return;
        if (MessageBox.Show(this, $"Stop monitoring {t.DisplayName}?", "Delete monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _config.Targets.Remove(t);
        SaveConfig();
    }

    private void OnEnabledToggled(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0 || _rebuilding) return;
        if (_grid.Rows[e.RowIndex].Tag is not Guid id || _config.Targets.FirstOrDefault(t => t.Id == id) is not { } t) return;
        t.Enabled = _grid.Rows[e.RowIndex].Cells[0].Value is true;
        BeginInvoke(SaveConfig); // can't rebuild the grid inside its own event
    }

    private void EditSmtp()
    {
        using var dlg = new SmtpSettingsForm(_config.Smtp);
        if (dlg.ShowDialog(this) == DialogResult.OK) SaveConfig();
    }

    private void EditGlobal()
    {
        using var dlg = new GlobalSettingsForm(_config.Global);
        if (dlg.ShowDialog(this) == DialogResult.OK) SaveConfig();
    }

    private void OpenLogs()
    {
        // Explorer runs un-elevated, so make sure the logs folder grants signed-in users read access.
        DataFolder.EnsureSecured(_store.DataDirectory, _store.LogDirectory);
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"\"{_store.LogDirectory}\"");
        }
        catch
        {
            // Fallback: Notepad started from this elevated process can always read the log.
            var log = Path.Combine(_store.LogDirectory, "service.log");
            if (File.Exists(log)) System.Diagnostics.Process.Start("notepad.exe", $"\"{log}\"");
            else FormHelpers.Error(this, "No log file yet. Is the service running?");
        }
    }

    private bool _rebuilding;
    private Font? _boldFontCache;
    private Font _boldFont => _boldFontCache ??= new Font(Font, FontStyle.Bold);

    private void RebuildGrid()
    {
        _rebuilding = true;
        try
        {
            var selectedId = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag : null;
            _grid.Rows.Clear();
            foreach (var t in _config.Targets)
            {
                var g = _config.Global;
                var check = CheckFactory.Create(t).Check.Kind;
                int i = _grid.Rows.Add(t.Enabled, t.DisplayName, t.Address, check,
                    $"{t.EffectiveInterval(g)}s", $"{t.EffectiveThreshold(g)} fails", "", "", "");
                _grid.Rows[i].Tag = t.Id;
                if (Equals(t.Id, selectedId)) _grid.Rows[i].Selected = true;
            }
        }
        finally { _rebuilding = false; }
    }

    private void RefreshStatus()
    {
        // Service state
        var svc = ServiceManager.GetStatus();
        _serviceLabel.Text = svc switch
        {
            null => "Not installed",
            ServiceControllerStatus.Running => "Running",
            ServiceControllerStatus.Stopped => "Stopped",
            { } s => s.ToString(),
        };
        _serviceLabel.ForeColor = svc == ServiceControllerStatus.Running ? Color.ForestGreen : Color.Firebrick;
        _install.Enabled = svc is null;
        _uninstall.Enabled = svc is not null;
        _start.Enabled = svc == ServiceControllerStatus.Stopped;
        _stop.Enabled = svc == ServiceControllerStatus.Running;

        _smtpLabel.Text = _config.Smtp.IsConfigured
            ? $"   Alerts go to: {_config.Smtp.To}  (via {_config.Smtp.Host})"
            : "   ⚠ Email not configured — click \"Email (SMTP) settings\"";
        _smtpLabel.ForeColor = _config.Smtp.IsConfigured ? SystemColors.ControlText : Color.DarkOrange;

        // Per-target status from the service
        ServiceStatus? status = null;
        try { status = _store.LoadStatus(); } catch { /* service mid-write; next tick */ }
        var byId = status?.Targets.ToDictionary(s => s.TargetId) ?? new();
        bool fresh = svc == ServiceControllerStatus.Running;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not Guid id) continue;
            var t = _config.Targets.FirstOrDefault(x => x.Id == id);
            string state, last = "", detail = "";
            Color color = SystemColors.GrayText;

            if (t is { Enabled: false }) state = "Paused";
            else if (!fresh) state = "—";
            else if (!byId.TryGetValue(id, out var s) || s.LastCheck is null) state = "Pending";
            else
            {
                last = s.LastCheck.Value.LocalDateTime.ToString("HH:mm:ss");
                if (s.IsDown)
                {
                    state = "DOWN";
                    color = Color.Firebrick;
                    detail = $"Down since {s.DownSince?.LocalDateTime:g}. {s.LastMessage}";
                }
                else if (s.LastSuccess == false)
                {
                    state = $"Failing {s.ConsecutiveFailures}/{t?.EffectiveThreshold(_config.Global)}";
                    color = Color.DarkOrange;
                    detail = s.LastMessage;
                }
                else
                {
                    state = "UP";
                    color = Color.ForestGreen;
                    detail = $"{s.LastMessage} · {s.LatencyMs} ms";
                }
                if (!string.IsNullOrEmpty(s.LastAlertError)) detail += $"  [email failed: {s.LastAlertError}]";
            }

            row.Cells["Status"].Value = state;
            row.Cells["Status"].Style.ForeColor = color;
            row.Cells["Status"].Style.Font = state is "UP" or "DOWN" ? _boldFont : Font;
            row.Cells["Last check"].Value = last;
            row.Cells["Details"].Value = detail;
        }
    }

    private void ServiceAction(Action action, string done)
    {
        UseWaitCursor = true;
        try
        {
            action();
            RefreshStatus();
            MessageBox.Show(this, $"Service {done}.", "SiteMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            FormHelpers.Error(this, ex.Message);
        }
        finally
        {
            UseWaitCursor = false;
            RefreshStatus();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refresh.Stop();
        base.OnFormClosed(e);
    }
}
