using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Events;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;

namespace CraneWifi.WidgetApp.Views;

public partial class MiniWidgetWindow : Window
{
    private readonly IConfigService _configService;
    private readonly ISmartWifiService _wifiService;
    private readonly IConnectionWatchdogService _watchdogService;
    private readonly Action _openKioskCallback;
    private Storyboard? _pulseStoryboard;
    private bool _isPulseActive = false;

    public MiniWidgetWindow(
        IConfigService configService,
        ISmartWifiService wifiService,
        IConnectionWatchdogService watchdogService,
        Action openKioskCallback)
    {
        InitializeComponent();
        _configService = configService;
        _wifiService = wifiService;
        _watchdogService = watchdogService;
        _openKioskCallback = openKioskCallback;

        _pulseStoryboard = (Storyboard)Resources["PulseStoryBoard"];

        Loaded += MiniWidgetWindow_Loaded;
        Closing += MiniWidgetWindow_Closing;

        // Wire service events
        _wifiService.ConnectionChanged += WifiService_ConnectionChanged;
        _wifiService.Roamed += WifiService_Roamed;
        _watchdogService.WatchdogTick += WatchdogService_WatchdogTick;
        _watchdogService.AdapterResetting += WatchdogService_AdapterResetting;
        _watchdogService.AdapterResetCompleted += WatchdogService_AdapterResetCompleted;
    }

