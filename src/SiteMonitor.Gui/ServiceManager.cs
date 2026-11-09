using System.Diagnostics;
using System.ServiceProcess;

namespace SiteMonitor.Gui;

/// <summary>Install/uninstall/start/stop the SiteMonitor Windows Service (GUI runs elevated).</summary>
internal static class ServiceManager
{
    public const string ServiceName = "SiteMonitor";
    public const string DisplayName = "Site Monitor";
    public const string ServiceExe = "SiteMonitorService.exe";

    public static string ServiceExePath => Path.Combine(AppContext.BaseDirectory, ServiceExe);

    public static ServiceControllerStatus? GetStatus()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status;
        }
        catch (InvalidOperationException)
        {
            return null; // not installed
        }
    }

    public static void Install()
    {
        if (!File.Exists(ServiceExePath))
            throw new FileNotFoundException($"{ServiceExe} must be in the same folder as SiteMonitor.exe.", ServiceExePath);

        RunSc($"create {ServiceName} binPath= \"\\\"{ServiceExePath}\\\"\" start= delayed-auto DisplayName= \"{DisplayName}\"");
        RunSc($"description {ServiceName} \"Monitors websites and hosts and emails an alert when they go down.\"");
        // Restart automatically if the process ever crashes.
        RunSc($"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/300000", ignoreErrors: true);
        Start();
    }

    public static void Uninstall()
    {
        if (GetStatus() is { } s && s != ServiceControllerStatus.Stopped) Stop();
        RunSc($"delete {ServiceName}");
    }

    public static void Start()
    {
        using var sc = new ServiceController(ServiceName);
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
    }

    public static void Stop()
    {
        using var sc = new ServiceController(ServiceName);
        if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending) return;
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
    }

    private static void RunSc(string args, bool ignoreErrors = false)
    {
        var psi = new ProcessStartInfo("sc.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 && !ignoreErrors)
            throw new InvalidOperationException($"sc.exe {args.Split(' ')[0]} failed ({p.ExitCode}):\n{output.Trim()}");
    }
}
