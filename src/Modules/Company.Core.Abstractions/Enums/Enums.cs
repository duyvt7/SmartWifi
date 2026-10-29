namespace Company.Core.Abstractions.Enums;

public enum WifiState
{
    Unknown = 0,
    Disconnected = 1,
    Connecting = 2,
    Connected = 3,
    Roaming = 4,
    Resetting = 5
}

public enum AppLogLevel
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Success = 3
}

public enum AdapterResetMethod
{
    Auto = 0,
    Netsh = 1,
    PowerShell = 2,
    Wmi = 3
}