    private void MiniWidgetWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreOrSetDefaultPosition();
        UpdateUiState(_wifiService.CurrentConnection, _watchdogService.LastKnownStatuses, _watchdogService.CurrentDisconnectedSeconds);
    }

    private void RestoreOrSetDefaultPosition()
    {
        var settings = _configService.CurrentConfig.SystemSettings;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        if (settings.WidgetPositionX >= 0 && settings.WidgetPositionX < screenWidth &&
            settings.WidgetPositionY >= 0 && settings.WidgetPositionY < screenHeight)
        {
            Left = settings.WidgetPositionX;
            Top = settings.WidgetPositionY;
        }
        else
        {
            // Default top-right corner with 20px margin
            Left = screenWidth - Width - 20;
            Top = 20;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            SnapToScreenEdges();
            SaveCurrentPosition();
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        // Handled after drag completes
    }

    private void SnapToScreenEdges()
    {
        const double snapThreshold = 25.0;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        // Snap Left
        if (Math.Abs(Left) < snapThreshold) Left = 0;
        // Snap Right
        if (Math.Abs(screenWidth - (Left + Width)) < snapThreshold) Left = screenWidth - Width;
        // Snap Top
        if (Math.Abs(Top) < snapThreshold) Top = 0;
        // Snap Bottom
        if (Math.Abs(screenHeight - (Top + Height)) < snapThreshold) Top = screenHeight - Height;
    }

    private void SaveCurrentPosition()
    {
        _configService.CurrentConfig.SystemSettings.WidgetPositionX = Left;
        _configService.CurrentConfig.SystemSettings.WidgetPositionY = Top;
        _ = _configService.SaveAsync(_configService.CurrentConfig);
    }

    private void BtnOpenKiosk_Click(object sender, RoutedEventArgs e)
    {
        _openKioskCallback();
    }

    private void WifiService_ConnectionChanged(object? sender, WifiConnectionInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateUiState(info, _watchdogService.LastKnownStatuses, _watchdogService.CurrentDisconnectedSeconds);
        });
    }

    private void WifiService_Roamed(object? sender, WifiRoamingEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatusBadge.Text = $"Đã chuyển BSSID ({e.ToBssid.Substring(Math.Max(0, e.ToBssid.Length - 5))})";
            TxtStatusBadge.Foreground = (Brush)FindResource("CyanAccentBrush");
        });
    }

    private void WatchdogService_WatchdogTick(object? sender, WatchdogTickEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateLatencyDisplay(e.BestLatencyMs, e.AnyServerOnline, e.CurrentDisconnectedDurationSeconds);
        });
    }

    private void WatchdogService_AdapterResetting(object? sender, AdapterResettingEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatusBadge.Text = "Đang tự cứu hộ Card mạng...";
            TxtStatusBadge.Foreground = (Brush)FindResource("RedDangerBrush");
            StartPulseAnimation();
        });
    }

    private void WatchdogService_AdapterResetCompleted(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatusBadge.Text = "Card mạng đã reset xong";
            TxtStatusBadge.Foreground = (Brush)FindResource("AmberWarningBrush");
        });
    }

    private void UpdateUiState(WifiConnectionInfo wifi, IReadOnlyList<ServerPingStatus> serverStatuses, int disconnectedSeconds)
    {
        if (wifi.State == WifiState.Connected && !string.IsNullOrWhiteSpace(wifi.Ssid))
        {
            TxtSsid.Text = wifi.Ssid;
            TxtSignalPercent.Text = $" ({wifi.SignalQualityPercent}%)";

            if (wifi.SignalQualityPercent >= 70)
            {
                WifiIconPath.Fill = (Brush)FindResource("NeonGreenBrush");
                GlowEffect.Color = (Color)FindResource("NeonGreenColor");
                StopPulseAnimation();
                if (disconnectedSeconds == 0)
                {
                    TxtStatusBadge.Text = "Hệ thống ổn định";
                    TxtStatusBadge.Foreground = (Brush)FindResource("NeonGreenBrush");
                }
            }
            else if (wifi.SignalQualityPercent >= 45)
            {
                WifiIconPath.Fill = (Brush)FindResource("AmberWarningBrush");
                GlowEffect.Color = (Color)FindResource("AmberWarningColor");
                StopPulseAnimation();
                if (disconnectedSeconds == 0)
                {
                    TxtStatusBadge.Text = "Sóng Wi-Fi trung bình";
                    TxtStatusBadge.Foreground = (Brush)FindResource("AmberWarningBrush");
                }
            }
            else
            {
                WifiIconPath.Fill = (Brush)FindResource("RedDangerBrush");
                GlowEffect.Color = (Color)FindResource("RedDangerColor");
                StartPulseAnimation();
                TxtStatusBadge.Text = "Sóng Wi-Fi yếu! Đang tìm trạm...";
                TxtStatusBadge.Foreground = (Brush)FindResource("RedDangerBrush");
            }
        }
        else
        {
            TxtSsid.Text = "MẤT KẾT NỐI";
            TxtSignalPercent.Text = " (0%)";
            WifiIconPath.Fill = (Brush)FindResource("RedDangerBrush");
            GlowEffect.Color = (Color)FindResource("RedDangerColor");
            StartPulseAnimation();

            TxtStatusBadge.Text = "Đang tự động quét kết nối...";
            TxtStatusBadge.Foreground = (Brush)FindResource("RedDangerBrush");
        }
    }

    private void UpdateLatencyDisplay(long latencyMs, bool anyOnline, int disconnectedSeconds)
    {
        if (anyOnline && latencyMs >= 0)
        {
            TxtLatency.Text = $"Server: {latencyMs} ms";
            TxtLatency.Foreground = (Brush)FindResource("NeonGreenBrush");
        }
        else
        {
            TxtLatency.Text = $"Mất mạng ({disconnectedSeconds}s)";
            TxtLatency.Foreground = (Brush)FindResource("RedDangerBrush");
            TxtStatusBadge.Text = "Toàn bộ Server mất tín hiệu!";
            TxtStatusBadge.Foreground = (Brush)FindResource("RedDangerBrush");
            StartPulseAnimation();
        }
    }

    private void StartPulseAnimation()
    {
        if (!_isPulseActive && _pulseStoryboard != null)
        {
            _pulseStoryboard.Begin();
            _isPulseActive = true;
        }
    }

    private void StopPulseAnimation()
    {
        if (_isPulseActive && _pulseStoryboard != null)
        {
            _pulseStoryboard.Stop();
            GlowEffect.Opacity = 0.5;
            _isPulseActive = false;
        }
    }

    private void MiniWidgetWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Unwire events to prevent leaks
        _wifiService.ConnectionChanged -= WifiService_ConnectionChanged;
        _wifiService.Roamed -= WifiService_Roamed;
        _watchdogService.WatchdogTick -= WatchdogService_WatchdogTick;
        _watchdogService.AdapterResetting -= WatchdogService_AdapterResetting;
        _watchdogService.AdapterResetCompleted -= WatchdogService_AdapterResetCompleted;
    }
}

