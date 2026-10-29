using System.Runtime.InteropServices;
using System.Text;

namespace Company.Module.SmartWifi.Interop;

public static class WlanNativeApi
{
    private const string WlanApiDll = "wlanapi.dll";

    public const uint WLAN_CLIENT_VERSION_VISTA = 2;

    [DllImport(WlanApiDll, SetLastError = true)]
    public static extern uint WlanOpenHandle(
        uint dwClientVersion,
        IntPtr pReserved,
        out uint pdwNegotiatedVersion,
        out IntPtr phClientHandle);

    [DllImport(WlanApiDll, SetLastError = true)]
    public static extern uint WlanCloseHandle(
        IntPtr hClientHandle,
        IntPtr pReserved);

    [DllImport(WlanApiDll, SetLastError = true)]
    public static extern uint WlanEnumInterfaces(
        IntPtr hClientHandle,
        IntPtr pReserved,
        out IntPtr ppInterfaceList);

    [DllImport(WlanApiDll, SetLastError = true)]
    public static extern uint WlanQueryInterface(
        IntPtr hClientHandle,
        [In] ref Guid pInterfaceGuid,
        WLAN_INTF_OPCODE OpCode,
        IntPtr pReserved,
        out uint pdwDataSize,
        out IntPtr ppData,
        IntPtr pWlanOpcodeValueType);

    [DllImport(WlanApiDll, SetLastError = true)]
    public static extern uint WlanGetNetworkBssList(
        IntPtr hClientHandle,
        [In] ref Guid pInterfaceGuid,
        IntPtr pDot11Ssid,
        DOT11_BSS_TYPE dot11BssType,
        bool bSecurityEnabled,
        IntPtr pReserved,
        out IntPtr ppWlanBssList);

    [DllImport(WlanApiDll, SetLastError = true)]
    public static extern uint WlanScan(
        IntPtr hClientHandle,
        [In] ref Guid pInterfaceGuid,
        IntPtr pDot11Ssid,
        IntPtr pIeData,
        IntPtr pReserved);

