// Wi-Fi data service: one shared WLAN client handle while any Wi-Fi tile is on a bar.
// Event-driven (WlanRegisterNotification ACM+MSM + NetworkChange.NetworkAddressChanged), debounced, worker-thread only.
// Polling: signal quality every 15 s while a tile is live; internet state every 60 s while a panel is open. Nothing else.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using Windows.Devices.Radios;

namespace NovaBar
{
    public enum WifiState { NoAdapter, RadioOff, Disconnected, Connecting, Connected }

    public class WifiNet
    {
        public string Ssid = "", Profile = ""; public byte[] Raw = new byte[0];
        public int Quality, Auth, Cipher, Band, Rssi, BandMask; public bool Secured, HasProfile, Connected, Connectable = true;
        public int Bars { get { return WifiSvc.BarsOf(Quality); } }
    }

    /// <summary>Immutable snapshot (new object per change).</summary>
    public class WifiSnap
    {
        public WifiState State = WifiState.NoAdapter;
        public bool HwOff;                          // hardware switch off (software toggle can't help)
        public string Ssid = "", Profile = "", Phy = "", AuthText = "";
        public int Quality, Rssi, Band, Channel, Auth;
        public bool Secured;
        public long RxMbps, TxMbps;
        public string Ip = "";
        public int Internet = -1;                   // -1 unknown, 0 none/limited, 1 internet
        public bool Wired; public string WiredName = "", WiredIp = ""; public long WiredMbps;
        public List<WifiNet> Nets = new List<WifiNet>();
        public bool NetsLoaded, Scanning;
        public string ConnectingSsid = "";          // user-initiated attempt in flight
        public string FailSsid = "", FailText = ""; // last failure (shown inline in the list)
        public int Bars { get { return WifiSvc.BarsOf(Quality); } }
        public string Sig; public bool Ready;
    }

    public static class WifiSvc
    {
        static readonly object gate = new object();
        static readonly HashSet<Widget> subs = new HashSet<Widget>(), panels = new HashSet<Widget>();
        static IntPtr handle = IntPtr.Zero;
        static Guid iface = Guid.Empty; static bool hasIface;
        // the notification delegate must stay alive while registered → static field
        static readonly WifiNative.NotifyCallback notifyCb = OnNotify;
        static readonly NetworkAddressChangedEventHandler addrCb = (s, e) => Request(400, false);
        static Timer refreshTimer, signalTimer, internetTimer, connectTimer, scanTimer;
        static volatile bool wantNets; static int busy;
        static volatile WifiSnap snap = new WifiSnap();
        static volatile bool scanning; static int scanStamp;
        static string connectingSsid = "", failSsid = "", failText = "", createdProfile = null;
        static int internet = -1; static int internetStamp;
        public static readonly string Demo = (Environment.GetEnvironmentVariable("NOVABAR_WIFI_DEMO") ?? "").Trim().ToLowerInvariant();

        public static WifiSnap Snap { get { return snap; } }
        public static bool PanelOpen { get { lock (gate) return panels.Count > 0; } }

        public static int BarsOf(int q) { return q <= 0 ? 0 : q < 30 ? 1 : q < 55 ? 2 : q < 78 ? 3 : 4; }

        // ================= lifecycle =================
        public static void Attach(Widget w)
        {
            bool first;
            lock (gate) { first = subs.Count == 0; subs.Add(w); }
            if (Demo.Length > 0) { snap = WifiDemo.Make(Demo); return; }
            if (!first) { Notify(); return; }
            ThreadPool.QueueUserWorkItem(_ => Open());
            try { NetworkChange.NetworkAddressChanged += addrCb; } catch { }
            signalTimer = new Timer(_ => SignalPoll(), null, 15000, 15000);
        }
        public static void Detach(Widget w)
        {
            bool last;
            lock (gate) { subs.Remove(w); panels.Remove(w); last = subs.Count == 0; }
            if (!last || Demo.Length > 0) return;
            try { NetworkChange.NetworkAddressChanged -= addrCb; } catch { }
            DisposeTimer(ref signalTimer); DisposeTimer(ref internetTimer); DisposeTimer(ref refreshTimer); DisposeTimer(ref connectTimer); DisposeTimer(ref scanTimer);
            IntPtr h;
            lock (gate) { h = handle; handle = IntPtr.Zero; hasIface = false; }
            // WlanCloseHandle also unregisters notifications and waits for in-flight callbacks → call it outside our lock, off the UI thread
            if (h != IntPtr.Zero) ThreadPool.QueueUserWorkItem(_ => { try { WifiNative.WlanCloseHandle(h, IntPtr.Zero); } catch { } });
        }
        static void DisposeTimer(ref Timer t) { var x = t; t = null; if (x != null) try { x.Dispose(); } catch { } }

