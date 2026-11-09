using SiteMonitor.Core.Models;

namespace SiteMonitor.Gui;

internal sealed class GlobalSettingsForm : Form
{
    public GlobalSettingsForm(GlobalSettings settings)
    {
        FormHelpers.ConfigureDialog(this, "Default check settings", 460);
        var tips = new ToolTip();
        var g = FormHelpers.Grid();
        var interval = g.AddRow("Check every (seconds):", FormHelpers.Number(MonitorTarget.MinIntervalSeconds, 86400, settings.DefaultIntervalSeconds),
            "Default for monitors that use global defaults.", tips);
        var timeout = g.AddRow("Timeout (ms):", FormHelpers.Number(500, 120000, settings.DefaultTimeoutMs));
        timeout.Increment = 500;
        var threshold = g.AddRow("Alert after N failures:", FormHelpers.Number(1, 100, settings.DefaultFailThreshold),
            "Consecutive failures before a DOWN email is sent.", tips);
        var recovery = (CheckBox)g.AddFull(new CheckBox { Text = "Send an email when a site comes back up", AutoSize = true, Checked = settings.SendRecoveryEmails });
        var reminder = g.AddRow("Remind every (hours):", FormHelpers.Number(0, 168, settings.ReminderHours),
            "Re-send a STILL DOWN email every N hours while a site stays down. 0 = never.", tips);

        Controls.Add(g);
        Controls.Add(FormHelpers.DialogButtons(this, () =>
        {
            settings.DefaultIntervalSeconds = (int)interval.Value;
            settings.DefaultTimeoutMs = (int)timeout.Value;
            settings.DefaultFailThreshold = (int)threshold.Value;
            settings.SendRecoveryEmails = recovery.Checked;
            settings.ReminderHours = (int)reminder.Value;
            return true;
        }));
    }
}
