using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Events;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;

namespace CraneWifi.WidgetApp.Views;

public class ServerDisplayItem
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string StatusText { get; set; } = "Đang kiểm tra...";
    public string LatencyText { get; set; } = "-- ms";
    public Brush StatusColor { get; set; } = Brushes.Gray;
}

public class PreferredWifiDisplayItem
{
    public string Ssid { get; set; } = string.Empty;
    public string PasswordMasked { get; set; } = string.Empty;
    public string PriorityText { get; set; } = "Ưu tiên 1";
}

public partial class KioskWindow : Window
{
    private readonly IConfigService _configService;
    private readonly ISmartWifiService _wifiService;
    private readonly IConnectionWatchdogService _watchdogService;
    private readonly INetworkAdapterManager _adapterManager;
    private readonly IOsModeService _osModeService;
    private readonly ILoggerService _logger;
    private readonly Action _showMiniWidgetCallback;
    private readonly Action _exitAppCallback;

    private readonly ObservableCollection<ServerDisplayItem> _serverItems = new();
    private readonly ObservableCollection<PreferredWifiDisplayItem> _wifiItems = new();
    private readonly DispatcherTimer _clockTimer;
    private bool _isExplicitExit = false;
    private bool _isUiLoaded = false;

    public KioskWindow(
        IConfigService configService,
        ISmartWifiService wifiService,
        IConnectionWatchdogService watchdogService,
        INetworkAdapterManager adapterManager,
        IOsModeService osModeService,
        ILoggerService logger,
        Action showMiniWidgetCallback,
        Action exitAppCallback)
    {
        InitializeComponent();

        _configService = configService;
        _wifiService = wifiService;
        _watchdogService = watchdogService;
        _adapterManager = adapterManager;
        _osModeService = osModeService;
        _logger = logger;
        _showMiniWidgetCallback = showMiniWidgetCallback;
        _exitAppCallback = exitAppCallback;

        ServersItemsControl.ItemsSource = _serverItems;
        PreferredWifiItemsControl.ItemsSource = _wifiItems;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();

        Loaded += KioskWindow_Loaded;

        // Wire service events
        _watchdogService.WatchdogTick += WatchdogService_WatchdogTick;
        _wifiService.ConnectionChanged += WifiService_ConnectionChanged;
        _wifiService.Roamed += WifiService_Roamed;
        _wifiService.FailoverTriggered += WifiService_FailoverTriggered;
        _logger.LogEmitted += Logger_LogEmitted;
    }

