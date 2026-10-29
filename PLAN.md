# KẾ HOẠCH PHÁT TRIỂN DỰ ÁN: CRANE WI-FI CONTROLLER (C#)

Tài liệu này định nghĩa kiến trúc, giải pháp kỹ thuật và lộ trình triển khai cho ứng dụng điều khiển, giám sát và tự động tối ưu hóa kết nối Wi-Fi chuyên dụng cho máy tính công nghiệp đặt trên cẩu (Crane PC). Tài liệu được thiết kế chuẩn hóa để AI Agent (Antigravity) thực thi tuần tự.

---

## 1. TỔNG QUAN & KIẾN TRÚC HỆ THỐNG

* **Ngôn ngữ & Nền tảng:** C# (.NET 8.0 - Windows Desktop, yêu cầu `TargetFramework` là `net8.0-windows10.0.19041.0` trở lên để hỗ trợ gọi trực tiếp Windows Runtime APIs).
* **Loại hình ứng dụng:** Kết hợp **Floating Widget** (tiện ích nổi siêu nhẹ, luôn hiển thị ở góc màn hình cho người vận hành) và **Kiosk Settings Panel** (bảng điều khiển toàn màn hình/cố định có khóa mật khẩu dành cho kỹ thuật viên/admin).
* **Tối ưu hóa dung lượng & tài nguyên:** 
  * Không sử dụng các thư viện UI nặng nề của bên thứ ba. Tự định nghĩa Custom Style bằng Pure XAML.
  * Sử dụng cơ chế giải phóng bộ nhớ chủ động và tối ưu hóa vòng lặp nền (`PeriodicTimer`) để RAM tiêu thụ luôn dưới 50MB và CPU dưới 1%.
* **Kiến trúc phần mềm:** **Modular Architecture (Kiến trúc đa mô-đun)**. Toàn bộ logic nghiệp vụ được tách thành các **Class Library (.dll) hoàn toàn độc lập**, không phụ thuộc vào UI, giao tiếp thông qua `Interfaces` và `Dependency Injection (Microsoft.Extensions.DependencyInjection)`. Các module này có thể đóng gói thành NuGet package để tái sử dụng cho các dự án khác trong tương lai.

---

## 2. CẤU TRÚC THƯ MỤC & SOLUTION (MODULAR DESIGN)

Antigravity cần khởi tạo Solution `CraneWifiController.sln` với cấu trúc các Project như sau:

```text
CraneWifiController/
│
├── src/
│   ├── Modules/ (Các thư viện .dll độc lập hoàn toàn để tái sử dụng)
│   │   ├── Company.Core.Abstractions/       # Chứa toàn bộ Interfaces, DTOs, Events dùng chung
│   │   ├── Company.Module.OsTweaks/         # Module can thiệp hệ điều hành (Airplane, Tablet mode)
│   │   ├── Company.Module.NetworkWatchdog/  # Module kiểm tra kết nối Server & Reset Card mạng
│   │   ├── Company.Module.SmartWifi/        # Module quản lý Wi-Fi, kiểm tra sóng ngầm & Fast Roaming
│   │   └── Company.Module.ConfigManager/    # Module lưu trữ cấu hình (JSON) & Quản lý Wi-Fi Profiles
│   │
│   └── Presentation/
│       └── CraneWifi.WidgetApp/             # Ứng dụng WPF chính (Giao diện Widget/Kiosk, DI Host)
│
└── PLAN.md
```

---

## 3. ĐẶC TẢ KỸ THUẬT CHI TIẾT TỪNG MODULE

### 3.1. `Company.Core.Abstractions` (Class Library)
Định nghĩa các giao ước chung để các module không bị phụ thuộc chéo:
* `IOsModeService`: Giao diện kiểm soát trạng thái hệ điều hành.
* `IConnectionWatchdogService`: Giao diện theo dõi kết nối tới danh sách Server.
* `INetworkAdapterManager`: Giao diện bật/tắt/khởi động lại phần cứng Card mạng.
* `ISmartWifiService`: Giao diện quản lý sóng Wi-Fi, đọc BSSID và chuyển vùng sóng (Roaming).
* `IConfigService`: Giao diện đọc/ghi cấu hình tự động.

