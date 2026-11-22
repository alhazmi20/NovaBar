// Quick toggles (Wi-Fi / Bluetooth via WinRT Radios, hotspot via tethering manager, airplane via settings) and brightness.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace NovaBar
{
    public class CtlRadioSnap
    {
        // -1 = not present, 0 = off, 1 = on, 2 = disabled/transition
        public int Wifi = -1, Bt = -1, Hotspot = -1; public bool Airplane; public string Ssid = ""; public bool Online, Wired;
        public override bool Equals(object o)
        {
            var x = o as CtlRadioSnap;
            return x != null && x.Wifi == Wifi && x.Bt == Bt && x.Hotspot == Hotspot && x.Airplane == Airplane && x.Ssid == Ssid && x.Online == Online && x.Wired == Wired;
        }
        public override int GetHashCode() { return Wifi ^ (Bt << 3) ^ (Hotspot << 6); }
    }

    public static class CtlRadios
    {
        public const string Key = "ctl.radios";
        static List<Radio> radios; static readonly object gate = new object();
        static CtlRadios() { Hub.Define(Key, 4000, Sample); }
        public static void Init() { }

        static void Load()
        {
            if (radios != null) return;
            try { CtlWinRT.Wait(Radio.RequestAccessAsync(), 4000); } catch { }
            var l = new List<Radio>();
            try { var r = CtlWinRT.Wait(Radio.GetRadiosAsync()); if (r != null) l.AddRange(r); } catch { }
            radios = l;
        }
        static int St(RadioKind k)
        {
            var r = radios.FirstOrDefault(x => x.Kind == k); if (r == null) return -1;
            try { var s = r.State; return s == RadioState.On ? 1 : s == RadioState.Off ? 0 : 2; } catch { return -1; }
        }
        static object Sample()
        {
            lock (gate) Load();
            var s = new CtlRadioSnap { Wifi = St(RadioKind.WiFi), Bt = St(RadioKind.Bluetooth) };
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState")) { var v = k == null ? null : k.GetValue(""); s.Airplane = v != null && Convert.ToInt32(v) == 1; } } catch { }
            try
            {
                var p = NetworkInformation.GetInternetConnectionProfile();
                if (p != null)
                {
                    s.Online = p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
                    if (p.IsWlanConnectionProfile) s.Ssid = p.WlanConnectionProfileDetails.GetConnectedSsid() ?? "";
                    else { s.Wired = true; s.Ssid = p.ProfileName ?? ""; }
                    try
                    {
                        if (NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(p) == TetheringCapability.Enabled)
                        {
                            var m = NetworkOperatorTetheringManager.CreateFromConnectionProfile(p);
                            var ts = m.TetheringOperationalState;
                            s.Hotspot = ts == TetheringOperationalState.On ? 1 : ts == TetheringOperationalState.Off ? 0 : 2;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return s;
        }

        public static void SetRadio(RadioKind k, bool on)
        {
            Ctl.Async(() =>
            {
                lock (gate) Load();
                var r = radios.FirstOrDefault(x => x.Kind == k);
                if (r == null) { Ctl.Open(k == RadioKind.Bluetooth ? "ms-settings:bluetooth" : "ms-settings:network-wifi"); return; }
                RadioAccessStatus st = RadioAccessStatus.Unspecified;
                try { st = CtlWinRT.Wait(r.SetStateAsync(on ? RadioState.On : RadioState.Off)); } catch { }
                if (st != RadioAccessStatus.Allowed) Ctl.Open(k == RadioKind.Bluetooth ? "ms-settings:bluetooth" : "ms-settings:network-wifi");
                Hub.Refresh(Key);
            });
        }
        public static void SetHotspot(bool on)
        {
            Ctl.Async(() =>
            {
                bool ok = false;
                try
                {
                    var p = NetworkInformation.GetInternetConnectionProfile();
                    if (p != null)
                    {
                        var m = NetworkOperatorTetheringManager.CreateFromConnectionProfile(p);
                        var res = CtlWinRT.Wait(on ? m.StartTetheringAsync() : m.StopTetheringAsync());
                        ok = res.Status == TetheringOperationStatus.Success;
                    }
                }
                catch { }
                if (!ok) Ctl.Open("ms-settings:network-mobilehotspot");
                Hub.Refresh(Key);
            });
        }
    }

    [Widget("quicktoggles", "Quick Toggles", "System", "Wi-Fi, Bluetooth, mobile hotspot and airplane mode.", RefName = "Quick Toggles", Data = DataKind.Live)]
    public class CtlQuickTogglesWidget : Widget
    {
        static CtlQuickTogglesWidget() { CtlRadios.Init(); }
        public override void OnStart() { Hub.Subscribe(CtlRadios.Key, this); }
        public override void OnStop() { Hub.Unsubscribe(CtlRadios.Key, this); }
        public override void OnPanelOpen() { Hub.Refresh(CtlRadios.Key); }
        CtlRadioSnap Snap { get { return Hub.Get<CtlRadioSnap>(CtlRadios.Key) ?? new CtlRadioSnap(); } }

        public override int Measure(DrawCtx c) { var s = Snap; int n = 2 + (s.Hotspot == 1 ? 1 : 0) + (s.Airplane ? 1 : 0); return c.Si(n * 22 + 16); }
        public override void Draw(DrawCtx c)
        {
            var s = Snap; var b = c.Bounds; float x = b.X + c.S(8), w = c.S(22);
            Action<string, bool, bool> ic = (g, on, present) =>
            {
                var r = new RectangleF(x, b.Y, w, b.Height);
                Ui.Icon(c.G, g, r, !present ? c.T.TextFaint : on ? c.T.Accent : c.T.TextDim, c.S(14));
                x += w;
            };
            ic(s.Wifi == 1 ? Icons.Wifi : Icons.WifiOff, s.Wifi == 1, s.Wifi >= 0);
            ic(Icons.Bluetooth, s.Bt == 1, s.Bt >= 0);
            if (s.Hotspot == 1) ic(Icons.Hotspot, true, true);     // compact like the taskbar: extra radios only when on
            if (s.Airplane) ic(Icons.Airplane, true, true);
        }

        public override Size PanelSize(float sc) { return new Size((int)(320 * sc), (int)(272 * sc)); }
        public override void DrawPanel(DrawCtx c)
        {
            var s = Snap; var b = c.Bounds;
            string sub = s.Ssid.Length > 0 ? s.Ssid : (s.Online ? "Online" : "Offline");
            float y = c.PanelHeader("Quick toggles", null, Icons.Network);
            // connection line
            var cr = new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(20));
            Ui.FillCircle(c.G, new PointF(cr.X + c.S(4), cr.Y + cr.Height / 2), c.S(3.5f), s.Online ? c.T.Good : c.T.Warn);
            Ui.Text(c.G, (s.Wired ? "Wired · " : s.Ssid.Length > 0 ? "Wi-Fi · " : "") + sub, c.F(12), c.T.TextDim, new RectangleF(cr.X + c.S(14), cr.Y, cr.Width - c.S(14), cr.Height));
            y += c.S(28);
            float gap = c.S(10), tw = (b.Width - c.S(32) - gap) / 2, th = c.S(74);
            Tile(c, new RectangleF(b.X + c.S(16), y, tw, th), s.Wifi == 1 ? Icons.Wifi : Icons.WifiOff, "Wi-Fi",
                s.Wifi < 0 ? "Not available" : s.Wifi == 1 ? (s.Ssid.Length > 0 && !s.Wired ? s.Ssid : "On") : s.Wifi == 2 ? "Disabled" : "Off", s.Wifi == 1,
                () => { if (s.Wifi >= 0 && s.Wifi != 2) CtlRadios.SetRadio(RadioKind.WiFi, s.Wifi != 1); else Ctl.Open("ms-settings:network-wifi"); });
            Tile(c, new RectangleF(b.X + c.S(16) + tw + gap, y, tw, th), Icons.Bluetooth, "Bluetooth",
                s.Bt < 0 ? "Not available" : s.Bt == 1 ? "On" : s.Bt == 2 ? "Disabled" : "Off", s.Bt == 1,
                () => { if (s.Bt >= 0 && s.Bt != 2) CtlRadios.SetRadio(RadioKind.Bluetooth, s.Bt != 1); else Ctl.Open("ms-settings:bluetooth"); });
            y += th + gap;
            Tile(c, new RectangleF(b.X + c.S(16), y, tw, th), Icons.Hotspot, "Hotspot",
                s.Hotspot < 0 ? "Settings" : s.Hotspot == 1 ? "On" : s.Hotspot == 2 ? "Switching…" : "Off", s.Hotspot == 1,
                () => { if (s.Hotspot >= 0) CtlRadios.SetHotspot(s.Hotspot != 1); else Ctl.Open("ms-settings:network-mobilehotspot"); });
            Tile(c, new RectangleF(b.X + c.S(16) + tw + gap, y, tw, th), Icons.Airplane, "Airplane mode", s.Airplane ? "On" : "Off", s.Airplane,
                () => Ctl.Open("ms-settings:network-airplanemode"));
            y += th + c.S(12);
            float bw = (b.Width - c.S(32) - gap) / 2;
            c.Button(new RectangleF(b.X + c.S(16), y, bw, c.S(30)), "Network", () => Ctl.Open("ms-settings:network"), false, Icons.Settings);
            c.Button(new RectangleF(b.X + c.S(16) + bw + gap, y, bw, c.S(30)), "Devices", () => Ctl.Open("ms-settings:bluetooth"), false, Icons.Bluetooth);
        }
        void Tile(DrawCtx c, RectangleF r, string glyph, string title, string state, bool on, Action click)
        {
            bool h = c.IsHover(r);
            var bg = on ? (h ? Ui.Mix(c.T.Accent, Color.White, 0.12) : c.T.Accent) : (h ? c.T.TileHover : c.T.Tile);
            Ui.FillRound(c.G, r, c.S(14), bg);
            if (!on) Ui.StrokeRound(c.G, r, c.S(14), c.T.TileBorder);
            var fg = on ? Color.White : c.T.Text; var fg2 = on ? Color.FromArgb(220, 255, 255, 255) : c.T.TextDim;
            Ui.Icon(c.G, glyph, new RectangleF(r.X + c.S(12), r.Y + c.S(10), c.S(22), c.S(22)), fg, c.S(17));
            Ui.Text(c.G, title, c.FS(12.5f), fg, new RectangleF(r.X + c.S(13), r.Y + c.S(38), r.Width - c.S(20), c.S(16)));
            Ui.Text(c.G, state, c.F(11), fg2, new RectangleF(r.X + c.S(13), r.Y + c.S(54), r.Width - c.S(20), c.S(14)));
            c.Hit(r, click);
        }
    }

    // ------------------------------------------------------------------ Brightness
    public class CtlBrightSnap
    {
        public int Value = -1; public string Method = "";
        public override bool Equals(object o) { var x = o as CtlBrightSnap; return x != null && x.Value == Value && x.Method == Method; }
        public override int GetHashCode() { return Value; }
    }

    public static class CtlBright
    {
        public const string Key = "ctl.bright";
        static string method; // "wmi" | "ddc" | "none"
        static int pending = -1, busy;
        static CtlBright() { Hub.Define(Key, 30000, Sample); }
        public static void Init() { }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
        delegate bool MonProc(IntPtr hMon, IntPtr hdc, ref Native.RECT r, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonProc cb, IntPtr data);
        [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint n);
        [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint n, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint n, PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint v);

        static void ForEachPhysical(Func<IntPtr, bool> f)
        {
            var mons = new List<IntPtr>();
            MonProc cb = (IntPtr m, IntPtr dc, ref Native.RECT r, IntPtr d) => { mons.Add(m); return true; };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            foreach (var m in mons)
            {
                uint n; if (!GetNumberOfPhysicalMonitorsFromHMONITOR(m, out n) || n == 0) continue;
                var arr = new PHYSICAL_MONITOR[n];
                if (!GetPhysicalMonitorsFromHMONITOR(m, n, arr)) continue;
                try { foreach (var p in arr) if (!f(p.h)) return; } finally { DestroyPhysicalMonitors(n, arr); }
            }
        }
        static int ReadWmi()
        {
            using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE"))
            using (var res = s.Get())
                foreach (ManagementObject o in res) using (o) return Convert.ToInt32(o["CurrentBrightness"]);
            return -1;
        }
        static int ReadDdc()
        {
            int val = -1;
            ForEachPhysical(h =>
            {
                uint mn, cur, mx;
                if (GetMonitorBrightness(h, out mn, out cur, out mx) && mx > mn) { val = (int)Math.Round(100.0 * (cur - mn) / (mx - mn)); return false; }
                return true;
            });
            return val;
        }
        static object Sample()
        {
            int v = -1;
            if (method == null || method == "wmi") { try { v = ReadWmi(); } catch { v = -1; } if (v >= 0) method = "wmi"; }
            if (v < 0 && (method == null || method == "ddc")) { try { v = ReadDdc(); } catch { v = -1; } if (v >= 0) method = "ddc"; }
            if (v < 0 && method == null) method = "none";
            return new CtlBrightSnap { Value = v, Method = method ?? "none" };
        }
        /// <summary>Coalescing setter: only the latest requested value is applied, off the UI thread.</summary>
        public static void Set(int pct)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            Interlocked.Exchange(ref pending, pct);
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int v;
                    while ((v = Interlocked.Exchange(ref pending, -1)) >= 0)
                    {
                        try
                        {
                            if (method == "wmi")
                            {
                                using (var mc = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null))
                                using (var inst = mc.GetInstances())
                                    foreach (ManagementObject o in inst) using (o) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v });
                            }
                            else if (method == "ddc")
                                ForEachPhysical(h => { uint mn, cur, mx; if (GetMonitorBrightness(h, out mn, out cur, out mx) && mx > mn) SetMonitorBrightness(h, (uint)(mn + (mx - mn) * v / 100.0)); return true; });
                        }
                        catch { }
                    }
                }
                finally { Interlocked.Exchange(ref busy, 0); }
                Hub.Refresh(Key);
            });
        }
    }

    [Widget("brightness", "Brightness", "System", "A slider for the display's brightness.", RefName = "Brightness", Data = DataKind.Live)]
    public class CtlBrightnessWidget : Widget
    {
        static CtlBrightnessWidget() { CtlBright.Init(); }
        int local = -1; DateTime localAt;
        public override void OnStart() { Hub.Subscribe(CtlBright.Key, this); }
        public override void OnStop() { Hub.Unsubscribe(CtlBright.Key, this); }
        public override void OnPanelOpen() { Hub.Refresh(CtlBright.Key); }
        CtlBrightSnap Snap { get { return Hub.Get<CtlBrightSnap>(CtlBright.Key) ?? new CtlBrightSnap(); } }
        int Value { get { var s = Snap; if (local >= 0 && (DateTime.Now - localAt).TotalSeconds < 3) return local; return s.Value; } }
        bool Supported { get { var s = Snap; return s.Method == "wmi" || s.Method == "ddc"; } }
        void SetVal(int v) { v = Math.Max(0, Math.Min(100, v)); local = v; localAt = DateTime.Now; CtlBright.Set(v); Invalidate(); }

        public override int Measure(DrawCtx c) { return c.Si(74); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; int v = Value;
            var ir = new RectangleF(b.X + c.S(9), b.Y, c.S(18), b.Height);
            Ui.Icon(c.G, Icons.Brightness, ir, v >= 0 ? c.T.Text : c.T.TextFaint, c.S(15));
            Ui.Text(c.G, v >= 0 ? v + "%" : (Snap.Method == "" ? "…" : "—"), c.F(12.5f, true), c.T.Text, new RectangleF(ir.Right + c.S(5), b.Y, b.Right - ir.Right - c.S(8), b.Height));
            if (Supported) c.Wheel(b, d => SetVal((Value < 0 ? 50 : Value) + d * 5));
        }

        public override Size PanelSize(float sc) { return new Size((int)(320 * sc), (int)(176 * sc)); }
        public override void DrawPanel(DrawCtx c)
        {
            var s = Snap; var b = c.Bounds; int v = Value;
            string sub = s.Method == "wmi" ? "Built-in display" : s.Method == "ddc" ? "External monitor · DDC/CI" : s.Method == "" ? "Detecting…" : null;
            float y = c.PanelHeader("Brightness", sub, Icons.Brightness);
            if (!Supported && s.Method != "")
            {
                Ui.TextWrap(c.G, "This display doesn't expose brightness control (no WMI and no DDC/CI). Use the monitor's buttons or Windows display settings.", c.F(12), c.T.TextDim, new RectangleF(b.X + c.S(16), y + c.S(4), b.Width - c.S(32), c.S(60)));
                c.Button(new RectangleF(b.X + c.S(16), b.Bottom - c.S(44), b.Width - c.S(32), c.S(30)), "Display settings", () => Ctl.Open("ms-settings:display"), false, Icons.Settings);
                return;
            }
            Ui.Text(c.G, (v >= 0 ? v.ToString() : "–"), c.F(30, true), c.T.Text, new RectangleF(b.X + c.S(16), y, c.S(80), c.S(40)));
            Ui.Text(c.G, "%", c.F(14), c.T.TextDim, new RectangleF(b.X + c.S(16) + Ui.MeasureW(c.G, v >= 0 ? v.ToString() : "–", c.F(30, true)) + c.S(3), y + c.S(6), c.S(30), c.S(30)));
            y += c.S(46);
            var lo = new RectangleF(b.X + c.S(12), y, c.S(20), c.S(24));
            var hi = new RectangleF(b.Right - c.S(32), y, c.S(20), c.S(24));
            c.IconButton(lo, Icons.Remove, () => SetVal(Value - 10), false, 11);
            c.IconButton(hi, Icons.Add, () => SetVal(Value + 10), false, 11);
            Ctl.Slider(c, new RectangleF(lo.Right + c.S(10), y, hi.X - lo.Right - c.S(20), c.S(24)), Math.Max(0, v) / 100.0, f => SetVal((int)Math.Round(f * 100)));
            y += c.S(36);
            int[] presets = { 0, 25, 50, 75, 100 };
            float gap = c.S(6), pw = (b.Width - c.S(32) - gap * 4) / 5;
            for (int i = 0; i < presets.Length; i++)
            {
                int p = presets[i]; var r = new RectangleF(b.X + c.S(16) + i * (pw + gap), y, pw, c.S(26));
                bool on = v == p, h = c.IsHover(r);
                Ui.FillRound(c.G, r, c.S(8), on ? c.T.Accent : h ? c.T.TileHover : c.T.Tile);
                Ui.Text(c.G, p + "%", c.F(11.5f, on), on ? Color.White : c.T.Text, r, 0);
                c.Hit(r, () => SetVal(p));
            }
        }
    }
}
