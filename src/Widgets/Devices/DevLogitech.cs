// Logitech battery levels over HID++ 2.0 — port of wirless-charge/src/logitech.rs (which used hidapi's windows-native backend).
// Requests are HID++ "long" reports (id 0x11, 20 bytes) sent to the vendor collection (usage page 0xFF00, usage 0x0002)
// of LIGHTSPEED / Unifying / Bolt receivers and directly connected devices. Logitech G HUB / Options keep running:
// the device is opened shared (FILE_SHARE_READ|WRITE, never exclusive), our replies are recognised by a private
// software id, and every read uses overlapped I/O with a 300 ms deadline. Runs on the Hub thread-pool thread only.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NovaBar
{
    public static class DevLogitech
    {
        const ushort LogitechVid = 0x046D;
        const byte ReportLong = 0x11;
        const byte SwId = 0x0B;
        const int TimeoutMs = 300;

        const ushort FeatRoot = 0x0000, FeatDeviceInfo = 0x0003, FeatNameType = 0x0005,
            FeatBatteryStatus = 0x1000, FeatBatteryVoltage = 0x1001, FeatUnifiedBattery = 0x1004;

        public static List<DevDevice> Scan()
        {
            // Keyed by the device's own identity (unit id), so a mouse that switches from its receiver to the
            // charging cable stays the same device.
            var devices = new List<DevDevice>();
            foreach (var itf in DevHid.Enumerate(LogitechVid, 0xFF00, 0x0002))
            {
                DevHidConn hid = DevHidConn.Open(itf.Path);
                if (hid == null) continue;
                try
                {
                    // Receivers address paired devices by slot 1..6; devices plugged in directly (cable / Bluetooth) answer on 0xFF.
                    bool receiver = itf.Product.ToLowerInvariant().Contains("receiver");
                    byte[] indexes = receiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF };
                    foreach (var index in indexes)
                    {
                        var d = Probe(hid, index);
                        if (d == null) continue;
                        d.Conn = receiver ? DevConn.Receiver : (itf.Path.ToLowerInvariant().Contains("00001812") ? DevConn.Bluetooth : DevConn.Cable);
                        int at = devices.FindIndex(e => e.Key == d.Key);
                        if (at < 0) devices.Add(d);
                        else if (d.Charging && !devices[at].Charging) devices[at] = d; // seen on two connections at once: prefer the charging one
                    }
                }
                finally { hid.Dispose(); }
            }
            return devices;
        }

        static DevDevice Probe(DevHidConn hid, byte index)
        {
            var p = new Proto { Hid = hid, Index = index };
            // Ping (root feature, function 1). Fails fast-ish when the slot is empty or the device is asleep/off.
            if (p.Call(0x00, 1, new byte[] { 0, 0, 0xAA }) == null) return null;

            string name = "Logitech device"; DevKind kind = DevKind.Other; bool hasKind = false;
            int f = p.Feature(FeatNameType);
            if (f > 0)
            {
                name = p.Name((byte)f) ?? "Logitech device";
                int k = p.Kind((byte)f); if (k >= 0) { kind = (DevKind)k; hasKind = true; }
            }
            if (!hasKind) kind = DevKinds.FromName(name);

            int pct; bool charging;
            if (!p.Battery(out pct, out charging)) return null;
            var id = p.UnitId();
            return new DevDevice { Key = "logi:" + (id ?? name), Name = name, Kind = kind, Battery = pct, Charging = charging };
        }

        class Proto
        {
            public DevHidConn Hid; public byte Index;
            readonly byte[] buf = new byte[64];

            /// <summary>Sends one request and returns the 16 parameter bytes of its reply (null on timeout / error reply).</summary>
            public byte[] Call(byte featureIndex, byte function, byte[] prms)
            {
                byte fnSw = (byte)((function << 4) | SwId);
                var req = new byte[20];
                req[0] = ReportLong; req[1] = Index; req[2] = featureIndex; req[3] = fnSw;
                if (prms != null) Array.Copy(prms, 0, req, 4, Math.Min(16, prms.Length));

                while (Hid.Read(buf, 0) > 0) { } // drop stale input
                if (!Hid.Write(req)) return null;

                int deadline = Environment.TickCount + TimeoutMs;
                while (true)
                {
                    int left = deadline - Environment.TickCount;
                    if (left <= 0) return null;
                    int n = Hid.Read(buf, Math.Max(1, left));
                    if (n < 0) return null;
                    if (n < 20 || buf[0] != ReportLong || buf[1] != Index)
                    {
                        if (n == 0) return null;
                        continue;
                    }
                    if (buf[2] == 0xFF && buf[3] == featureIndex && buf[4] == fnSw) return null; // HID++ 2.0 error reply
                    if (buf[2] == featureIndex && buf[3] == fnSw)
                    {
                        var outb = new byte[16]; Array.Copy(buf, 4, outb, 0, 16); return outb;
                    }
                }
            }

            /// <summary>Index of a feature on this device via root feature 0x0000 (0 = not present).</summary>
            public int Feature(ushort id)
            {
                var r = Call((byte)FeatRoot, 0, new byte[] { (byte)(id >> 8), (byte)id });
                return r == null ? 0 : r[0];
            }

            /// <summary>Serial-like unit id from Device Information (0x0003); the same over the receiver and the USB cable.</summary>
            public string UnitId()
            {
                int f = Feature(FeatDeviceInfo); if (f <= 0) return null;
                var r = Call((byte)f, 0, null); if (r == null) return null;
                bool any = false; var sb = new StringBuilder();
                for (int i = 1; i < 5; i++) { if (r[i] != 0) any = true; sb.Append(r[i].ToString("X2")); }
                return any ? sb.ToString() : null;
            }

            public string Name(byte f)
            {
                var r = Call(f, 0, null); if (r == null) return null;
                int len = r[0]; var bytes = new List<byte>(len);
                while (bytes.Count < len)
                {
                    var chunk = Call(f, 1, new byte[] { (byte)bytes.Count }); if (chunk == null) return null;
                    int take = Math.Min(len - bytes.Count, 16);
                    for (int i = 0; i < take; i++) bytes.Add(chunk[i]);
                }
                var s = Encoding.UTF8.GetString(bytes.ToArray()).Trim('\0').Trim();
                return s.Length > 0 ? s : null;
            }

            /// <summary>HID++ device type (0x0005 fn 2) mapped to a kind; -1 when unknown.</summary>
            public int Kind(byte f)
            {
                var r = Call(f, 2, null); if (r == null) return -1;
                switch (r[0])
                {
                    case 0: case 2: return (int)DevKind.Keyboard;
                    case 3: case 4: case 5: return (int)DevKind.Mouse;
                    case 8: return (int)DevKind.Headset;
                    case 11: case 12: return (int)DevKind.Gamepad;
                    default: return (int)DevKind.Other;
                }
            }

            /// <summary>(percent, charging) from whichever battery feature the device has.</summary>
            public bool Battery(out int pct, out bool charging)
            {
                pct = 0; charging = false;
                int f = Feature(FeatUnifiedBattery);
                if (f > 0)
                {
                    var r = Call((byte)f, 1, null); if (r == null) return false;
                    int p = r[0];
                    if (p == 0) p = (r[1] & 8) != 0 ? 90 : (r[1] & 4) != 0 ? 50 : (r[1] & 2) != 0 ? 20 : 5; // coarse level flags
                    pct = Math.Min(p, 100); charging = r[2] == 1 || r[2] == 2; return true;
                }
                f = Feature(FeatBatteryStatus);
                if (f > 0)
                {
                    var r = Call((byte)f, 0, null); if (r == null) return false;
                    pct = Math.Min((int)r[0], 100); charging = r[2] == 1 || r[2] == 2; return true;
                }
                f = Feature(FeatBatteryVoltage);
                if (f > 0)
                {
                    var r = Call((byte)f, 0, null); if (r == null) return false;
                    pct = VoltageToPercent((r[0] << 8) | r[1]); charging = (r[2] & 0x80) != 0; return true;
                }
                return false;
            }
        }

        /// <summary>Li-ion discharge curve used by Logitech mice that only report voltage (from logitech.rs).</summary>
        static readonly int[,] Curve = { { 4186, 100 }, { 4067, 90 }, { 3989, 80 }, { 3922, 70 }, { 3859, 60 }, { 3811, 50 }, { 3778, 40 },
            { 3751, 30 }, { 3717, 20 }, { 3671, 10 }, { 3646, 5 }, { 3579, 2 }, { 3500, 0 } };
        public static int VoltageToPercent(int mv)
        {
            if (mv >= Curve[0, 0]) return 100;
            for (int i = 0; i + 1 < Curve.GetLength(0); i++)
            {
                int hv = Curve[i, 0], hp = Curve[i, 1], lv = Curve[i + 1, 0], lp = Curve[i + 1, 1];
                if (mv >= lv)
                {
                    float t = (mv - lv) / (float)(hv - lv);
                    return (int)Math.Round(lp + t * (hp - lp), MidpointRounding.AwayFromZero);
                }
            }
            return 0;
        }
    }

    public class DevHidInfo { public string Path, Product; public ushort Vid, Pid, UsagePage, Usage; public int InLen, OutLen; }

    /// <summary>Minimal HID enumeration (what hidapi's windows backend does): SetupDi on GUID_DEVINTERFACE_HID,
    /// open each interface with zero access to read attributes/caps/product string.</summary>
    public static class DevHid
    {
        static Guid HidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        public static List<DevHidInfo> Enumerate(ushort vid, ushort usagePage, ushort usage)
        {
            var list = new List<DevHidInfo>();
            IntPtr set = DevNative.SetupDiGetClassDevs(ref HidGuid, IntPtr.Zero, IntPtr.Zero, DevNative.DIGCF_PRESENT | DevNative.DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
            try
            {
                var did = new DevNative.SP_DEVICE_INTERFACE_DATA(); did.cbSize = Marshal.SizeOf(did);
                for (uint i = 0; DevNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref HidGuid, i, ref did); i++)
                {
                    string path = DetailPath(set, ref did);
                    if (path == null) continue;
                    string lp = path.ToLowerInvariant();
                    if (!lp.Contains(vid.ToString("x4"))) continue; // cheap prefilter (vid_046d / vid&02046d); verified below
                    var info = Query(path);
                    if (info != null && info.Vid == vid && info.UsagePage == usagePage && info.Usage == usage) list.Add(info);
                }
            }
            finally { DevNative.SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        static string DetailPath(IntPtr set, ref DevNative.SP_DEVICE_INTERFACE_DATA did)
        {
            int req;
            DevNative.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
            if (req <= 0) return null;
            IntPtr buf = Marshal.AllocHGlobal(req);
            try
            {
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                if (!DevNative.SetupDiGetDeviceInterfaceDetail(set, ref did, buf, req, out req, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static DevHidInfo Query(string path)
        {
            using (var h = DevNative.CreateFile(path, 0, DevNative.FILE_SHARE_READ | DevNative.FILE_SHARE_WRITE, IntPtr.Zero, DevNative.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var a = new DevNative.HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(a);
                if (!DevNative.HidD_GetAttributes(h, ref a)) return null;
                var info = new DevHidInfo { Path = path, Vid = a.VendorID, Pid = a.ProductID, Product = "" };
                IntPtr pp;
                if (DevNative.HidD_GetPreparsedData(h, out pp))
                {
                    try
                    {
                        DevNative.HIDP_CAPS caps;
                        if (DevNative.HidP_GetCaps(pp, out caps) == DevNative.HIDP_STATUS_SUCCESS)
                        { info.UsagePage = caps.UsagePage; info.Usage = caps.Usage; info.InLen = caps.InputReportByteLength; info.OutLen = caps.OutputReportByteLength; }
                    }
                    finally { DevNative.HidD_FreePreparsedData(pp); }
                }
                var sb = new byte[512];
                if (DevNative.HidD_GetProductString(h, sb, sb.Length)) info.Product = Encoding.Unicode.GetString(sb).Split('\0')[0];
                return info;
            }
        }
    }

    /// <summary>One shared (non-exclusive) overlapped HID handle. Like hidapi, a read that times out stays pending and is
    /// picked up by the next Read; Dispose cancels it.</summary>
    public sealed class DevHidConn : IDisposable
    {
        SafeFileHandle h; IntPtr rov, wov, rev, wev, rbuf, wbuf; int inLen, outLen; bool pending;
        static readonly int OvSize = IntPtr.Size == 8 ? 32 : 20;
        static int EventOffset { get { return IntPtr.Size * 2 + 8; } }

        public static DevHidConn Open(string path)
        {
            var h = DevNative.CreateFile(path, DevNative.GENERIC_READ | DevNative.GENERIC_WRITE, DevNative.FILE_SHARE_READ | DevNative.FILE_SHARE_WRITE,
                IntPtr.Zero, DevNative.OPEN_EXISTING, DevNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }
            int inLen = 20, outLen = 20; IntPtr pp;
            if (DevNative.HidD_GetPreparsedData(h, out pp))
            {
                try { DevNative.HIDP_CAPS caps; if (DevNative.HidP_GetCaps(pp, out caps) == DevNative.HIDP_STATUS_SUCCESS) { inLen = Math.Max(1, (int)caps.InputReportByteLength); outLen = Math.Max(1, (int)caps.OutputReportByteLength); } }
                finally { DevNative.HidD_FreePreparsedData(pp); }
            }
            DevNative.HidD_SetNumInputBuffers(h, 64);
            var c = new DevHidConn { h = h, inLen = inLen, outLen = outLen };
            c.rev = DevNative.CreateEvent(IntPtr.Zero, true, false, null);
            c.wev = DevNative.CreateEvent(IntPtr.Zero, true, false, null);
            c.rov = Marshal.AllocHGlobal(OvSize); c.wov = Marshal.AllocHGlobal(OvSize);
            c.rbuf = Marshal.AllocHGlobal(Math.Max(64, inLen)); c.wbuf = Marshal.AllocHGlobal(Math.Max(64, outLen));
            return c;
        }

        static void ResetOv(IntPtr ov, IntPtr ev)
        {
            for (int i = 0; i < OvSize; i += 4) Marshal.WriteInt32(ov, i, 0);
            Marshal.WriteIntPtr(ov, EventOffset, ev);
            DevNative.ResetEvent(ev);
        }

        /// <summary>Bytes read (report id first), 0 on timeout, -1 on error.</summary>
        public int Read(byte[] dst, int timeoutMs)
        {
            int n;
            if (!pending)
            {
                ResetOv(rov, rev);
                if (DevNative.ReadFile(h, rbuf, inLen, IntPtr.Zero, rov)) { pending = true; }
                else if (Marshal.GetLastWin32Error() == DevNative.ERROR_IO_PENDING) pending = true;
                else return -1;
            }
            if (DevNative.WaitForSingleObject(rev, (uint)Math.Max(0, timeoutMs)) != 0) return 0; // still pending
            pending = false;
            if (!DevNative.GetOverlappedResult(h, rov, out n, false)) return -1;
            n = Math.Min(n, dst.Length);
            if (n > 0) Marshal.Copy(rbuf, dst, 0, n);
            return n;
        }

        public bool Write(byte[] data)
        {
            int len = Math.Max(outLen, data.Length);
            var tmp = new byte[len]; Array.Copy(data, tmp, data.Length); // hidapi pads to the output report length
            if (len > Math.Max(64, outLen)) return false;
            Marshal.Copy(tmp, 0, wbuf, len);
            ResetOv(wov, wev);
            int n;
            if (!DevNative.WriteFile(h, wbuf, len, IntPtr.Zero, wov) && Marshal.GetLastWin32Error() != DevNative.ERROR_IO_PENDING) return false;
            if (DevNative.WaitForSingleObject(wev, 1000) != 0)
            {
                DevNative.CancelIoEx(h, wov); DevNative.GetOverlappedResult(h, wov, out n, true); return false;
            }
            return DevNative.GetOverlappedResult(h, wov, out n, false) && n > 0;
        }

        public void Dispose()
        {
            if (h == null) return;
            int n;
            if (pending) { DevNative.CancelIoEx(h, rov); DevNative.GetOverlappedResult(h, rov, out n, true); pending = false; }
            h.Dispose(); h = null;
            DevNative.CloseHandle(rev); DevNative.CloseHandle(wev);
            Marshal.FreeHGlobal(rov); Marshal.FreeHGlobal(wov); Marshal.FreeHGlobal(rbuf); Marshal.FreeHGlobal(wbuf);
        }
    }

    static class DevNative
    {
        public const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int ERROR_IO_PENDING = 997, HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)] public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices, NumberOutputButtonCaps,
                NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint index, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(SafeFileHandle h, IntPtr buf, int len, IntPtr read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(SafeFileHandle h, IntPtr buf, int len, IntPtr written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out int n, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] public static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

        [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
        [DllImport("hid.dll")] public static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_SetNumInputBuffers(SafeFileHandle h, int n);
    }
}