### 3.2. `Company.Module.OsTweaks` (Class Library)
**Mục tiêu:** Tự động tắt chế độ Máy bay (Airplane Mode) và chế độ Máy tính bảng (Tablet Mode) ngay khi bật app, đồng thời lắng nghe để chặn nếu người dùng vô tình bật lại.
* **Giải pháp kỹ thuật:**
  * **Tablet Mode:** Can thiệp vào Registry `HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell`. Đặt giá trị `TabletMode = 0` và `SignInMode = 1` (Luôn vào Desktop mode).
  * **Airplane Mode:** 
    * Cách 1 (Qua Registry/System): Can thiệp `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState` đặt về `0`.
    * Cách 2 (Qua WinRT API - Khuyên dùng): Sử dụng namespace `Windows.Devices.Radios`. Gọi `Radio.GetRadiosAsync()`, lọc các thiết bị thuộc `RadioKind.WiFi`, và ép trạng thái `SetStateAsync(RadioState.On)`.
* **Yêu cầu quyền hạn:** Module này yêu cầu ứng dụng chạy dưới quyền `Administrator` (Cấu hình sẵn trong file `app.manifest` với `requestedExecutionLevel level="requireAdministrator"`).

### 3.3. `Company.Module.NetworkWatchdog` (Class Library)
**Mục tiêu:** Kiểm tra kết nối liên tục tới các Server do người dùng chỉ định mà không gây nghẽn mạng. Tự động reset card mạng nếu mất kết nối quá lâu.
* **Giải pháp kỹ thuật kiểm tra kết nối (Non-disruptive Ping):**
  * Cho phép truyền vào một danh sách `List<ServerTarget>` (hỗ trợ cả IP và Domain, có thể thêm/sửa/xóa lúc chương trình đang chạy).
  * Sử dụng `System.Net.NetworkInformation.Ping` kết hợp với vòng lặp bất đồng bộ `PeriodicTimer` (Chu kỳ mặc định: 5 giây/lần).
  * **Nguyên tắc không gây mất kết nối:** Chỉ gửi gói tin ICMP siêu nhỏ (32 bytes), thiết lập Timeout ngắn (1500ms). Chỉ cần **1 trong số các Server** trong danh sách phản hồi thành công thì tính là mạng đang通 (Online).
* **Giải pháp tự động Reset Card mạng (Self-Healing):**
  * Duy trì bộ đếm thời gian mất kết nối liên tục (`DisconnectionDuration`).
  * Nếu toàn bộ các Server đều không phản hồi vượt quá ngưỡng cấu hình (ví dụ: 45 giây):
    1. Phát sự kiện cảnh báo lên UI (`OnAdapterResetting`).
    2. Sử dụng **WMI (`System.Management`)** truy vấn lớp `Win32_NetworkAdapter` (lọc theo `NetConnectionID LIKE '%Wi-Fi%'` hoặc `AdapterTypeID = 9`), gọi phương thức `Disable()`, đợi 3 giây (`Task.Delay`), sau đó gọi `Enable()`. *(Có phương án dự phòng bằng cách chạy tiến trình ngầm `netsh interface set interface name="Wi-Fi" admin=disabled/enabled`)*.
    3. Sau khi Reset xong, đặt thời gian chờ phục hồi (Cooldown) là 60 giây trước khi bắt đầu đếm lỗi lại để tránh vòng lặp reset liên tục.

