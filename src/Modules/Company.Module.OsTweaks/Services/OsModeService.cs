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

    public Task<bool> IsAirplaneModeEnabledAsync()
    {
        try
        {
            // 1. Check Subkey HKLM\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState (default value)
            using var subKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState");
            if (subKey != null)
            {
                var val = subKey.GetValue("");
                if (val != null && Convert.ToInt32(val) == 1)
                {
                    return Task.FromResult(true);
                }
            }

            // 2. Check parent key value
            using var parentKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement");
            if (parentKey != null)
            {
                var val = parentKey.GetValue("SystemRadioState");
                if (val != null && Convert.ToInt32(val) == 1)
                {
                    return Task.FromResult(true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, "OsTweaks", $"Lỗi đọc trạng thái Airplane Mode: {ex.Message}");
        }

        return Task.FromResult(false);
    }

    public Task<bool> IsTabletModeEnabledAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell");
            if (key != null)
            {
                var val = key.GetValue("TabletMode");
                if (val != null && Convert.ToInt32(val) == 1)
                {
                    return Task.FromResult(true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, "OsTweaks", $"Lỗi đọc trạng thái Tablet Mode: {ex.Message}");
        }

        return Task.FromResult(false);
    }

    public Task<bool> DisableAirplaneModeAsync()
    {
        bool changed = false;

        try
        {
            // 1. Set SystemRadioState subkey default value to 0
            using var subKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState", writable: true);
            if (subKey != null)
            {
                subKey.SetValue("", 0, RegistryValueKind.DWord);
                changed = true;
            }

            // 2. Set SystemRadioState in parent key to 0
            using var parentKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement", writable: true);
            if (parentKey != null)
            {
                parentKey.SetValue("SystemRadioState", 0, RegistryValueKind.DWord);
                changed = true;
            }

            if (changed)
            {
                _logger.Log(AppLogLevel.Success, "OsTweaks", "ĐÃ TẮT CHẾ ĐỘ MÁY BAY: Đặt SystemRadioState = 0.");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, "OsTweaks", $"Lỗi vô hiệu hóa Airplane Mode trong Registry: {ex.Message}");
        }

        // 3. Ensure WlanSvc (WLAN AutoConfig) is running
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "start WlanSvc",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(1000);
        }
        catch { }

        // 4. Ensure Wi-Fi adapter is enabled via netsh
        try
        {
            var psiNetsh = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "interface set interface name=\"Wi-Fi\" admin=enable",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var procNetsh = Process.Start(psiNetsh);
            procNetsh?.WaitForExit(1500);
        }
        catch { }

        return Task.FromResult(true);
    }

    public Task<bool> DisableTabletModeAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell", writable: true);
            if (key != null)
            {
                key.SetValue("TabletMode", 0, RegistryValueKind.DWord);
                key.SetValue("SignInMode", 1, RegistryValueKind.DWord);
                _logger.Log(AppLogLevel.Success, "OsTweaks", "ĐÃ TẮT CHẾ ĐỘ TABLET: Khóa về Desktop Mode (TabletMode=0, SignInMode=1).");
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, "OsTweaks", $"Lỗi vô hiệu hóa Tablet Mode: {ex.Message}");
        }

        return Task.FromResult(false);
    }

    public async Task<bool> CheckAndDisableIfActiveAsync()
    {
        bool wasAirplaneActive = await IsAirplaneModeEnabledAsync();
        bool wasTabletActive = await IsTabletModeEnabledAsync();

        if (wasAirplaneActive || wasTabletActive)
        {
            _logger.Log(AppLogLevel.Warning, "OsTweaks", 
                $"Phát hiện chế độ can thiệp đang BẬT: [Airplane Mode: {(wasAirplaneActive ? "BẬT" : "Tắt")}, Tablet Mode: {(wasTabletActive ? "BẬT" : "Tắt")}]. Đang tự động tắt...");

            if (wasAirplaneActive) await DisableAirplaneModeAsync();
            if (wasTabletActive) await DisableTabletModeAsync();

            return true;
        }

        return false;
    }

    public Task<bool> EnsureAirplaneModeDisabledAsync()
    {
        return DisableAirplaneModeAsync();
    }

    public Task<bool> EnsureTabletModeDisabledAsync()
    {
        return DisableTabletModeAsync();
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
            // Check frequently (every 3 seconds) to immediately block if user turns on Airplane or Tablet mode
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                if (ct.IsCancellationRequested) break;

                if (_configService.CurrentConfig.SystemSettings.DisableAirplaneModeOnStartup)
                {
                    bool isAirplaneOn = await IsAirplaneModeEnabledAsync();
                    if (isAirplaneOn)
                    {
                        await DisableAirplaneModeAsync();
                    }
                }

                if (_configService.CurrentConfig.SystemSettings.DisableTabletModeOnStartup)
                {
                    bool isTabletOn = await IsTabletModeEnabledAsync();
                    if (isTabletOn)
                    {
                        await DisableTabletModeAsync();
                    }
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
