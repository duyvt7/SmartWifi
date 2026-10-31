/**
 * @Module: Company.Core.Abstractions.Events
 * @Purpose: Định nghĩa các lớp EventArgs truyền phát sự kiện trong hệ thống (Roaming, Failover, Reset card mạng, Watchdog Tick).
 * @Dependencies: Company.Core.Abstractions.Models.
 * @Reusability: Chuẩn EventArgs decoupling hoàn toàn giữa Services và UI / Logging handlers.
 */

using Company.Core.Abstractions.Models;

namespace Company.Core.Abstractions.Events;

/// <summary>
/// Dữ liệu sự kiện khi hệ thống thực hiện chuyển vùng Access Point (Roaming thành công).
/// </summary>
public class WifiRoamingEventArgs : EventArgs
{
    public string Ssid { get; set; } = string.Empty;
    public string FromBssid { get; set; } = string.Empty;
    public string ToBssid { get; set; } = string.Empty;
    public int OldSignalQuality { get; set; }
    public int NewSignalQuality { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Dữ liệu sự kiện khi hệ thống kích hoạt ngắt kết nối và chuyển sang mạng Wi-Fi khác (Failover do mất Ping).
/// </summary>
public class WifiFailoverEventArgs : EventArgs
{
    public string PreviousSsid { get; set; } = string.Empty;
    public string? PreviousBssid { get; set; }
    public string TargetSsid { get; set; } = string.Empty;
    public int DisconnectedSeconds { get; set; }
    public int BlacklistDurationSeconds { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Dữ liệu sự kiện khi Watchdog bắt đầu quy trình reset khẩn cấp card mạng.
/// </summary>
public class AdapterResettingEventArgs : EventArgs
{
    public string AdapterName { get; set; } = string.Empty;
    public int DisconnectedDurationSeconds { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Dữ liệu sự kiện định kỳ của Watchdog sau khi hoàn tất chu kỳ Ping máy chủ.
/// </summary>
public class WatchdogTickEventArgs : EventArgs
{
    public IReadOnlyList<ServerPingStatus> ServerStatuses { get; set; } = Array.Empty<ServerPingStatus>();
    public bool AnyServerOnline { get; set; }
    public int CurrentDisconnectedDurationSeconds { get; set; }
    public long BestLatencyMs { get; set; } = -1;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
