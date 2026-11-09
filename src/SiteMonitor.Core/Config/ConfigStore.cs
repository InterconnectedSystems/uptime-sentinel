using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SiteMonitor.Core.Models;

namespace SiteMonitor.Core.Config;

/// <summary>Compile-time JSON metadata: no reflection, safe to trim, faster startup.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(ServiceStatus))]
internal sealed partial class SiteMonitorJsonContext : JsonSerializerContext;

/// <summary>Reads/writes JSON files in the shared data folder (C:\ProgramData\SiteMonitor on Windows).</summary>
public sealed class ConfigStore
{
    public string DataDirectory { get; }
    public string ConfigPath => Path.Combine(DataDirectory, "config.json");
    public string StatusPath => Path.Combine(DataDirectory, "status.json");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public ConfigStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory
            ?? Environment.GetEnvironmentVariable("SITEMONITOR_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SiteMonitor");
    }

    public AppConfig LoadConfig() => Read(ConfigPath, SiteMonitorJsonContext.Default.AppConfig) ?? new AppConfig();
    public void SaveConfig(AppConfig config) => WriteAtomic(ConfigPath, config, SiteMonitorJsonContext.Default.AppConfig);

    public ServiceStatus? LoadStatus() => Read(StatusPath, SiteMonitorJsonContext.Default.ServiceStatus);
    public void SaveStatus(ServiceStatus status) => WriteAtomic(StatusPath, status, SiteMonitorJsonContext.Default.ServiceStatus);

    private static T? Read<T>(string path, JsonTypeInfo<T> typeInfo) where T : class
    {
        // The other process may be mid-rename; retry briefly on sharing violations.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize(fs, typeInfo);
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }

    private void WriteAtomic<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        Directory.CreateDirectory(DataDirectory);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(50);
            }
        }
    }
}
