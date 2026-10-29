# Crane Wi-Fi Controller (C# .NET 8)

Ứng dụng điều khiển, giám sát và tự động tối ưu hóa kết nối Wi-Fi chuyên dụng cho máy tính công nghiệp đặt trên cẩu (Crane PC).

---

## 🌟 Tính Năng Nổi Bật

* **Kiến trúc Modular Độc lập (Clean Architecture):** 5 thư viện Class Library (.dll) độc lập hoàn toàn với tầng Presentation (WPF), có thể tái sử dụng hoặc đóng gói NuGet.
* **Smart Roaming (Fast Roaming 0ms downtime):** 
  * Giám sát thụ động cường độ sóng qua bộ đệm Native Wi-Fi Driver (`wlanapi.dll`).
  * Tự động chuyển Access Point (BSSID) có sóng khỏe nhất trên cùng SSID theo thuật toán Hysteresis mà không làm rớt gói tin.
  * Tự động kết nối lại vào mạng dự phòng khi mất hẳn sóng.
* **Network Watchdog & Self-Healing:**
  * Giám sát kết nối tới danh sách máy chủ TOS / Gateway định kỳ bằng gói tin ICMP siêu nhỏ (32 bytes).
  * Tự động reset card mạng Wi-Fi (netsh / PowerShell / WMI fallback) khi mất kết nối liên tục vượt ngưỡng.
* **Can thiệp Hệ điều hành (OS Tweaks):**
  * Tự động khóa và vô hiệu hóa Airplane Mode và Tablet Mode.
  * Tích hợp Windows Task Scheduler với quyền Administrator (`/rl highest /sc onlogon`), tự bật khi khởi động Windows mà không bị kẹt ở màn hình UAC.
* **Giao diện Kép (Mini-Widget & Kiosk Bento Panel):**
  * **Floating Mini-Widget:** Tiện ích nổi siêu nhẹ, luôn ở trên cùng, tự động hít vào cạnh màn hình, đèn báo Vector đổi màu theo chất lượng sóng.
  * **Kiosk Control Panel:** Thiết kế phong cách Dark Glassmorphism công nghiệp tương phản cao, tích hợp bàn phím ảo (Touch Numpad) cho màn hình cảm ứng, Live Log Console thời gian thực.
* **Siêu nhẹ & Hiệu năng cao:**
  * File thực thi nén toàn bộ chỉ **~827 KB**.
  * Tiêu thụ RAM chỉ **~12MB - 18MB**, CPU **< 0.5%**.

---

## 🏗 Cấu Trúc Dự Án

```text
CraneWifiController/
│
├── src/
│   ├── Modules/
│   │   ├── Company.Core.Abstractions/       # Interfaces, Enums, DTOs, Events
│   │   ├── Company.Module.ConfigManager/    # JSON settings & Rolling file logger (7 ngày)
│   │   ├── Company.Module.OsTweaks/         # Khóa Airplane & Tablet Mode, Task Scheduler
│   │   ├── Company.Module.NetworkWatchdog/  # Non-disruptive Ping, Watchdog, NetAdapter Reset
│   │   └── Company.Module.SmartWifi/        # Native Wifi API (wlanapi P/Invoke), Fast Roaming
│   │
│   └── Presentation/
│       └── CraneWifi.WidgetApp/             # Giao diện WPF (Mini-Widget, Kiosk Dashboard, System Tray)
│
├── tests/
│   └── CraneWifi.Tests/                     # Bộ kiểm thử đơn vị xUnit (7/7 Passed)
│
├── CraneWifiController.sln
└── PLAN.md
```

---

## 🚀 Hướng Dẫn Biên Dịch & Chạy Thử

### Yêu cầu:
* Windows 10/11 (64-bit).
* .NET 8.0 SDK (Windows Desktop workload).

### Biên dịch toàn bộ Solution:
```powershell
dotnet build CraneWifiController.sln -c Release
```

### Chạy ứng dụng trực tiếp:
```powershell
dotnet run --project src/Presentation/CraneWifi.WidgetApp/CraneWifi.WidgetApp.csproj
```

### Đóng gói thành 1 file .exe duy nhất:
```powershell
dotnet publish src/Presentation/CraneWifi.WidgetApp/CraneWifi.WidgetApp.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/lightweight
```
File thực thi tạo ra sẽ nằm tại `publish/lightweight/CraneWifi.WidgetApp.exe` (dung lượng ~827 KB).
