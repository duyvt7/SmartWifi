using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;

namespace Company.Module.NetworkWatchdog.Services;

public class NetworkAdapterManager : INetworkAdapterManager
{
    private readonly ILoggerService _logger;

    public NetworkAdapterManager(ILoggerService logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                bool isWireless = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                                  ni.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
                                  ni.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                                  ni.Description.Contains("802.11", StringComparison.OrdinalIgnoreCase);

                list.Add(new NetworkAdapterInfo
                {
                    Id = ni.Id,
                    Name = ni.Name,
                    Description = ni.Description,
                    IsWireless = isWireless,
                    IsOperational = ni.OperationalStatus == OperationalStatus.Up,
                    MacAddress = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Error, "AdapterManager", $"Lỗi truy vấn danh sách card mạng: {ex.Message}");
        }

        return Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>(list);
    }

    public async Task<NetworkAdapterInfo?> GetActiveWifiAdapterAsync(string? preferredName = null)
    {
        var adapters = await GetAdaptersAsync();

        // 1. If preferred name specified, search for exact or substring match
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var match = adapters.FirstOrDefault(a => a.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase) ||
                                                    a.Description.Equals(preferredName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        // 2. Look for operational wireless adapter
        var activeWireless = adapters.FirstOrDefault(a => a.IsWireless && a.IsOperational);
        if (activeWireless != null) return activeWireless;

        // 3. Fallback to any wireless adapter
        var anyWireless = adapters.FirstOrDefault(a => a.IsWireless);
        if (anyWireless != null) return anyWireless;

        // 4. Default to adapter with "Wi-Fi" in name
        return adapters.FirstOrDefault(a => a.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ResetAdapterAsync(string adapterName, AdapterResetMethod method = AdapterResetMethod.Auto)
    {
        _logger.Log(AppLogLevel.Warning, "AdapterManager", $"Bắt đầu quá trình Reset Card mạng '{adapterName}' (Phương thức: {method})...");

        if (method == AdapterResetMethod.Auto || method == AdapterResetMethod.Netsh)
        {
            var netshSuccess = await ResetViaNetshAsync(adapterName);
            if (netshSuccess)
            {
                _logger.Log(AppLogLevel.Success, "AdapterManager", $"Reset card mạng '{adapterName}' thành công qua netsh.");
                return true;
            }
            if (method == AdapterResetMethod.Netsh) return false;
        }

        if (method == AdapterResetMethod.Auto || method == AdapterResetMethod.PowerShell)
        {
            var psSuccess = await ResetViaPowerShellAsync(adapterName);
            if (psSuccess)
            {
                _logger.Log(AppLogLevel.Success, "AdapterManager", $"Reset card mạng '{adapterName}' thành công qua PowerShell Restart-NetAdapter.");
                return true;
            }
            if (method == AdapterResetMethod.PowerShell) return false;
        }

        if (method == AdapterResetMethod.Auto || method == AdapterResetMethod.Wmi)
        {
            var wmiSuccess = await ResetViaWmiAsync(adapterName);
            if (wmiSuccess)
            {
                _logger.Log(AppLogLevel.Success, "AdapterManager", $"Reset card mạng '{adapterName}' thành công qua WMI.");
                return true;
            }
        }

        _logger.Log(AppLogLevel.Error, "AdapterManager", $"Toàn bộ phương thức reset card mạng '{adapterName}' đều thất bại!");
        return false;
    }

    private async Task<bool> ResetViaNetshAsync(string adapterName)
    {
        try
        {
            // 1. Disable
            var psiDisable = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"interface set interface name=\"{adapterName}\" admin=disable",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var proc = Process.Start(psiDisable))
            {
                if (proc == null) return false;
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0)
                {
                    _logger.Log(AppLogLevel.Warning, "AdapterManager", $"netsh disable lỗi: {await proc.StandardError.ReadToEndAsync()}");
                    return false;
                }
            }

            // Wait 3 seconds for driver teardown
            await Task.Delay(3000);

            // 2. Enable
            var psiEnable = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"interface set interface name=\"{adapterName}\" admin=enable",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var proc = Process.Start(psiEnable))
            {
                if (proc == null) return false;
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, "AdapterManager", $"Lỗi khi gọi netsh: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ResetViaPowerShellAsync(string adapterName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Restart-NetAdapter -Name '{adapterName}' -Confirm:$false\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, "AdapterManager", $"Lỗi khi gọi PowerShell Restart-NetAdapter: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ResetViaWmiAsync(string adapterName)
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID = '{adapterName}' OR Name LIKE '%{adapterName}%'");

                foreach (ManagementObject mo in searcher.Get())
                {
                    var disableResult = mo.InvokeMethod("Disable", null);
                    await Task.Delay(3000);
                    var enableResult = mo.InvokeMethod("Enable", null);

                    int returnCode = Convert.ToInt32(enableResult);
                    return returnCode == 0;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(AppLogLevel.Warning, "AdapterManager", $"Lỗi WMI Reset: {ex.Message}");
            }
            return false;
        });
    }
}

