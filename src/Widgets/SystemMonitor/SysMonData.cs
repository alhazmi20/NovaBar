// System Monitor data layer: cheap shared samplers published through Hub keys.
//   sysmon.cpu   1s   GetSystemTimes deltas (+60s history ring)
//   sysmon.cores 1s   NtQuerySystemInformation(SystemProcessorPerformanceInformation) — subscribed only while a CPU panel is open
//   sysmon.mem   1s   GlobalMemoryStatusEx (+60s history ring)
//   sysmon.net   1s   NetworkInterface.GetIPv4Statistics deltas (+60s history rings)
//   sysmon.power 2s   GetSystemPowerStatus (NOVABAR_FAKE_BATTERY=63,charging overrides for testing)
//   sysmon.procs 3s   NtQuerySystemInformation(SystemProcessInformation): one syscall, no handles, private working set like Task Manager
//   sysmon.temp  5s   WMI thermal zone (cached searcher), graceful "no sensor"
//   sysmon.disk  30s  DriveInfo fixed drives
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NovaBar
{
    /// <summary>Fixed-size ring buffer of doubles (thread-safe push/copy).</summary>
    public class SysMonRing
    {
        readonly double[] a; int head, count;
        public int Count { get { return count; } }
        public SysMonRing(int n) { a = new double[n]; }
        public int Capacity { get { return a.Length; } }
        public void Push(double v) { lock (a) { a[head] = v; head = (head + 1) % a.Length; if (count < a.Length) count++; } }
        /// <summary>Copies oldest..newest right-aligned into dst (left padded with 0). Returns the max value.</summary>
        public double CopyTo(double[] dst)
        {
            lock (a)
            {
                double mx = 0; int n = Math.Min(count, dst.Length), pad = dst.Length - n;
                for (int i = 0; i < pad; i++) dst[i] = 0;
                int start = (head - n + a.Length) % a.Length;
                for (int i = 0; i < n; i++) { var v = a[(start + i) % a.Length]; dst[pad + i] = v; if (v > mx) mx = v; }
                return mx;
            }
        }
    }

    public class SysMonCpuSnap { public double Load; }
    public class SysMonCoresSnap { public double[] Loads; }
    public class SysMonMemSnap { public double Load; public ulong Total, Avail, CommitUsed, CommitLimit; }
    public class SysMonNetSnap { public double Down, Up; public long TotalRx, TotalTx; public string Adapter, Ip, Kind; public bool Connected; }
    public class SysMonPowerSnap
    {
        public bool HasBattery, Plugged, Charging, Fake; public int Pct; public int SecsLeft = -1;
        public override bool Equals(object o) { var p = o as SysMonPowerSnap; return p != null && p.HasBattery == HasBattery && p.Plugged == Plugged && p.Charging == Charging && p.Pct == Pct && p.SecsLeft / 60 == SecsLeft / 60; }
        public override int GetHashCode() { return Pct; }
    }
    public class SysMonProc { public string Name; public double Cpu; public long Mem; }
    public class SysMonProcSnap
    {
        public SysMonProc[] TopCpu, TopRam; public int Count;
        public override bool Equals(object o)
        {
            var p = o as SysMonProcSnap; if (p == null) return false;
            return Same(p.TopCpu, TopCpu, true) && Same(p.TopRam, TopRam, false);
        }
        static bool Same(SysMonProc[] a, SysMonProc[] b, bool cpu)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].Name != b[i].Name) return false;
                if (cpu ? Math.Round(a[i].Cpu * 100) != Math.Round(b[i].Cpu * 100) : a[i].Mem / (1 << 20) != b[i].Mem / (1 << 20)) return false;
            }
            return true;
        }
        public override int GetHashCode() { return Count; }
    }
    public class SysMonTempSnap
    {
        public double C = double.NaN; public string Source;
        public bool Ok { get { return !double.IsNaN(C); } }
        public override bool Equals(object o) { var t = o as SysMonTempSnap; return t != null && (t.C.Equals(C)) && t.Source == Source; }
        public override int GetHashCode() { return 1; }
    }
    public class SysMonDrive { public string Name, Label; public long Total, Free; }
    public class SysMonDiskSnap
    {
        public SysMonDrive[] Drives;
        public override bool Equals(object o)
        {
            var d = o as SysMonDiskSnap; if (d == null || d.Drives.Length != Drives.Length) return false;
            for (int i = 0; i < Drives.Length; i++) if (d.Drives[i].Name != Drives[i].Name || d.Drives[i].Free / (1 << 24) != Drives[i].Free / (1 << 24) || d.Drives[i].Label != Drives[i].Label) return false;
            return true;
        }
        public override int GetHashCode() { return Drives.Length; }
    }

    public static class SysMon
    {
        public const int HistLen = 60;
        public static readonly SysMonRing CpuHist = new SysMonRing(HistLen), MemHist = new SysMonRing(HistLen), DownHist = new SysMonRing(HistLen), UpHist = new SysMonRing(HistLen);

        static bool defined;
        public static void Ensure()
        {
            if (defined) return; defined = true;
            Hub.Define("sysmon.cpu", 1000, SampleCpu);
            Hub.Define("sysmon.cores", 1000, SampleCores);
            Hub.Define("sysmon.mem", 1000, SampleMem);
            Hub.Define("sysmon.net", 1000, SampleNet);
            Hub.Define("sysmon.power", 2000, SamplePower);
            Hub.Define("sysmon.procs", 3000, SampleProcs);
            Hub.Define("sysmon.temp", 5000, SampleTemp);
            Hub.Define("sysmon.disk", 30000, SampleDisk);
        }

        public static SysMonCpuSnap Cpu { get { return Hub.Get<SysMonCpuSnap>("sysmon.cpu"); } }
        public static SysMonCoresSnap Cores { get { return Hub.Get<SysMonCoresSnap>("sysmon.cores"); } }
        public static SysMonMemSnap Mem { get { return Hub.Get<SysMonMemSnap>("sysmon.mem"); } }
        public static SysMonNetSnap Net { get { return Hub.Get<SysMonNetSnap>("sysmon.net"); } }
        public static SysMonPowerSnap Power { get { return Hub.Get<SysMonPowerSnap>("sysmon.power"); } }
        public static SysMonProcSnap Procs { get { return Hub.Get<SysMonProcSnap>("sysmon.procs"); } }
        public static SysMonTempSnap Temp { get { return Hub.Get<SysMonTempSnap>("sysmon.temp"); } }
        public static SysMonDiskSnap Disk { get { return Hub.Get<SysMonDiskSnap>("sysmon.disk"); } }

        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int retLen);
        const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

        // ---------------- CPU ----------------
        static ulong pIdle, pKernel, pUser;
        static object SampleCpu()
        {
            Native.FILETIME i, k, u;
            if (!Native.GetSystemTimes(out i, out k, out u)) return null;
            ulong di = i.Value - pIdle, dk = k.Value - pKernel, du = u.Value - pUser, tot = dk + du;
            // need at least ~50ms of wall time across all cores to get a meaningful number
            bool first = pKernel == 0;
            if (!first && tot < 500000UL * (ulong)Environment.ProcessorCount) return null;
            pIdle = i.Value; pKernel = k.Value; pUser = u.Value;
            if (first) return null;
            double load = Math.Max(0, Math.Min(1, 1 - di / (double)tot));
            CpuHist.Push(load);
            return new SysMonCpuSnap { Load = load };
        }

        static string cpuName;
        public static string CpuName
        {
            get
            {
                if (cpuName == null)
                {
                    try
                    {
                        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                            cpuName = key == null ? "" : (Convert.ToString(key.GetValue("ProcessorNameString")) ?? "").Trim();
                        while (cpuName.Contains("  ")) cpuName = cpuName.Replace("  ", " ");
                        cpuName = cpuName.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace(" CPU", "");
                    }
                    catch { cpuName = ""; }
                    if (cpuName.Length == 0) cpuName = "Processor";
                }
                return cpuName;
            }
        }

        // ---------------- per core ----------------
        static IntPtr coreBuf = IntPtr.Zero; static int coreBufLen; static long[] corePrev;
        static object SampleCores()
        {
            int n = Environment.ProcessorCount, sz = 48, ret;
            if (coreBuf == IntPtr.Zero) { coreBufLen = sz * n; coreBuf = Marshal.AllocHGlobal(coreBufLen); }
            if (NtQuerySystemInformation(8, coreBuf, coreBufLen, out ret) != 0) return null;
            n = Math.Min(n, ret / sz);
            var cur = new long[n * 3];
            for (int c = 0; c < n; c++) { cur[c * 3] = Marshal.ReadInt64(coreBuf, c * sz); cur[c * 3 + 1] = Marshal.ReadInt64(coreBuf, c * sz + 8); cur[c * 3 + 2] = Marshal.ReadInt64(coreBuf, c * sz + 16); }
            var prev = corePrev; corePrev = cur;
            if (prev == null || prev.Length != cur.Length) return null;
            var loads = new double[n];
            for (int c = 0; c < n; c++)
            {
                double di = cur[c * 3] - prev[c * 3], tot = (cur[c * 3 + 1] - prev[c * 3 + 1]) + (cur[c * 3 + 2] - prev[c * 3 + 2]);
                loads[c] = tot <= 0 ? 0 : Math.Max(0, Math.Min(1, 1 - di / tot));
            }
            return new SysMonCoresSnap { Loads = loads };
        }
        /// <summary>Call from OnPanelClose so a later open starts with a fresh delta.</summary>
        public static void ResetCores() { corePrev = null; }

        // ---------------- memory ----------------
        static readonly Native.MEMORYSTATUSEX ms = new Native.MEMORYSTATUSEX();
        static object SampleMem()
        {
            ms.dwLength = 64;
            if (!Native.GlobalMemoryStatusEx(ms) || ms.ullTotalPhys == 0) return null;
            var s = new SysMonMemSnap { Total = ms.ullTotalPhys, Avail = ms.ullAvailPhys, CommitLimit = ms.ullTotalPageFile, CommitUsed = ms.ullTotalPageFile - ms.ullAvailPageFile };
            s.Load = (s.Total - s.Avail) / (double)s.Total;
            MemHist.Push(s.Load);
            return s;
        }

        // ---------------- network ----------------
        static HashSet<string> netIds; static int netSelTick = -100000; static string netName = "", netIp = "", netKind = "";
        static readonly Dictionary<string, long[]> netPrev = new Dictionary<string, long[]>();
        static long netPrevTs;
        static object SampleNet()
        {
            var all = NetworkInterface.GetAllNetworkInterfaces();
            if (netIds == null || Environment.TickCount - netSelTick > 15000) SelectAdapters(all);
            long now = Stopwatch.GetTimestamp();
            double dt = netPrevTs == 0 ? 0 : (now - netPrevTs) / (double)Stopwatch.Frequency; netPrevTs = now;
            long rxSum = 0, txSum = 0, dRx = 0, dTx = 0;
            foreach (var ni in all)
            {
                if (!netIds.Contains(ni.Id)) continue;
                long rx, tx;
                try { var st = ni.GetIPv4Statistics(); rx = st.BytesReceived; tx = st.BytesSent; } catch { continue; }
                rxSum += rx; txSum += tx;
                long[] p;
                if (netPrev.TryGetValue(ni.Id, out p)) { dRx += Math.Max(0, rx - p[0]); dTx += Math.Max(0, tx - p[1]); p[0] = rx; p[1] = tx; }
                else netPrev[ni.Id] = new long[] { rx, tx };
            }
            if (dt <= 0.05) return null;
            var s = new SysMonNetSnap { Down = dRx / dt, Up = dTx / dt, TotalRx = rxSum, TotalTx = txSum, Adapter = netName, Ip = netIp, Kind = netKind, Connected = netIds.Count > 0 };
            DownHist.Push(s.Down); UpHist.Push(s.Up);
            return s;
        }
        static void SelectAdapters(NetworkInterface[] all)
        {
            netSelTick = Environment.TickCount;
            var withGw = new List<NetworkInterface>(); var up = new List<NetworkInterface>();
            foreach (var ni in all)
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                up.Add(ni);
                try { if (ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.ToString().StartsWith("0."))) withGw.Add(ni); } catch { }
            }
            var sel = withGw.Count > 0 ? withGw : up;
            netIds = new HashSet<string>(sel.Select(n => n.Id));
            foreach (var k in netPrev.Keys.Where(k => !netIds.Contains(k)).ToList()) netPrev.Remove(k);
            NetworkInterface prim = null; long best = -1;
            foreach (var ni in sel) { try { var st = ni.GetIPv4Statistics(); if (st.BytesReceived > best) { best = st.BytesReceived; prim = ni; } } catch { } }
            if (prim == null) { netName = "Disconnected"; netIp = "—"; netKind = ""; return; }
            netName = prim.Name;
            netKind = prim.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : prim.NetworkInterfaceType == NetworkInterfaceType.Ethernet || prim.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet ? "Ethernet" : prim.Description;
            netIp = "—";
            try { foreach (var ua in prim.GetIPProperties().UnicastAddresses) if (ua.Address.AddressFamily == AddressFamily.InterNetwork) { netIp = ua.Address.ToString(); break; } } catch { }
        }

        // ---------------- power ----------------
        static object SamplePower()
        {
            var fake = FakeBattery();
            if (fake != null) return fake;
            Native.SYSTEM_POWER_STATUS s;
            if (!Native.GetSystemPowerStatus(out s)) return new SysMonPowerSnap { HasBattery = false, Plugged = true };
            bool has = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255 && s.BatteryLifePercent != 255;
            return new SysMonPowerSnap
            {
                HasBattery = has, Plugged = s.ACLineStatus == 1 || !has, Charging = has && (s.BatteryFlag & 8) != 0,
                Pct = has ? Math.Min(100, (int)s.BatteryLifePercent) : 0, SecsLeft = has ? s.BatteryLifeTime : -1
            };
        }
        /// <summary>Testing aid: env NOVABAR_FAKE_BATTERY or config extra "sysmon.fakeBattery" = "63", "63,charging", "80,ac".</summary>
        static SysMonPowerSnap FakeBattery()
        {
            string v = Environment.GetEnvironmentVariable("NOVABAR_FAKE_BATTERY");
            if (string.IsNullOrEmpty(v)) { try { object o; if (AppConfig.Current.Extra.TryGetValue("sysmon.fakeBattery", out o) && o != null) v = Convert.ToString(o); } catch { } }
            if (string.IsNullOrEmpty(v)) return null;
            var parts = v.ToLowerInvariant().Split(',');
            int pct; if (!int.TryParse(parts[0].Trim(), out pct)) return null;
            bool chg = parts.Any(x => x.Trim() == "charging"), ac = chg || parts.Any(x => x.Trim() == "ac" || x.Trim() == "plugged");
            pct = Math.Max(0, Math.Min(100, pct));
            return new SysMonPowerSnap { HasBattery = true, Fake = true, Pct = pct, Charging = chg, Plugged = ac, SecsLeft = ac ? -1 : pct * 260 };
        }

        // ---------------- processes ----------------
        class ProcEntry { public long Create, Time; public string Name; }
        static IntPtr procBuf = IntPtr.Zero; static int procBufLen;
        static Dictionary<long, ProcEntry> procPrev = new Dictionary<long, ProcEntry>();
        static long procPrevTs;
        static readonly HashSet<string> procHide = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Idle", "Memory Compression", "Registry", "Secure System", "System Idle Process" };
        static object SampleProcs()
        {
            if (procBuf == IntPtr.Zero) { procBufLen = 1 << 20; procBuf = Marshal.AllocHGlobal(procBufLen); }
            int st, ret;
            for (int tries = 0; ; tries++)
            {
                st = NtQuerySystemInformation(5, procBuf, procBufLen, out ret);
                if (st != STATUS_INFO_LENGTH_MISMATCH || tries > 4) break;
                Marshal.FreeHGlobal(procBuf); procBufLen = Math.Max(ret + (1 << 16), procBufLen * 2); procBuf = Marshal.AllocHGlobal(procBufLen);
            }
            if (st != 0) return null;
            long now = Stopwatch.GetTimestamp();
            double wall = procPrevTs == 0 ? 0 : (now - procPrevTs) / (double)Stopwatch.Frequency; procPrevTs = now;
            double den = wall * 1e7 * Environment.ProcessorCount;
            bool x64 = IntPtr.Size == 8;
            var cpuBy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var memBy = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var seen = new Dictionary<long, ProcEntry>(procPrev.Count + 32);
            int off = 0, count = 0;
            while (true)
            {
                IntPtr p = new IntPtr(procBuf.ToInt64() + off);
                int next = Marshal.ReadInt32(p, 0);
                long pid = Marshal.ReadIntPtr(p, x64 ? 80 : 68).ToInt64();
                if (pid != 0)
                {
                    long wsPriv = Marshal.ReadInt64(p, 8), create = Marshal.ReadInt64(p, 32), t = Marshal.ReadInt64(p, 40) + Marshal.ReadInt64(p, 48);
                    ProcEntry e; double cpu = 0;
                    if (procPrev.TryGetValue(pid, out e) && e.Create == create)
                    {
                        if (den > 0) cpu = Math.Max(0, (t - e.Time) / den);
                        e.Time = t;
                    }
                    else
                    {
                        int len = (ushort)Marshal.ReadInt16(p, 56); IntPtr nb = Marshal.ReadIntPtr(p, x64 ? 64 : 60);
                        string name = nb != IntPtr.Zero && len > 0 ? Marshal.PtrToStringUni(nb, len / 2) : "System";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                        e = new ProcEntry { Create = create, Time = t, Name = name };
                    }
                    seen[pid] = e; count++;
                    if (!procHide.Contains(e.Name))
                    {
                        double c0; cpuBy.TryGetValue(e.Name, out c0); cpuBy[e.Name] = c0 + cpu;
                        long m0; memBy.TryGetValue(e.Name, out m0); memBy[e.Name] = m0 + wsPriv;
                    }
                }
                if (next == 0) break;
                off += next;
                if (off >= procBufLen) break;
            }
            procPrev = seen;
            var snap = new SysMonProcSnap { Count = count };
            snap.TopRam = memBy.OrderByDescending(kv => kv.Value).Take(3).Select(kv => new SysMonProc { Name = Friendly(kv.Key), Mem = kv.Value }).ToArray();
            if (den > 0) snap.TopCpu = cpuBy.OrderByDescending(kv => kv.Value).Take(3).Select(kv => new SysMonProc { Name = Friendly(kv.Key), Cpu = Math.Min(1, kv.Value) }).ToArray();
            return snap;
        }
        static readonly Dictionary<string, string> friendly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"msedge","Edge"},{"msedgewebview2","Edge WebView"},{"RadeonSoftware","Radeon"},{"nvcontainer","NVIDIA"},{"Cam_helper","Camera Helper"},{"chrome","Chrome"},{"firefox","Firefox"},{"brave","Brave"},{"opera","Opera"},{"Code","VS Code"},{"devenv","Visual Studio"},
            {"explorer","Explorer"},{"dwm","Desktop Window Mgr"},{"svchost","Service Host"},{"MsMpEng","Defender"},{"vmmem","WSL / VM"},{"vmmemWSL","WSL"},
            {"ms-teams","Teams"},{"Teams","Teams"},{"Discord","Discord"},{"Spotify","Spotify"},{"WINWORD","Word"},{"EXCEL","Excel"},{"POWERPNT","PowerPoint"},{"OUTLOOK","Outlook"},
            {"olk","Outlook"},{"steam","Steam"},{"steamwebhelper","Steam"},{"Taskmgr","Task Manager"},{"SearchHost","Search"},{"StartMenuExperienceHost","Start"},{"csrss","Client Server Runtime"},
            {"NovaBar","NovaBar"},{"WindowsTerminal","Terminal"},{"powershell","PowerShell"},{"pwsh","PowerShell"},{"OneDrive","OneDrive"},{"slack","Slack"},{"Zoom","Zoom"},{"audiodg","Audio"},
        };
        public static string Friendly(string n)
        {
            string f; if (friendly.TryGetValue(n, out f)) return f;
            if (n.Length == 0) return n;
            var sb = new System.Text.StringBuilder(n.Length + 4);
            for (int i = 0; i < n.Length; i++)
            {
                char ch = n[i];
                if (ch == '_' || ch == '-') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(ch) && char.IsLower(n[i - 1])) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(ch) : ch);
            }
            return sb.ToString();
        }

        // ---------------- temperature ----------------
        static ManagementObjectSearcher tempSearcher; static int tempMode; static int tempNoneTick; static string tempSrc;
        static object SampleTemp()
        {
            var fake = Environment.GetEnvironmentVariable("NOVABAR_FAKE_TEMP"); double fv;
            if (!string.IsNullOrEmpty(fake) && double.TryParse(fake, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv)) return new SysMonTempSnap { C = fv, Source = "Simulated" };
            // External sensor tools first (Windows itself exposes no CPU temperature on most desktops):
            string xsrc; double xv = SysMonExtTemp.Read(out xsrc);
            if (!double.IsNaN(xv)) return new SysMonTempSnap { C = Math.Round(xv, 1), Source = xsrc };
            if (tempMode < 0)
            {
                if (Environment.TickCount - tempNoneTick < 300000) return new SysMonTempSnap();
                tempMode = 0;
            }
            if (tempMode == 0)
            {
                // try ACPI first (often admin-only), then the perf-counter class (no admin, Kelvin)
                double v = TryQuery(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", "CurrentTemperature", 10);
                if (!double.IsNaN(v)) { tempMode = 1; tempSrc = "ACPI thermal zone"; return new SysMonTempSnap { C = v, Source = tempSrc }; }
                DisposeTemp();
                v = TryQuery(@"root\CIMV2", "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation", "Temperature", 1);
                if (!double.IsNaN(v)) { tempMode = 2; tempSrc = "Thermal zone sensor"; return new SysMonTempSnap { C = v, Source = tempSrc }; }
                DisposeTemp(); tempMode = -1; tempNoneTick = Environment.TickCount;
                return new SysMonTempSnap();
            }
            double r = RunTemp(tempMode == 1 ? "CurrentTemperature" : "Temperature", tempMode == 1 ? 10 : 1);
            if (double.IsNaN(r)) { DisposeTemp(); tempMode = 0; return new SysMonTempSnap(); }
            return new SysMonTempSnap { C = r, Source = tempSrc };
        }
        static double TryQuery(string scope, string q, string prop, double div)
        {
            try { tempSearcher = new ManagementObjectSearcher(scope, q); return RunTemp(prop, div); }
            catch { return double.NaN; }
        }
        static double RunTemp(string prop, double div)
        {
            double best = double.NaN;
            try
            {
                using (var col = tempSearcher.Get())
                    foreach (ManagementBaseObject o in col)
                        using (o)
                        {
                            var raw = o[prop]; if (raw == null) continue;
                            double c = Convert.ToDouble(raw) / div - 273.15;
                            if (c > 1 && c < 125 && (double.IsNaN(best) || c > best)) best = c;
                        }
            }
            catch { return double.NaN; }
            return double.IsNaN(best) ? best : Math.Round(best, 1);
        }
        static void DisposeTemp() { if (tempSearcher != null) { try { tempSearcher.Dispose(); } catch { } tempSearcher = null; } }

        // ---------------- disks ----------------
        static object SampleDisk()
        {
            var l = new List<SysMonDrive>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    l.Add(new SysMonDrive { Name = d.Name.TrimEnd('\\'), Label = string.IsNullOrEmpty(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel, Total = d.TotalSize, Free = d.TotalFreeSpace });
                }
                catch { }
            }
            return new SysMonDiskSnap { Drives = l.ToArray() };
        }

        // ---------------- formatting ----------------
        public static string Uptime()
        {
            var t = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h";
            if (t.TotalHours >= 1) return t.Hours + "h " + t.Minutes + "m";
            return t.Minutes + "m";
        }
        public static string Gb(double b) { double g = b / 1073741824.0; if (g >= 1000) return (g / 1024).ToString(g >= 10240 ? "0" : "0.0#") + " TB"; return g >= 100 ? g.ToString("0") + " GB" : g >= 10 ? g.ToString("0.#") + " GB" : g.ToString("0.0") + " GB"; }
        public static string MemStr(long b) { return b >= 1073741824L ? (b / 1073741824.0).ToString("0.0") + " GB" : (b / 1048576.0).ToString("0") + " MB"; }
        public static string RateShort(double bps)
        {
            if (bps < 1024) return bps.ToString("0") + " B/s";
            if (bps < 1048576) return (bps / 1024).ToString(bps < 10240 ? "0.0" : "0") + " KB/s";
            if (bps < 1073741824) return (bps / 1048576).ToString(bps < 10485760 ? "0.0" : "0") + " MB/s";
            return (bps / 1073741824).ToString("0.0") + " GB/s";
        }
        public static bool Imperial { get { return AppConfig.Current.Units == "imperial"; } }
        public static string TempStr(double c) { if (double.IsNaN(c)) return "—"; return (Imperial ? Math.Round(c * 9 / 5 + 32) : Math.Round(c)) + "°"; }
    }
}


