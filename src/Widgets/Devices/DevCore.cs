// Device batteries — shared model + background poller.
// Port of the user's Rust tray app "bt-battery-tray" (wirless-charge/src/main.rs, device.rs):
// every 30 s scan Bluetooth (PnP property / GATT) + Logitech HID++; a device that stops answering keeps
// its last level for 30 min and is shown as "asleep" once it has missed a scan.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Windows.Foundation;

namespace NovaBar
{
    public enum DevKind { Keyboard = 0, Mouse = 1, Headset = 2, Gamepad = 3, Other = 4 }
    public enum DevConn { Bluetooth, Receiver, Cable }

    public static class DevKinds
    {
        public static string Label(DevKind k)
        {
            switch (k) { case DevKind.Keyboard: return "Keyboard"; case DevKind.Mouse: return "Mouse"; case DevKind.Headset: return "Headset"; case DevKind.Gamepad: return "Controller"; default: return "Device"; }
        }
        public static string Letter(DevKind k)
        {
            switch (k) { case DevKind.Keyboard: return "K"; case DevKind.Mouse: return "M"; case DevKind.Headset: return "H"; case DevKind.Gamepad: return "G"; default: return "B"; }
        }
        public static string ConnLabel(DevConn c)
        {
            switch (c) { case DevConn.Receiver: return "Logitech receiver"; case DevConn.Cable: return "USB cable"; default: return "Bluetooth"; }
        }
        static bool Has(string n, params string[] words) { foreach (var w in words) if (n.Contains(w)) return true; return false; }
        /// <summary>device.rs Kind::from_name — best-effort guess from a product name.</summary>
        public static DevKind FromName(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            if (Has(n, "mouse", "mx master", "mx anywhere", "trackball", "trackpad", "touchpad")) return DevKind.Mouse;
            if (Has(n, "keyboard", "keys", "kb", "ak8", "keychron")) return DevKind.Keyboard;
            if (Has(n, "headset", "headphone", "buds", "airpods", "earbud", "wh-", "wf-", "bose", "jabra", "beats", "earphone")) return DevKind.Headset;
            if (Has(n, "controller", "gamepad", "xbox", "dualsense", "dualshock", "joy-con")) return DevKind.Gamepad;
            return DevKind.Other;
        }
    }

    /// <summary>One scan result (immutable once published).</summary>
    public class DevDevice
    {
        public string Key, Name; public DevKind Kind; public int Battery; public bool Charging; public DevConn Conn;
        public DateTime LastSeen; public bool Asleep;
        public DevDevice Clone() { return (DevDevice)MemberwiseClone(); }
        public bool SameAs(DevDevice o)
        {
            return o != null && o.Key == Key && o.Name == Name && o.Kind == Kind && o.Battery == Battery && o.Charging == Charging && o.Conn == Conn && o.Asleep == Asleep && o.LastSeen == LastSeen;
        }
    }

    public class DevSnap
    {
        public DevDevice[] Devices = new DevDevice[0];
        public string[] Errors = new string[0];
        public DateTime ScanTime;
        public bool Demo;
        public override bool Equals(object obj)
        {
            var o = obj as DevSnap; if (o == null) return false;
            if (o.ScanTime != ScanTime || o.Demo != Demo || o.Devices.Length != Devices.Length || o.Errors.Length != Errors.Length) return false;
            for (int i = 0; i < Devices.Length; i++) if (!Devices[i].SameAs(o.Devices[i])) return false;
            return true;
        }
        public override int GetHashCode() { return Devices.Length; }
        public DevDevice Find(string name)
        {
            if (Devices.Length == 0) return null;
            if (string.IsNullOrEmpty(name) || name == DevHub.FirstDevice) return Devices[0];
            foreach (var d in Devices) if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) return d;
            foreach (var d in Devices) if (d.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return d;
            return null;
        }
    }

    /// <summary>Same wait pattern as MediaWinRT.Wait (AsTask does not compile with this csc).</summary>
    public static class DevWinRT
    {
        public static T Wait<T>(IAsyncOperation<T> op, int ms)
        {
            using (var ev = new ManualResetEvent(false))
            {
                op.Completed = (o, st) => { try { ev.Set(); } catch { } };
                if (!ev.WaitOne(ms)) { try { op.Cancel(); } catch { } throw new TimeoutException(); }
                if (op.Status != AsyncStatus.Completed) throw new InvalidOperationException("WinRT op " + op.Status);
                return op.GetResults();
            }
        }
    }

