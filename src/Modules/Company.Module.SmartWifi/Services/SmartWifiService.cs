/**
 * @Module: Company.Module.SmartWifi.SmartWifiService
 * @Purpose: Dịch vụ điều khiển kết nối Wi-Fi thông minh cho xe cẩu công nghiệp. Cung cấp: Quét BSS thụ động qua Native Windows API (tiết kiệm thời gian, không gây rớt gói), Roaming nhanh giữa các Access Point dựa trên thuật toán Hysteresis chống chập chờn (Flapping), Tự động chuyển mạng Wi-Fi (Failover) và Cách ly (Blacklist) tạm thời khi mất kết nối máy chủ.
 * @Dependencies: Company.Core.Abstractions (Enums, Events, Interfaces, Models), Company.Module.SmartWifi.Interop (WlanNativeApi).
 * @Reusability: Tách biệt hoàn toàn qua ISmartWifiService interface và DI (IConfigService, ILoggerService). Có thể tái sử dụng cho bất kỳ hệ thống AGV, xe nâng, cẩu cảng, thiết bị tự hành di chuyển liên tục trong mạng Wi-Fi diện rộng (Campus/Industrial Mesh).
 */

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Events;
using Company.Core.Abstractions.Interfaces;
using Company.Core.Abstractions.Models;
using Company.Module.SmartWifi.Interop;

namespace Company.Module.SmartWifi.Services;

/// <summary>
/// Dịch vụ điều khiển Wi-Fi thông minh: Roaming nhanh native BSS, Failover đa mạng và cách ly Blacklist.
/// </summary>
public class SmartWifiService : ISmartWifiService, IDisposable
{
    private readonly IConfigService _configService;
    private readonly ILoggerService _logger;
    private IntPtr _clientHandle = IntPtr.Zero;
    private Guid _activeInterfaceGuid = Guid.Empty;
    private string _activeInterfaceName = string.Empty;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private WifiConnectionInfo _currentConnection = new();

    // Hysteresis candidate tracking: BSSID -> streak count
    private readonly Dictionary<string, int> _roamCandidateStreaks = new();
    private DateTime _lastScanTime = DateTime.MinValue;

    // Temporary Blacklist / Quarantine for SSIDs that fail server ping
    private readonly ConcurrentDictionary<string, BlacklistedWifiInfo> _blacklistedNetworks = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<WifiConnectionInfo>? ConnectionChanged;
    public event EventHandler<WifiRoamingEventArgs>? Roamed;
    public event EventHandler<WifiFailoverEventArgs>? FailoverTriggered;

    public bool IsRunning => _monitorTask != null && !_monitorTask.IsCompleted;
    public WifiConnectionInfo CurrentConnection => _currentConnection;

