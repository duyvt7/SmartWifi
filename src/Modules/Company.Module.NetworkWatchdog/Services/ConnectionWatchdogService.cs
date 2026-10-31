/**
 * @Module: Company.Module.NetworkWatchdog.ConnectionWatchdogService
 * @Purpose: Dịch vụ giám sát độ trễ và mất gói tin (ICMP Ping) định kỳ tới các máy chủ công nghiệp (TOS/Gateway), thực hiện cứu hộ đa tầng: Tự động ngắt & đổi Wi-Fi khi mất ping (Failover), can thiệp tắt Airplane/Tablet Mode, và Reset phần cứng card mạng khi mất mạng quá hạn.
 * @Dependencies: Company.Core.Abstractions (Enums, Events, Interfaces, Models).
 * @Reusability: Độc lập hoàn toàn qua Dependency Injection (IConfigService, INetworkAdapterManager, IOsModeService, ILoggerService, ISmartWifiService). Sẵn sàng tái sử dụng cho các hệ thống SCADA, cẩu giàn, IoT trên Windows.
 */

using System.Net.NetworkInformation;
using System.Text;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Events;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;

namespace Company.Module.NetworkWatchdog.Services;

/// <summary>
/// Dịch vụ Connection Watchdog giám sát Ping tới máy chủ và tự cứu hộ mạng đa tầng.
/// </summary>
public class ConnectionWatchdogService : IConnectionWatchdogService
{
    private readonly IConfigService _configService;
    private readonly INetworkAdapterManager _adapterManager;
    private readonly IOsModeService _osModeService;
    private readonly ILoggerService _logger;
    private readonly ISmartWifiService? _smartWifiService;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private int _disconnectedSeconds = 0;
    private DateTime _lastResetTime = DateTime.MinValue;
    private DateTime _lastFailoverTime = DateTime.MinValue;
    private List<ServerPingStatus> _lastStatuses = new();
    private readonly byte[] _pingBuffer = Encoding.ASCII.GetBytes("CRANE_WIFI_WATCHDOG_PING_PACKET");

    /// <summary>
    /// Sự kiện kích hoạt định kỳ sau mỗi chu kỳ kiểm tra Ping.
    /// </summary>
    public event EventHandler<WatchdogTickEventArgs>? WatchdogTick;

    /// <summary>
    /// Sự kiện kích hoạt ngay trước khi card mạng chuẩn bị được reset.
    /// </summary>
    public event EventHandler<AdapterResettingEventArgs>? AdapterResetting;

    /// <summary>
    /// Sự kiện kích hoạt sau khi quá trình reset card mạng hoàn tất.
    /// </summary>
    public event EventHandler? AdapterResetCompleted;

    /// <summary>
    /// Trạng thái hoạt động của tiến trình Watchdog.
    /// </summary>
    public bool IsRunning => _workerTask != null && !_workerTask.IsCompleted;

    /// <summary>
    /// Số giây gián đoạn kết nối máy chủ liên tục hiện hành.
    /// </summary>
    public int CurrentDisconnectedSeconds => _disconnectedSeconds;

    /// <summary>
    /// Danh sách kết quả phản hồi Ping gần nhất của các máy chủ.
    /// </summary>
    public IReadOnlyList<ServerPingStatus> LastKnownStatuses => _lastStatuses.AsReadOnly();

    /// <summary>
    /// Khởi tạo ConnectionWatchdogService với các dịch vụ phụ thuộc.
    /// </summary>
    public ConnectionWatchdogService(
        IConfigService configService,
        INetworkAdapterManager adapterManager,
        IOsModeService osModeService,
        ILoggerService logger,
        ISmartWifiService? smartWifiService = null)
    {
        _configService = configService;
        _adapterManager = adapterManager;
        _osModeService = osModeService;
        _logger = logger;
        _smartWifiService = smartWifiService;
    }

