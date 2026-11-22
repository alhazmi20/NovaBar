// Native Wifi API (wlanapi.dll) P/Invoke layer for the Wi-Fi widget.
// Structures are read by offset (Marshal.ReadInt32 etc.) — layouts documented inline; all WLAN memory is freed with WlanFreeMemory.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace NovaBar
{
    public static class WifiNative
    {
        // ---- constants ----
        public const int OP_RADIO_STATE = 4, OP_INTERFACE_STATE = 6, OP_CURRENT_CONNECTION = 7, OP_CHANNEL_NUMBER = 8, OP_RSSI = 0x10000102;
        public const uint SRC_ACM = 0x08, SRC_MSM = 0x10;
        // ACM codes
        public const uint ACM_SCAN_COMPLETE = 7, ACM_SCAN_FAIL = 8, ACM_CONNECTION_START = 9, ACM_CONNECTION_COMPLETE = 10, ACM_CONNECTION_ATTEMPT_FAIL = 11,
            ACM_INTERFACE_ARRIVAL = 13, ACM_INTERFACE_REMOVAL = 14, ACM_PROFILE_CHANGE = 15, ACM_NETWORK_NOT_AVAILABLE = 18, ACM_NETWORK_AVAILABLE = 19,
            ACM_DISCONNECTING = 20, ACM_DISCONNECTED = 21;
        // MSM codes
        public const uint MSM_ASSOCIATING = 1, MSM_ASSOCIATED = 2, MSM_AUTHENTICATING = 3, MSM_CONNECTED = 4, MSM_ROAMING_END = 6, MSM_RADIO_STATE_CHANGE = 7,
            MSM_SIGNAL_QUALITY_CHANGE = 8, MSM_DISASSOCIATING = 9, MSM_DISCONNECTED = 10, MSM_ADAPTER_REMOVAL = 13, MSM_LINK_DEGRADED = 15, MSM_LINK_IMPROVED = 16;
        // WLAN_INTERFACE_STATE
        public const int IS_NOT_READY = 0, IS_CONNECTED = 1, IS_DISCONNECTING = 3, IS_DISCONNECTED = 4, IS_ASSOCIATING = 5, IS_DISCOVERING = 6, IS_AUTHENTICATING = 7;
        public const uint AVAIL_FLAG_CONNECTED = 1, AVAIL_FLAG_HAS_PROFILE = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct NOTIFICATION_DATA { public uint Source; public uint Code; public Guid Iface; public uint DataSize; public IntPtr Data; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CONNECTION_PARAMETERS
        {
            public int Mode;                                     // 0 = wlan_connection_mode_profile
            [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
            public IntPtr Ssid, BssidList;
            public int BssType;                                  // 1 = infrastructure
            public uint Flags;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void NotifyCallback(IntPtr data, IntPtr ctx);

        [DllImport("wlanapi.dll")] public static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] public static extern uint WlanCloseHandle(IntPtr h, IntPtr reserved);
        [DllImport("wlanapi.dll")] public static extern void WlanFreeMemory(IntPtr p);
        [DllImport("wlanapi.dll")] public static extern uint WlanEnumInterfaces(IntPtr h, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] public static extern uint WlanQueryInterface(IntPtr h, ref Guid iface, int opcode, IntPtr reserved, out uint size, out IntPtr data, out int valueType);
        [DllImport("wlanapi.dll")] public static extern uint WlanGetAvailableNetworkList(IntPtr h, ref Guid iface, uint flags, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] public static extern uint WlanGetNetworkBssList(IntPtr h, ref Guid iface, IntPtr ssid, int bssType, bool secEnabled, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] public static extern uint WlanScan(IntPtr h, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
        [DllImport("wlanapi.dll")] public static extern uint WlanConnect(IntPtr h, ref Guid iface, ref CONNECTION_PARAMETERS p, IntPtr reserved);
        [DllImport("wlanapi.dll")] public static extern uint WlanDisconnect(IntPtr h, ref Guid iface, IntPtr reserved);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] public static extern uint WlanSetProfile(IntPtr h, ref Guid iface, uint flags, string xml, string security, bool overwrite, IntPtr reserved, out uint reason);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] public static extern uint WlanDeleteProfile(IntPtr h, ref Guid iface, string name, IntPtr reserved);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] public static extern uint WlanReasonCodeToString(uint reason, uint bufSize, StringBuilder buf, IntPtr reserved);
        [DllImport("wlanapi.dll")] public static extern uint WlanRegisterNotification(IntPtr h, uint source, bool ignoreDup, NotifyCallback cb, IntPtr ctx, IntPtr reserved, out uint prev);

        // ---- helpers ----
        /// <summary>DOT11_SSID { ULONG len; UCHAR ssid[32]; } at p → raw bytes.</summary>
        public static byte[] ReadSsid(IntPtr p)
        {
            int len = Marshal.ReadInt32(p); if (len < 0) len = 0; if (len > 32) len = 32;
            var b = new byte[len]; if (len > 0) Marshal.Copy(IntPtr.Add(p, 4), b, 0, len); return b;
        }
        public static string SsidText(byte[] b)
        {
            if (b == null || b.Length == 0) return "";
            try { return new UTF8Encoding(false, true).GetString(b); } catch { return Encoding.Default.GetString(b); }
        }
        public static string Hex(byte[] b) { var sb = new StringBuilder(b.Length * 2); foreach (var x in b) sb.Append(x.ToString("X2")); return sb.ToString(); }
        public static string ReasonText(uint reason)
        {
            try { var sb = new StringBuilder(512); if (WlanReasonCodeToString(reason, 512, sb, IntPtr.Zero) == 0 && sb.Length > 0) return sb.ToString().Trim(); } catch { }
            return "Error 0x" + reason.ToString("X");
        }

        public static string AuthName(int a)
        {
            switch (a)
            {
                case 1: return "Open"; case 2: return "WEP"; case 3: return "WPA"; case 4: return "WPA"; case 6: return "WPA2-Ent"; case 7: return "WPA2";
                case 8: return "WPA3-Ent"; case 9: return "WPA3"; case 10: return "OWE"; case 11: return "WPA3-Ent"; default: return "Secured";
            }
        }
        public static bool IsEnterprise(int auth) { return auth == 3 || auth == 6 || auth == 8 || auth == 11; }
        /// <summary>DOT11_PHY_TYPE → marketing generation.</summary>
        public static string PhyName(int phy)
        {
            switch (phy)
            {
                case 4: return "802.11a"; case 5: return "802.11b"; case 6: return "802.11g"; case 7: return "Wi-Fi 4"; case 8: return "Wi-Fi 5";
                case 9: return "WiGig"; case 10: return "Wi-Fi 6"; case 11: return "Wi-Fi 7"; default: return "";
            }
        }
        public static int BandOf(uint centerKHz)
        {
            if (centerKHz >= 5925000) return 6; if (centerKHz >= 4900000) return 5; if (centerKHz >= 2400000) return 2; return 0;
        }
    }
}
