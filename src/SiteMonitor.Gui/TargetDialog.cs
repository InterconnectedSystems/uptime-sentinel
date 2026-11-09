using SiteMonitor.Core.Checks;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Gui;

internal sealed class TargetDialog : Form
{
    private readonly MonitorTarget _target;
    private readonly GlobalSettings _global;
    private readonly TextBox _name = new() { Width = 320 };
    private readonly TextBox _address = new() { Width = 320, PlaceholderText = "e.g. https://example.com, example.com or 192.168.1.10" };
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Anchor = AnchorStyles.Left };
    private readonly CheckBox _useDefaults = new() { Text = "Use global defaults for interval / timeout / failures", AutoSize = true };
    private readonly NumericUpDown _interval;
    private readonly NumericUpDown _timeout;
    private readonly NumericUpDown _threshold;
    private readonly NumericUpDown _port = FormHelpers.Number(0, 65535, 0);
    private readonly NumericUpDown _status = FormHelpers.Number(0, 599, 0);
    private readonly TextBox _keyword = new() { Width = 320 };
    private readonly CheckBox _enabled = new() { Text = "Enabled", AutoSize = true, Checked = true };
    private readonly Label _resolved = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _test = new() { Text = "Test now", Width = 100, Anchor = AnchorStyles.Left };

    public TargetDialog(MonitorTarget target, GlobalSettings global, bool isNew)
    {
        _target = target.Clone();
        _global = global;
        FormHelpers.ConfigureDialog(this, isNew ? "Add monitor" : "Edit monitor", 520);

        _interval = FormHelpers.Number(MonitorTarget.MinIntervalSeconds, 86400, target.IntervalSeconds ?? global.DefaultIntervalSeconds);
        _timeout = FormHelpers.Number(500, 120000, target.TimeoutMs ?? global.DefaultTimeoutMs);
        _timeout.Increment = 500;
        _threshold = FormHelpers.Number(1, 100, target.FailThreshold ?? global.DefaultFailThreshold);

        _type.Items.AddRange(new object[] { "Auto (recommended)", "Ping (ICMP)", "TCP port", "HTTP / HTTPS" });
        var tips = new ToolTip();
        var g = FormHelpers.Grid();
        g.AddRow("Name:", _name, "Friendly name used in alert emails (optional).", tips);
        g.AddRow("Address:", _address, "Website URL, domain name, or IP address.", tips);
        g.AddRow("Check type:", _type,
            "Auto: URLs use HTTP; bare hosts/IPs are pinged, then TCP 443/80 is tried (many hosts block ping).", tips);
        g.AddRow("", _resolved);
        g.AddFull(_useDefaults);
        g.AddRow("Check every (seconds):", _interval, $"How often to check. Minimum {MonitorTarget.MinIntervalSeconds}s.", tips);
        g.AddRow("Timeout (ms):", _timeout, "How long to wait for a response before counting a failure.", tips);
        g.AddRow("Alert after N failures:", _threshold, "Consecutive failed checks before the DOWN email is sent.", tips);
        g.AddRow("TCP port:", _port, "TCP checks: port to connect to. 0 = default (443).", tips);
        g.AddRow("Expected HTTP status:", _status, "HTTP checks: required status code. 0 = any 2xx/3xx.", tips);
        g.AddRow("Must contain text:", _keyword, "HTTP checks: page must contain this text (optional).", tips);
        g.AddFull(_enabled);
        g.AddRow("", _test);

        Controls.Add(g);
        Controls.Add(FormHelpers.DialogButtons(this, Save));

        _name.Text = target.Name;
        _address.Text = target.Address;
        _type.SelectedIndex = (int)target.CheckType;
        _useDefaults.Checked = target.IntervalSeconds is null && target.TimeoutMs is null && target.FailThreshold is null;
        _port.Value = target.Port ?? 0;
        _status.Value = target.ExpectedStatus ?? 0;
        _keyword.Text = target.Keyword ?? "";
        _enabled.Checked = target.Enabled;

        _useDefaults.CheckedChanged += (_, _) => UpdateEnabled();
        _type.SelectedIndexChanged += (_, _) => UpdateEnabled();
        _address.TextChanged += (_, _) => UpdateEnabled();
        _port.ValueChanged += (_, _) => UpdateEnabled();
        _test.Click += async (_, _) => await TestAsync();
        UpdateEnabled();
    }

    public MonitorTarget Result => _target;

    private void UpdateEnabled()
    {
        _interval.Enabled = _timeout.Enabled = _threshold.Enabled = !_useDefaults.Checked;
        if (_useDefaults.Checked)
        {
            _interval.Value = _global.DefaultIntervalSeconds;
            _timeout.Value = _global.DefaultTimeoutMs;
            _threshold.Value = _global.DefaultFailThreshold;
        }
        var t = BuildTarget();
        var type = CheckFactory.ResolveType(t);
        _port.Enabled = type == CheckType.Tcp || (t.CheckType == CheckType.Auto && !CheckFactory.TryGetUri(t.Address, out _));
        _status.Enabled = _keyword.Enabled = type == CheckType.Http;
        _resolved.Text = string.IsNullOrWhiteSpace(t.Address) ? "" : "Will check using: " + CheckFactory.Create(t).Check.Kind;
    }

    private MonitorTarget BuildTarget()
    {
        var t = _target.Clone();
        t.Name = _name.Text.Trim();
        t.Address = _address.Text.Trim();
        t.CheckType = (CheckType)Math.Max(0, _type.SelectedIndex);
        bool custom = !_useDefaults.Checked;
        t.IntervalSeconds = custom ? (int)_interval.Value : null;
        t.TimeoutMs = custom ? (int)_timeout.Value : null;
        t.FailThreshold = custom ? (int)_threshold.Value : null;
        t.Port = _port.Value > 0 ? (int)_port.Value : null;
        t.ExpectedStatus = _status.Value > 0 ? (int)_status.Value : null;
        t.Keyword = string.IsNullOrWhiteSpace(_keyword.Text) ? null : _keyword.Text.Trim();
        t.Enabled = _enabled.Checked;
        return t;
    }

    private bool Save()
    {
        var t = BuildTarget();
        var errors = t.Validate();
        if (errors.Count > 0)
        {
            FormHelpers.Error(this, string.Join("\n", errors));
            return false;
        }
        _target.Name = t.Name;
        _target.Address = t.Address;
        _target.CheckType = t.CheckType;
        _target.IntervalSeconds = t.IntervalSeconds;
        _target.TimeoutMs = t.TimeoutMs;
        _target.FailThreshold = t.FailThreshold;
        _target.Port = t.Port;
        _target.ExpectedStatus = t.ExpectedStatus;
        _target.Keyword = t.Keyword;
        _target.Enabled = t.Enabled;
        return true;
    }

    private async Task TestAsync()
    {
        var t = BuildTarget();
        if (string.IsNullOrWhiteSpace(t.Address)) { FormHelpers.Error(this, "Enter an address first."); return; }
        _test.Enabled = false;
        _test.Text = "Testing...";
        try
        {
            var rc = CheckFactory.Create(t);
            var r = await rc.Check.RunAsync(rc.Endpoint, t.EffectiveTimeout(_global), CancellationToken.None);
            MessageBox.Show(this,
                $"{(r.Success ? "UP" : "FAILED")}\n\n{rc.Check.Kind} → {rc.Endpoint}\n{r.Message}\n{r.LatencyMs} ms",
                "Test result", MessageBoxButtons.OK, r.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            _test.Enabled = true;
            _test.Text = "Test now";
        }
    }
}
