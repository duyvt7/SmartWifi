using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Interfaces;
using Company.Module.ConfigManager.Services;
using Company.Module.NetworkWatchdog.Services;
using Company.Module.OsTweaks.Services;
using Company.Module.SmartWifi.Services;
using CraneWifi.WidgetApp.Utils;
using CraneWifi.WidgetApp.Views;

namespace CraneWifi.WidgetApp;

public partial class App : System.Windows.Application
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    private ServiceProvider? _serviceProvider;
    private NativeNotifyIcon? _notifyIcon;
    private MiniWidgetWindow? _miniWidget;
    private KioskWindow? _kioskWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Setup global error handling
        DispatcherUnhandledException += (s, args) =>
        {
            var logger = _serviceProvider?.GetService<ILoggerService>();
            logger?.Log(AppLogLevel.Error, "AppCrash", $"Unhandled Exception: {args.Exception.Message}\n{args.Exception.StackTrace}");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var logger = _serviceProvider?.GetService<ILoggerService>();
            if (args.ExceptionObject is Exception ex)
            {
                logger?.Log(AppLogLevel.Error, "FatalCrash", $"Fatal Domain Crash: {ex.Message}");
            }
        };

        // Configure Services
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var logger = _serviceProvider.GetRequiredService<ILoggerService>();
        logger.Log(AppLogLevel.Info, "App", "=== KHỞI ĐỘNG HỆ THỐNG CRANE WI-FI CONTROLLER ===");

        // Load Configuration
        var configService = _serviceProvider.GetRequiredService<IConfigService>();
        var config = await configService.LoadAsync();

        // Start Core Services
        var osService = _serviceProvider.GetRequiredService<IOsModeService>();
        await osService.StartMonitoringAsync();

        if (config.SystemSettings.AutoStartWithWindows)
        {
            _ = osService.SetAutoStartAsync(true);
        }

        var watchdog = _serviceProvider.GetRequiredService<IConnectionWatchdogService>();
        await watchdog.StartAsync();

        var smartWifi = _serviceProvider.GetRequiredService<ISmartWifiService>();
        await smartWifi.StartAsync();

        // Setup Native System Tray
        SetupSystemTray();

        // Lazy-loading: Only create MiniWidget initially (saving ~40MB RAM)
        _miniWidget = new MiniWidgetWindow(
            configService,
            smartWifi,
            watchdog,
            openKioskCallback: ShowKiosk);

        if (config.SystemSettings.StartInMiniWidgetMode)
        {
            ShowMiniWidget();
        }
        else
        {
            ShowKiosk();
        }

        // Initial memory trim
        TrimWorkingSet();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<ILoggerService, RollingFileLoggerService>();
        services.AddSingleton<IOsModeService, OsModeService>();
        services.AddSingleton<INetworkAdapterManager, NetworkAdapterManager>();
        services.AddSingleton<ISmartWifiService, SmartWifiService>();
        services.AddSingleton<IConnectionWatchdogService, ConnectionWatchdogService>();
    }

    private void SetupSystemTray()
    {
        var contextMenu = new ContextMenu();

        var itemWidget = new MenuItem { Header = "Hiện Mini-Widget" };
        itemWidget.Click += (s, e) => ShowMiniWidget();
        contextMenu.Items.Add(itemWidget);

        var itemKiosk = new MenuItem { Header = "Mở Bảng điều khiển Kiosk" };
        itemKiosk.Click += (s, e) => ShowKiosk();
        contextMenu.Items.Add(itemKiosk);

        contextMenu.Items.Add(new Separator());

        var itemReset = new MenuItem { Header = "Làm mới Card mạng (Force Reset)" };
        itemReset.Click += async (s, e) =>
        {
            if (_serviceProvider != null)
            {
                var mgr = _serviceProvider.GetRequiredService<INetworkAdapterManager>();
                var cfg = _serviceProvider.GetRequiredService<IConfigService>().CurrentConfig;
                var adapter = await mgr.GetActiveWifiAdapterAsync(cfg.WatchdogSettings.PreferredAdapterName);
                await mgr.ResetAdapterAsync(adapter?.Name ?? "Wi-Fi");
            }
        };
        contextMenu.Items.Add(itemReset);

        contextMenu.Items.Add(new Separator());

        var itemExit = new MenuItem { Header = "Thoát ứng dụng" };
        itemExit.Click += (s, e) => ExitApplication();
        contextMenu.Items.Add(itemExit);

        _notifyIcon = new NativeNotifyIcon("Crane Wi-Fi Controller (Hoạt động 24/7)", contextMenu)
        {
            DoubleClickAction = ShowKiosk
        };
    }

    public void ShowMiniWidget()
    {
        Dispatcher.Invoke(() =>
        {
            _kioskWindow?.Hide();
            if (_miniWidget != null)
            {
                _miniWidget.Show();
                _miniWidget.Activate();
            }
            TrimWorkingSet();
        });
    }

    public void ShowKiosk()
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                _miniWidget?.Hide();
                if (_kioskWindow == null && _serviceProvider != null)
                {
                    // Lazy instantiation of KioskWindow only when user asks for it
                    _kioskWindow = new KioskWindow(
                        _serviceProvider.GetRequiredService<IConfigService>(),
                        _serviceProvider.GetRequiredService<ISmartWifiService>(),
                        _serviceProvider.GetRequiredService<IConnectionWatchdogService>(),
                        _serviceProvider.GetRequiredService<INetworkAdapterManager>(),
                        _serviceProvider.GetRequiredService<IOsModeService>(),
                        _serviceProvider.GetRequiredService<ILoggerService>(),
                        showMiniWidgetCallback: ShowMiniWidget,
                        exitAppCallback: ExitApplication);
                }

                if (_kioskWindow != null)
                {
                    _kioskWindow.Show();
                    _kioskWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                var logger = _serviceProvider?.GetService<ILoggerService>();
                logger?.Log(AppLogLevel.Error, "KioskWindow", $"Lỗi khi mở bảng điều khiển Kiosk: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Không thể mở Bảng điều khiển Kiosk: {ex.Message}", "Lỗi giao diện", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowMiniWidget();
            }
        });
    }

    private void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch
        {
            // Non-critical optimization
        }
    }

    public async void ExitApplication()
    {
        try
        {
            if (_serviceProvider != null)
            {
                var logger = _serviceProvider.GetService<ILoggerService>();
                logger?.Log(AppLogLevel.Info, "App", "Đang dừng các dịch vụ và đóng ứng dụng...");

                var os = _serviceProvider.GetService<IOsModeService>();
                if (os != null) await os.StopMonitoringAsync();

                var wd = _serviceProvider.GetService<IConnectionWatchdogService>();
                if (wd != null) await wd.StopAsync();

                var wifi = _serviceProvider.GetService<ISmartWifiService>();
                if (wifi != null) await wifi.StopAsync();
            }

            _notifyIcon?.Dispose();
            _notifyIcon = null;

            _kioskWindow?.MarkForExplicitExit();
            _miniWidget?.Close();
            _kioskWindow?.Close();

            Shutdown();
        }
        catch
        {
            Environment.Exit(0);
        }
    }
}