### 3.4. `Company.Module.SmartWifi` (Class Library)
**Mục tiêu:** Kiểm tra tín hiệu sóng Wi-Fi thường xuyên mà **không gây ảnh hưởng kết nối hiện tại**. Tự động chuyển sang cục phát (Access Point - BSSID) hoặc mạng (SSID) có sóng tốt hơn với thời gian gián đoạn (downtime) thấp nhất. Tự động hóa tối đa cho người vận hành.
* **Vấn đề kỹ thuật cần tránh:** Tuyệt đối **KHÔNG** gọi lệnh buộc card mạng quét lại toàn bộ băng tần (`WlanScan`) ở tần suất cao (ví dụ mỗi 5 giây), vì lúc card mạng quét các kênh (channel) khác, nó sẽ ngừng truyền nhận dữ liệu ở kênh hiện tại trong khoảng 100ms - 500ms, gây rớt gói tin (packet loss) trên cẩu.
* **Giải pháp kỹ thuật (Zero-Impact Signal Monitoring & Fast Roaming):**
  * **Sử dụng Native Wifi API (`wlanapi.dll` qua P/Invoke hoặc gói thư viện mỏng `ManagedNativeWifi`):**
  * **Bước 1 - Giám sát thụ động (Passive Monitoring - Chu kỳ 2 giây/lần):** 
    * Chỉ gọi hàm `WlanQueryInterface` với cờ `wlan_intf_opcode_current_connection` và `wlan_intf_opcode_rssi`. Thao tác này đọc trực tiếp từ bộ nhớ đệm của Driver card mạng, tốn **0ms downtime**, hoàn toàn không ảnh hưởng kết nối.
  * **Bước 2 - Đọc danh sách AP từ bộ đệm (Cached BSS List - Chu kỳ 10 giây/lần):**
    * Gọi hàm `WlanGetNetworkBssList` để lấy danh sách các trạm phát (BSSID/MAC Address) xung quanh mà card mạng đã tự thu thập được một cách thụ động qua các gói tin Beacon.
    * Chỉ kích hoạt quét chủ động (`WlanScan`) khi và chỉ khi: Cường độ sóng hiện tại giảm xuống mức nguy hiểm (ví dụ: Signal Quality < 45% hoặc RSSI < -75 dBm).
  * **Bước 3 - Thuật toán quyết định chuyển sóng (Hysteresis Roaming Algorithm):**
    * Tránh hiện tượng "Ping-Pong" (chuyển qua lại liên tục giữa 2 trạm phát).
    * Điều kiện chuyển sóng: Đang có một trạm phát mới (cùng tên Wi-Fi SSID hoặc nằm trong danh sách Wi-Fi ưu tiên đã lưu sẵn cấu hình) có chất lượng sóng **cao hơn sóng hiện tại ít nhất 15%** (hoặc cao hơn 12 dBm) VÀ duy trì ổn định qua 2 lần kiểm tra liên tiếp.
  * **Bước 4 - Thực thi chuyển sóng giảm thiểu Downtime:**
    * Sử dụng hàm `WlanConnect` với cấu trúc `WLAN_CONNECTION_PARAMETERS`. 
    * **Mấu chốt giảm Downtime:** Truyền chính xác địa chỉ MAC (`DOT11_BSSID_LIST`) của cục phát Wi-Fi có sóng mạnh nhất vào tham số `pDesiredBssidList`. Việc chỉ định thẳng BSSID giúp card mạng nhảy thẳng tới kênh của cục phát đó mà không mất thời gian quét lại từ đầu, giảm thời gian chuyển mạng xuống chỉ còn vài chục mili-giây.
* **Tự động kết nối cho công nhân vận hành (Zero-Touch Provisioning):**
  * Ứng dụng tự động nạp sẵn các Wi-Fi Profile (SSID + Password + chuẩn mã hóa WPA2/WPA3 dưới dạng XML) từ file cấu hình vào Windows thông qua hàm `WlanSetProfile`.
  * Nếu bị đứt mạng đột ngột, module tự động quét danh sách các mạng đã lưu có sóng khỏe nhất và tự động kết nối lại, người lái cẩu không cần bấm bất kỳ nút nào.

---

## 4. THIẾT KẾ GIAO DIỆN (UI/UX) - WIDGET & KIOSK MODE

Giao diện sử dụng **WPF** được thiết kế theo phong cách **Modern Industrial Dark Glassmorphism** (Nền tối sâu `#121824`, viền sáng mảnh, bo góc mượt `CornerRadius="12"`, font chữ `Segoe UI Semibold` rõ nét, độ tương phản cực cao để dễ nhìn trong cabin cẩu bị chói nắng hoặc ban đêm).

### 4.1. Chế độ 1: Floating Mini-Widget (Chế độ mặc định cho người vận hành)
* **Kích thước:** Nhỏ gọn (ví dụ: `260px x 90px`), `WindowStyle="None"`, `AllowsTransparency="True"`, `Topmost="True"` (luôn nổi trên các phần mềm điều khiển cẩu khác), cho phép kéo thả tới bất kỳ góc nào trên màn hình và tự động hít vào cạnh màn hình (Snap to screen edges).
* **Thông tin hiển thị trực quan (Không có nút cấu hình phức tạp):**
  1. **Biểu tượng Cột sóng Wi-Fi lớn (Vector SVG):** Thay đổi màu sắc theo chất lượng sóng kèm hiệu ứng nhịp thở (Pulse animation):
     * Xanh lá neon (`#00E676`): Sóng tốt (> 70%).
     * Vàng cam (`#FFAB00`): Sóng trung bình (45% - 70%).
     * Đỏ chớp tắt (`#FF1744`): Sóng yếu (< 45%) hoặc mất kết nối Server.
  2. **Thông số cô đọng:** Tên Wi-Fi hiện tại + Phần trăm sóng (ví dụ: `CRANE_NET_01 (88%)`) và độ trễ Ping tới Server (ví dụ: `Server: 12ms`).
  3. **Trạng thái hệ thống (Status Badge):** Hiển thị dòng chữ trạng thái tự động rõ ràng: *"Hệ thống ổn định"*, *"Đang chuyển trạm phát sóng tốt hơn..."*, hoặc *"Đang tự động sửa lỗi mạng..."*.
  4. **Nút mở bảng Kiosk (Biểu tượng bánh răng nhỏ):** Bấm giữ 3 giây (hoặc nhập mã PIN đơn giản nếu cần) để mở giao diện cấu hình đầy đủ, tránh việc người vận hành bấm nhầm.

