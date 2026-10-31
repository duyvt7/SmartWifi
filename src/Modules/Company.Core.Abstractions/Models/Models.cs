using Company.Core.Abstractions.Enums;

namespace Company.Core.Abstractions.Models;

public class SystemSettings
{
    public bool DisableAirplaneModeOnStartup { get; set; } = true;
    public bool DisableTabletModeOnStartup { get; set; } = true;
    public int EnforceOsModesIntervalSeconds { get; set; } = 10;
    public bool StartInMiniWidgetMode { get; set; } = true;
    public double WidgetPositionX { get; set; } = -1;
    public double WidgetPositionY { get; set; } = -1;
    public bool AutoStartWithWindows { get; set; } = true;
}

public class TargetServer
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public class WatchdogSettings
{
    public int CheckIntervalSeconds { get; set; } = 5;
    public int PingTimeoutMs { get; set; } = 1500;
    public int MaxDisconnectedSecondsBeforeReset { get; set; } = 45;
    public int ResetCooldownSeconds { get; set; } = 60;
    public string? PreferredAdapterName { get; set; } = null;
    public List<TargetServer> TargetServers { get; set; } = new();
}

public class PreferredNetwork
{
    public string Ssid { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int Priority { get; set; } = 1;
    public bool AutoConnect { get; set; } = true;
}

public class SmartWifiSettings
{
    public int PassiveCheckIntervalSeconds { get; set; } = 2;
    public int BssCacheReadIntervalSeconds { get; set; } = 10;
    public int WeakSignalThresholdPercent { get; set; } = 45;
    public int RoamingHysteresisDeltaPercent { get; set; } = 15;
    public bool EnableSmartRoaming { get; set; } = true;
    public bool EnablePingFailWifiSwitch { get; set; } = true;
    public int PingFailWifiSwitchSeconds { get; set; } = 15;
    public int WifiBlacklistDurationSeconds { get; set; } = 300;
    public List<PreferredNetwork> PreferredNetworks { get; set; } = new();
}

public class BlacklistedWifiInfo
{
    public string Ssid { get; set; } = string.Empty;
    public string? Bssid { get; set; }
    public DateTime QuarantinedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiryUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = string.Empty;
    public double RemainingSeconds => Math.Max(0, (ExpiryUtc - DateTime.UtcNow).TotalSeconds);
}

public class AppConfiguration
{
    public SystemSettings SystemSettings { get; set; } = new();
    public WatchdogSettings WatchdogSettings { get; set; } = new();
    public SmartWifiSettings SmartWifiSettings { get; set; } = new();
}

public class WifiConnectionInfo
{
    public WifiState State { get; set; } = WifiState.Disconnected;
    public string Ssid { get; set; } = string.Empty;
    public string Bssid { get; set; } = string.Empty;
    public int SignalQualityPercent { get; set; }
    public int RssiDbm { get; set; } = -100;
    public int Channel { get; set; }
    public string Band { get; set; } = string.Empty; // e.g. "2.4 GHz" or "5 GHz"
    public string InterfaceName { get; set; } = string.Empty;
    public Guid InterfaceId { get; set; }
    public long? LinkSpeedMbps { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

public class BssNetworkInfo
{
    public string Ssid { get; set; } = string.Empty;
    public string Bssid { get; set; } = string.Empty;
    public int SignalQualityPercent { get; set; }
    public int RssiDbm { get; set; }
    public int Channel { get; set; }
    public string Band { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
}

public class ServerPingStatus
{
    public TargetServer Target { get; set; } = new();
    public bool IsOnline { get; set; }
    public long RoundtripTimeMs { get; set; } = -1;
    public DateTime LastCheckedUtc { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}

public class NetworkAdapterInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; // e.g. "Wi-Fi"
    public string Description { get; set; } = string.Empty;
    public bool IsWireless { get; set; }
    public bool IsOperational { get; set; }
    public string MacAddress { get; set; } = string.Empty;
}

public class SystemModeStatus
{
    public bool IsAirplaneModeDisabled { get; set; }
    public bool IsTabletModeDisabled { get; set; }
    public bool IsTaskSchedulerRegistered { get; set; }
    public DateTime LastEnforcedUtc { get; set; } = DateTime.UtcNow;
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public AppLogLevel Level { get; set; } = AppLogLevel.Info;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

