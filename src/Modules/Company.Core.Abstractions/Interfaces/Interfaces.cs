/**
 * @Module: Company.Core.Abstractions.Interfaces
 * @Purpose: Định nghĩa toàn bộ Contracts/Interfaces cốt lõi của hệ thống Crane Wi-Fi Controller (Cấu hình, Logging, Can thiệp OS, Quản lý Card mạng, Watchdog và Smart Wi-Fi Roaming & Failover).
 * @Dependencies: Company.Core.Abstractions (Enums, Events, Models). Không phụ thuộc bên ngoài.
 * @Reusability: Chuẩn Interface trừu tượng độc lập, sẵn sàng tái sử dụng cho bất kỳ hệ thống IoT/Desktop nào cần quản trị mạng Windows.
 */

using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Events;
using Company.Core.Abstractions.Models;

namespace Company.Core.Abstractions.Interfaces;

/// <summary>
/// Dịch vụ quản lý cấu hình hệ thống (JSON config, thread-safe, auto-save).
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Đối tượng cấu hình hiện hành trong bộ nhớ.
    /// </summary>
    AppConfiguration CurrentConfig { get; }

    /// <summary>
    /// Nạp cấu hình từ file đĩa (nếu chưa có sẽ tự tạo mặc định).
    /// </summary>
    /// <returns>Đối tượng cấu hình đã nạp.</returns>
    Task<AppConfiguration> LoadAsync();

    /// <summary>
    /// Lưu cấu hình hiện hành xuống đĩa và thông báo sự kiện thay đổi.
    /// </summary>
    /// <param name="config">Cấu hình cần lưu.</param>
    Task SaveAsync(AppConfiguration config);

    /// <summary>
    /// Sự kiện kích hoạt khi cấu hình có sự thay đổi.
    /// </summary>
    event EventHandler<AppConfiguration>? ConfigurationChanged;
}

/// <summary>
/// Dịch vụ ghi log cuốn chiếu theo ngày (Rolling File Logger) và bộ nhớ đệm.
/// </summary>
public interface ILoggerService
{
    /// <summary>
    /// Ghi nhận 1 dòng log theo phân cấp mức độ.
    /// </summary>
    /// <param name="level">Mức độ log (Info, Warning, Error, Success).</param>
    /// <param name="source">Nguồn phát sinh log (App, Watchdog, SmartWifi...).</param>
    /// <param name="message">Nội dung chi tiết.</param>
    void Log(AppLogLevel level, string source, string message);

    /// <summary>
    /// Lấy danh sách các dòng log gần nhất từ bộ nhớ đệm RAM.
    /// </summary>
    /// <param name="maxCount">Số lượng dòng log tối đa cần lấy.</param>
    /// <returns>Danh sách các entry log gần nhất.</returns>
    IReadOnlyList<LogEntry> GetRecentLogs(int maxCount = 100);

    /// <summary>
    /// Sự kiện kích hoạt ngay khi có dòng log mới được tạo.
    /// </summary>
    event EventHandler<LogEntry>? LogEmitted;
}

/// <summary>
/// Dịch vụ can thiệp và kiểm soát các chế độ Windows OS (Chế độ máy bay, Tablet Mode, Khởi động cùng Windows).
/// </summary>
public interface IOsModeService
{
    /// <summary>
    /// Cưỡng chế tắt Chế độ máy bay và Tablet Mode, đồng thời kiểm tra trạng thái Task Scheduler.
    /// </summary>
    /// <returns>Trạng thái tổng hợp của OS Mode sau khi can thiệp.</returns>
    Task<SystemModeStatus> EnforceOsModesAsync();

    /// <summary>
    /// Kiểm tra xem Windows có đang bị bật Chế độ máy bay (Airplane Mode) hay không.
    /// </summary>
    Task<bool> IsAirplaneModeEnabledAsync();

    /// <summary>
    /// Kiểm tra xem Windows có đang ở Chế độ Tablet (Tablet Mode) hay không.
    /// </summary>
    Task<bool> IsTabletModeEnabledAsync();