### 4.2. Chế độ 2: Kiosk Control Panel (Giao diện cấu hình & Giám sát chi tiết)
Khi mở rộng từ Widget, hiển thị một cửa sổ Kiosk hiện đại, bố cục dạng **Bento Grid (Lưới thẻ thông tin)** với các nút bấm kích thước lớn (Tối thiểu `48x48px` cho màn hình cảm ứng):
* **Thẻ 1 - Quản lý Danh sách Server (Server Watchdog):**
  * Danh sách các IP/Domain đang kiểm tra kèm đèn báo trạng thái (Online/Offline) và số ms Ping theo thời gian thực.
  * Ô nhập liệu lớn kèm bàn phím số ảo (On-screen Numpad tích hợp sẵn trên UI để dễ nhập IP `192.168.x.x` trên màn hình cảm ứng mà không cần bàn phím cứng).
  * Nút **Thêm Server**, **Xóa**, và thanh trượt chỉnh **Thời gian chờ trước khi Reset card mạng** (ví dụ: 30s - 120s).
* **Thẻ 2 - Quản lý Wi-Fi Tự động & Roaming:**
  * Danh sách các mạng Wi-Fi ưu tiên của cảng/nhà máy (Cho phép Admin thêm trước SSID và Password vào danh sách tự động kết nối).
  * Hiển thị thông tin kỹ thuật chuyên sâu: BSSID (MAC của cục phát hiện tại), Kênh (Channel), Tần số (2.4GHz / 5GHz), Cường độ RSSI (dBm).
  * Nút gạt (Toggle Switch) bật/tắt tính năng **Smart Roaming** và thanh trượt chỉnh ngưỡng nhạy chuyển sóng.
* **Thẻ 3 - Trạng thái Hệ điều hành & Cứu hộ khẩn cấp:**
  * Đèn báo xác nhận: `Airplane Mode: LOCKED OFF` | `Tablet Mode: LOCKED OFF`.
  * Nút bấm khẩn cấp kích thước lớn: **"LÀM MỚI MẠNG NGAY (FORCE RESET)"** dành cho trường hợp người vận hành muốn chủ động ép hệ thống kết nối lại.
  * Hộp hiển thị Nhật ký hoạt động trực tiếp (Live Log Console) ghi lại các sự kiện chuyển sóng, rớt mạng, reset card.

---

## 5. CẤU HÌNH LƯU TRỮ (`appsettings.json`)

Module `Company.Module.ConfigManager` sẽ tự động tạo và đồng bộ cấu hình xuống file `appsettings.json` cùng thư mục chạy file `.exe`:

```json
{
  "SystemSettings": {
    "DisableAirplaneModeOnStartup": true,
    "DisableTabletModeOnStartup": true,
    "EnforceOsModesIntervalSeconds": 10,
    "StartInMiniWidgetMode": true,
    "WidgetPositionX": 1600,
    "WidgetPositionY": 20
  },
  "WatchdogSettings": {
    "CheckIntervalSeconds": 5,
    "PingTimeoutMs": 1500,
    "MaxDisconnectedSecondsBeforeReset": 45,
    "ResetCooldownSeconds": 60,
    "TargetServers": [
      { "Name": "TOS Server Chính", "Address": "192.168.1.10", "IsEnabled": true },
      { "Name": "Gateway Cảng", "Address": "192.168.1.1", "IsEnabled": true }
    ]
  },
  "SmartWifiSettings": {
    "PassiveCheckIntervalSeconds": 2,
    "BssCacheReadIntervalSeconds": 10,
    "WeakSignalThresholdPercent": 50,
    "RoamingHysteresisDeltaPercent": 15,
    "PreferredNetworks": [
      { "Ssid": "PORT_CRANE_5G", "Password": "EncryptedPasswordHere", "Priority": 1, "AutoConnect": true },
      { "Ssid": "PORT_CRANE_2.4G", "Password": "EncryptedPasswordHere", "Priority": 2, "AutoConnect": true }
    ]
  }
}
```

---

## 6. LỘ TRÌNH THỰC THI CHO ANTIGRAVITY (IMPLEMENTATION STEPS)

