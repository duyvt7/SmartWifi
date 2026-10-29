using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Events;
using Company.Core.Abstractions.Models;

namespace Company.Core.Abstractions.Interfaces;

public interface IConfigService
{
    AppConfiguration CurrentConfig { get; }
    Task<AppConfiguration> LoadAsync();
    Task SaveAsync(AppConfiguration config);
    event EventHandler<AppConfiguration>? ConfigurationChanged;
}

public interface ILoggerService
{
    void Log(AppLogLevel level, string source, string message);
    IReadOnlyList<LogEntry> GetRecentLogs(int maxCount = 100);
    event EventHandler<LogEntry>? LogEmitted;
}

public interface IOsModeService
{
    Task<SystemModeStatus> EnforceOsModesAsync();
    Task<bool> IsAirplaneModeEnabledAsync();
    Task<bool> IsTabletModeEnabledAsync();
    Task<bool> DisableAirplaneModeAsync();
    Task<bool> DisableTabletModeAsync();
    Task<bool> CheckAndDisableIfActiveAsync();
    Task<bool> EnsureAirplaneModeDisabledAsync();
    Task<bool> EnsureTabletModeDisabledAsync();
    Task<bool> IsAutoStartRegisteredAsync();
    Task<bool> SetAutoStartAsync(bool enable);
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task StopMonitoringAsync();
}

public interface INetworkAdapterManager
{
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync();
    Task<NetworkAdapterInfo?> GetActiveWifiAdapterAsync(string? preferredName = null);
    Task<bool> ResetAdapterAsync(string adapterName, AdapterResetMethod method = AdapterResetMethod.Auto);
}

public interface IConnectionWatchdogService
{
    bool IsRunning { get; }
    int CurrentDisconnectedSeconds { get; }
    IReadOnlyList<ServerPingStatus> LastKnownStatuses { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task TriggerCheckNowAsync();
    event EventHandler<WatchdogTickEventArgs>? WatchdogTick;
    event EventHandler<AdapterResettingEventArgs>? AdapterResetting;
    event EventHandler? AdapterResetCompleted;
}

public interface ISmartWifiService
{
    bool IsRunning { get; }
    WifiConnectionInfo CurrentConnection { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<IReadOnlyList<BssNetworkInfo>> GetNearbyBssListAsync(bool forceScan = false);
    Task<bool> FastRoamToBssidAsync(string bssid, string ssid);
    Task<bool> ConnectToPreferredProfileAsync(string ssid, string password);
    Task TriggerEvaluationNowAsync();
    event EventHandler<WifiConnectionInfo>? ConnectionChanged;
    event EventHandler<WifiRoamingEventArgs>? Roamed;
}

