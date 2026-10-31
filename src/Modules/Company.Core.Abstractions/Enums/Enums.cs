/**
 * @Module: Company.Core.Abstractions.Enums
 * @Purpose: Định nghĩa các kiểu liệt kê Enums chuẩn cho trạng thái Wi-Fi, mức độ ghi log và phương thức reset card mạng.
 * @Dependencies: Không có dependency bên ngoài.
 * @Reusability: Tái sử dụng cho mọi tầng trong kiến trúc ứng dụng (Core, Module, UI).
 */

namespace Company.Core.Abstractions.Enums;

/// <summary>
/// Trạng thái kết nối của Wi-Fi client.
/// </summary>
public enum WifiState
{
    Unknown = 0,
    Disconnected = 1,
    Connecting = 2,
    Connected = 3,
    Roaming = 4,
    Resetting = 5
}

/// <summary>
/// Phân cấp mức độ nhật ký ứng dụng.
/// </summary>
public enum AppLogLevel
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Success = 3
}

/// <summary>
/// Phương thức can thiệp để khởi động lại (Reset) card mạng Windows.
/// </summary>
public enum AdapterResetMethod
{
    Auto = 0,
    Netsh = 1,
    PowerShell = 2,
    Wmi = 3
}
