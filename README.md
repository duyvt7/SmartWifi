# Crane Wi-Fi Controller (C# .NET 8)

Ứng dụng điều khiển, giám sát và tự động tối ưu hóa kết nối Wi-Fi chuyên dụng cho máy tính công nghiệp đặt trên cẩu (Crane PC) tại các bến cảng, kho bãi và nhà máy.

---

## ⚡ HƯỚNG DẪN KHỞI ĐỘNG NHANH (QUICK START)

1. **Tải và giải nén thư mục:**
   - Tải source code hoặc bản release về máy tính cẩu và giải nén.
2. **Khởi chạy ứng dụng:**
   - Nhấp đúp vào file [**`Start.bat`**](file:///d:/Application/SmartWifi-main/Start.bat) ở thư mục gốc.
   - File thực thi siêu nhẹ [**`App_SingleFile/CraneWifiController.exe`**](file:///d:/Application/SmartWifi-main/App_SingleFile/CraneWifiController.exe) (chỉ ~846 KB) đã được đóng gói sẵn.
   - `Start.bat` sẽ tự động yêu cầu quyền Administrator để quản lý card mạng và mở **Mini-Widget** ở góc trên bên phải màn hình.
3. **Yêu cầu môi trường:**
   - Hệ điều hành: Windows 10 / Windows 11 (64-bit).
   - Yêu cầu cài đặt **[.NET Desktop Runtime 8.0 (x64)](https://dotnet.microsoft.com/download/dotnet/8.0/runtime)** của Microsoft (nếu máy chưa có, `Start.bat` sẽ tự động hiển thị thông báo hướng dẫn tải).

---

## 🌟 TÍNH NĂNG NỔI BẬT & CƠ CHẾ HOẠT ĐỘNG

### 1. Smart Roaming (Fast Roaming 0ms Downtime)
* **Giám sát thụ động:** Đọc liên tục cường độ tín hiệu sóng (RSSI/Signal Quality) từ bộ nhớ đệm Native Wi-Fi Driver (`wlanapi.dll`) mỗi 2 giây mà **không kích hoạt quét chủ động (Active Scan)**, loại bỏ hoàn toàn hiện tượng rớt gói tin (packet drop) khi cẩu đang vận hành.
* **Thuật toán Hysteresis:** Chỉ chuyển trạm phát (Access Point - BSSID) khi trạm mới có sóng vượt trội hơn trạm hiện tại một khoảng quy định (mặc định +15%) và duy trì ổn định qua 2 chu kỳ liên tiếp để tránh hiện tượng "Ping-Pong" (nhảy qua lại liên tục giữa 2 cột sóng).
* **Chuyển sóng siêu tốc:** Truyền trực tiếp địa chỉ MAC trạm phát mong muốn (`DOT11_BSSID_LIST`) vào hàm kết nối native để card mạng chuyển thẳng tới trạm đó mà không mất thời gian dò lại từ đầu.

### 2. Tự Động Chuyển Wi-Fi Khi Mất Ping Server (Failover on Ping Timeout)
* **Phát hiện mất thông mạng:** Watchdog gửi các gói tin ICMP siêu nhẹ (32 bytes) định kỳ tới các máy chủ nghiệp vụ (TOS Server, Gateway cảng). Nếu toàn bộ server không phản hồi vượt quá ngưỡng (mặc định 15 giây):
  1. **Ngắt kết nối ngay lập tức:** Gọi hàm `WlanDisconnect` ngắt card mạng khỏi Wi-Fi hiện tại.
  2. **Tạm khóa cách ly (Quarantine/Blacklist):** Đưa SSID lỗi vào danh sách cách ly tạm thời (mặc định 300 giây / 5 phút). Trong thời gian này, thuật toán tự động kết nối và Smart Roaming sẽ **bỏ qua hoàn toàn** mạng này.
  3. **Chuyển sang Wi-Fi khác:** Quét và tự động kết nối ngay tới mạng Wi-Fi ưu tiên tiếp theo (trong danh sách `PreferredNetworks`, ví dụ chuyển từ `PORT_CRANE_5G` sang `PORT_CRANE_2.4G`).
  4. **Cấp thời gian ổn định (Grace Period):** Cấp thời gian chờ 10 giây để card mạng kịp nhận IP DHCP trước khi tiếp tục kiểm tra ping.
  5. **Tự động mở khóa:** Khi hết hạn 5 phút, mạng Wi-Fi đó sẽ tự động được gỡ khỏi danh sách tạm khóa và có thể kết nối lại nếu có chất lượng sóng tốt.

### 3. Tự Cứu Hộ Đa Tầng (3-Layer Self-Healing)
* **Tầng 1 (Failover Wi-Fi):** Đổi sang mạng Wi-Fi dự phòng khác khi mất ping máy chủ vượt 15 giây.
* **Tầng 2 (OS Tweaks Check):** Tự động phát hiện và tắt chế độ Máy bay (Airplane Mode) hoặc Chế độ Máy tính bảng (Tablet Mode) nếu người vận hành vô tình bật nhầm.
* **Tầng 3 (Hardware Adapter Reset):** Nếu tất cả các mạng Wi-Fi đều mất kết nối quá thời gian quy định (mặc định 45 giây), hệ thống tự động Reset card mạng phần cứng (qua WMI `Win32_NetworkAdapter`, netsh và PowerShell fallback) để khôi phục driver mạng.

### 4. Can Thiệp Hệ Điều Hành (OS Tweaks)
* **Khóa Airplane Mode:** Can thiệp Registry `HKLM\SYSTEM\CurrentControlSet\Control\RadioManagement` đặt `SystemRadioState = 0`.
* **Khóa Tablet Mode:** Can thiệp Registry `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell` đặt `TabletMode = 0` và `SignInMode = 1` (luôn vào Desktop Mode).
* **Tự khởi động cùng Windows không qua UAC:** Tự động tạo tác vụ trong Windows Task Scheduler với quyền cao nhất (`/rl highest /sc onlogon`), giúp phần mềm tự bật khi máy tính cẩu khởi động mà không bị kẹt ở màn hình xác nhận UAC.

### 5. Giao Diện Kép Chuyên Dụng Cho Cabin Cẩu
* **Floating Mini-Widget:** Tiện ích nổi siêu nhẹ ở góc màn hình, luôn hiển thị trên cùng (`Topmost`), tự động hít vào 4 cạnh màn hình (Edge Snapping), lưu nhớ vị trí kéo thả. Đèn LED vector tự đổi màu theo chất lượng kết nối:
  * 🟢 **Xanh lá (> 70%):** Sóng khỏe, hệ thống ổn định.
  * 🟡 **Vàng cam (45% - 70%):** Sóng trung bình hoặc đang chuyển mạng dự phòng.
  * 🔴 **Đỏ nhấp nháy (< 45% hoặc mất mạng):** Sóng yếu, mất ping server hoặc đang tự cứu hộ.
* **Kiosk Control Panel:** Thiết kế phong cách **Dark Glassmorphism** công nghiệp tương phản cao, phím bấm kích thước lớn (tối thiểu 48x48px) cho màn hình cảm ứng cabin cẩu, tích hợp bàn phím ảo số (On-screen Numpad) để nhập IP không cần bàn phím cứng, Live Log Console thời gian thực.

---

## 📖 HƯỚNG DẪN SỬ DỤNG CHI TIẾT

```mermaid
flowchart LR
    A["Khởi động phần mềm (Start.bat)"] --> B["Floating Mini-Widget (Mặc định)"]
    B -- "Bấm nút bánh răng ⚙️ hoặc đúp click khay hệ thống" --> C["Kiosk Control Panel (Cấu hình)"]
    C -- "Bấm nút Thu nhỏ _ hoặc nút X" --> B
```

### 1. Sử Dụng Floating Mini-Widget (Dành cho tài xế cẩu)
* **Xem nhanh trạng thái:**
  * **Tên mạng & Sóng:** Ví dụ `PORT_CRANE_5G (88%)`.
  * **Độ trễ Ping:** Ví dụ `Server: 12ms` (hoặc `Timeout` khi mất kết nối).
  * **Huy hiệu trạng thái (Status Badge):** Tự động hiển thị các thông báo quan trọng:
    * `"Hệ thống ổn định"`
    * `"Đã chuyển BSSID (xx:xx)"`
    * `"Mất ping! Đổi sang PORT_CRANE_2.4G"`
    * `"Đang tự cứu hộ Card mạng..."`
* **Di chuyển Widget:** Nhấp giữ chuột trái (hoặc chạm giữ trên màn hình cảm ứng) vào vùng widget và kéo thả đến vị trí mong muốn. Khi thả ra gần mép màn hình, widget sẽ tự động hít vào cạnh.
* **Mở Bảng điều khiển Kiosk:** Bấm vào biểu tượng bánh răng nhỏ ⚙️ ở góc trên bên phải của Widget.

---

### 2. Sử Dụng Bảng Điều Khiển Kiosk (Dành cho Kỹ thuật viên & Admin)

Giao diện Kiosk chia thành 3 thẻ chức năng chính (Bento Grid):

#### 📌 Thẻ 1: Quản Lý Máy Chủ & Giám Sát (Server Watchdog)
* **Danh sách máy chủ:** Hiển thị thời gian thực trạng thái (ONLINE / OFFLINE) và số ms ping của từng máy chủ (TOS Server, Gateway cảng, v.v.).
* **Thêm máy chủ mới:**
  * Nhập Tên máy chủ (ví dụ: `TOS Server 2`).
  * Nhập Địa chỉ IP hoặc Domain (ví dụ: `192.168.1.20`).
  * **Bàn phím ảo (Touch Numpad):** Sử dụng các phím số lớn `0-9`, dấu chấm `.`, phím xóa `DEL` và xóa hết `CLR` tích hợp sẵn trên giao diện để nhập nhanh IP trên màn hình cảm ứng mà không cần cắm bàn phím vật lý.
  * Bấm **"THÊM MÁY CHỦ"**.
* **Xóa máy chủ:** Bấm nút **"XÓA"** màu đỏ cạnh server muốn loại bỏ.
* **Thanh trượt "Thời gian chờ trước khi Reset Card":** Kéo chỉnh ngưỡng giây mất kết nối liên tục (30s – 120s) trước khi kích hoạt reset card mạng phần cứng.

#### 📌 Thẻ 2: Quản Lý Wi-Fi, Roaming & Chuyển Mạng Dự Phòng
* **Thông số Wi-Fi kỹ thuật:** Xem chi tiết SSID, BSSID (địa chỉ MAC của Access Point hiện tại), Kênh phát (Channel), Băng tần (2.4 GHz / 5 GHz), Cường độ tín hiệu (RSSI tính bằng dBm) và Tốc độ Link Speed (Mbps).
* **Cấu hình Fast Roaming:**
  * Bật/Tắt checkbox: *"Tự động Fast Roaming (Chuyển AP sóng khỏe)"*.
  * Thanh trượt *"Độ lệch Hysteresis để nhảy sóng"*: Mặc định `+15%` (chỉ nhảy sang AP mới nếu AP đó mạnh hơn AP cũ ít nhất 15%).
  * Nút **"QUÉT & CHUYỂN SÓNG NGAY"**: Kích hoạt card mạng quét sóng ngay lập tức để tìm AP tốt hơn.
* **Cấu hình Tự chuyển Wi-Fi khi mất Ping Server (Failover & Quarantine):**
  * Checkbox: *"Tự chuyển Wi-Fi khi mất Ping Server"* (bật/tắt tính năng failover).
  * Thanh trượt *"Ngưỡng mất Ping để đổi Wi-Fi"*: Mặc định `15s` (từ 5s đến 40s).
  * Thanh trượt *"Thời gian tạm khóa Wi-Fi lỗi"*: Mặc định `5 phút` (từ 1 phút đến 30 phút).
  * Dòng trạng thái *"Mạng đang bị khóa: X"*: Thống kê số lượng SSID đang bị cách ly.
  * Nút **"XÓA KHÓA TẤT CẢ"**: Nhấp vào đây để mở khóa thủ công cho toàn bộ các mạng đang bị cách ly mà không cần đợi hết thời gian timeout.
* **Danh sách Wi-Fi cấu hình sẵn:** Hiển thị các mạng ưu tiên đã nạp vào hệ thống (`PORT_CRANE_5G`, `PORT_CRANE_2.4G`, v.v.).

#### 📌 Thẻ 3: Hệ Điều Hành & Cứu Hộ Khẩn Cấp
* **Trạng thái OS:** Đèn xác nhận `Airplane Mode: LOCKED OFF` và `Tablet Mode: LOCKED OFF`.
* **Tự bật cùng Windows:** Checkbox *"Tự bật cùng Windows (Task Scheduler)"* tự động đăng ký hoặc gỡ bỏ tác vụ khởi động ngầm không qua UAC.
* **Chọn Card mạng Wi-Fi:** Menu thả xuống cho phép chọn đúng tên card mạng vật lý đang kết nối anten trên cẩu (ví dụ `Intel(R) Wi-Fi 6 AX201`).
* **Nút cứu hộ khẩn cấp:** **"🚨 LÀM MỚI MẠNG NGAY (FORCE RESET)"** — Dành cho trường hợp người vận hành muốn chủ động ép ngắt và khởi động lại card mạng ngay lập tức.
* **Live Log Console:** Cửa sổ hiển thị nhật ký thời gian thực ghi nhận mọi sự kiện chuyển sóng, mất mạng, đổi mạng, reset card. Có nút **"XÓA NHẬT KÝ"** để làm sạch màn hình.

---

### 3. Khay Hệ Thống (System Tray Icon)
Ứng dụng luôn chạy thường trực dưới khay hệ thống (góc dưới bên phải thanh Taskbar Windows):
* **Nhấp đúp chuột trái vào icon:** Mở nhanh Bảng điều khiển Kiosk.
* **Nhấp chuột phải vào icon:** Mở menu tác vụ nhanh:
  * *Hiện Mini-Widget*
  * *Mở Bảng điều khiển Kiosk*
  * *Làm mới Card mạng (Force Reset)*
  * *Thoát ứng dụng*

> [!NOTE]
> Khi bấm nút đóng (X) trên Bảng điều khiển Kiosk, ứng dụng **không bị tắt** mà sẽ tự động thu nhỏ về dạng Mini-Widget nổi để đảm bảo hệ thống bảo vệ mạng cẩu luôn chạy 24/7. Chỉ khi chọn nút **"THOÁT ỨNG DỤNG"** có màu đỏ trong Kiosk hoặc trên menu khay hệ thống thì ứng dụng mới dừng hoàn toàn.

---

## ⚙️ CẤU HÌNH TRỰC TIẾP QUA FILE `appsettings.json`

File cấu hình nằm cùng thư mục chạy file exe ([`App_SingleFile/appsettings.json`](file:///d:/Application/SmartWifi-main/App_SingleFile/appsettings.json)). Các kỹ thuật viên IT có thể chỉnh sửa trực tiếp bằng Notepad:

```json
{
  "SystemSettings": {
    "DisableAirplaneModeOnStartup": true,
    "DisableTabletModeOnStartup": true,
    "EnforceOsModesIntervalSeconds": 10,
    "StartInMiniWidgetMode": true,
    "WidgetPositionX": -1,
    "WidgetPositionY": -1,
    "AutoStartWithWindows": true
  },
  "WatchdogSettings": {
    "CheckIntervalSeconds": 5,
    "PingTimeoutMs": 1500,
    "MaxDisconnectedSecondsBeforeReset": 45,
    "ResetCooldownSeconds": 60,
    "PreferredAdapterName": null,
    "TargetServers": [
      {
        "Name": "TOS Server Chính",
        "Address": "192.168.1.10",
        "IsEnabled": true
      },
      {
        "Name": "Gateway Cảng",
        "Address": "192.168.1.1",
        "IsEnabled": true
      },
      {
        "Name": "DNS Dự phòng",
        "Address": "8.8.8.8",
        "IsEnabled": false
      }
    ]
  },
  "SmartWifiSettings": {
    "PassiveCheckIntervalSeconds": 2,
    "BssCacheReadIntervalSeconds": 10,
    "WeakSignalThresholdPercent": 45,
    "RoamingHysteresisDeltaPercent": 15,
    "EnableSmartRoaming": true,
    "EnablePingFailWifiSwitch": true,
    "PingFailWifiSwitchSeconds": 15,
    "WifiBlacklistDurationSeconds": 300,
    "PreferredNetworks": [
      {
        "Ssid": "PORT_CRANE_5G",
        "Password": "portpassword",
        "Priority": 1,
        "AutoConnect": true
      },
      {
        "Ssid": "PORT_CRANE_2.4G",
        "Password": "portpassword",
        "Priority": 2,
        "AutoConnect": true
      }
    ]
  }
}
```

### Ý nghĩa các thông số quan trọng:

| Nhóm | Tham số | Giá trị gợi ý | Ý nghĩa |
|---|---|---|---|
| **System** | `AutoStartWithWindows` | `true` | Tự động tạo Task Scheduler bật app cùng Windows. |
| | `StartInMiniWidgetMode` | `true` | Mặc định mở Widget nhỏ gọn khi khởi động. |
| **Watchdog** | `CheckIntervalSeconds` | `5` | Chu kỳ gửi gói tin kiểm tra ping (giây). |
| | `MaxDisconnectedSecondsBeforeReset` | `45` | Thời gian mất toàn bộ server trước khi Reset card mạng vật lý. |
| | `ResetCooldownSeconds` | `60` | Thời gian nghỉ sau khi reset card mạng trước khi bắt đầu đếm lỗi lại. |
| **SmartWifi** | `PassiveCheckIntervalSeconds` | `2` | Chu kỳ đọc chất lượng sóng thụ động từ driver (0ms downtime). |
| | `RoamingHysteresisDeltaPercent` | `15` | Độ lệch sóng cần thiết (+%) để nhảy sang trạm phát mới. |
| | `EnablePingFailWifiSwitch` | `true` | Bật tính năng tự ngắt và chuyển Wi-Fi khi mất ping server. |
| | `PingFailWifiSwitchSeconds` | `15` | Thời gian mất ping liên tục để kích hoạt đổi Wi-Fi (nên nhỏ hơn thời gian Reset card). |
| | `WifiBlacklistDurationSeconds` | `300` | Thời gian cách ly tạm thời mạng lỗi (giây). 300s = 5 phút. |
| | `PreferredNetworks` | Danh sách | Các mạng Wi-Fi ưu tiên (kèm mật khẩu plaintext để app tự tạo Profile kết nối ngầm). |

---

## 🏗 CẤU TRÚC MÃ NGUỒN (MODULAR ARCHITECTURE)

```text
CraneWifiController/
│
├── App_SingleFile/
│   ├── CraneWifiController.exe              # File chạy siêu nhẹ ~846 KB có sẵn
│   └── appsettings.json                     # File cấu hình JSON
│
├── src/
│   ├── Modules/                             # Các thư viện Class Library (.dll) độc lập
│   │   ├── Company.Core.Abstractions/       # Interfaces, Enums, DTOs, Events
│   │   ├── Company.Module.ConfigManager/    # Quản lý đọc/ghi JSON & Rolling File Logger
│   │   ├── Company.Module.OsTweaks/         # Khóa Airplane & Tablet Mode, Task Scheduler
│   │   ├── Company.Module.NetworkWatchdog/  # Non-disruptive Ping, Watchdog, NetAdapter Reset
│   │   └── Company.Module.SmartWifi/        # Native Wifi API (wlanapi P/Invoke), Fast Roaming & Quarantine
│   │
│   └── Presentation/
│       └── CraneWifi.WidgetApp/             # Giao diện WPF (Mini-Widget, Kiosk Dashboard, System Tray)
│
├── tests/
│   └── CraneWifi.Tests/                     # Bộ kiểm thử đơn vị xUnit (13/13 Passed 100%)
│
├── CraneWifiController.sln
├── Start.bat                                # Trình kích hoạt 1-click có kiểm tra Administrator
└── README.md
```

---

## 🚀 HƯỚNG DẪN BIÊN DỊCH DÀNH CHO LẬP TRÌNH VIÊN

### Yêu cầu:
* Windows 10/11 (64-bit).
* .NET 8.0 SDK (Windows Desktop workload).

### 1. Biên dịch toàn bộ Solution:
```powershell
dotnet build CraneWifiController.sln -c Release
```

### 2. Chạy bộ kiểm thử tự động (Unit Tests):
```powershell
dotnet test tests/CraneWifi.Tests/CraneWifi.Tests.csproj -c Release
```

### 3. Đóng gói lại thành 1 file thực thi siêu nhẹ (.exe):
```powershell
dotnet publish src/Presentation/CraneWifi.WidgetApp/CraneWifi.WidgetApp.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./App_SingleFile
```
