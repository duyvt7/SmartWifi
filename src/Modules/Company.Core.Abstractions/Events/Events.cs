using Company.Core.Abstractions.Models;

namespace Company.Core.Abstractions.Events;

public class WifiRoamingEventArgs : EventArgs
{
    public string Ssid { get; set; } = string.Empty;
    public string FromBssid { get; set; } = string.Empty;
    public string ToBssid { get; set; } = string.Empty;
    public int OldSignalQuality { get; set; }
    public int NewSignalQuality { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AdapterResettingEventArgs : EventArgs
{
    public string AdapterName { get; set; } = string.Empty;
    public int DisconnectedDurationSeconds { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class WatchdogTickEventArgs : EventArgs
{
    public IReadOnlyList<ServerPingStatus> ServerStatuses { get; set; } = Array.Empty<ServerPingStatus>();
    public bool AnyServerOnline { get; set; }
    public int CurrentDisconnectedDurationSeconds { get; set; }
    public long BestLatencyMs { get; set; } = -1;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