        public static void PanelOpened(Widget w)
        {
            lock (gate) panels.Add(w);
            if (Demo.Length > 0) return;
            internetStamp = 0;
            if (internetTimer == null) internetTimer = new Timer(_ => { internetStamp = 0; Request(0, false); }, null, 60000, 60000);
            Scan();
            Request(0, true);
        }
        public static void PanelClosed(Widget w)
        {
            bool none; lock (gate) { panels.Remove(w); none = panels.Count == 0; }
            if (none) DisposeTimer(ref internetTimer);
        }

        static void Open()
        {
            lock (gate)
            {
                if (handle != IntPtr.Zero) return;
                if (subs.Count == 0) return;
                try
                {
                    uint ver; IntPtr h;
                    if (WifiNative.WlanOpenHandle(2, IntPtr.Zero, out ver, out h) == 0)
                    {
                        handle = h; uint prev;
                        WifiNative.WlanRegisterNotification(h, WifiNative.SRC_ACM | WifiNative.SRC_MSM, true, notifyCb, IntPtr.Zero, IntPtr.Zero, out prev);
                    }
                }
                catch { handle = IntPtr.Zero; } // wlanapi missing (Server SKU without WLAN service) → NoAdapter
            }
            Request(0, false);
        }

        // ================= notifications (WLAN thread: keep it tiny) =================
        static void OnNotify(IntPtr p, IntPtr ctx)
        {
            try
            {
                var n = (WifiNative.NOTIFICATION_DATA)Marshal.PtrToStructure(p, typeof(WifiNative.NOTIFICATION_DATA));
                if (n.Source == WifiNative.SRC_ACM)
                {
                    switch (n.Code)
                    {
                        case WifiNative.ACM_SCAN_COMPLETE: case WifiNative.ACM_SCAN_FAIL: scanning = false; Request(150, true); break;
                        case WifiNative.ACM_CONNECTION_COMPLETE: case WifiNative.ACM_CONNECTION_ATTEMPT_FAIL: OnConnectResult(n); break;
                        case WifiNative.ACM_INTERFACE_ARRIVAL: case WifiNative.ACM_INTERFACE_REMOVAL: lock (gate) hasIface = false; Request(300, true); break;
                        case WifiNative.ACM_CONNECTION_START: case WifiNative.ACM_DISCONNECTING: case WifiNative.ACM_DISCONNECTED:
                        case WifiNative.ACM_NETWORK_AVAILABLE: case WifiNative.ACM_NETWORK_NOT_AVAILABLE: case WifiNative.ACM_PROFILE_CHANGE:
                            internetStamp = 0; Request(250, PanelOpen); break;
                    }
                }
                else if (n.Source == WifiNative.SRC_MSM)
                {
                    switch (n.Code)
                    {
                        case WifiNative.MSM_SIGNAL_QUALITY_CHANGE: case WifiNative.MSM_LINK_DEGRADED: case WifiNative.MSM_LINK_IMPROVED: Request(500, false); break;
                        case WifiNative.MSM_ADAPTER_REMOVAL: lock (gate) hasIface = false; Request(300, PanelOpen); break;
                        case WifiNative.MSM_RADIO_STATE_CHANGE: Request(300, PanelOpen); break;
                        case WifiNative.MSM_ASSOCIATING: case WifiNative.MSM_AUTHENTICATING: case WifiNative.MSM_CONNECTED: case WifiNative.MSM_ROAMING_END:
                        case WifiNative.MSM_DISASSOCIATING: case WifiNative.MSM_DISCONNECTED: internetStamp = 0; Request(250, false); break;
                    }
                }
            }
            catch { }
        }
        static void OnConnectResult(WifiNative.NOTIFICATION_DATA n)
        {
            if (n.Data == IntPtr.Zero || n.DataSize < 564) { Request(200, true); return; }
            // WLAN_CONNECTION_NOTIFICATION_DATA: mode@0, profile[256]@4, DOT11_SSID@516, bssType@552, secEnabled@556, reason@560
            string profile = Marshal.PtrToStringUni(IntPtr.Add(n.Data, 4));
            string ssid = WifiNative.SsidText(WifiNative.ReadSsid(IntPtr.Add(n.Data, 516)));
            uint reason = (uint)Marshal.ReadInt32(n.Data, 560);
            string toDelete = null;
            lock (gate)
            {
                bool ours = connectingSsid.Length > 0 && (connectingSsid == ssid || connectingSsid == profile);
                if (n.Code == WifiNative.ACM_CONNECTION_COMPLETE)
                {
                    if (reason == 0) { if (ours) { connectingSsid = ""; failSsid = ""; failText = ""; createdProfile = null; } }
                    else if (ours)
                    {
                        failSsid = connectingSsid; failText = Friendly(reason); connectingSsid = "";
                        if (createdProfile != null && createdProfile == profile) toDelete = createdProfile; // don't keep a profile with a wrong password
                        createdProfile = null;
                    }
                }
            }
            if (toDelete != null) { var name = toDelete; ThreadPool.QueueUserWorkItem(_ => { lock (gate) { if (handle != IntPtr.Zero && hasIface) try { WifiNative.WlanDeleteProfile(handle, ref iface, name, IntPtr.Zero); } catch { } } }); }
            internetStamp = 0;
            Request(200, true);
            Request2(4000); // NLM needs a few seconds to classify the new network
        }
        static string Friendly(uint reason)
        {
            switch (reason)
            {
                case 0x00048001: case 0x00048002: case 0x00048003: case 0x0004800A: return "Couldn't connect — check the password";
                case 0x00028002: return "Network is out of range";
                case 0x00038006: case 0x00038002: return "Couldn't connect — check the password";
            }
            var s = WifiNative.ReasonText(reason);
            return s.Length > 90 ? s.Substring(0, 88) + "…" : s;
        }

