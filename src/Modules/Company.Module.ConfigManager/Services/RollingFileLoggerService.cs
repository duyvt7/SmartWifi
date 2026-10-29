using System.Collections.Concurrent;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;

namespace Company.Module.ConfigManager.Services;

public class RollingFileLoggerService : ILoggerService
{
    private readonly string _logsDirectory;
    private readonly ConcurrentQueue<LogEntry> _recentLogs = new();
    private readonly int _maxRecentLogs;
    private readonly object _fileLock = new();
    private DateTime _lastCleanupDate = DateTime.MinValue;

    public event EventHandler<LogEntry>? LogEmitted;

    public RollingFileLoggerService(string? logsDirectory = null, int maxRecentLogs = 200)
    {
        _logsDirectory = logsDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        _maxRecentLogs = maxRecentLogs;

        try
        {
            if (!Directory.Exists(_logsDirectory))
            {
                Directory.CreateDirectory(_logsDirectory);
            }
        }
        catch
        {
            // Ignore directory creation failure on readonly filesystems
        }
    }

    public void Log(AppLogLevel level, string source, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Source = source,
            Message = message
        };

        _recentLogs.Enqueue(entry);
        while (_recentLogs.Count > _maxRecentLogs && _recentLogs.TryDequeue(out _)) { }

        WriteToFile(entry);

        try
        {
            LogEmitted?.Invoke(this, entry);
        }
        catch
        {
            // Avoid handler exception bubbling
        }
    }

    public IReadOnlyList<LogEntry> GetRecentLogs(int maxCount = 100)
    {
        return _recentLogs.TakeLast(Math.Min(maxCount, _recentLogs.Count)).ToList();
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            lock (_fileLock)
            {
                if (!Directory.Exists(_logsDirectory))
                {
                    Directory.CreateDirectory(_logsDirectory);
                }

                var today = DateTime.Today;
                var fileName = $"crane-wifi-{today:yyyyMMdd}.log";
                var filePath = Path.Combine(_logsDirectory, fileName);

                var logLine = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level.ToString().ToUpperInvariant()}] [{entry.Source}] {entry.Message}{Environment.NewLine}";
                File.AppendAllText(filePath, logLine);

                // Run cleanup once per day
                if (_lastCleanupDate != today)
                {
                    CleanupOldLogs(retentionDays: 7);
                    _lastCleanupDate = today;
                }
            }
        }
        catch
        {
            // Fail silently to avoid crashing mission-critical threads
        }
    }

    private void CleanupOldLogs(int retentionDays)
    {
        try
        {
            var dir = new DirectoryInfo(_logsDirectory);
            if (!dir.Exists) return;

            var threshold = DateTime.Now.AddDays(-retentionDays);
            foreach (var file in dir.GetFiles("crane-wifi-*.log"))
            {
                if (file.LastWriteTime < threshold)
                {
                    try { file.Delete(); } catch { }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

