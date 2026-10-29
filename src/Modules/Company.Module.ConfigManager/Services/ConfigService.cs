using System.Text.Json;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;

namespace Company.Module.ConfigManager.Services;

public class ConfigService : IConfigService
{
    private readonly string _configFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private AppConfiguration _currentConfig;

    public event EventHandler<AppConfiguration>? ConfigurationChanged;

    public AppConfiguration CurrentConfig => _currentConfig;

    public ConfigService(string? customConfigPath = null)
    {
        _configFilePath = customConfigPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        _currentConfig = CreateDefaultConfiguration();
    }

    public async Task<AppConfiguration> LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_configFilePath))
            {
                _currentConfig = CreateDefaultConfiguration();
                var json = JsonSerializer.Serialize(_currentConfig, _jsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);
                return _currentConfig;
            }

            var content = await File.ReadAllTextAsync(_configFilePath);
            var loaded = JsonSerializer.Deserialize<AppConfiguration>(content, _jsonOptions);
            _currentConfig = loaded ?? CreateDefaultConfiguration();
            return _currentConfig;
        }
        catch (Exception)
        {
            // If corrupt, fallback to current memory state or default
            return _currentConfig;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppConfiguration config)
    {
        await _lock.WaitAsync();
        try
        {
            _currentConfig = config;
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            var tempFile = _configFilePath + ".tmp";
            await File.WriteAllTextAsync(tempFile, json);
            File.Move(tempFile, _configFilePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }

        ConfigurationChanged?.Invoke(this, _currentConfig);
    }

    private static AppConfiguration CreateDefaultConfiguration()
    {
        return new AppConfiguration
        {
            SystemSettings = new SystemSettings
            {
                DisableAirplaneModeOnStartup = true,
                DisableTabletModeOnStartup = true,
                EnforceOsModesIntervalSeconds = 10,
                StartInMiniWidgetMode = true,
                AutoStartWithWindows = true,
                WidgetPositionX = -1,
                WidgetPositionY = -1
            },
            WatchdogSettings = new WatchdogSettings
            {
                CheckIntervalSeconds = 5,
                PingTimeoutMs = 1500,
                MaxDisconnectedSecondsBeforeReset = 45,
                ResetCooldownSeconds = 60,
                PreferredAdapterName = null,
                TargetServers = new List<TargetServer>
                {
                    new() { Name = "TOS Server Chính", Address = "192.168.1.10", IsEnabled = true },
                    new() { Name = "Gateway Cảng", Address = "192.168.1.1", IsEnabled = true },
                    new() { Name = "DNS Dự phòng", Address = "8.8.8.8", IsEnabled = false }
                }
            },
            SmartWifiSettings = new SmartWifiSettings
            {
                PassiveCheckIntervalSeconds = 2,
                BssCacheReadIntervalSeconds = 10,
                WeakSignalThresholdPercent = 45,
                RoamingHysteresisDeltaPercent = 15,
                EnableSmartRoaming = true,
                PreferredNetworks = new List<PreferredNetwork>
                {
                    new() { Ssid = "PORT_CRANE_5G", Password = "portpassword", Priority = 1, AutoConnect = true },
                    new() { Ssid = "PORT_CRANE_2.4G", Password = "portpassword", Priority = 2, AutoConnect = true }
                }
            }
        };
    }
}