        // ================= refresh =================
        public static void Request(int delayMs, bool nets)
        {
            if (Demo.Length > 0) return;
            if (nets) wantNets = true;
            lock (gate)
            {
                if (subs.Count == 0) return;
                if (refreshTimer == null) refreshTimer = new Timer(_ => Refresh(), null, delayMs, Timeout.Infinite);
                else refreshTimer.Change(delayMs, Timeout.Infinite); // debounce: restart the window
            }
        }
        static void Request2(int delayMs) { ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(delayMs); Request(0, false); }); }

        static bool EnsureIface()
        {
            if (handle == IntPtr.Zero) return false;
            if (hasIface) return true;
            IntPtr list = IntPtr.Zero;
            try
            {
                if (WifiNative.WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero) return false;
                // WLAN_INTERFACE_INFO_LIST: count@0, index@4, items@8 (each 532 bytes: GUID, WCHAR[256], state)
                int count = Marshal.ReadInt32(list); Guid pick = Guid.Empty; bool found = false;
                for (int i = 0; i < count; i++)
                {
                    var item = IntPtr.Add(list, 8 + i * 532);
                    var g = (Guid)Marshal.PtrToStructure(item, typeof(Guid));
                    int st = Marshal.ReadInt32(item, 528);
                    if (!found || st == WifiNative.IS_CONNECTED) { pick = g; found = true; }
                }
                iface = pick; hasIface = found; return found;
            }
            catch { return false; }
            finally { if (list != IntPtr.Zero) WifiNative.WlanFreeMemory(list); }
        }

        static int QueryInt(int op, int offset)
        {
            uint size; IntPtr data; int vt;
            if (WifiNative.WlanQueryInterface(handle, ref iface, op, IntPtr.Zero, out size, out data, out vt) != 0 || data == IntPtr.Zero) return int.MinValue;
            try { return size >= offset + 4 ? Marshal.ReadInt32(data, offset) : int.MinValue; } finally { WifiNative.WlanFreeMemory(data); }
        }

        class Conn { public int State; public string Profile = "", Ssid = ""; public byte[] Bssid = new byte[6]; public int Phy, Quality, Auth, Cipher; public long Rx, Tx; public bool Secured; }
        static Conn QueryConn()
        {
            uint size; IntPtr d; int vt;
            if (WifiNative.WlanQueryInterface(handle, ref iface, WifiNative.OP_CURRENT_CONNECTION, IntPtr.Zero, out size, out d, out vt) != 0 || d == IntPtr.Zero) return null;
            try
            {
                if (size < 604) return null;
                // WLAN_CONNECTION_ATTRIBUTES: state@0 mode@4 profile[256]@8; association@520: ssid(36) bssType@556 bssid@560 phy@568 phyIdx@572 quality@576 rx@580 tx@584; security@588: enabled, oneX, auth@596, cipher@600
                var c = new Conn { State = Marshal.ReadInt32(d, 0), Profile = Marshal.PtrToStringUni(IntPtr.Add(d, 8)) ?? "" };
                c.Ssid = WifiNative.SsidText(WifiNative.ReadSsid(IntPtr.Add(d, 520)));
                Marshal.Copy(IntPtr.Add(d, 560), c.Bssid, 0, 6);
                c.Phy = Marshal.ReadInt32(d, 568); c.Quality = Marshal.ReadInt32(d, 576);
                c.Rx = (uint)Marshal.ReadInt32(d, 580) / 1000; c.Tx = (uint)Marshal.ReadInt32(d, 584) / 1000;
                c.Secured = Marshal.ReadInt32(d, 588) != 0; c.Auth = Marshal.ReadInt32(d, 596); c.Cipher = Marshal.ReadInt32(d, 600);
                return c;
            }
            finally { WifiNative.WlanFreeMemory(d); }
        }

        class Bss { public byte[] Ssid, Bssid; public int Rssi, Band, Phy; public uint Freq; }
        static List<Bss> QueryBss()
        {
            var res = new List<Bss>(); IntPtr list;
            if (WifiNative.WlanGetNetworkBssList(handle, ref iface, IntPtr.Zero, 3, false, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero) return res;
            try
            {
                // WLAN_BSS_LIST: totalSize@0 count@4 entries@8 (360 bytes): ssid@0 phyId@36 bssid@40 bssType@48 phy@52 rssi@56 linkQ@60 ... freq(kHz)@92
                int n = Marshal.ReadInt32(list, 4);
                for (int i = 0; i < n && i < 512; i++)
                {
                    var e = IntPtr.Add(list, 8 + i * 360);
                    var b = new Bss { Ssid = WifiNative.ReadSsid(e), Bssid = new byte[6] };
                    Marshal.Copy(IntPtr.Add(e, 40), b.Bssid, 0, 6);
                    b.Phy = Marshal.ReadInt32(e, 52); b.Rssi = Marshal.ReadInt32(e, 56); b.Freq = (uint)Marshal.ReadInt32(e, 92); b.Band = WifiNative.BandOf(b.Freq);
                    res.Add(b);
                }
            }
            finally { WifiNative.WlanFreeMemory(list); }
            return res;
        }

        static List<WifiNet> QueryNets(List<Bss> bss)
        {
            var map = new Dictionary<string, WifiNet>(); IntPtr list;
            if (WifiNative.WlanGetAvailableNetworkList(handle, ref iface, 0, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero) return new List<WifiNet>();
            try
            {
                // WLAN_AVAILABLE_NETWORK_LIST: count@0 index@4 items@8 (628 bytes): profile[256]@0 ssid@512 bssType@548 nBss@552 connectable@556
                // notConnReason@560 nPhy@564 phys[8]@568 more@600 quality@604 secEnabled@608 auth@612 cipher@616 flags@620
                int n = Marshal.ReadInt32(list);
                for (int i = 0; i < n && i < 512; i++)
                {
                    var e = IntPtr.Add(list, 8 + i * 628);
                    if (Marshal.ReadInt32(e, 548) != 1) continue;               // infrastructure only
                    var raw = WifiNative.ReadSsid(IntPtr.Add(e, 512));
                    if (raw.Length == 0 || raw.All(x => x == 0)) continue;      // hidden networks
                    var key = WifiNative.Hex(raw);
                    uint flags = (uint)Marshal.ReadInt32(e, 620);
                    var net = new WifiNet
                    {
                        Raw = raw, Ssid = WifiNative.SsidText(raw), Profile = Marshal.PtrToStringUni(e) ?? "",
                        Connectable = Marshal.ReadInt32(e, 556) != 0, Quality = Marshal.ReadInt32(e, 604), Secured = Marshal.ReadInt32(e, 608) != 0,
                        Auth = Marshal.ReadInt32(e, 612), Cipher = Marshal.ReadInt32(e, 616),
                        Connected = (flags & WifiNative.AVAIL_FLAG_CONNECTED) != 0, HasProfile = (flags & WifiNative.AVAIL_FLAG_HAS_PROFILE) != 0
                    };
                    WifiNet old;
                    if (map.TryGetValue(key, out old))
                    {
                        // dedupe by SSID: keep strongest signal, merge flags (profile entries come separately from the plain entry)
                        old.Quality = Math.Max(old.Quality, net.Quality); old.Connected |= net.Connected;
                        if (net.HasProfile && !old.HasProfile) { old.HasProfile = true; old.Profile = net.Profile; }
                        if (net.Auth > old.Auth && !net.HasProfile) { old.Auth = net.Auth; old.Cipher = net.Cipher; }
                    }
                    else map[key] = net;
                }
            }
            finally { WifiNative.WlanFreeMemory(list); }
            foreach (var net in map.Values)
            {
                Bss best = null;
                foreach (var b in bss) if (b.Ssid.Length == net.Raw.Length && b.Ssid.SequenceEqual(net.Raw)) { net.BandMask |= 1 << b.Band; if (best == null || b.Rssi > best.Rssi) best = b; }
                if (best != null) { net.Band = best.Band; net.Rssi = best.Rssi; }
            }
            return map.Values.OrderByDescending(x => x.Connected).ThenByDescending(x => x.Quality).ThenBy(x => x.Ssid, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static void Refresh()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) { Request(200, false); return; }
            try
            {
                bool nets = wantNets; wantNets = false;
                bool panel = PanelOpen;
                var s = new WifiSnap { Ready = true };
                var prev = snap;
                lock (gate)
                {
                    if (subs.Count == 0) return;
                    if (EnsureIface())
                    {
                        s.State = WifiState.Disconnected;
                        int sw = QueryInt(WifiNative.OP_RADIO_STATE, 8), hw = QueryInt(WifiNative.OP_RADIO_STATE, 12);
                        // DOT11_RADIO_STATE: 1 on, 2 off
                        if (hw == 2) { s.State = WifiState.RadioOff; s.HwOff = true; }
                        else if (sw == 2) s.State = WifiState.RadioOff;
                        if (s.State != WifiState.RadioOff)
                        {
                            var c = QueryConn();
                            int ist = c != null ? c.State : QueryInt(WifiNative.OP_INTERFACE_STATE, 0);
                            if (c != null && ist == WifiNative.IS_CONNECTED)
                            {
                                s.State = WifiState.Connected; s.Ssid = c.Ssid; s.Profile = c.Profile; s.Quality = c.Quality; s.Secured = c.Secured;
                                s.Auth = c.Auth; s.AuthText = c.Secured ? WifiNative.AuthName(c.Auth) : "Open"; s.Phy = WifiNative.PhyName(c.Phy);
                                s.RxMbps = c.Rx; s.TxMbps = c.Tx;
                                int rssi = QueryInt(WifiNative.OP_RSSI, 0); if (rssi != int.MinValue && rssi < 0) s.Rssi = rssi;
                                if (panel || prev.Band == 0 || prev.Ssid != s.Ssid || nets)
                                {
                                    var bss = QueryBss();
                                    var me = bss.FirstOrDefault(b => b.Bssid.SequenceEqual(c.Bssid));
                                    if (me != null) { s.Band = me.Band; if (s.Rssi == 0) s.Rssi = me.Rssi; }
                                    if (nets && panel) { s.Nets = QueryNets(bss); s.NetsLoaded = true; }
                                }
                                else s.Band = prev.Band;
                                int ch = QueryInt(WifiNative.OP_CHANNEL_NUMBER, 0); if (ch > 0) s.Channel = ch;
                                if (s.Band == 0 && ch > 0 && ch <= 14) s.Band = 2;
                            }
                            else if (ist == WifiNative.IS_ASSOCIATING || ist == WifiNative.IS_AUTHENTICATING || ist == WifiNative.IS_DISCOVERING)
                            {
                                s.State = WifiState.Connecting; if (c != null) s.Ssid = c.Ssid;
                            }
                            if (nets && panel && !s.NetsLoaded) { s.Nets = QueryNets(QueryBss()); s.NetsLoaded = true; }
                        }
                    }
                    if (!s.NetsLoaded && prev.NetsLoaded && s.State != WifiState.RadioOff && s.State != WifiState.NoAdapter) { s.Nets = prev.Nets; s.NetsLoaded = true; }
                    if (s.NetsLoaded && s.State == WifiState.Connected)
                        foreach (var n in s.Nets) n.Connected = n.Ssid == s.Ssid; // keep the "connected" flag in sync between full scans
                    if (connectingSsid.Length > 0 && s.State != WifiState.Connected) { s.State = WifiState.Connecting; if (s.Ssid.Length == 0) s.Ssid = connectingSsid; }
                    if (connectingSsid.Length > 0 && s.State == WifiState.Connected && s.Ssid == connectingSsid) { connectingSsid = ""; failSsid = ""; failText = ""; createdProfile = null; }
                    s.ConnectingSsid = connectingSsid; s.FailSsid = failSsid; s.FailText = failText;
                }
                s.Scanning = scanning;
                // IPv4 / wired fallback (System.Net.NetworkInformation) — only when it's visible somewhere
                bool needWired = s.State == WifiState.NoAdapter || s.State == WifiState.RadioOff || s.State == WifiState.Disconnected;
                if (panel || needWired) ReadInterfaces(s, needWired || panel);
                // internet: cheap NLM query, cached ≥ 60 s unless an event reset the stamp
                if (s.State == WifiState.Connected || s.Wired)
                {
                    int now = Environment.TickCount;
                    if (internetStamp == 0 || now - internetStamp > 60000 || internet < 0) { internet = QueryInternet(); internetStamp = now == 0 ? 1 : now; }
                    s.Internet = internet;
                }
                else { internet = -1; }
                s.Sig = SigOf(s);
                if (s.Sig != prev.Sig || !ReferenceEquals(prev.Nets, s.Nets) && s.NetsLoaded) { snap = s; Notify(); }
            }
            catch { }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        static string SigOf(WifiSnap s)
        {
            var sb = new StringBuilder();
            sb.Append((int)s.State).Append('|').Append(s.Ssid).Append('|').Append(s.Bars).Append('|').Append(s.Quality).Append('|').Append(s.Rssi).Append('|').Append(s.Band)
              .Append('|').Append(s.RxMbps).Append('|').Append(s.TxMbps).Append('|').Append(s.Ip).Append('|').Append(s.Internet).Append('|').Append(s.Wired).Append(s.WiredName).Append(s.WiredIp)
              .Append('|').Append(s.Scanning).Append('|').Append(s.ConnectingSsid).Append('|').Append(s.FailSsid).Append(s.FailText).Append('|').Append(s.NetsLoaded).Append(s.HwOff);
            foreach (var n in s.Nets) sb.Append('#').Append(n.Ssid).Append(n.Bars).Append(n.Connected).Append(n.HasProfile).Append(n.BandMask);
            return sb.ToString();
        }

        static void SignalPoll()
        {
            if (Demo.Length > 0) return;
            var s = snap; if (s.State != WifiState.Connected) return;
            int q = int.MinValue, rssi = int.MinValue;
            lock (gate)
            {
                if (handle == IntPtr.Zero || !hasIface) return;
                try { var c = QueryConn(); if (c != null) q = c.Quality; rssi = QueryInt(WifiNative.OP_RSSI, 0); } catch { }
            }
            if (q != int.MinValue && (q != s.Quality || (rssi != int.MinValue && rssi != s.Rssi))) Request(0, false);
        }

        static void ReadInterfaces(WifiSnap s, bool wired)
        {
            try
            {
                string id = "{" + iface.ToString().ToUpperInvariant() + "}";
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (hasIface && string.Equals(ni.Id, id, StringComparison.OrdinalIgnoreCase)) { s.Ip = Ipv4(ni); continue; }
                    if (!wired || s.Wired) continue;
                    var t = ni.NetworkInterfaceType;
                    if (t != NetworkInterfaceType.Ethernet && t != NetworkInterfaceType.GigabitEthernet && t != NetworkInterfaceType.FastEthernetT && t != NetworkInterfaceType.FastEthernetFx) continue;
                    string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (d.Contains("virtual") || d.Contains("hyper-v") || d.Contains("vethernet") || d.Contains("vmware") || d.Contains("virtualbox") || d.Contains("tap-") || d.Contains("wireguard") || d.Contains("bluetooth") || d.Contains("loopback") || d.Contains("vpn")) continue;
                    var p = ni.GetIPProperties();
                    if (!p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any))) continue;
                    s.Wired = true; s.WiredName = ni.Name; s.WiredIp = Ipv4(ni); s.WiredMbps = ni.Speed / 1000000;
                }
            }
            catch { }
        }
        static string Ipv4(NetworkInterface ni)
        {
            try { foreach (var a in ni.GetIPProperties().UnicastAddresses) if (a.Address.AddressFamily == AddressFamily.InterNetwork) return a.Address.ToString(); } catch { }
            return "";
        }
        static int QueryInternet()
        {
            try
            {
                var p = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                if (p == null) return 0;
                return p.GetNetworkConnectivityLevel() == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess ? 1 : 0;
            }
            catch { return -1; }
        }

        static void Notify()
        {
            Widget[] ws; lock (gate) ws = subs.ToArray();
            foreach (var w in ws) { try { w.Invalidate(); } catch { } }
        }

        // ================= actions (all off the UI thread) =================
        public static void Scan()
        {
            if (Demo.Length > 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (gate)
                {
                    if (handle == IntPtr.Zero) { /* Open() still pending */ }
                    else if (EnsureIface())
                    {
                        try { if (WifiNative.WlanScan(handle, ref iface, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0) { scanning = true; scanStamp = Environment.TickCount; } } catch { }
                    }
                }
                Request(30, false);
                // scan_complete normally arrives within ~4 s; make sure the spinner never sticks
                DisposeTimer(ref scanTimer);
                scanTimer = new Timer(__ => { if (scanning && Environment.TickCount - scanStamp >= 5500) { scanning = false; Request(0, true); } }, null, 6000, Timeout.Infinite);
            });
        }

        public static void Disconnect()
        {
            if (Demo.Length > 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (gate) { if (handle != IntPtr.Zero && EnsureIface()) try { WifiNative.WlanDisconnect(handle, ref iface, IntPtr.Zero); } catch { } }
                Request(300, true);
            });
        }

        /// <summary>Connect to a nearby network. password == null → saved profile or open network.</summary>
        public static void Connect(WifiNet net, string password)
        {
            if (net == null || Demo.Length > 0) return;
            lock (gate) { connectingSsid = net.Ssid; failSsid = ""; failText = ""; }
            Notify(); Request(0, false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = null;
                lock (gate)
                {
                    try
                    {
                        if (handle == IntPtr.Zero || !EnsureIface()) err = "Wi-Fi adapter not available";
                        else
                        {
                            string profile = net.HasProfile && password == null ? net.Profile : null;
                            if (profile == null)
                            {
                                string xml = WifiProfile.Build(net, password);
                                if (xml == null) err = "This network needs to be set up in Windows Settings";
                                else
                                {
                                    uint reason; uint r = WifiNative.WlanSetProfile(handle, ref iface, 0, xml, null, true, IntPtr.Zero, out reason);
                                    if (r != 0) err = reason != 0 ? WifiNative.ReasonText(reason) : (r == 1206 ? "Invalid password format" : "Couldn't save the network (" + r + ")");
                                    else { profile = WifiProfile.NameOf(net); createdProfile = profile; }
                                }
                            }
                            if (err == null)
                            {
                                var p = new WifiNative.CONNECTION_PARAMETERS { Mode = 0, Profile = profile, BssType = 1 };
                                uint r = WifiNative.WlanConnect(handle, ref iface, ref p, IntPtr.Zero);
                                if (r != 0) err = "Couldn't start connecting (" + r + ")";
                            }
                        }
                    }
                    catch (Exception ex) { err = ex.Message; }
                    if (err != null) { failSsid = net.Ssid; failText = err; connectingSsid = ""; }
                }
                Request(0, false);
                // safety timeout: never leave the UI spinning forever
                DisposeTimer(ref connectTimer);
                var ssid = net.Ssid;
                connectTimer = new Timer(__ =>
                {
                    bool fire = false;
                    lock (gate) if (connectingSsid == ssid) { connectingSsid = ""; failSsid = ssid; failText = "Connection timed out"; fire = true; }
                    if (fire) Request(0, true);
                }, null, 45000, Timeout.Infinite);
            });
        }
        public static void ClearFailure() { lock (gate) { failSsid = ""; failText = ""; } Request(0, false); }

        /// <summary>Software radio on/off through WinRT Windows.Devices.Radios (falls back to Settings when not allowed).</summary>
        public static void SetRadio(bool on)
        {
            if (Demo.Length > 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try
                {
                    try { WifiWinRT.Wait(Radio.RequestAccessAsync(), 4000); } catch { }
                    var radios = WifiWinRT.Wait(Radio.GetRadiosAsync());
                    if (radios != null)
                        foreach (var r in radios)
                            if (r.Kind == RadioKind.WiFi) { ok = WifiWinRT.Wait(r.SetStateAsync(on ? RadioState.On : RadioState.Off)) == RadioAccessStatus.Allowed; break; }
                }
                catch { }
                if (!ok) WifiSvc.OpenUri("ms-settings:network-wifi");
                Request(500, true);
            });
        }

        public static void OpenUri(string uri)
        {
            ThreadPool.QueueUserWorkItem(_ => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); } catch { } });
        }
    }

    /// <summary>WLAN profile XML builders (SSID as hex + escaped name; key escaped).</summary>
    public static class WifiProfile
    {
        public static string NameOf(WifiNet n) { return n.Ssid.Length > 0 ? n.Ssid : WifiNative.Hex(n.Raw); }
        public static string Build(WifiNet n, string key)
        {
            string auth, enc; bool shared = true;
            if (!n.Secured || n.Auth == 1) { auth = "open"; enc = "none"; shared = false; }
            else if (n.Auth == 10) { auth = "OWE"; enc = "AES"; shared = false; }
            else if (n.Auth == 9) { auth = "WPA3SAE"; enc = "AES"; }
            else if (n.Auth == 7) { auth = "WPA2PSK"; enc = n.Cipher == 2 ? "TKIP" : "AES"; }
            else if (n.Auth == 4) { auth = "WPAPSK"; enc = n.Cipher == 4 ? "AES" : "TKIP"; }
            else return null; // WEP / enterprise → Settings
            if (shared && string.IsNullOrEmpty(key)) return null;
            string name = SecurityElement.Escape(NameOf(n));
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
            sb.Append("<name>").Append(name).Append("</name>");
            sb.Append("<SSIDConfig><SSID><hex>").Append(WifiNative.Hex(n.Raw)).Append("</hex><name>").Append(name).Append("</name></SSID></SSIDConfig>");
            sb.Append("<connectionType>ESS</connectionType><connectionMode>auto</connectionMode>");
            sb.Append("<MSM><security><authEncryption><authentication>").Append(auth).Append("</authentication><encryption>").Append(enc)
              .Append("</encryption><useOneX>false</useOneX>");
            if (auth == "WPA3SAE") sb.Append("<transitionMode xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v4\">true</transitionMode>");
            sb.Append("</authEncryption>");
            if (shared)
            {
                bool hexKey = key.Length == 64 && key.All(Uri.IsHexDigit);
                sb.Append("<sharedKey><keyType>").Append(hexKey ? "networkKey" : "passPhrase").Append("</keyType><protected>false</protected><keyMaterial>")
                  .Append(SecurityElement.Escape(key)).Append("</keyMaterial></sharedKey>");
            }
            sb.Append("</security></MSM></WLANProfile>");
            return sb.ToString();
        }
    }

    /// <summary>Synchronous wait on WinRT async ops (no AsTask in this csc setup). Background threads only.</summary>
    public static class WifiWinRT
    {
        public static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op, int timeoutMs = 8000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (sw.ElapsedMilliseconds > timeoutMs) { try { op.Cancel(); } catch { } throw new TimeoutException(); }
                Thread.Sleep(10);
            }
            if (op.Status != Windows.Foundation.AsyncStatus.Completed) throw new InvalidOperationException("WinRT op " + op.Status);
            return op.GetResults();
        }
    }
}