    [DllImport(WlanApiDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint WlanSetProfile(
        IntPtr hClientHandle,
        [In] ref Guid pInterfaceGuid,
        uint dwFlags,
        string strProfileXml,
        string? strAllUserProfileSecurity,
        bool bOverwrite,
        IntPtr pReserved,
        out uint pdwReasonCode);

    [DllImport(WlanApiDll, SetLastError = true)]
    public static extern uint WlanConnect(
        IntPtr hClientHandle,
        [In] ref Guid pInterfaceGuid,
        [In] ref WLAN_CONNECTION_PARAMETERS pConnectionParameters,
        IntPtr pReserved);

    [DllImport(WlanApiDll, SetLastError = true)]
    public static extern uint WlanDisconnect(
        IntPtr hClientHandle,
        [In] ref Guid pInterfaceGuid,
        IntPtr pReserved);

    [DllImport(WlanApiDll)]
    public static extern void WlanFreeMemory(IntPtr pMemory);

    #region Structs and Enums

    public enum WLAN_INTF_OPCODE
    {
        wlan_intf_opcode_autoconf_enabled = 1,
        wlan_intf_opcode_background_scan_enabled = 2,
        wlan_intf_opcode_media_streaming_mode = 3,
        wlan_intf_opcode_radio_state = 4,
        wlan_intf_opcode_bss_type = 5,
        wlan_intf_opcode_interface_state = 6,
        wlan_intf_opcode_current_connection = 7,
        wlan_intf_opcode_channel_number = 8,
        wlan_intf_opcode_supported_infrastructure_auth_cipher_pairs = 9,
        wlan_intf_opcode_supported_adhoc_auth_cipher_pairs = 10,
        wlan_intf_opcode_supported_country_or_region_string_list = 11,
        wlan_intf_opcode_current_operation_mode = 12,
        wlan_intf_opcode_supported_safe_mode = 13,
        wlan_intf_opcode_certified_safe_mode = 14,
        wlan_intf_opcode_hosted_network_capable = 15,
        wlan_intf_opcode_management_frame_protection_capable = 16,
        wlan_intf_opcode_rssi = 0x10000102
    }

    public enum WLAN_INTERFACE_STATE
    {
        wlan_interface_state_not_ready = 0,
        wlan_interface_state_connected = 1,
        wlan_interface_state_ad_hoc_network_formed = 2,
        wlan_interface_state_disconnecting = 3,
        wlan_interface_state_disconnected = 4,
        wlan_interface_state_associating = 5,
        wlan_interface_state_discovering = 6,
        wlan_interface_state_authenticating = 7
    }

    public enum DOT11_BSS_TYPE
    {
        dot11_BSS_type_infrastructure = 1,
        dot11_BSS_type_independent = 2,
        dot11_BSS_type_any = 3
    }

    public enum WLAN_CONNECTION_MODE
    {
        wlan_connection_mode_profile = 0,
        wlan_connection_mode_temporary_profile = 1,
        wlan_connection_mode_discovery_profile = 2,
        wlan_connection_mode_discovery_secure = 3,
        wlan_connection_mode_auto = 4,
        wlan_connection_mode_invalid = 5
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strInterfaceDescription;
        public WLAN_INTERFACE_STATE isState;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WLAN_INTERFACE_INFO_LIST
    {
        public uint dwNumberOfItems;
        public uint dwIndex;
        // Followed by WLAN_INTERFACE_INFO[]
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DOT11_SSID
    {
        public uint uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ucSSID;

        public string GetString()
        {
            if (uSSIDLength == 0 || ucSSID == null) return string.Empty;
            int len = (int)Math.Min(uSSIDLength, 32);
            return Encoding.UTF8.GetString(ucSSID, 0, len);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WLAN_CONNECTION_PARAMETERS
    {
        public WLAN_CONNECTION_MODE wlanConnectionMode;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? strProfile;
        public IntPtr pDot11Ssid;
        public IntPtr pDesiredBssidList;
        public DOT11_BSS_TYPE dot11BssType;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NDIS_OBJECT_HEADER
    {
        public byte Type;
        public byte Revision;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DOT11_BSSID_LIST
    {
        public NDIS_OBJECT_HEADER Header;
        public uint uNumOfEntries;
        public uint uTotalNumOfEntries;
        // Followed by byte[6] MAC entries
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WLAN_ASSOCIATION_ATTRIBUTES
    {
        public DOT11_SSID dot11Ssid;
        public DOT11_BSS_TYPE dot11BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] dot11Bssid;
        public uint dot11PhyType;
        public uint uDot11PhyIndex;
        public uint wlanSignalQuality;
        public uint ulRxRate;
        public uint ulTxRate;

        public string GetBssidMac()
        {
            return dot11Bssid == null ? string.Empty : string.Join(":", dot11Bssid.Select(b => b.ToString("X2")));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WLAN_CONNECTION_ATTRIBUTES
    {
        public WLAN_INTERFACE_STATE isState;
        public WLAN_CONNECTION_MODE wlanConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;
        public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
        // Security attributes follow
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WLAN_BSS_ENTRY
    {
        public DOT11_SSID dot11Ssid;
        public uint uPhyId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] dot11Bssid;
        public DOT11_BSS_TYPE dot11BssType;
        public uint dot11BssPhyType;
        public int lRssi;
        public uint uLinkQuality;
        public bool bInRegDomain;
        public ushort usBeaconPeriod;
        public ulong ullTimestamp;
        public ulong ullHostTimestamp;
        public ushort usCapabilityInformation;
        public uint ulChCenterFrequency;
        // WLAN_RATE_SET and IE follow
    }

    #endregion

    public static string FormatMac(byte[]? mac)
    {
        return mac == null || mac.Length != 6 ? string.Empty : string.Join(":", mac.Select(b => b.ToString("X2")));
    }

    public static byte[]? ParseMac(string? macStr)
    {
        if (string.IsNullOrWhiteSpace(macStr)) return null;
        var parts = macStr.Split(new[] { ':', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6) return null;
        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                return null;
        }
        return bytes;
    }

    public static string FrequencyToBand(uint freqKhz)
    {
        if (freqKhz >= 2400000 && freqKhz <= 2500000) return "2.4 GHz";
        if (freqKhz >= 4900000 && freqKhz <= 5900000) return "5 GHz";
        if (freqKhz >= 5925000 && freqKhz <= 7125000) return "6 GHz";
        return $"{freqKhz / 1000.0:F1} MHz";
    }

    public static int FrequencyToChannel(uint freqKhz)
    {
        if (freqKhz == 2484000) return 14;
        if (freqKhz >= 2412000 && freqKhz <= 2472000)
            return (int)((freqKhz - 2412000) / 5000) + 1;
        if (freqKhz >= 5180000 && freqKhz <= 5825000)
            return (int)((freqKhz - 5180000) / 5000) / 4 * 4 + 36;
        return 0;
    }
}

