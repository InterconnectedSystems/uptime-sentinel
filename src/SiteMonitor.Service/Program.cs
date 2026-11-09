using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.EventLog;
using SiteMonitor.Core.Config;
using SiteMonitor.Service;

var store = new ConfigStore();
DataFolder.EnsureSecured(store.DataDirectory, store.LogDirectory);

// Empty builder: no appsettings/env/command-line config providers or file watchers on the
// content root, and no default console/debug/EventSource loggers. Keeps the service lean.
var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Services.AddWindowsService(o => o.ServiceName = ServiceInfo.Name);

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
builder.Logging.AddProvider(new RollingFileLoggerProvider(store.LogDirectory));
if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(new EventLogSettings { SourceName = ServiceInfo.Name, LogName = "Application" });
    builder.Logging.AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Warning);
}
if (!WindowsServiceHelpers.IsWindowsService()) builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

builder.Services.AddSingleton(store);
builder.Services.AddHostedService<Worker>();

builder.Build().Run();

public static class ServiceInfo
{
    public const string Name = "SiteMonitor";
}