    public static class DevHub
    {
        public const string Key = "dev.batteries";
        public const string FirstDevice = "(first device)";
        const int PollMs = 30000;                                   // main.rs POLL_INTERVAL
        static readonly TimeSpan KeepMissingFor = TimeSpan.FromMinutes(30);  // main.rs KEEP_MISSING_FOR
        static readonly TimeSpan AsleepAfter = TimeSpan.FromSeconds(35);     // POLL_INTERVAL + 5 s
        static readonly Dictionary<string, DevDevice> seen = new Dictionary<string, DevDevice>();
        static readonly object L = new object();
        static string knownSaved;
        public static readonly bool DemoMode = Environment.GetEnvironmentVariable("NOVABAR_DEV_DEMO") == "1";

        public static void Ensure() { Hub.Define(Key, PollMs, Sample); }
        public static DevSnap Snap { get { return Hub.Get<DevSnap>(Key); } }
        public static void Refresh() { Ensure(); Hub.Refresh(Key); }

        static object Sample()
        {
            if (DemoMode) return DemoSnap();
            var errors = new List<string>();
            var found = new List<DevDevice>();
            try { found.AddRange(DevBluetooth.Scan()); }
            catch (Exception e) { errors.Add("Bluetooth: " + e.Message); }
            try { found.AddRange(DevLogitech.Scan()); }
            catch (Exception e) { errors.Add("HID: " + e.Message); }
            var now = DateTime.Now;
            DevDevice[] list;
            lock (L)
            {
                foreach (var d in found) { d.LastSeen = now; seen[d.Key] = d; }
                foreach (var k in seen.Where(kv => now - kv.Value.LastSeen >= KeepMissingFor).Select(kv => kv.Key).ToList()) seen.Remove(k);
                list = seen.Values.Select(d => { var c = d.Clone(); c.Asleep = now - d.LastSeen > AsleepAfter; return c; })
                    .OrderBy(d => (int)d.Kind).ThenBy(d => d.Name.ToLowerInvariant()).ToArray();
            }
            SaveKnown(list);
            return new DevSnap { Devices = list, Errors = errors.ToArray(), ScanTime = now };
        }

        /// <summary>Known device names (persisted so the Settings app can offer them in the single-device Choice).</summary>
        public static string[] KnownNames()
        {
            var s = Snap; var names = new List<string>();
            if (s != null) foreach (var d in s.Devices) if (!names.Contains(d.Name)) names.Add(d.Name);
            try
            {
                var st = StateStore.Load("dev.known"); object v;
                if (st.TryGetValue("names", out v) && v is System.Collections.IEnumerable && !(v is string))
                    foreach (var o in (System.Collections.IEnumerable)v) { var n = J.AsStr(o); if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n); }
            }
            catch { }
            return names.ToArray();
        }
        static void SaveKnown(DevDevice[] list)
        {
            if (list.Length == 0) return;
            try
            {
                var st = StateStore.Load("dev.known"); var names = new List<string>(); object v;
                if (st.TryGetValue("names", out v) && v is System.Collections.IEnumerable && !(v is string))
                    foreach (var o in (System.Collections.IEnumerable)v) { var n = J.AsStr(o); if (!string.IsNullOrEmpty(n)) names.Add(n); }
                bool changed = false;
                foreach (var d in list) if (!names.Contains(d.Name)) { names.Add(d.Name); changed = true; }
                var sig = string.Join("\n", names);
                if (!changed || sig == knownSaved) return;
                knownSaved = sig;
                if (names.Count > 24) names = names.Skip(names.Count - 24).ToList();
                st["names"] = names.Cast<object>().ToArray();
                StateStore.Save("dev.known", st);
            }
            catch { }
        }

        static DevSnap DemoSnap()
        {
            var now = DateTime.Now;
            var l = new[]
            {
                new DevDevice { Key = "bt:DC35622F0948", Name = "AK820 5.1-1", Kind = DevKind.Keyboard, Battery = 93, Conn = DevConn.Bluetooth, LastSeen = now },
                new DevDevice { Key = "logi:6EAE1691", Name = "G502 X PLUS", Kind = DevKind.Mouse, Battery = 53, Charging = true, Conn = DevConn.Cable, LastSeen = now },
                new DevDevice { Key = "logi:1A2B3C4D", Name = "MX Anywhere 3", Kind = DevKind.Mouse, Battery = 14, Conn = DevConn.Receiver, LastSeen = now },
                new DevDevice { Key = "bt:A0B1C2D3E4F5", Name = "WH-1000XM5", Kind = DevKind.Headset, Battery = 100, Conn = DevConn.Bluetooth, LastSeen = now },
                new DevDevice { Key = "bt:112233445566", Name = "Xbox Wireless Controller", Kind = DevKind.Gamepad, Battery = 38, Conn = DevConn.Bluetooth, LastSeen = now.AddMinutes(-12), Asleep = true },
            };
            return new DevSnap { Devices = l, ScanTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0), Demo = true };
        }

        public static void Open(string target)
        {
            ThreadPool.QueueUserWorkItem(_ => { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } });
        }
    }
}