    public SmartWifiService(IConfigService configService, ILoggerService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        bool initialized = InitializeWlanHandle();
        if (!initialized)
        {
            _logger.Log(AppLogLevel.Error, "SmartWifi", "Không thể khởi tạo Windows Native Wi-Fi API (wlanapi.dll).");
            return;
        }

        // Auto-provision preferred profiles
        await ProvisionProfilesFromConfigAsync();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token), _cts.Token);
        _logger.Log(AppLogLevel.Info, "SmartWifi", $"Đã kích hoạt Smart Wi-Fi Monitor trên card mạng '{_activeInterfaceName}'.");
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_monitorTask != null)
            {
                try { await _monitorTask; } catch (OperationCanceledException) { }
            }
            _cts.Dispose();
            _cts = null;
            _monitorTask = null;
        }

        CloseWlanHandle();
        _logger.Log(AppLogLevel.Info, "SmartWifi", "Đã dừng dịch vụ Smart Wi-Fi.");
    }

    private bool InitializeWlanHandle()
    {
        if (_clientHandle != IntPtr.Zero) return true;

        uint result = WlanNativeApi.WlanOpenHandle(
            WlanNativeApi.WLAN_CLIENT_VERSION_VISTA,
            IntPtr.Zero,
            out _,
            out _clientHandle);

        if (result != 0 || _clientHandle == IntPtr.Zero)
        {
            return false;
        }

        // Find wireless interface
        result = WlanNativeApi.WlanEnumInterfaces(_clientHandle, IntPtr.Zero, out IntPtr pInterfaceList);
        if (result != 0 || pInterfaceList == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var listHeader = Marshal.PtrToStructure<WlanNativeApi.WLAN_INTERFACE_INFO_LIST>(pInterfaceList);
            if (listHeader.dwNumberOfItems > 0)
            {
                IntPtr itemPtr = IntPtr.Add(pInterfaceList, Marshal.SizeOf<WlanNativeApi.WLAN_INTERFACE_INFO_LIST>());
                var preferredAdapterName = _configService.CurrentConfig.WatchdogSettings.PreferredAdapterName;

                for (int i = 0; i < listHeader.dwNumberOfItems; i++)
                {
                    var info = Marshal.PtrToStructure<WlanNativeApi.WLAN_INTERFACE_INFO>(itemPtr);
                    if (string.IsNullOrWhiteSpace(preferredAdapterName) ||
                        info.strInterfaceDescription.Contains(preferredAdapterName, StringComparison.OrdinalIgnoreCase))
                    {
                        _activeInterfaceGuid = info.InterfaceGuid;
                        _activeInterfaceName = info.strInterfaceDescription;
                        break;
                    }

                    if (i == 0) // Default to first interface if preferred not matched yet
                    {
                        _activeInterfaceGuid = info.InterfaceGuid;
                        _activeInterfaceName = info.strInterfaceDescription;
                    }

                    itemPtr = IntPtr.Add(itemPtr, Marshal.SizeOf<WlanNativeApi.WLAN_INTERFACE_INFO>());
                }
                return _activeInterfaceGuid != Guid.Empty;
            }
        }
        finally
        {
            WlanNativeApi.WlanFreeMemory(pInterfaceList);
        }

        return false;
    }

    private void CloseWlanHandle()
    {
        if (_clientHandle != IntPtr.Zero)
        {
            WlanNativeApi.WlanCloseHandle(_clientHandle, IntPtr.Zero);
            _clientHandle = IntPtr.Zero;
        }
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        int tickCounter = 0;

        while (!ct.IsCancellationRequested)
        {
            var passiveInterval = _configService.CurrentConfig.SmartWifiSettings.PassiveCheckIntervalSeconds;
            if (passiveInterval <= 0) passiveInterval = 2;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(passiveInterval), ct);
                if (ct.IsCancellationRequested) break;

                tickCounter++;

                // 1. Passive query of current connection (0ms impact)
                UpdateCurrentConnectionPassive();

                // 2. Evaluate BSS Cache periodically
                var bssInterval = _configService.CurrentConfig.SmartWifiSettings.BssCacheReadIntervalSeconds;
                int bssTicks = Math.Max(1, bssInterval / passiveInterval);

                if (tickCounter % bssTicks == 0)
                {
                    await EvaluateBssListAndRoamAsync();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Log(AppLogLevel.Warning, "SmartWifi", $"Lỗi trong vòng lặp SmartWifi: {ex.Message}");
            }
        }
    }

    private void UpdateCurrentConnectionPassive()
    {
        if (_clientHandle == IntPtr.Zero || _activeInterfaceGuid == Guid.Empty) return;

        var guid = _activeInterfaceGuid;
        uint result = WlanNativeApi.WlanQueryInterface(
            _clientHandle,
            ref guid,
            WlanNativeApi.WLAN_INTF_OPCODE.wlan_intf_opcode_current_connection,
            IntPtr.Zero,
            out uint dataSize,
            out IntPtr pData,
            IntPtr.Zero);

        if (result == 0 && pData != IntPtr.Zero)
        {
            try
            {
                var conn = Marshal.PtrToStructure<WlanNativeApi.WLAN_CONNECTION_ATTRIBUTES>(pData);
                var ssid = conn.wlanAssociationAttributes.dot11Ssid.GetString();
                var bssid = conn.wlanAssociationAttributes.GetBssidMac();
                int quality = (int)conn.wlanAssociationAttributes.wlanSignalQuality;
                int rssi = quality > 0 ? (quality / 2) - 100 : -100;

                var state = conn.isState switch
                {
                    WlanNativeApi.WLAN_INTERFACE_STATE.wlan_interface_state_connected => WifiState.Connected,
                    WlanNativeApi.WLAN_INTERFACE_STATE.wlan_interface_state_associating => WifiState.Connecting,
                    WlanNativeApi.WLAN_INTERFACE_STATE.wlan_interface_state_authenticating => WifiState.Connecting,
                    _ => WifiState.Disconnected
                };

                var info = new WifiConnectionInfo
                {
                    State = state,
                    Ssid = ssid,
                    Bssid = bssid,
                    SignalQualityPercent = quality,
                    RssiDbm = rssi,
                    InterfaceName = _activeInterfaceName,
                    InterfaceId = _activeInterfaceGuid,
                    LinkSpeedMbps = (conn.wlanAssociationAttributes.ulRxRate + conn.wlanAssociationAttributes.ulTxRate) / 2000,
                    LastUpdatedUtc = DateTime.UtcNow
                };

                _currentConnection = info;
                ConnectionChanged?.Invoke(this, info);
            }
            finally
            {
                WlanNativeApi.WlanFreeMemory(pData);
            }
        }
        else
        {
            if (_currentConnection.State != WifiState.Disconnected)
            {
                _currentConnection = new WifiConnectionInfo
                {
                    State = WifiState.Disconnected,
                    InterfaceName = _activeInterfaceName,
                    InterfaceId = _activeInterfaceGuid,
                    LastUpdatedUtc = DateTime.UtcNow
                };
                ConnectionChanged?.Invoke(this, _currentConnection);
            }
        }
    }

    public async Task<IReadOnlyList<BssNetworkInfo>> GetNearbyBssListAsync(bool forceScan = false)
    {
        var list = new List<BssNetworkInfo>();
        if (_clientHandle == IntPtr.Zero || _activeInterfaceGuid == Guid.Empty) return list;

        var guid = _activeInterfaceGuid;

        if (forceScan)
        {
            WlanNativeApi.WlanScan(_clientHandle, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(2000); // Allow driver to populate cache
        }

        uint result = WlanNativeApi.WlanGetNetworkBssList(
            _clientHandle,
            ref guid,
            IntPtr.Zero,
            WlanNativeApi.DOT11_BSS_TYPE.dot11_BSS_type_infrastructure,
            false,
            IntPtr.Zero,
            out IntPtr pBssList);

        if (result == 0 && pBssList != IntPtr.Zero)
        {
            try
            {
                uint numItems = (uint)Marshal.ReadInt32(pBssList, 4); // dwNumberOfItems offset
                IntPtr entryPtr = IntPtr.Add(pBssList, 8); // Offset to entries array

                for (int i = 0; i < numItems; i++)
                {
                    var bss = Marshal.PtrToStructure<WlanNativeApi.WLAN_BSS_ENTRY>(entryPtr);
                    var bssid = WlanNativeApi.FormatMac(bss.dot11Bssid);
                    var ssid = bss.dot11Ssid.GetString();

                    list.Add(new BssNetworkInfo
                    {
                        Ssid = ssid,
                        Bssid = bssid,
                        SignalQualityPercent = (int)bss.uLinkQuality,
                        RssiDbm = bss.lRssi,
                        Channel = WlanNativeApi.FrequencyToChannel(bss.ulChCenterFrequency),
                        Band = WlanNativeApi.FrequencyToBand(bss.ulChCenterFrequency),
                        IsConnected = string.Equals(bssid, _currentConnection.Bssid, StringComparison.OrdinalIgnoreCase)
                    });

                    // Advance to next entry (approximate structure size with padding)
                    entryPtr = IntPtr.Add(entryPtr, Marshal.SizeOf<WlanNativeApi.WLAN_BSS_ENTRY>());
                }
            }
            finally
            {
                WlanNativeApi.WlanFreeMemory(pBssList);
            }
        }

        return list;
    }

    private async Task EvaluateBssListAndRoamAsync()
    {
        var settings = _configService.CurrentConfig.SmartWifiSettings;
        if (!settings.EnableSmartRoaming) return;

        // If signal is critical or disconnected, trigger active scan
        if (_currentConnection.State != WifiState.Connected ||
            _currentConnection.SignalQualityPercent < settings.WeakSignalThresholdPercent)
        {
            var secondsSinceScan = (DateTime.UtcNow - _lastScanTime).TotalSeconds;
            if (secondsSinceScan >= 15) // Limit scan frequency to avoid channel hopping packet drops
            {
                var guid = _activeInterfaceGuid;
                WlanNativeApi.WlanScan(_clientHandle, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                _lastScanTime = DateTime.UtcNow;
            }
        }

        var bssList = await GetNearbyBssListAsync(forceScan: false);
        if (bssList.Count == 0) return;

        // 1. If currently connected, check if there is a better BSSID for current SSID
        if (_currentConnection.State == WifiState.Connected && !string.IsNullOrWhiteSpace(_currentConnection.Ssid))
        {
            // If current SSID is blacklisted (e.g. from ping failure), do not roam within this bad SSID
            if (IsSsidBlacklisted(_currentConnection.Ssid)) return;

            var sameSsidCandidates = bssList
                .Where(b => string.Equals(b.Ssid, _currentConnection.Ssid, StringComparison.OrdinalIgnoreCase))
                .Where(b => !string.Equals(b.Bssid, _currentConnection.Bssid, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(b => b.SignalQualityPercent)
                .ToList();

            var bestCandidate = sameSsidCandidates.FirstOrDefault();
            if (bestCandidate != null)
            {
                int diff = bestCandidate.SignalQualityPercent - _currentConnection.SignalQualityPercent;
                if (diff >= settings.RoamingHysteresisDeltaPercent)
                {
                    // Candidate qualifies, increment hysteresis streak
                    _roamCandidateStreaks.TryGetValue(bestCandidate.Bssid, out int currentStreak);
                    currentStreak++;
                    _roamCandidateStreaks[bestCandidate.Bssid] = currentStreak;

                    if (currentStreak >= 2)
                    {
                        _logger.Log(AppLogLevel.Info, "SmartWifi",
                            $"Phát hiện Access Point '{bestCandidate.Bssid}' có sóng ({bestCandidate.SignalQualityPercent}%) vượt trội hơn trạm hiện tại ({_currentConnection.SignalQualityPercent}%). Tiến hành Fast Roaming...");

                        bool roamed = await FastRoamToBssidAsync(bestCandidate.Bssid, bestCandidate.Ssid);
                        if (roamed)
                        {
                            _roamCandidateStreaks.Clear();
                            return;
                        }
                    }
                }
                else
                {
                    _roamCandidateStreaks.Clear();
                }
            }
        }
        else if (_currentConnection.State == WifiState.Disconnected)
        {
            // Auto connect to best preferred network (ignoring blacklisted SSIDs)
            var preferred = settings.PreferredNetworks
                .Where(p => p.AutoConnect && !IsSsidBlacklisted(p.Ssid))
                .OrderBy(p => p.Priority)
                .ToList();

            foreach (var pref in preferred)
            {
                var candidate = bssList.FirstOrDefault(b => string.Equals(b.Ssid, pref.Ssid, StringComparison.OrdinalIgnoreCase));
                if (candidate != null && !IsSsidBlacklisted(candidate.Ssid))
                {
                    _logger.Log(AppLogLevel.Info, "SmartWifi", $"Đang tự động kết nối lại vào mạng ưu tiên '{pref.Ssid}' (Sóng: {candidate.SignalQualityPercent}%)...");
                    bool connected = await ConnectToPreferredProfileAsync(pref.Ssid, pref.Password);
                    if (connected) break;
                }
            }
        }
    }

    public Task<bool> FastRoamToBssidAsync(string bssid, string ssid)
    {
        if (_clientHandle == IntPtr.Zero || _activeInterfaceGuid == Guid.Empty)
            return Task.FromResult(false);

        byte[]? macBytes = WlanNativeApi.ParseMac(bssid);
        if (macBytes == null) return Task.FromResult(false);

        var oldBssid = _currentConnection.Bssid;
        var oldQuality = _currentConnection.SignalQualityPercent;

        IntPtr pBssidList = IntPtr.Zero;
        try
        {
            int headerSize = Marshal.SizeOf<WlanNativeApi.DOT11_BSSID_LIST>();
            int totalSize = headerSize + 6;
            pBssidList = Marshal.AllocHGlobal(totalSize);

            var bssidList = new WlanNativeApi.DOT11_BSSID_LIST
            {
                Header = new WlanNativeApi.NDIS_OBJECT_HEADER
                {
                    Type = 1,
                    Revision = 1,
                    Size = (ushort)totalSize
                },
                uNumOfEntries = 1,
                uTotalNumOfEntries = 1
            };

            Marshal.StructureToPtr(bssidList, pBssidList, false);
            IntPtr macDest = IntPtr.Add(pBssidList, headerSize);
            Marshal.Copy(macBytes, 0, macDest, 6);

            var connParams = new WlanNativeApi.WLAN_CONNECTION_PARAMETERS
            {
                wlanConnectionMode = WlanNativeApi.WLAN_CONNECTION_MODE.wlan_connection_mode_profile,
                strProfile = ssid,
                pDot11Ssid = IntPtr.Zero,
                pDesiredBssidList = pBssidList,
                dot11BssType = WlanNativeApi.DOT11_BSS_TYPE.dot11_BSS_type_infrastructure,
                dwFlags = 0
            };

            var guid = _activeInterfaceGuid;
            uint res = WlanNativeApi.WlanConnect(_clientHandle, ref guid, ref connParams, IntPtr.Zero);
            bool success = (res == 0);

            if (success)
            {
                _logger.Log(AppLogLevel.Success, "SmartWifi", $"Fast Roaming tới trạm phát '{bssid}' thành công (0ms downtime).");
                Roamed?.Invoke(this, new WifiRoamingEventArgs
                {
                    Ssid = ssid,
                    FromBssid = oldBssid,
                    ToBssid = bssid,
                    OldSignalQuality = oldQuality,
                    NewSignalQuality = 0,
                    Reason = "Sóng AP mới mạnh hơn ngưỡng Hysteresis"
                });
            }
            else
            {
                _logger.Log(AppLogLevel.Warning, "SmartWifi", $"Lệnh Fast Roaming trả về mã lỗi: {res}");
            }

            return Task.FromResult(success);
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Error, "SmartWifi", $"Lỗi thực thi Fast Roam: {ex.Message}");
            return Task.FromResult(false);
        }
        finally
        {
            if (pBssidList != IntPtr.Zero) Marshal.FreeHGlobal(pBssidList);
        }
    }

    public async Task<bool> ConnectToPreferredProfileAsync(string ssid, string password)
    {
        if (_clientHandle == IntPtr.Zero || _activeInterfaceGuid == Guid.Empty)
            return false;

        // Ensure profile is installed
        await SetWifiProfileXmlAsync(ssid, password);

        var connParams = new WlanNativeApi.WLAN_CONNECTION_PARAMETERS
        {
            wlanConnectionMode = WlanNativeApi.WLAN_CONNECTION_MODE.wlan_connection_mode_profile,
            strProfile = ssid,
            pDot11Ssid = IntPtr.Zero,
            pDesiredBssidList = IntPtr.Zero,
            dot11BssType = WlanNativeApi.DOT11_BSS_TYPE.dot11_BSS_type_infrastructure,
            dwFlags = 0
        };

        var guid = _activeInterfaceGuid;
        uint res = WlanNativeApi.WlanConnect(_clientHandle, ref guid, ref connParams, IntPtr.Zero);
        return res == 0;
    }

    public async Task TriggerEvaluationNowAsync()
    {
        await EvaluateBssListAndRoamAsync();
    }

    public Task<bool> DisconnectAsync()
    {
        if (_clientHandle == IntPtr.Zero || _activeInterfaceGuid == Guid.Empty)
            return Task.FromResult(false);

        var guid = _activeInterfaceGuid;
        uint res = WlanNativeApi.WlanDisconnect(_clientHandle, ref guid, IntPtr.Zero);
        bool success = (res == 0);
        if (success)
        {
            _logger.Log(AppLogLevel.Info, "SmartWifi", "Đã thực thi ngắt kết nối Wi-Fi hiện tại (WlanDisconnect).");
            UpdateCurrentConnectionPassive();
        }
        else
        {
            _logger.Log(AppLogLevel.Warning, "SmartWifi", $"Lệnh WlanDisconnect trả về mã lỗi: {res}");
        }
        return Task.FromResult(success);
    }

    public bool IsSsidBlacklisted(string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return false;
        if (_blacklistedNetworks.TryGetValue(ssid, out var info))
        {
            if (DateTime.UtcNow < info.ExpiryUtc)
            {
                return true;
            }
            else
            {
                _blacklistedNetworks.TryRemove(ssid, out _);
                _logger.Log(AppLogLevel.Info, "SmartWifi", $"Hết thời gian tạm khóa (Quarantine) của mạng '{ssid}'. Đã mở lại cho kết nối.");
            }
        }
        return false;
    }

    public void BlacklistSsid(string ssid, TimeSpan duration, string reason)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;
        var now = DateTime.UtcNow;
        var info = new BlacklistedWifiInfo
        {
            Ssid = ssid,
            QuarantinedAtUtc = now,
            ExpiryUtc = now.Add(duration),
            Reason = reason
        };
        _blacklistedNetworks[ssid] = info;
        _logger.Log(AppLogLevel.Warning, "SmartWifi",
            $"ĐÃ TẠM KHÓA MẠNG '{ssid}' trong {(int)duration.TotalSeconds}s (Hết hạn: {info.ExpiryUtc.ToLocalTime():HH:mm:ss}). Lý do: {reason}");
    }

    public void RemoveFromBlacklist(string ssid)
    {
        if (!string.IsNullOrWhiteSpace(ssid) && _blacklistedNetworks.TryRemove(ssid, out _))
        {
            _logger.Log(AppLogLevel.Info, "SmartWifi", $"Đã xóa thủ công mạng '{ssid}' khỏi danh sách tạm khóa.");
        }
    }

    public void ClearBlacklist()
    {
        _blacklistedNetworks.Clear();
        _logger.Log(AppLogLevel.Info, "SmartWifi", "Đã xóa toàn bộ danh sách tạm khóa Wi-Fi.");
    }

    public IReadOnlyList<BlacklistedWifiInfo> GetBlacklistedNetworks()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _blacklistedNetworks)
        {
            if (now >= kvp.Value.ExpiryUtc)
            {
                _blacklistedNetworks.TryRemove(kvp.Key, out _);
            }
        }
        return _blacklistedNetworks.Values.ToList().AsReadOnly();
    }

    public async Task<bool> TriggerFailoverToAlternativeNetworkAsync(string reason)
    {
        var currentSsid = _currentConnection.Ssid;
        var currentBssid = _currentConnection.Bssid;
        var settings = _configService.CurrentConfig.SmartWifiSettings;
        int blacklistSec = settings.WifiBlacklistDurationSeconds > 0 ? settings.WifiBlacklistDurationSeconds : 300;

        _logger.Log(AppLogLevel.Warning, "SmartWifi",
            $"KÍCH HOẠT WI-FI FAILOVER: Mạng '{currentSsid}' mất thông kết nối tới Server. Đang tiến hành cách ly và chuyển mạng...");

        // 1. Blacklist current SSID
        if (!string.IsNullOrWhiteSpace(currentSsid))
        {
            BlacklistSsid(currentSsid, TimeSpan.FromSeconds(blacklistSec), reason);
        }

        // 2. Disconnect current Wi-Fi
        await DisconnectAsync();

        // 3. Scan nearby BSS to refresh cached view
        var bssList = await GetNearbyBssListAsync(forceScan: true);

        // 4. Find best alternative preferred network that is NOT blacklisted
        var alternativePreferred = settings.PreferredNetworks
            .Where(p => p.AutoConnect && !IsSsidBlacklisted(p.Ssid) && !string.Equals(p.Ssid, currentSsid, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Priority)
            .ToList();

        PreferredNetwork? chosenNetwork = null;
        foreach (var pref in alternativePreferred)
        {
            var bssCandidate = bssList.FirstOrDefault(b => string.Equals(b.Ssid, pref.Ssid, StringComparison.OrdinalIgnoreCase));
            if (bssCandidate != null)
            {
                chosenNetwork = pref;
                break;
            }
        }

        // If none of the in-range candidates matched preferred list yet, try first non-blacklisted preferred network anyway
        if (chosenNetwork == null && alternativePreferred.Count > 0)
        {
            chosenNetwork = alternativePreferred.First();
        }

        if (chosenNetwork == null)
        {
            _logger.Log(AppLogLevel.Error, "SmartWifi",
                "Không tìm thấy mạng Wi-Fi thay thế hợp lệ nào (Tất cả mạng ưu tiên đều đã bị khóa hoặc chưa được cấu hình).");
            return false;
        }

        _logger.Log(AppLogLevel.Info, "SmartWifi",
            $"Đang chuyển kết nối sang mạng thay thế '{chosenNetwork.Ssid}' (Ưu tiên {chosenNetwork.Priority})...");

        bool connected = await ConnectToPreferredProfileAsync(chosenNetwork.Ssid, chosenNetwork.Password);

        try
        {
            FailoverTriggered?.Invoke(this, new WifiFailoverEventArgs
            {
                PreviousSsid = currentSsid,
                PreviousBssid = currentBssid,
                TargetSsid = chosenNetwork.Ssid,
                DisconnectedSeconds = 0,
                BlacklistDurationSeconds = blacklistSec,
                Reason = reason,
                Timestamp = DateTime.UtcNow
            });
        }
        catch { }

        if (connected)
        {
            _logger.Log(AppLogLevel.Success, "SmartWifi",
                $"Đã chuyển kết nối thành công sang mạng thay thế '{chosenNetwork.Ssid}'!");
        }
        else
        {
            _logger.Log(AppLogLevel.Warning, "SmartWifi",
                $"Lệnh kết nối sang mạng thay thế '{chosenNetwork.Ssid}' chưa thành công. Chờ Watchdog đánh giá lại.");
        }

        return connected;
    }

    private async Task ProvisionProfilesFromConfigAsync()
    {
        var preferred = _configService.CurrentConfig.SmartWifiSettings.PreferredNetworks;
        foreach (var net in preferred)
        {
            if (!string.IsNullOrWhiteSpace(net.Ssid))
            {
                await SetWifiProfileXmlAsync(net.Ssid, net.Password);
            }
        }
    }

    private Task<bool> SetWifiProfileXmlAsync(string ssid, string password)
    {
        if (_clientHandle == IntPtr.Zero || _activeInterfaceGuid == Guid.Empty)
            return Task.FromResult(false);

        string profileXml = GenerateWpa2PskProfileXml(ssid, password);
        var guid = _activeInterfaceGuid;

        uint res = WlanNativeApi.WlanSetProfile(
            _clientHandle,
            ref guid,
            0,
            profileXml,
            null,
            true,
            IntPtr.Zero,
            out uint reasonCode);

        bool success = (res == 0);
        if (success)
        {
            _logger.Log(AppLogLevel.Info, "SmartWifi", $"Đã nạp hồ sơ Wi-Fi Profile cho SSID '{ssid}'.");
        }
        else
        {
            _logger.Log(AppLogLevel.Warning, "SmartWifi", $"Không thể nạp hồ sơ SSID '{ssid}' (Mã lỗi: {res}, Reason: {reasonCode}).");
        }

        return Task.FromResult(success);
    }

    private static string GenerateWpa2PskProfileXml(string ssid, string password)
    {
        // Generate hex-encoded SSID for XML
        var ssidBytes = Encoding.UTF8.GetBytes(ssid);
        var hexSsid = string.Concat(ssidBytes.Select(b => b.ToString("X2")));

        return $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig>
        <SSID>
            <hex>{hexSsid}</hex>
            <name>{ssid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{password}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        CloseWlanHandle();
        GC.SuppressFinalize(this);
    }
}