    /// <summary>
    /// Tắt Chế độ máy bay qua Registry và đảm bảo service mạng hoạt động.
    /// </summary>
    Task<bool> DisableAirplaneModeAsync();

    /// <summary>
    /// Tắt Chế độ Tablet qua Registry và khóa về Desktop Mode.
    /// </summary>
    Task<bool> DisableTabletModeAsync();

    /// <summary>
    /// Kiểm tra và tự động tắt ngay nếu phát hiện Airplane Mode hoặc Tablet Mode đang bật.
    /// </summary>
    Task<bool> CheckAndDisableIfActiveAsync();

    /// <summary>
    /// Đảm bảo Airplane Mode bị tắt hoàn toàn.
    /// </summary>
    Task<bool> EnsureAirplaneModeDisabledAsync();

    /// <summary>
    /// Đảm bảo Tablet Mode bị tắt hoàn toàn.
    /// </summary>
    Task<bool> EnsureTabletModeDisabledAsync();

    /// <summary>
    /// Kiểm tra ứng dụng đã được đăng ký tự khởi động qua Windows Task Scheduler hay chưa.
    /// </summary>
    Task<bool> IsAutoStartRegisteredAsync();

    /// <summary>
    /// Đăng ký hoặc gỡ bỏ ứng dụng khỏi Windows Task Scheduler (quyền Administrator cao nhất).
    /// </summary>
    /// <param name="enable">True để đăng ký, False để gỡ bỏ.</param>
    Task<bool> SetAutoStartAsync(bool enable);

    /// <summary>
    /// Bắt đầu tiến trình ngầm giám sát và tự động tắt Airplane Mode / Tablet Mode liên tục.
    /// </summary>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Dừng tiến trình ngầm giám sát OS Mode.
    /// </summary>
    Task StopMonitoringAsync();
}

/// <summary>
/// Quản lý phần cứng Card mạng Windows và cơ chế cứu hộ Reset khẩn cấp đa tầng.
/// </summary>
public interface INetworkAdapterManager
{
    /// <summary>
    /// Lấy danh sách tất cả các card mạng trên máy tính.
    /// </summary>
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync();

    /// <summary>
    /// Lấy thông tin card mạng Wi-Fi đang hoạt động hoặc theo tên ưu tiên.
    /// </summary>
    Task<NetworkAdapterInfo?> GetActiveWifiAdapterAsync(string? preferredName = null);

    /// <summary>
    /// Thực hiện Reset khẩn cấp card mạng qua Netsh, PowerShell hoặc WMI.
    /// </summary>
    /// <param name="adapterName">Tên card mạng (vd: 'Wi-Fi').</param>
    /// <param name="method">Phương thức reset (Auto, Netsh, PowerShell, Wmi).</param>
    /// <returns>True nếu reset thành công.</returns>
    Task<bool> ResetAdapterAsync(string adapterName, AdapterResetMethod method = AdapterResetMethod.Auto);
}

/// <summary>
/// Dịch vụ Watchdog giám sát Ping tới máy chủ và kích hoạt cứu hộ khi mất mạng.
/// </summary>
public interface IConnectionWatchdogService
{
    /// <summary>
    /// Trạng thái hoạt động của tiến trình Watchdog.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Số giây gián đoạn kết nối máy chủ liên tục hiện tại.
    /// </summary>
    int CurrentDisconnectedSeconds { get; }

    /// <summary>
    /// Danh sách trạng thái phản hồi Ping gần nhất của từng máy chủ mục tiêu.
    /// </summary>
    IReadOnlyList<ServerPingStatus> LastKnownStatuses { get; }

    /// <summary>
    /// Bắt đầu vòng lặp giám sát Ping.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Dừng vòng lặp giám sát Ping.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Kích hoạt 1 chu kỳ kiểm tra Ping ngay lập tức.
    /// </summary>
    Task TriggerCheckNowAsync();

    /// <summary>
    /// Sự kiện phát sinh sau mỗi chu kỳ Ping máy chủ.
    /// </summary>
    event EventHandler<WatchdogTickEventArgs>? WatchdogTick;

