/**
 * @Module: Company.Module.OsTweaks.OsModeService
 * @Purpose: Quản lý và cưỡng chế tắt các chế độ can thiệp mạng của Windows (Chế độ máy bay - Airplane Mode, Chế độ máy tính bảng - Tablet Mode), đồng thời quản lý tự khởi động cùng Windows bằng Task Scheduler với quyền quản trị Administrator cao nhất.
 * @Dependencies: Microsoft.Win32.Registry, System.Diagnostics, Company.Core.Abstractions.
 * @Reusability: Độc lập hoàn toàn, dễ dàng tích hợp vào bất kỳ ứng dụng công nghiệp, POS, Kiosk nào cần khóa Desktop và ngăn chặn người dùng hoặc hệ thống tự kích hoạt Airplane Mode.
 */

using System.Diagnostics;
using Microsoft.Win32;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;

namespace Company.Module.OsTweaks.Services;

/// <summary>
/// Dịch vụ can thiệp Registry và Task Scheduler để khóa và tối ưu chế độ hoạt động của Windows.
/// </summary>
public class OsModeService : IOsModeService
{
    private const string TaskSchedulerTaskName = "CraneWifiController";
    private readonly IConfigService _configService;
    private readonly ILoggerService _logger;
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;

    /// <summary>
    /// Khởi tạo OsModeService với cấu hình và logger.
    /// </summary>
    public OsModeService(IConfigService configService, ILoggerService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Cưỡng chế tắt Chế độ máy bay, Tablet Mode và kiểm tra trạng thái Task Scheduler.
    /// </summary>
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

    /// <summary>
    /// Kiểm tra xem Windows có đang ở Chế độ máy bay (SystemRadioState = 1) hay không.
    /// </summary>
    public Task<bool> IsAirplaneModeEnabledAsync()
    {
        try
        {
            // 1. Kiểm tra khóa con HKLM\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState
            using var subKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState");
            if (subKey != null)
            {
                var val = subKey.GetValue("");
                if (val != null && Convert.ToInt32(val) == 1)
                {
                    return Task.FromResult(true);
                }
            }

            // 2. Kiểm tra giá trị khóa cha
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

    /// <summary>
    /// Kiểm tra xem Windows có đang ở Chế độ máy tính bảng (Tablet Mode = 1) hay không.
    /// </summary>
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

    /// <summary>
    /// Tắt Chế độ máy bay trong Registry và khởi động lại dịch vụ Wi-Fi nếu bị treo.
    /// </summary>
    public Task<bool> DisableAirplaneModeAsync()
    {
        bool changed = false;

        try
        {
            // 1. Ghi giá trị mặc định của SystemRadioState = 0
            using var subKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState", writable: true);
            if (subKey != null)
            {
                subKey.SetValue("", 0, RegistryValueKind.DWord);
                changed = true;
            }

            // 2. Ghi giá trị SystemRadioState trong khóa cha = 0
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

        // 3. Đảm bảo dịch vụ WlanSvc (WLAN AutoConfig) đang chạy
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

        // 4. Kích hoạt lại card Wi-Fi nếu đang bị admin disable
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

    /// <summary>
    /// Tắt Chế độ Tablet Mode và khóa giao diện về Desktop Mode.
    /// </summary>
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

    /// <summary>
    /// Kiểm tra và tự động tắt ngay lập tức nếu phát hiện Airplane Mode hoặc Tablet Mode đang bật.
    /// </summary>
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

    /// <summary>
    /// Đảm bảo Airplane Mode bị tắt hoàn toàn.
    /// </summary>
    public Task<bool> EnsureAirplaneModeDisabledAsync()
    {
        return DisableAirplaneModeAsync();
    }

    /// <summary>
    /// Đảm bảo Tablet Mode bị tắt hoàn toàn.
    /// </summary>
    public Task<bool> EnsureTabletModeDisabledAsync()
    {
        return DisableTabletModeAsync();
    }

    /// <summary>
    /// Kiểm tra ứng dụng đã được đăng ký Task Scheduler tự chạy khi khởi động hay chưa.
    /// </summary>
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

    /// <summary>
    /// Đăng ký hoặc hủy đăng ký tự khởi động cùng Windows qua Task Scheduler với quyền cao nhất (rl highest).
    /// </summary>
    public Task<bool> SetAutoStartAsync(bool enable)
    {
        try
        {
            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return Task.FromResult(false);

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

    /// <summary>
    /// Bắt đầu vòng lặp giám sát OS Mode ngầm (kiểm tra mỗi 3 giây).
    /// </summary>
    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_monitoringTask != null) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = Task.Run(() => MonitorLoopAsync(_cts.Token), _cts.Token);
        _logger.Log(AppLogLevel.Info, "OsTweaks", "Đã khởi động tiến trình giám sát OS Modes (Tablet & Airplane mode).");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Dừng vòng lặp giám sát OS Mode ngầm.
    /// </summary>
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
        // Thực thi ngay lần đầu
        await EnforceOsModesAsync();

        while (!ct.IsCancellationRequested)
        {
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