namespace NovaBar
{
    /// <summary>CPU temperature from monitoring tools that publish sensors without NovaBar needing a kernel driver:
    /// LibreHardwareMonitor / OpenHardwareMonitor (WMI) and HWiNFO (shared memory). Missing tools are re-checked once a minute.</summary>
    public static class SysMonExtTemp
    {
        static int webMissAt = -100000, lhmMissAt = -100000, ohmMissAt = -100000, hwiMissAt = -100000;
        static readonly string[] Prefer = { "core (tctl/tdie)", "cpu (tctl/tdie)", "tctl/tdie", "core (tdie)", "cpu package", "package", "tctl", "cpu die", "cpu" };

        public static double Read(out string src)
        {
            src = null; double v;
            if (Environment.TickCount - webMissAt > 60000) { v = LhmWeb(); if (!double.IsNaN(v)) { src = "LibreHardwareMonitor"; return v; } webMissAt = Environment.TickCount; }
            if (Environment.TickCount - lhmMissAt > 60000) { v = Wmi(@"root\LibreHardwareMonitor"); if (!double.IsNaN(v)) { src = "LibreHardwareMonitor"; return v; } lhmMissAt = Environment.TickCount; }
            if (Environment.TickCount - hwiMissAt > 60000) { v = Hwinfo(); if (!double.IsNaN(v)) { src = "HWiNFO"; return v; } hwiMissAt = Environment.TickCount; }
            if (Environment.TickCount - ohmMissAt > 60000) { v = Wmi(@"root\OpenHardwareMonitor"); if (!double.IsNaN(v)) { src = "OpenHardwareMonitor"; return v; } ohmMissAt = Environment.TickCount; }
            return double.NaN;
        }
        static int Rank(string label)
        {
            label = (label ?? "").ToLowerInvariant();
            for (int i = 0; i < Prefer.Length; i++) if (label.Contains(Prefer[i])) return i;
            return 99;
        }
        static double Wmi(string ns)
        {
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(ns, "SELECT Name, Identifier, Value FROM Sensor WHERE SensorType='Temperature'"))
                using (var col = s.Get())
                {
                    double best = double.NaN; int bestRank = 1000;
                    foreach (System.Management.ManagementBaseObject o in col)
                        using (o)
                        {
                            var id = Convert.ToString(o["Identifier"]) ?? ""; var name = Convert.ToString(o["Name"]) ?? "";
                            if (id.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) < 0) continue;    // /amdcpu/0/… or /intelcpu/0/…
                            double val = Convert.ToDouble(o["Value"]);
                            if (val <= 1 || val > 125) continue;
                            int r = Rank(name);
                            if (r < bestRank || (r == bestRank && val > best)) { bestRank = r; best = val; }
                        }
                    return best;
                }
            }
            catch { return double.NaN; }
        }
        // LibreHardwareMonitor 0.9.5+ dropped WMI; read its local web server (Options → Remote Web Server, 127.0.0.1:8085)
        static double LhmWeb()
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://127.0.0.1:8085/data.json");
                req.Timeout = 800; req.ReadWriteTimeout = 800; req.Proxy = null;
                string body;
                using (var resp = req.GetResponse())
                using (var rd = new System.IO.StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8)) body = rd.ReadToEnd();
                double best = double.NaN; int bestRank = 1000;
                WalkLhm(J.Parse(body), false, ref best, ref bestRank);
                return best;
            }
            catch { return double.NaN; }
        }
        static void WalkLhm(object node, bool inCpu, ref double best, ref int bestRank)
        {
            var o = node as Dictionary<string, object>;
            if (o == null) return;
            string text = J.Str(o, "Text"), img = J.Str(o, "ImageURL"), sid = J.Str(o, "SensorId"), type = J.Str(o, "Type");
            if (img.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) >= 0) inCpu = true;
            string val = J.Str(o, "Value");
            bool isTemp = type == "Temperature" || val.EndsWith("\u00B0C");
            if (isTemp && (inCpu || sid.IndexOf("cpu/", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                double d;
                var num = val.Replace("\u00B0C", "").Trim().Replace(',', '.');
                if (double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) && d > 1 && d <= 125)
                {
                    int r = Rank(text);
                    if (r < bestRank || (r == bestRank && d > best)) { bestRank = r; best = d; }
                }
            }
            var kids = J.Arr(o, "Children");
            if (kids != null) foreach (var k in kids) WalkLhm(k, inCpu, ref best, ref bestRank);
        }
        // HWiNFO shared memory (Settings → "Shared Memory Support")
        static double Hwinfo()
        {
            try
            {
                using (var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting("Global\\HWiNFO_SENS_SM2", System.IO.MemoryMappedFiles.MemoryMappedFileRights.Read))
                using (var a = mmf.CreateViewAccessor(0, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read))
                {
                    uint offRead = a.ReadUInt32(32), sizeRead = a.ReadUInt32(36), numRead = a.ReadUInt32(40);
                    double best = double.NaN; int bestRank = 1000;
                    var buf = new byte[128];
                    for (uint i = 0; i < numRead && i < 4096; i++)
                    {
                        long b = offRead + (long)i * sizeRead;
                        if (a.ReadUInt32(b) != 1) continue;                        // SENSOR_TYPE_TEMP
                        a.ReadArray(b + 12, buf, 0, 128);                          // szLabelOrig
                        var label = System.Text.Encoding.ASCII.GetString(buf).TrimEnd('\0');
                        if (label.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) < 0 && label.IndexOf("tctl", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        double val = a.ReadDouble(b + 12 + 128 + 128 + 16);
                        if (val <= 1 || val > 125) continue;
                        int r = Rank(label);
                        if (r < bestRank || (r == bestRank && val > best)) { bestRank = r; best = val; }
                    }
                    return best;
                }
            }
            catch { return double.NaN; }
        }
    }
}