    /// <summary>
    /// Sự kiện kích hoạt trước khi card mạng bị reset do mất mạng quá lâu.
    /// </summary>
    event EventHandler<AdapterResettingEventArgs>? AdapterResetting;

    /// <summary>
    /// Sự kiện kích hoạt sau khi quá trình reset card mạng hoàn tất.
    /// </summary>
    event EventHandler? AdapterResetCompleted;
}

/// <summary>
/// Dịch vụ điều khiển Wi-Fi thông minh: Roaming nhanh qua BSSID Native API, Failover dự phòng và Blacklist.
/// </summary>
public interface ISmartWifiService
{
    /// <summary>
    /// Trạng thái hoạt động của tiến trình giám sát Wi-Fi.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Thông tin chi tiết về kết nối Wi-Fi hiện hành.
    /// </summary>
    WifiConnectionInfo CurrentConnection { get; }

    /// <summary>
    /// Bắt đầu dịch vụ Smart Wi-Fi.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Dừng dịch vụ Smart Wi-Fi.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Quét và lấy danh sách các trạm phát sóng (BSS) lân cận.
    /// </summary>
    Task<IReadOnlyList<BssNetworkInfo>> GetNearbyBssListAsync(bool forceScan = false);

    /// <summary>
    /// Chuyển vùng roaming tức thì sang 1 BSSID cụ thể mà không làm gián đoạn TCP socket.
    /// </summary>
    Task<bool> FastRoamToBssidAsync(string bssid, string ssid);

    /// <summary>
    /// Kết nối tới 1 SSID cấu hình sẵn với mật khẩu WPA2-PSK.
    /// </summary>
    Task<bool> ConnectToPreferredProfileAsync(string ssid, string password);

    /// <summary>
    /// Ngắt kết nối Wi-Fi hiện tại.
    /// </summary>
    Task<bool> DisconnectAsync();

    /// <summary>
    /// Kích hoạt chuyển đổi mạng dự phòng (Failover) khi Wi-Fi hiện tại mất kết nối máy chủ.
    /// </summary>
    Task<bool> TriggerFailoverToAlternativeNetworkAsync(string reason);

    /// <summary>
    /// Kiểm tra xem SSID có đang nằm trong danh sách đen tạm thời (Quarantine/Blacklist) hay không.
    /// </summary>
    bool IsSsidBlacklisted(string ssid);

    /// <summary>
    /// Đưa SSID vào danh sách cách ly tạm thời trong 1 khoảng thời gian xác định.
    /// </summary>
    void BlacklistSsid(string ssid, TimeSpan duration, string reason);

    /// <summary>
    /// Xóa SSID khỏi danh sách cách ly.
    /// </summary>
    void RemoveFromBlacklist(string ssid);

    /// <summary>
    /// Xóa toàn bộ danh sách cách ly SSID.
    /// </summary>
    void ClearBlacklist();

    /// <summary>
    /// Lấy danh sách các mạng Wi-Fi đang bị cách ly tạm thời.
    /// </summary>
    IReadOnlyList<BlacklistedWifiInfo> GetBlacklistedNetworks();

    /// <summary>
    /// Kích hoạt đánh giá chất lượng sóng và roaming ngay lập tức.
    /// </summary>
    Task TriggerEvaluationNowAsync();

    /// <summary>
    /// Sự kiện kích hoạt khi trạng thái kết nối Wi-Fi thay đổi.
    /// </summary>
    event EventHandler<WifiConnectionInfo>? ConnectionChanged;

    /// <summary>
    /// Sự kiện kích hoạt khi chuyển vùng BSSID thành công.
    /// </summary>
    event EventHandler<WifiRoamingEventArgs>? Roamed;

    /// <summary>
    /// Sự kiện kích hoạt khi cơ chế Failover chuyển mạng Wi-Fi được kích hoạt.
    /// </summary>
    event EventHandler<WifiFailoverEventArgs>? FailoverTriggered;
}