    /// <summary>
    /// Khởi động tiến trình giám sát Ping trong nền.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workerTask = Task.Run(() => WatchdogLoopAsync(_cts.Token), _cts.Token);
        _logger.Log(AppLogLevel.Info, "Watchdog", "Đã khởi chạy dịch vụ Connection Watchdog giám sát Server.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Dừng tiến trình giám sát Ping.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_workerTask != null)
            {
                try
                {
                    await _workerTask;
                }
                catch (OperationCanceledException) { }
            }
            _cts.Dispose();
            _cts = null;
            _workerTask = null;
            _logger.Log(AppLogLevel.Info, "Watchdog", "Đã dừng dịch vụ Connection Watchdog.");
        }
    }

    /// <summary>
    /// Thực hiện kiểm tra Ping ngay lập tức mà không cần đợi hết chu kỳ.
    /// </summary>
    public async Task TriggerCheckNowAsync()
    {
        await PerformSingleCheckAsync();
    }

    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        // Kiểm tra ngay lập tức khi khởi động
        await PerformSingleCheckAsync();

        while (!ct.IsCancellationRequested)
        {
            var interval = _configService.CurrentConfig.WatchdogSettings.CheckIntervalSeconds;
            if (interval <= 0) interval = 5;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);
                if (ct.IsCancellationRequested) break;

                await PerformSingleCheckAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Log(AppLogLevel.Warning, "Watchdog", $"Lỗi vòng lặp Watchdog: {ex.Message}");
            }
        }
    }

    private async Task PerformSingleCheckAsync()
    {
        var settings = _configService.CurrentConfig.WatchdogSettings;
        var activeServers = settings.TargetServers.Where(s => s.IsEnabled).ToList();

        if (activeServers.Count == 0)
        {
            _disconnectedSeconds = 0;
            return;
        }

        // Ping đồng thời tất cả các máy chủ đang kích hoạt
        var tasks = activeServers.Select(s => PingServerAsync(s, settings.PingTimeoutMs)).ToList();
        var results = await Task.WhenAll(tasks);
        _lastStatuses = results.ToList();

        bool anyOnline = results.Any(r => r.IsOnline);
        long bestLatency = results.Where(r => r.IsOnline).Select(r => r.RoundtripTimeMs).DefaultIfEmpty(-1).Min();

        if (anyOnline)
        {
            if (_disconnectedSeconds > 0)
            {
                _logger.Log(AppLogLevel.Success, "Watchdog", $"Kết nối máy chủ đã phục hồi sau {_disconnectedSeconds}s mất tín hiệu.");
            }
            _disconnectedSeconds = 0;
        }
        else
        {
            _disconnectedSeconds += settings.CheckIntervalSeconds;
            _logger.Log(AppLogLevel.Warning, "Watchdog", $"Toàn bộ máy chủ không phản hồi! Thời gian gián đoạn: {_disconnectedSeconds}s / {settings.MaxDisconnectedSecondsBeforeReset}s");

            var smartWifiSettings = _configService.CurrentConfig.SmartWifiSettings;

            // KIỂM TRA TẦNG 1: Tự động ngắt kết nối và chuyển sang Wi-Fi khác khi mất ping quá thời gian quy định
            if (_smartWifiService != null &&
                smartWifiSettings.EnablePingFailWifiSwitch &&
                _smartWifiService.CurrentConnection.State == WifiState.Connected &&
                !string.IsNullOrWhiteSpace(_smartWifiService.CurrentConnection.Ssid) &&
                _disconnectedSeconds >= smartWifiSettings.PingFailWifiSwitchSeconds &&
                (DateTime.UtcNow - _lastFailoverTime).TotalSeconds >= Math.Max(15, smartWifiSettings.PingFailWifiSwitchSeconds))
            {
                _logger.Log(AppLogLevel.Warning, "Watchdog",
                    $"ĐÃ ĐẠT NGƯỠNG MẤT PING ({_disconnectedSeconds}s >= {smartWifiSettings.PingFailWifiSwitchSeconds}s). Tiến hành ngắt Wi-Fi hiện tại '{_smartWifiService.CurrentConnection.Ssid}' và chuyển sang mạng khác...");

                _lastFailoverTime = DateTime.UtcNow;
                _ = Task.Run(async () =>
                {
                    await _smartWifiService.TriggerFailoverToAlternativeNetworkAsync(
                        $"Mất kết nối máy chủ liên tục {_disconnectedSeconds}s");
                });

                // Cấp thời gian chờ 10s (Grace Period) để Wi-Fi mới kết nối và nhận DHCP trước khi watchdog đếm lỗi tiếp
                _disconnectedSeconds = Math.Max(0, _disconnectedSeconds - 10);
            }

            // KIỂM TRA TẦNG 2 & 3: Tự cứu hộ OS Mode và Reset card mạng phần cứng nếu vẫn mất mạng quá lâu
            var timeSinceLastReset = (DateTime.UtcNow - _lastResetTime).TotalSeconds;
            if (_disconnectedSeconds >= settings.MaxDisconnectedSecondsBeforeReset && timeSinceLastReset >= settings.ResetCooldownSeconds)
            {
                await TriggerSelfHealingResetAsync(settings);
            }
        }

        try
        {
            WatchdogTick?.Invoke(this, new WatchdogTickEventArgs
            {
                ServerStatuses = _lastStatuses,
                AnyServerOnline = anyOnline,
                CurrentDisconnectedDurationSeconds = _disconnectedSeconds,
                BestLatencyMs = bestLatency,
                Timestamp = DateTime.UtcNow
            });
        }
        catch
        {
            // Bảo vệ luồng Watchdog nếu subscriber ném ngoại lệ
        }
    }

    private async Task<ServerPingStatus> PingServerAsync(TargetServer server, int timeoutMs)
    {
        var status = new ServerPingStatus
        {
            Target = server,
            LastCheckedUtc = DateTime.UtcNow
        };

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(server.Address, timeoutMs, _pingBuffer);
            if (reply.Status == IPStatus.Success)
            {
                status.IsOnline = true;
                status.RoundtripTimeMs = reply.RoundtripTime;
            }
            else
            {
                status.IsOnline = false;
                status.ErrorMessage = reply.Status.ToString();
            }
        }
        catch (Exception ex)
        {
            status.IsOnline = false;
            status.ErrorMessage = ex.InnerException?.Message ?? ex.Message;
        }

        return status;
    }

    private async Task TriggerSelfHealingResetAsync(WatchdogSettings settings)
    {
        // BƯỚC 1: Kiểm tra trạng thái Airplane Mode & Tablet Mode trước khi can thiệp phần cứng
        bool isAirplaneOn = await _osModeService.IsAirplaneModeEnabledAsync();
        bool isTabletOn = await _osModeService.IsTabletModeEnabledAsync();

        if (isAirplaneOn || isTabletOn)
        {
            _logger.Log(AppLogLevel.Warning, "Watchdog", 
                $"PHÁT HIỆN NGUYÊN NHÂN MẤT MẠNG: [Chế độ Máy bay: {(isAirplaneOn ? "ĐANG BẬT" : "Tắt")}, Tablet Mode: {(isTabletOn ? "ĐANG BẬT" : "Tắt")}]. " +
                "Tiến hành tắt các chế độ này trước khi can thiệp reset card mạng...");

            await _osModeService.CheckAndDisableIfActiveAsync();

            // Đợi 4 giây để card mạng và Wi-Fi tự động tái kết nối
            await Task.Delay(4000);

            // Kiểm tra lại kết nối ngay lập tức
            var activeServers = settings.TargetServers.Where(s => s.IsEnabled).ToList();
            if (activeServers.Count > 0)
            {
                var checkTasks = activeServers.Select(s => PingServerAsync(s, settings.PingTimeoutMs)).ToList();
                var checkResults = await Task.WhenAll(checkTasks);

                if (checkResults.Any(r => r.IsOnline))
                {
                    _logger.Log(AppLogLevel.Success, "Watchdog", 
                        "KẾT NỐI MẠNG ĐÃ PHỤC HỒI THÀNH CÔNG ngay sau khi tắt Airplane/Tablet Mode! Không cần reset card mạng.");
                    _disconnectedSeconds = 0;
                    return;
                }
                else
                {
                    _logger.Log(AppLogLevel.Warning, "Watchdog", 
                        "Đã tắt Airplane/Tablet Mode nhưng vẫn chưa thông mạng tới Server. Chuyển sang bước Reset card mạng...");
                }
            }
        }

        // BƯỚC 2: Nếu không phải do Airplane/Tablet Mode, hoặc đã tắt rồi mà vẫn mất mạng -> Tiến hành Reset card mạng
        var wifiAdapter = await _adapterManager.GetActiveWifiAdapterAsync(settings.PreferredAdapterName);
        var adapterName = wifiAdapter?.Name ?? settings.PreferredAdapterName ?? "Wi-Fi";

        _logger.Log(AppLogLevel.Error, "Watchdog", 
            $"KÍCH HOẠT RESET CARD MẠNG: Mất kết nối liên tục {_disconnectedSeconds}s vượt ngưỡng {settings.MaxDisconnectedSecondsBeforeReset}s. Đang reset '{adapterName}'...");

        try
        {
            AdapterResetting?.Invoke(this, new AdapterResettingEventArgs
            {
                AdapterName = adapterName,
                DisconnectedDurationSeconds = _disconnectedSeconds,
                Reason = "Mất kết nối toàn bộ Server vượt ngưỡng thời gian cho phép",
                Timestamp = DateTime.UtcNow
            });
        }
        catch { }

        _lastResetTime = DateTime.UtcNow;
        var resetSuccess = await _adapterManager.ResetAdapterAsync(adapterName);

        if (resetSuccess)
        {
            _logger.Log(AppLogLevel.Success, "Watchdog", $"Reset card mạng hoàn tất. Thiết lập thời gian chờ phục hồi (Cooldown) {settings.ResetCooldownSeconds}s.");
        }
        else
        {
            _logger.Log(AppLogLevel.Error, "Watchdog", "Không thể reset card mạng!");
        }

        try
        {
            AdapterResetCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }
}