Antigravity cần thực hiện tuần tự qua từng bước sau, đảm bảo code biên dịch thành công (Build Succeeded) ở mỗi bước trước khi qua bước tiếp theo:

* **Bước 1: Khởi tạo Solution và Architecture Core**
  * Tạo Solution và 6 Projects theo đúng cấu trúc ở Mục 2.
  * Cài đặt các gói NuGet cần thiết: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting`, `System.Management` (cho WMI), `ManagedNativeWifi` (hoặc viết P/Invoke wrapper cho `wlanapi.dll`).
  * Xây dựng toàn bộ Interfaces và Models trong `Company.Core.Abstractions`.

* **Bước 2: Xây dựng Module Cấu hình (`Company.Module.ConfigManager`)**
  * Triển khai đọc/ghi file `appsettings.json` an toàn (Thread-safe), tự động sinh file cấu hình mặc định nếu chưa tồn tại.
  * Hỗ trợ sự kiện `OnConfigurationChanged` để khi người dùng thêm/sửa IP Server trên giao diện, các module chạy ngầm tự động áp dụng ngay lập tức mà không cần khởi động lại app.

* **Bước 3: Xây dựng Module Hệ điều hành (`Company.Module.OsTweaks`)**
  * Viết logic can thiệp Registry tắt Tablet Mode và gọi WinRT API / Registry tắt Airplane Mode.
  * Tạo Background Worker kiểm tra định kỳ mỗi 10 giây để đảm bảo 2 chế độ này luôn bị vô hiệu hóa.

* **Bước 4: Xây dựng Module Giám sát Server & Phục hồi (`Company.Module.NetworkWatchdog`)**
  * Viết dịch vụ Ping bất đồng bộ tới danh sách Server.
  * Viết lớp điều khiển WMI / Netsh để Disable và Enable lại Card mạng Wi-Fi khi mất kết nối vượt ngưỡng.

* **Bước 5: Xây dựng Module Quản lý Wi-Fi Thông minh (`Company.Module.SmartWifi`)**
  * Triển khai việc nạp tự động Wi-Fi Profile (Zero-touch connection).
  * Triển khai đọc thông số RSSI/Signal Quality thụ động (không quét chủ động gây lag mạng).
  * Triển khai thuật toán so sánh danh sách BSSID và thực thi lệnh `WlanConnect` chỉ định thẳng địa chỉ MAC của Access Point có sóng tốt nhất để giảm thiểu downtime.

* **Bước 6: Xây dựng Giao diện WPF Widget & Kiosk (`CraneWifi.WidgetApp`)**
  * Cấu hình `app.manifest` yêu cầu quyền Administrator.
  * Thiết lập Dependency Injection trong `App.xaml.cs`, đăng ký toàn bộ các Module dưới dạng Singleton Services.
  * Thiết kế ResourceDictionary chứa toàn bộ Style giao diện tối (Dark Glassmorphism), Vector Icons, hiệu ứng chuyển động mượt mà.
  * Xây dựng cửa sổ **Mini-Widget** (luôn nổi, gọn nhẹ, tự động chạy mọi thứ) và cửa sổ **Kiosk Dashboard** (có bàn phím ảo Numpad để nhập IP Server trên màn hình cảm ứng).
  * Tích hợp System Tray Icon để ứng dụng không bao giờ bị tắt nhầm bởi người vận hành (Bấm nút X chỉ thu nhỏ về Widget).

---

## 7. TIÊU CHÍ NGHIỆM THU (ACCEPTANCE CRITERIA)

1. **Tính đóng gói & Tái sử dụng:** Các project trong thư mục `Modules` tuyệt đối không chứa tham chiếu tới `PresentationFramework` (WPF). Có thể copy bất kỳ file `.dll` nào sang một dự án Console hoặc WinForms khác đều chạy được ngay.
2. **Hiệu năng & Dung lượng:** Ứng dụng chạy mượt, giao diện phản hồi tức thì (60 FPS), không làm giật lag phần mềm điều khiển cẩu.
3. **Độ ổn định mạng:** Quá trình kiểm tra sóng Wi-Fi định kỳ (mỗi 2 giây) không làm tăng độ trễ (Ping spike) của kết nối hiện tại.
4. **Tự động hóa 100%:** Khi khởi động máy tính trên cẩu, ứng dụng tự bật, tự tắt Airplane/Tablet mode, tự kết nối vào mạng Wi-Fi khỏe nhất đã cấu hình, tự theo dõi Server và tự cứu hộ (Reset card mạng) khi treo mà người lái cẩu không cần chạm vào màn hình.