    private async void KioskWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSettingsToUi();
        await LoadAdaptersDropdownAsync();
        await LoadRecentLogsAsync();
        await RefreshOsStatusAsync();
        UpdateWifiDisplay(_wifiService.CurrentConnection);
        _isUiLoaded = true;
    }

    private void LoadSettingsToUi()
    {
        var config = _configService.CurrentConfig;

        // Watchdog settings
        SliderResetThreshold.Value = config.WatchdogSettings.MaxDisconnectedSecondsBeforeReset;
        TxtResetThresholdValue.Text = $"{config.WatchdogSettings.MaxDisconnectedSecondsBeforeReset}s";
        RefreshServersList();

        // Smart Wi-Fi settings
        ChkEnableRoaming.IsChecked = config.SmartWifiSettings.EnableSmartRoaming;
        SliderHysteresis.Value = config.SmartWifiSettings.RoamingHysteresisDeltaPercent;
        TxtHysteresisValue.Text = $"+{config.SmartWifiSettings.RoamingHysteresisDeltaPercent}%";

        // Failover & Quarantine settings
        ChkEnableFailover.IsChecked = config.SmartWifiSettings.EnablePingFailWifiSwitch;
        SliderFailoverThreshold.Value = config.SmartWifiSettings.PingFailWifiSwitchSeconds;
        TxtFailoverThresholdValue.Text = $"{config.SmartWifiSettings.PingFailWifiSwitchSeconds}s";

        int blacklistMinutes = Math.Max(1, config.SmartWifiSettings.WifiBlacklistDurationSeconds / 60);
        SliderBlacklistDuration.Value = blacklistMinutes;
        TxtBlacklistDurationValue.Text = $"{blacklistMinutes} phút";

        UpdateQuarantinedCountDisplay();
        RefreshPreferredWifiList();

        // OS settings
        ChkAutoStart.IsChecked = config.SystemSettings.AutoStartWithWindows;
    }

    private void RefreshServersList()
    {
        _serverItems.Clear();
        var servers = _configService.CurrentConfig.WatchdogSettings.TargetServers;
        var statuses = _watchdogService.LastKnownStatuses;

        foreach (var s in servers)
        {
            var match = statuses.FirstOrDefault(st => st.Target.Address.Equals(s.Address, StringComparison.OrdinalIgnoreCase));
            bool isOnline = match?.IsOnline ?? false;
            long latency = match?.RoundtripTimeMs ?? -1;

            _serverItems.Add(new ServerDisplayItem
            {
                Name = s.Name,
                Address = s.Address,
                StatusText = isOnline ? "ONLINE" : "OFFLINE",
                LatencyText = latency >= 0 ? $"{latency} ms" : "Timeout",
                StatusColor = isOnline ? (Brush)FindResource("NeonGreenBrush") : (Brush)FindResource("RedDangerBrush")
            });
        }
    }

    private void RefreshPreferredWifiList()
    {
        _wifiItems.Clear();
        foreach (var w in _configService.CurrentConfig.SmartWifiSettings.PreferredNetworks)
        {
            _wifiItems.Add(new PreferredWifiDisplayItem
            {
                Ssid = w.Ssid,
                PasswordMasked = string.IsNullOrEmpty(w.Password) ? "(Không mật khẩu)" : "••••••••",
                PriorityText = $"Ưu tiên {w.Priority}"
            });
        }
    }

    private async Task LoadAdaptersDropdownAsync()
    {
        try
        {
            var adapters = await _adapterManager.GetAdaptersAsync();
            CmbAdapters.Items.Clear();
            CmbAdapters.Items.Add("Tự động nhận diện (Khuyên dùng)");

            int selectedIndex = 0;
            var preferredName = _configService.CurrentConfig.WatchdogSettings.PreferredAdapterName;

            for (int i = 0; i < adapters.Count; i++)
            {
                var a = adapters[i];
                var display = $"{a.Name} ({a.Description})";
                CmbAdapters.Items.Add(display);

                if (!string.IsNullOrWhiteSpace(preferredName) && a.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i + 1;
                }
            }

            CmbAdapters.SelectedIndex = selectedIndex;
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, "Kiosk", $"Lỗi tải danh sách adapter: {ex.Message}");
        }
    }

    private async Task RefreshOsStatusAsync()
    {
        var status = await _osModeService.EnforceOsModesAsync();
        TxtAirplaneStatus.Text = status.IsAirplaneModeDisabled ? "LOCKED OFF" : "UNLOCKED";
        TxtAirplaneStatus.Foreground = status.IsAirplaneModeDisabled ? (Brush)FindResource("NeonGreenBrush") : (Brush)FindResource("RedDangerBrush");

        TxtTabletStatus.Text = status.IsTabletModeDisabled ? "LOCKED OFF" : "UNLOCKED";
        TxtTabletStatus.Foreground = status.IsTabletModeDisabled ? (Brush)FindResource("NeonGreenBrush") : (Brush)FindResource("RedDangerBrush");
    }

    private Task LoadRecentLogsAsync()
    {
        var recent = _logger.GetRecentLogs(50);
        var sb = new System.Text.StringBuilder();
        foreach (var l in recent)
        {
            sb.AppendLine($"[{l.Timestamp:HH:mm:ss}] [{l.Level.ToString().ToUpperInvariant()}] [{l.Source}] {l.Message}");
        }
        TxtLiveLogs.Text = sb.ToString();
        LogScrollViewer.ScrollToEnd();
        return Task.CompletedTask;
    }

    private void WatchdogService_WatchdogTick(object? sender, WatchdogTickEventArgs e)
    {
        Dispatcher.Invoke(RefreshServersList);
    }

    private void WifiService_ConnectionChanged(object? sender, WifiConnectionInfo info)
    {
        Dispatcher.Invoke(() => UpdateWifiDisplay(info));
    }

    private void WifiService_Roamed(object? sender, WifiRoamingEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _logger.Log(AppLogLevel.Success, "Kiosk", $"Đã Roam tới Access Point '{e.ToBssid}' (Lý do: {e.Reason}).");
        });
    }

    private void UpdateWifiDisplay(WifiConnectionInfo info)
    {
        if (info.State == WifiState.Connected && !string.IsNullOrWhiteSpace(info.Ssid))
        {
            TxtDetailSsid.Text = info.Ssid;
            TxtDetailSignal.Text = $"{info.SignalQualityPercent}%";
            TxtDetailSignal.Foreground = info.SignalQualityPercent >= 70 ? (Brush)FindResource("NeonGreenBrush") :
                                         info.SignalQualityPercent >= 45 ? (Brush)FindResource("AmberWarningBrush") :
                                         (Brush)FindResource("RedDangerBrush");

            TxtDetailBssid.Text = string.IsNullOrEmpty(info.Bssid) ? "--:--:--:--:--:--" : info.Bssid;
            TxtDetailChannel.Text = info.Channel > 0 ? $"Channel {info.Channel}" : "--";
            TxtDetailRssi.Text = $"{info.RssiDbm} dBm";
            TxtDetailSpeed.Text = info.LinkSpeedMbps.HasValue ? $"{info.LinkSpeedMbps} Mbps" : "--";
            TxtWifiBand.Text = string.IsNullOrEmpty(info.Band) ? "Wi-Fi" : info.Band;
        }
        else
        {
            TxtDetailSsid.Text = "CHƯA KẾT NỐI";
            TxtDetailSignal.Text = "0%";
            TxtDetailSignal.Foreground = (Brush)FindResource("RedDangerBrush");
            TxtDetailBssid.Text = "--:--:--:--:--:--";
            TxtDetailChannel.Text = "--";
            TxtDetailRssi.Text = "-100 dBm";
            TxtDetailSpeed.Text = "0 Mbps";
        }
    }

    private void Logger_LogEmitted(object? sender, LogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            var line = $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level.ToString().ToUpperInvariant()}] [{entry.Source}] {entry.Message}{Environment.NewLine}";
            TxtLiveLogs.AppendText(line);
            LogScrollViewer.ScrollToEnd();
        });
    }

    #region Touch Numpad & Server Management

    private void Numpad_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content != null)
        {
            TxtServerInput.Text += btn.Content.ToString();
            TxtServerInput.CaretIndex = TxtServerInput.Text.Length;
        }
    }

    private void NumpadBackspace_Click(object sender, RoutedEventArgs e)
    {
        if (TxtServerInput.Text.Length > 0)
        {
            TxtServerInput.Text = TxtServerInput.Text.Substring(0, TxtServerInput.Text.Length - 1);
            TxtServerInput.CaretIndex = TxtServerInput.Text.Length;
        }
    }

    private async void BtnAddServer_Click(object sender, RoutedEventArgs e)
    {
        var ip = TxtServerInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(ip)) return;

        var config = _configService.CurrentConfig;
        if (!config.WatchdogSettings.TargetServers.Any(s => s.Address.Equals(ip, StringComparison.OrdinalIgnoreCase)))
        {
            config.WatchdogSettings.TargetServers.Add(new TargetServer
            {
                Name = $"Server {ip}",
                Address = ip,
                IsEnabled = true
            });
            await _configService.SaveAsync(config);
            RefreshServersList();
            _logger.Log(AppLogLevel.Info, "Kiosk", $"Đã thêm máy chủ giám sát mới: {ip}");
        }
    }

    private async void BtnRemoveServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string ip)
        {
            var config = _configService.CurrentConfig;
            var target = config.WatchdogSettings.TargetServers.FirstOrDefault(s => s.Address.Equals(ip, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                config.WatchdogSettings.TargetServers.Remove(target);
                await _configService.SaveAsync(config);
                RefreshServersList();
                _logger.Log(AppLogLevel.Info, "Kiosk", $"Đã xóa máy chủ giám sát: {ip}");
            }
        }
    }

    private async void SliderResetThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUiLoaded || TxtResetThresholdValue == null || _configService?.CurrentConfig == null) return;
        int val = (int)e.NewValue;
        TxtResetThresholdValue.Text = $"{val}s";

        var config = _configService.CurrentConfig;
        if (config.WatchdogSettings.MaxDisconnectedSecondsBeforeReset != val)
        {
            config.WatchdogSettings.MaxDisconnectedSecondsBeforeReset = val;
            await _configService.SaveAsync(config);
        }
    }

    #endregion

    #region Roaming & Adapter Events

    private async void ChkEnableRoaming_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isUiLoaded || _configService?.CurrentConfig == null) return;
        var config = _configService.CurrentConfig;
        config.SmartWifiSettings.EnableSmartRoaming = ChkEnableRoaming.IsChecked == true;
        await _configService.SaveAsync(config);
    }

    private async void SliderHysteresis_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUiLoaded || TxtHysteresisValue == null || _configService?.CurrentConfig == null) return;
        int val = (int)e.NewValue;
        TxtHysteresisValue.Text = $"+{val}%";

        var config = _configService.CurrentConfig;
        if (config.SmartWifiSettings.RoamingHysteresisDeltaPercent != val)
        {
            config.SmartWifiSettings.RoamingHysteresisDeltaPercent = val;
            await _configService.SaveAsync(config);
        }
    }

    private async void BtnScanAndRoamNow_Click(object sender, RoutedEventArgs e)
    {
        _logger.Log(AppLogLevel.Info, "Kiosk", "Kỹ thuật viên yêu cầu quét và đánh giá Fast Roaming ngay lập tức.");
        await _wifiService.TriggerEvaluationNowAsync();
    }

    private async void ChkEnableFailover_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isUiLoaded || _configService?.CurrentConfig == null) return;
        var config = _configService.CurrentConfig;
        config.SmartWifiSettings.EnablePingFailWifiSwitch = (ChkEnableFailover.IsChecked == true);
        await _configService.SaveAsync(config);
    }

    private async void SliderFailoverThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUiLoaded || TxtFailoverThresholdValue == null || _configService?.CurrentConfig == null) return;
        int val = (int)e.NewValue;
        TxtFailoverThresholdValue.Text = $"{val}s";

        var config = _configService.CurrentConfig;
        if (config.SmartWifiSettings.PingFailWifiSwitchSeconds != val)
        {
            config.SmartWifiSettings.PingFailWifiSwitchSeconds = val;
            await _configService.SaveAsync(config);
        }
    }

    private async void SliderBlacklistDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isUiLoaded || TxtBlacklistDurationValue == null || _configService?.CurrentConfig == null) return;
        int minutes = (int)e.NewValue;
        TxtBlacklistDurationValue.Text = $"{minutes} phút";

        var config = _configService.CurrentConfig;
        int seconds = minutes * 60;
        if (config.SmartWifiSettings.WifiBlacklistDurationSeconds != seconds)
        {
            config.SmartWifiSettings.WifiBlacklistDurationSeconds = seconds;
            await _configService.SaveAsync(config);
        }
    }

    private void BtnClearBlacklist_Click(object sender, RoutedEventArgs e)
    {
        _wifiService.ClearBlacklist();
        UpdateQuarantinedCountDisplay();
        MessageBox.Show("Đã xóa toàn bộ danh sách tạm khóa Wi-Fi. Các mạng đã có thể kết nối lại bình thường.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UpdateQuarantinedCountDisplay()
    {
        Dispatcher.Invoke(() =>
        {
            if (TxtQuarantinedCount == null) return;
            var list = _wifiService.GetBlacklistedNetworks();
            TxtQuarantinedCount.Text = $"Mạng đang bị khóa: {list.Count}";
            if (list.Count > 0)
            {
                TxtQuarantinedCount.Foreground = (Brush)FindResource("AmberWarningBrush");
            }
            else
            {
                TxtQuarantinedCount.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }
        });
    }

    private void WifiService_FailoverTriggered(object? sender, WifiFailoverEventArgs e)
    {
        UpdateQuarantinedCountDisplay();
    }

    private async void CmbAdapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiLoaded || _configService?.CurrentConfig == null) return;
        if (CmbAdapters.SelectedIndex <= 0)
        {
            _configService.CurrentConfig.WatchdogSettings.PreferredAdapterName = null;
        }
        else
        {
            var text = CmbAdapters.SelectedItem?.ToString() ?? string.Empty;
            var adapterName = text.Split('(')[0].Trim();
            _configService.CurrentConfig.WatchdogSettings.PreferredAdapterName = adapterName;
        }
        await _configService.SaveAsync(_configService.CurrentConfig);
    }

    private async void BtnForceResetAdapter_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "CẢNH BÁO: Thao tác này sẽ ngắt và khởi động lại Card mạng Wi-Fi ngay lập tức. Bạn có chắc chắn muốn thực hiện?",
            "Xác nhận Reset khẩn cấp",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            var config = _configService.CurrentConfig;
            var adapter = await _adapterManager.GetActiveWifiAdapterAsync(config.WatchdogSettings.PreferredAdapterName);
            var name = adapter?.Name ?? config.WatchdogSettings.PreferredAdapterName ?? "Wi-Fi";
            await _adapterManager.ResetAdapterAsync(name);
        }
    }

    private async void ChkAutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isUiLoaded || _configService?.CurrentConfig == null) return;
        bool enable = ChkAutoStart.IsChecked == true;
        await _osModeService.SetAutoStartAsync(enable);
        _configService.CurrentConfig.SystemSettings.AutoStartWithWindows = enable;
        await _configService.SaveAsync(_configService.CurrentConfig);
    }

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        TxtLiveLogs.Clear();
    }

    #endregion

    #region Window Lifecycle

    private void BtnMinimizeToWidget_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _showMiniWidgetCallback();
    }

    private void BtnExitApplication_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Bạn có chắc chắn muốn THOÁT HOÀN TOÀN Crane Wi-Fi Controller?\n(Hệ thống cẩu sẽ không còn được tự động tối ưu sóng và cứu hộ mạng)",
            "Xác nhận Thoát ứng dụng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _isExplicitExit = true;
            _exitAppCallback();
        }
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        TxtFooterTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            // Minimize to widget instead of closing
            e.Cancel = true;
            Hide();
            _showMiniWidgetCallback?.Invoke();
        }
        else
        {
            Loaded -= KioskWindow_Loaded;
            if (_clockTimer != null)
            {
                _clockTimer.Stop();
                _clockTimer.Tick -= ClockTimer_Tick;
            }
            if (_watchdogService != null) _watchdogService.WatchdogTick -= WatchdogService_WatchdogTick;
            if (_wifiService != null)
            {
                _wifiService.ConnectionChanged -= WifiService_ConnectionChanged;
                _wifiService.Roamed -= WifiService_Roamed;
                _wifiService.FailoverTriggered -= WifiService_FailoverTriggered;
            }
            if (_logger != null) _logger.LogEmitted -= Logger_LogEmitted;
        }
    }

    public void MarkForExplicitExit()
    {
        _isExplicitExit = true;
    }

    #endregion
}
