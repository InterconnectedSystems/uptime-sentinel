using SiteMonitor.Core.Config;

namespace SiteMonitor.Gui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Global\SiteMonitor.Gui", out bool first);
        if (!first)
        {
            MessageBox.Show("SiteMonitor settings is already open.", "SiteMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var store = new ConfigStore();
        DataFolder.EnsureSecured(store.DataDirectory, store.LogDirectory);
        Application.Run(new MainForm(store));
    }
}
