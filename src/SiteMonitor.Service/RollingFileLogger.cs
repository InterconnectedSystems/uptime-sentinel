using System.Text;

namespace SiteMonitor.Service;

/// <summary>Minimal size-capped file logger: service.log rolls at 1 MB, keeps 3 old files.</summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 1024 * 1024;
    private const int KeepFiles = 3;
    private readonly string _dir;
    private readonly object _lock = new();

    public RollingFileLoggerProvider(string dir)
    {
        _dir = dir;
        try { Directory.CreateDirectory(dir); } catch { }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this);
    public void Dispose() { }

    private void Write(LogLevel level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ')
            .Append(level switch
            {
                LogLevel.Warning => "WARN ", LogLevel.Error => "ERROR", LogLevel.Critical => "CRIT ",
                LogLevel.Debug => "DEBUG", LogLevel.Trace => "TRACE", _ => "INFO "
            })
            .Append(' ').Append(message);
        if (ex is not null) line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        line.AppendLine();

        lock (_lock)
        {
            try
            {
                var path = Path.Combine(_dir, "service.log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes) Roll(path);
                File.AppendAllText(path, line.ToString());
            }
            catch { /* logging must never crash the service */ }
        }
    }

    private static void Roll(string path)
    {
        for (int i = KeepFiles - 1; i >= 1; i--)
        {
            var src = $"{path}.{i}";
            if (File.Exists(src)) File.Move(src, $"{path}.{i + 1}", true);
        }
        File.Move(path, $"{path}.1", true);
        var oldest = $"{path}.{KeepFiles + 1}";
        if (File.Exists(oldest)) File.Delete(oldest);
    }

    private sealed class FileLogger : ILogger
    {
        private readonly RollingFileLoggerProvider _p;
        public FileLogger(RollingFileLoggerProvider p) => _p = p;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) _p.Write(logLevel, formatter(state, exception), exception);
        }
    }
}
