/**
 * @Module: Company.Core.Abstractions.Models
 * @Purpose: Định nghĩa cấu trúc dữ liệu DTO/POCO cho toàn bộ hệ thống Crane Wi-Fi Controller (Cấu hình, Thông tin Wi-Fi, BSS, Trạng thái Ping, Card mạng, Nhật ký).
 * @Dependencies: Company.Core.Abstractions.Enums.
 * @Reusability: Độc lập hoàn toàn, dễ dàng serialize/deserialize JSON, dùng được cho cả Backend, Console CLI và Giao diện UI.
 */

using Company.Core.Abstractions.Enums;

namespace Company.Core.Abstractions.Models;

/// <summary>
/// Cấu hình can thiệp hệ điều hành Windows và vị trí hiển thị giao diện.
/// </summary>
public class SystemSettings
{
    public bool DisableAirplaneModeOnStartup { get; set; } = true;
    public bool DisableTabletModeOnStartup { get; set; } = true;
    public int EnforceOsModesIntervalSeconds { get; set; } = 10;
    public bool StartInMiniWidgetMode { get; set; } = true;
    public double WidgetPositionX { get; set; } = -1;
    public double WidgetPositionY { get; set; } = -1;
    public bool AutoStartWithWindows { get; set; } = true;
}

/// <summary>
/// Định nghĩa thông tin máy chủ mục tiêu cần ping giám sát kết nối.
/// </summary>
public class TargetServer
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Cấu hình cho dịch vụ Connection Watchdog và cứu hộ reset card mạng.
/// </summary>
public class WatchdogSettings
{
    public int CheckIntervalSeconds { get; set; } = 5;
    public int PingTimeoutMs { get; set; } = 1500;
    public int MaxDisconnectedSecondsBeforeReset { get; set; } = 45;
    public int ResetCooldownSeconds { get; set; } = 60;
    public string? PreferredAdapterName { get; set; } = null;
    public List<TargetServer> TargetServers { get; set; } = new();
}

/// <summary>
/// Thông tin mạng Wi-Fi ưu tiên cần tự động kết nối theo thứ tự.
/// </summary>
public class PreferredNetwork
{
    public string Ssid { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
    public bool AutoConnect { get; set; } = true;
}

/// <summary>
/// Cấu hình cho thuật toán Smart Roaming và chuyển mạch Wi-Fi khi mất ping (Failover).
/// </summary>
public class SmartWifiSettings
{
    public int PassiveCheckIntervalSeconds { get; set; } = 2;
    public int BssCacheReadIntervalSeconds { get; set; } = 10;
    public int WeakSignalThresholdPercent { get; set; } = 45;
    public int RoamingHysteresisDeltaPercent { get; set; } = 15;
    public bool EnableSmartRoaming { get; set; } = true;
    public bool EnablePingFailWifiSwitch { get; set; } = true;
    public int PingFailWifiSwitchSeconds { get; set; } = 15;
    public int WifiBlacklistDurationSeconds { get; set; } = 300;
    public List<PreferredNetwork> PreferredNetworks { get; set; } = new();
}

/// <summary>
/// Thông tin mạng Wi-Fi đang bị cách ly tạm thời (Blacklist/Quarantine).
/// </summary>
public class BlacklistedWifiInfo
{
    public string Ssid { get; set; } = string.Empty;
    public string? Bssid { get; set; }
    public DateTime QuarantinedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiryUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = string.Empty;
    public double RemainingSeconds => Math.Max(0, (ExpiryUtc - DateTime.UtcNow).TotalSeconds);
}

/// <summary>
/// Cấu hình gốc tổng thể của ứng dụng (Root Configuration).
/// </summary>
public class AppConfiguration
{
    public SystemSettings SystemSettings { get; set; } = new();
    public WatchdogSettings WatchdogSettings { get; set; } = new();
    public SmartWifiSettings SmartWifiSettings { get; set; } = new();
}

/// <summary>
/// Thông tin chi tiết về liên kết Wi-Fi hiện hành của máy tính.
/// </summary>
public class WifiConnectionInfo
{
    public WifiState State { get; set; } = WifiState.Disconnected;
    public string Ssid { get; set; } = string.Empty;
    public string Bssid { get; set; } = string.Empty;
    public int SignalQualityPercent { get; set; }
    public int RssiDbm { get; set; } = -100;
    public int Channel { get; set; }
    public string Band { get; set; } = string.Empty;
    public string InterfaceName { get; set; } = string.Empty;
    public Guid InterfaceId { get; set; }
    public long? LinkSpeedMbps { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Thông tin về một trạm phát sóng (BSS) trong danh sách quét Native API.
/// </summary>
public class BssNetworkInfo
{
    public string Ssid { get; set; } = string.Empty;
    public string Bssid { get; set; } = string.Empty;
    public int SignalQualityPercent { get; set; }
    public int RssiDbm { get; set; }
    public int Channel { get; set; }
    public string Band { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
}

/// <summary>
/// Kết quả kiểm tra phản hồi ICMP Ping tới một máy chủ.
/// </summary>
public class ServerPingStatus
{
    public TargetServer Target { get; set; } = new();
    public bool IsOnline { get; set; }
    public long RoundtripTimeMs { get; set; } = -1;
    public DateTime LastCheckedUtc { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Thông tin về một Card mạng vật lý/logic trên hệ điều hành.
/// </summary>
public class NetworkAdapterInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsWireless { get; set; }
    public bool IsOperational { get; set; }
    public string MacAddress { get; set; } = string.Empty;
}

/// <summary>
/// Trạng thái tổng hợp về các chế độ Windows (Airplane, Tablet, Task Scheduler).
/// </summary>
public class SystemModeStatus
{
    public bool IsAirplaneModeDisabled { get; set; }
    public bool IsTabletModeDisabled { get; set; }
    public bool IsTaskSchedulerRegistered { get; set; }
    public DateTime LastEnforcedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Bản ghi nhật ký hoạt động của ứng dụng (Log Entry).
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public AppLogLevel Level { get; set; } = AppLogLevel.Info;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
