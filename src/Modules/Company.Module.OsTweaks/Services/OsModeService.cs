using System.Diagnostics;
using Microsoft.Win32;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;

namespace Company.Module.OsTweaks.Services;

public class OsModeService : IOsModeService
{
    private const string TaskSchedulerTaskName = "CraneWifiController";
    private readonly IConfigService _configService;
    private readonly ILoggerService _logger;
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;

    public OsModeService(IConfigService configService, ILoggerService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task<SystemModeStatus> EnforceOsModesAsync()
    {
        var isAirplaneOff = await EnsureAirplaneModeDisabledAsync();
        var isTabletOff = await EnsureTabletModeDisabledAsync();
        var isAutoStart = await IsAutoStartRegisteredAsync();

        return new SystemModeStatus
        {
            IsAirplaneModeDisabled = isAirplaneOff,
            IsTabletModeDisabled = isTabletOff,
            IsTaskSchedulerRegistered = isAutoStart,
            LastEnforcedUtc = DateTime.UtcNow
        };
    }

    public Task<bool> EnsureAirplaneModeDisabledAsync()
    {
        bool success = false;

        // 1. Enforce Registry RadioManagement SystemRadioState = 0 (Airplane Mode OFF)
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement", writable: true);
            if (key != null)
            {
                var state = key.GetValue("SystemRadioState");
                if (state != null && Convert.ToInt32(state) != 0)
                {
                    key.SetValue("SystemRadioState", 0, RegistryValueKind.DWord);
                    _logger.Log(AppLogLevel.Info, "OsTweaks", "Đã ép tắt Airplane Mode qua Registry SystemRadioState=0.");
                }
                success = true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Non-elevated fallback
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, "OsTweaks", $"Lỗi ghi Registry RadioManagement: {ex.Message}");
        }

        // 2. Ensure WlanSvc (WLAN AutoConfig) Windows Service is running
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "start WlanSvc",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(1000);
        }
        catch
        {
            // Ignore
        }

        return Task.FromResult(success);
    }

    public Task<bool> EnsureTabletModeDisabledAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell", writable: true);
            if (key != null)
            {
                var tabletMode = key.GetValue("TabletMode");
                var signInMode = key.GetValue("SignInMode");

                bool changed = false;
                if (tabletMode == null || Convert.ToInt32(tabletMode) != 0)
                {
                    key.SetValue("TabletMode", 0, RegistryValueKind.DWord);
                    changed = true;
                }
                if (signInMode == null || Convert.ToInt32(signInMode) != 1)
                {
                    key.SetValue("SignInMode", 1, RegistryValueKind.DWord);
                    changed = true;
                }

                if (changed)
                {
                    _logger.Log(AppLogLevel.Info, "OsTweaks", "Đã khóa chế độ Tablet Mode về Desktop Mode (TabletMode=0, SignInMode=1).");
                }
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, "OsTweaks", $"Lỗi ghi Registry TabletMode: {ex.Message}");
        }

        return Task.FromResult(false);
    }

    public Task<bool> IsAutoStartRegisteredAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{TaskSchedulerTaskName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null) return Task.FromResult(false);
            process.WaitForExit(1500);
            return Task.FromResult(process.ExitCode == 0);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> SetAutoStartAsync(bool enable)
    {
        try
        {
            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return Task.FromResult(false);

                // Create task triggered at logon with highest privileges to bypass UAC
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/create /tn \"{TaskSchedulerTaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process == null) return Task.FromResult(false);
                process.WaitForExit(2000);

                bool ok = process.ExitCode == 0;
                if (ok)
                {
                    _logger.Log(AppLogLevel.Success, "OsTweaks", "Đã đăng ký Task Scheduler khởi động tự động với quyền Administrator.");
                }
                else
                {
                    var err = process.StandardError.ReadToEnd();
                    _logger.Log(AppLogLevel.Warning, "OsTweaks", $"Không thể tạo Task Scheduler: {err}");
                }
                return Task.FromResult(ok);
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{TaskSchedulerTaskName}\" /f",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process == null) return Task.FromResult(false);
                process.WaitForExit(2000);

                _logger.Log(AppLogLevel.Info, "OsTweaks", "Đã xóa Task Scheduler tự khởi động.");
                return Task.FromResult(process.ExitCode == 0);
            }
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Error, "OsTweaks", $"Lỗi thao tác schtasks: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_monitoringTask != null) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = Task.Run(() => MonitorLoopAsync(_cts.Token), _cts.Token);
        _logger.Log(AppLogLevel.Info, "OsTweaks", "Đã khởi động tiến trình giám sát OS Modes (Tablet & Airplane mode).");
        return Task.CompletedTask;
    }

    public async Task StopMonitoringAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_monitoringTask != null)
            {
                try
                {
                    await _monitoringTask;
                }
                catch (OperationCanceledException) { }
            }
            _cts.Dispose();
            _cts = null;
            _monitoringTask = null;
            _logger.Log(AppLogLevel.Info, "OsTweaks", "Đã dừng tiến trình giám sát OS Modes.");
        }
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        // Immediate first run
        await EnforceOsModesAsync();

        while (!ct.IsCancellationRequested)
        {
            var intervalSeconds = _configService.CurrentConfig.SystemSettings.EnforceOsModesIntervalSeconds;
            if (intervalSeconds <= 0) intervalSeconds = 10;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
                if (ct.IsCancellationRequested) break;

                if (_configService.CurrentConfig.SystemSettings.DisableAirplaneModeOnStartup)
                {
                    await EnsureAirplaneModeDisabledAsync();
                }

                if (_configService.CurrentConfig.SystemSettings.DisableTabletModeOnStartup)
                {
                    await EnsureTabletModeDisabledAsync();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Log(AppLogLevel.Warning, "OsTweaks", $"Lỗi trong vòng lặp giám sát OS: {ex.Message}");
            }
        }
    }
}
