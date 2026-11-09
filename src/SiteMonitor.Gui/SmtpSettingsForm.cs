using System.Diagnostics;
using SiteMonitor.Core.Alerting;
using SiteMonitor.Core.Config;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Gui;

internal sealed class SmtpSettingsForm : Form
{
    private readonly SmtpSettings _settings;
    private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly Label _tier = new() { AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
    private readonly Label _notes = new() { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = SystemColors.GrayText };
    private readonly LinkLabel _signup = new() { AutoSize = true };
    private readonly TextBox _host = new() { Width = 320 };
    private readonly NumericUpDown _port = FormHelpers.Number(1, 65535, 587);
    private readonly ComboBox _security = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Anchor = AnchorStyles.Left };
    private readonly TextBox _user = new() { Width = 320 };
    private readonly TextBox _password = new() { Width = 320, UseSystemPasswordChar = true };
    private readonly TextBox _from = new() { Width = 320, PlaceholderText = "alerts@yourdomain.com" };
    private readonly TextBox _to = new() { Width = 320, PlaceholderText = "you@example.com, team@example.com" };
    private readonly Button _test = new() { Text = "Send test email", Width = 130, Anchor = AnchorStyles.Left };
    private bool _passwordChanged;
    private bool _loading;

    public SmtpSettingsForm(SmtpSettings settings)
    {
        _settings = settings;
        FormHelpers.ConfigureDialog(this, "Email (SMTP) settings", 560);

        foreach (var p in SmtpPresets.All) _preset.Items.Add(p.Name);
        _security.Items.AddRange(new object[] { "None (port 25)", "STARTTLS (port 587)", "SSL/TLS (port 465)" });

        var tips = new ToolTip();
        var g = FormHelpers.Grid();
        g.AddRow("Provider:", _preset, "Pick a provider to fill in the server settings, or Custom to enter your own.", tips);
        g.AddRow("", _tier);
        g.AddRow("", _notes);
        g.AddRow("", _signup);
        g.AddRow("SMTP server:", _host);
        g.AddRow("Port:", _port);
        g.AddRow("Security:", _security);
        g.AddRow("Username:", _user, "Leave blank if your relay does not require login (e.g. internal relay by IP).", tips);
        g.AddRow("Password / API key:", _password, "Stored encrypted (Windows DPAPI).", tips);
        g.AddRow("From address:", _from, "Must be an address your provider allows you to send from.", tips);
        g.AddRow("Send alerts to:", _to, "One or more addresses, separated by commas.", tips);
        g.AddRow("", _test);
        g.AddFull(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = SystemColors.GrayText,
            Text = "Note: there is no legitimate free anonymous SMTP relay — open relays are blocked as spam sources. " +
                   "The listed providers have free tiers; sign up, then enter the credentials they give you.",
        });

        Controls.Add(g);
        Controls.Add(FormHelpers.DialogButtons(this, Save));

        _loading = true;
        _preset.SelectedItem = SmtpPresets.Find(settings.Preset)?.Name ?? SmtpPresets.CustomName;
        _host.Text = settings.Host;
        _port.Value = Math.Clamp(settings.Port, 1, 65535);
        _security.SelectedIndex = (int)settings.Security;
        _user.Text = settings.Username;
        _password.Text = string.IsNullOrEmpty(settings.ProtectedPassword) ? "" : "••••••••";
        _from.Text = settings.From;
        _to.Text = settings.To;
        _loading = false;
        ShowPresetInfo();

        _preset.SelectedIndexChanged += (_, _) => OnPresetChanged();
        _security.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _port.Value = _security.SelectedIndex switch { 0 => 25, 2 => 465, _ => 587 };
        };
        _password.TextChanged += (_, _) => { if (!_loading) _passwordChanged = true; };
        _password.Enter += (_, _) => { if (!_passwordChanged) _password.SelectAll(); };
        _signup.LinkClicked += (_, _) =>
        {
            if (_signup.Tag is string url && url.Length > 0)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };
        _test.Click += async (_, _) => await TestAsync();
    }

    private void OnPresetChanged()
    {
        var p = SmtpPresets.Find(_preset.SelectedItem as string ?? "") ?? SmtpPresets.All[0];
        if (p.Name != SmtpPresets.CustomName)
        {
            _loading = true;
            _host.Text = p.Host;
            _port.Value = p.Port;
            _security.SelectedIndex = (int)p.Security;
            _loading = false;
        }
        ShowPresetInfo();
    }

    private void ShowPresetInfo()
    {
        var p = SmtpPresets.Find(_preset.SelectedItem as string ?? "") ?? SmtpPresets.All[0];
        _tier.Text = string.IsNullOrEmpty(p.FreeTier) ? "" : "Free tier: " + p.FreeTier;
        _notes.Text = p.Notes;
        _signup.Text = string.IsNullOrEmpty(p.SignupUrl) ? "" : "Sign up / instructions: " + p.SignupUrl;
        _signup.Tag = p.SignupUrl;
    }

    private SmtpSettings Build()
    {
        return new SmtpSettings
        {
            Preset = _preset.SelectedItem as string ?? SmtpPresets.CustomName,
            Host = _host.Text.Trim(),
            Port = (int)_port.Value,
            Security = (SmtpSecurity)Math.Max(0, _security.SelectedIndex),
            Username = _user.Text.Trim(),
            ProtectedPassword = _passwordChanged ? SecretProtector.Protect(_password.Text) : _settings.ProtectedPassword,
            From = _from.Text.Trim(),
            To = _to.Text.Trim(),
        };
    }

    private string? Validate(SmtpSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.Host)) return "Enter the SMTP server.";
        if (!MimeKit.MailboxAddress.TryParse(s.From, out _)) return "Enter a valid From address.";
        if (s.Recipients().Count == 0) return "Enter at least one recipient.";
        foreach (var r in s.Recipients())
            if (!MimeKit.MailboxAddress.TryParse(r, out _)) return $"Invalid recipient: {r}";
        return null;
    }

    private bool Save()
    {
        var s = Build();
        if (Validate(s) is { } err) { FormHelpers.Error(this, err); return false; }
        _settings.Preset = s.Preset;
        _settings.Host = s.Host;
        _settings.Port = s.Port;
        _settings.Security = s.Security;
        _settings.Username = s.Username;
        _settings.ProtectedPassword = s.ProtectedPassword;
        _settings.From = s.From;
        _settings.To = s.To;
        return true;
    }

    private async Task TestAsync()
    {
        var s = Build();
        if (Validate(s) is { } err) { FormHelpers.Error(this, err); return; }
        _test.Enabled = false;
        _test.Text = "Sending...";
        UseWaitCursor = true;
        try
        {
            await EmailNotifier.SendAsync(s, "[SiteMonitor] Test email",
                $"This is a test email from SiteMonitor on {Environment.MachineName}.\n\nIf you received this, alert emails are working.",
                CancellationToken.None);
            MessageBox.Show(this, "Test email sent. Check the inbox (and spam folder) of: " + s.To,
                "SiteMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            FormHelpers.Error(this, "Sending failed:\n\n" + ex.Message);
        }
        finally
        {
            UseWaitCursor = false;
            _test.Enabled = true;
            _test.Text = "Send test email";
        }
    }
}
