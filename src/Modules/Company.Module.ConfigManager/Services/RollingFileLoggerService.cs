/**
 * @Module: Company.Module.ConfigManager.RollingFileLoggerService
 * @Purpose: Dịch vụ ghi nhật ký xoay vòng theo ngày (Rolling File Logger), tự động dọn dẹp file log cũ quá hạn (mặc định giữ 7 ngày), đồng thời lưu bộ đệm gần nhất trong ConcurrentQueue phục vụ UI hiển thị tức thì.
 * @Dependencies: Company.Core.Abstractions (Enums, Interfaces, Models).
 * @Reusability: Cung cấp thư mục log và số lượng log đệm tối đa qua constructor. Sẵn sàng nhúng vào bất kỳ ứng dụng .NET nào mà không phụ thuộc thư viện log bên thứ ba (zero 3rd-party dependencies).
 */

using System.Collections.Concurrent;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;

namespace Company.Module.ConfigManager.Services;

/// <summary>
/// Dịch vụ ghi log cuốn chiếu theo ngày độc lập và bộ nhớ đệm RAM.
/// </summary>
public class RollingFileLoggerService : ILoggerService
{
    private readonly string _logsDirectory;
    private readonly ConcurrentQueue<LogEntry> _recentLogs = new();
    private readonly int _maxRecentLogs;
    private readonly object _fileLock = new();
    private DateTime _lastCleanupDate = DateTime.MinValue;

    /// <summary>
    /// Sự kiện kích hoạt khi có dòng log mới được ghi.
    /// </summary>
    public event EventHandler<LogEntry>? LogEmitted;

    /// <summary>
    /// Khởi tạo dịch vụ ghi nhật ký với tùy chọn thư mục log và dung lượng bộ nhớ đệm RAM.
    /// </summary>
    /// <param name="logsDirectory">Thư mục chứa file log (mặc định: subfolder 'logs').</param>
    /// <param name="maxRecentLogs">Số lượng dòng log tối đa lưu trong RAM phục vụ UI.</param>
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
            // Bỏ qua lỗi tạo thư mục trên hệ thống chỉ đọc
        }
    }

    /// <summary>
    /// Ghi nhận 1 thông điệp log vào bộ nhớ đệm và file đĩa.
    /// </summary>
    /// <param name="level">Phân cấp log (Info, Warning, Error, Success).</param>
    /// <param name="source">Tên module hoặc tác vụ phát sinh log.</param>
    /// <param name="message">Nội dung chi tiết của log.</param>
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
            // Bảo vệ luồng ghi log không bị dừng nếu UI handler xảy ra lỗi
        }
    }

    /// <summary>
    /// Lấy danh sách các dòng log gần đây nhất trong bộ nhớ đệm.
    /// </summary>
    /// <param name="maxCount">Số dòng log tối đa.</param>
    /// <returns>Danh sách các bản ghi log.</returns>
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

                // Dọn dẹp file log cũ mỗi ngày một lần
                if (_lastCleanupDate != today)
                {
                    CleanupOldLogs(retentionDays: 7);
                    _lastCleanupDate = today;
                }
            }
        }
        catch
        {
            // Nuốt lỗi ghi file để bảo vệ các tác vụ mạng quan trọng không bị crash
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
            // Bỏ qua lỗi dọn dẹp file cũ
        }
    }
}
