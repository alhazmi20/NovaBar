// Configuration + per-widget state + account secrets. All files live in %APPDATA%\NovaBar.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NovaBar
{
    public class WidgetInstance
    {
        public string Id;      // widget type id, e.g. "cpu"
        public string Inst;    // unique instance id, e.g. "cpu-1a2b"
        public string Zone;    // "left" | "center" | "right"
        public Dictionary<string, object> Settings = new Dictionary<string, object>();
        /// <summary>Screen.DeviceName values (e.g. \\.\DISPLAY2) this widget shows on. Empty = every monitor.</summary>
        public List<string> Monitors = new List<string>();
        public bool ShowsOn(string deviceName)
        {
            if (Monitors == null || Monitors.Count == 0) return true;
            foreach (var m in Monitors) if (string.Equals(m, deviceName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        public WidgetInstance Clone()
        {
            return new WidgetInstance { Id = Id, Inst = Inst, Zone = Zone, Settings = Settings == null ? new Dictionary<string, object>() : new Dictionary<string, object>(Settings), Monitors = Monitors == null ? new List<string>() : new List<string>(Monitors) };
        }
    }

    public static class Paths
    {
        public static string Root
        {
            get
            {
                var p = Environment.GetEnvironmentVariable("NOVABAR_HOME");
                if (string.IsNullOrEmpty(p)) p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovaBar");
                Directory.CreateDirectory(p); return p;
            }
        }
        public static string ConfigFile { get { return Path.Combine(Root, "config.json"); } }
        public static string StateDir { get { var p = Path.Combine(Root, "state"); Directory.CreateDirectory(p); return p; } }
        public static string CacheDir { get { var p = Path.Combine(Root, "cache"); Directory.CreateDirectory(p); return p; } }
        public static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }
    }

    /// <summary>
    /// App configuration. Global options are typed properties; anything else goes in Extra.
    /// Saved as pretty JSON. The bar watches the file and live-reloads when the Settings app writes it.
    /// </summary>
    public class AppConfig
    {
        public string Theme = "dark";        // dark | white | glass | glasslight | midnight
        public string Position = "bottom";   // bottom | top | left | right (left/right = vertical bar)
        public string Style = "pill";        // pill (width fits the widgets, default) | full
        public string Size = "M";            // S | M | L  (default; Extra["size.<device>"] overrides per display)
        public string SizeFor(string dev)
        {
            object v; string s;
            if (dev != null && Extra != null && Extra.TryGetValue("size." + dev, out v) && (s = J.AsStr(v)).Length > 0) return s;
            return Size;
        }
        /// <summary>Edge for a display: Extra["position.&lt;device&gt;"] overrides the global Position.</summary>
        public string PositionFor(string dev)
        {
            object v; string s;
            if (dev != null && Extra != null && Extra.TryGetValue("position." + dev, out v) && (s = J.AsStr(v)).Length > 0) return s;
            return Position;
        }
        public void SetPositionFor(string dev, string pos)
        {
            if (Extra == null) Extra = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(pos)) Extra.Remove("position." + dev); else Extra["position." + dev] = pos;
        }
        public void SetSizeFor(string dev, string size)
        {
            if (Extra == null) Extra = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(size)) Extra.Remove("size." + dev); else Extra["size." + dev] = size;
        }
        public bool Clock24 = true;
        public bool LaunchAtStartup = false;
        public bool HideTaskbar = true;      // hide the Windows taskbar while the bar runs (restored on exit) — NovaBar replaces it
        public bool ReserveSpace = false;    // register as AppBar so maximized windows don't cover it
        public bool HideOnFullscreen = true;
        public bool BarBackground = false;   // false = floating tiles only (no bar container behind the widgets)
        /// <summary>"shell" style: edge-attached solid bar, curved screen corners, panels fused to the bar (quickshell look).</summary>
        public bool IsShell { get { return string.Equals(Style, "shell", StringComparison.OrdinalIgnoreCase); } }
        /// <summary>A bar container is painted (bar background on, or the shell style).</summary>
        public bool SolidBar { get { return BarBackground || IsShell; } }
        public string Accent = "#7C8CFF";
        public string Font = "Segoe UI Variable Text";
        public int Monitor = 0;              // 0 = primary
        public string Units = "metric";      // metric | imperial
        public string City = "Riyadh";       // weather city
        public double Lat = 24.7136, Lon = 46.6753;
        public string Currency = "USD";
        public List<WidgetInstance> Widgets = new List<WidgetInstance>();
        public Dictionary<string, Dictionary<string, string>> Accounts = new Dictionary<string, Dictionary<string, string>>();
        public Dictionary<string, object> Extra = new Dictionary<string, object>();

        public static AppConfig Current = new AppConfig();
        public static event Action Changed;
        public static void RaiseChanged() { var c = Changed; if (c != null) c(); }

        // -------- load / save --------
        public static AppConfig Load()
        {
            var c = new AppConfig();
            try
            {
                if (File.Exists(Paths.ConfigFile))
                {
                    var d = J.ParseObj(File.ReadAllText(Paths.ConfigFile, Encoding.UTF8));
                    if (d != null) c.FromDict(d);
                }
                else c.Widgets = DefaultWidgets();
            }
            catch { c.Widgets = DefaultWidgets(); }
            return c;
        }

        public void Save()
        {
            try
            {
                var tmp = Paths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, J.Stringify(ToDict(), true), Encoding.UTF8);
                if (File.Exists(Paths.ConfigFile)) File.Replace(tmp, Paths.ConfigFile, null); else File.Move(tmp, Paths.ConfigFile);
            }
            catch { }
        }

        public static List<WidgetInstance> DefaultWidgets()
        {
            var l = new List<WidgetInstance>();
            Action<string, string> add = (id, zone) => l.Add(new WidgetInstance { Id = id, Inst = NewInst(id), Zone = zone });
            // taskbar replacement layout; ids missing from the build are skipped by the bar
            add("apps", "left");
            add("nowplaying-compact", "center");
            add("tray", "right"); add("language", "right"); add("network", "right"); add("audio", "right"); add("battery", "right");
            add("notifications", "right"); add("clock", "right"); add("showdesktop", "right");
            return Normalize(l);
        }
        /// <summary>Single-bar layout: keep the visual order (left → center → right) and put every widget in one list.</summary>
        public static List<WidgetInstance> Normalize(List<WidgetInstance> l)
        {
            Func<string, int> rank = z => z == "left" ? 0 : z == "center" ? 1 : 2;
            var sorted = l.Select((w, i) => new { w, i }).OrderBy(x => rank(string.IsNullOrEmpty(x.w.Zone) ? "right" : x.w.Zone)).ThenBy(x => x.i).Select(x => x.w).ToList();
            foreach (var w in sorted) w.Zone = "center";
            return sorted;
        }
        public static string NewInst(string id) { return id + "-" + Guid.NewGuid().ToString("N").Substring(0, 6); }

        public Dictionary<string, object> ToDict()
        {
            var d = new Dictionary<string, object>();
            d["theme"] = Theme; d["position"] = Position; d["style"] = Style; d["size"] = Size; d["clock24"] = Clock24;
            d["launchAtStartup"] = LaunchAtStartup; d["hideTaskbar"] = HideTaskbar; d["reserveSpace"] = ReserveSpace; d["hideOnFullscreen"] = HideOnFullscreen; d["barBackground"] = BarBackground;
            d["accent"] = Accent; d["font"] = Font; d["monitor"] = Monitor; d["units"] = Units; d["city"] = City; d["lat"] = Lat; d["lon"] = Lon; d["currency"] = Currency;
            d["widgets"] = Widgets.Select(w => (object)new Dictionary<string, object> { { "id", w.Id }, { "inst", w.Inst }, { "zone", w.Zone ?? "right" }, { "settings", w.Settings }, { "monitors", (w.Monitors ?? new List<string>()).ToArray() } }).ToArray();
            d["accounts"] = Accounts;
            d["extra"] = Extra;
            return d;
        }

        public void FromDict(Dictionary<string, object> d)
        {
            Theme = J.Str(d, "theme", Theme); Position = J.Str(d, "position", Position); Style = J.Str(d, "style", Style); Size = J.Str(d, "size", Size);
            Clock24 = J.Bool(d, "clock24", Clock24); LaunchAtStartup = J.Bool(d, "launchAtStartup", LaunchAtStartup);
            HideTaskbar = J.Bool(d, "hideTaskbar", HideTaskbar); ReserveSpace = J.Bool(d, "reserveSpace", ReserveSpace); HideOnFullscreen = J.Bool(d, "hideOnFullscreen", HideOnFullscreen); BarBackground = J.Bool(d, "barBackground", BarBackground);
            Accent = J.Str(d, "accent", Accent); Font = J.Str(d, "font", Font); Monitor = (int)J.Num(d, "monitor", Monitor); Units = J.Str(d, "units", Units);
            City = J.Str(d, "city", City); Lat = J.Num(d, "lat", Lat); Lon = J.Num(d, "lon", Lon); Currency = J.Str(d, "currency", Currency);
            Widgets = new List<WidgetInstance>();
            var arr = J.Arr(d, "widgets");
            if (arr != null) foreach (var o in arr)
                {
                    var w = new WidgetInstance { Id = J.Str(o, "id"), Inst = J.Str(o, "inst"), Zone = J.Str(o, "zone", "right") };
                    if (string.IsNullOrEmpty(w.Inst)) w.Inst = NewInst(w.Id);
                    var s = J.Obj(o, "settings"); if (s != null) w.Settings = s;
                    var mons = J.Arr(o, "monitors"); if (mons != null) foreach (var m in mons) { var ms = J.AsStr(m); if (ms.Length > 0) w.Monitors.Add(ms); }
                    if (!string.IsNullOrEmpty(w.Id)) Widgets.Add(w);
                }
            Widgets = Normalize(Widgets);
            Accounts = new Dictionary<string, Dictionary<string, string>>();
            var acc = J.Obj(d, "accounts");
            if (acc != null) foreach (var kv in acc)
                {
                    var f = kv.Value as Dictionary<string, object>; if (f == null) continue;
                    Accounts[kv.Key] = f.ToDictionary(x => x.Key, x => J.AsStr(x.Value));
                }
            var ex = J.Obj(d, "extra"); if (ex != null) Extra = ex;
        }
    }

    /// <summary>
    /// Account credentials (API keys, tokens, iCal links). Values are encrypted with Windows DPAPI
    /// (CurrentUser) so they never leave this PC in readable form.
    /// </summary>
    public static class Accounts
    {
        public class Field { public string Key, Label, Placeholder; public bool Secret = true; }
        public class Def { public string Id, Name, Help, HelpUrl; public Field[] Fields; }
        static readonly Dictionary<string, Def> defs = new Dictionary<string, Def>();
        public static IEnumerable<Def> All { get { return defs.Values.OrderBy(d => d.Name); } }

        /// <summary>Declare an account type (call from a widget class static constructor or WidgetRegistry init).</summary>
        public static void Define(string id, string name, string help, string helpUrl, params Field[] fields)
        {
            defs[id] = new Def { Id = id, Name = name, Help = help, HelpUrl = helpUrl, Fields = fields };
        }
        public static Field F(string key, string label, bool secret = true, string placeholder = "") { return new Field { Key = key, Label = label, Secret = secret, Placeholder = placeholder }; }
        public static Def GetDef(string id) { Def d; return defs.TryGetValue(id, out d) ? d : null; }

        public static string Get(string account, string field)
        {
            Dictionary<string, string> f;
            if (!AppConfig.Current.Accounts.TryGetValue(account, out f)) return null;
            string v; if (!f.TryGetValue(field, out v) || string.IsNullOrEmpty(v)) return null;
            return Unprotect(v);
        }
        public static void Set(AppConfig cfg, string account, string field, string value)
        {
            Dictionary<string, string> f;
            if (!cfg.Accounts.TryGetValue(account, out f)) cfg.Accounts[account] = f = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(value)) f.Remove(field); else f[field] = Protect(value);
        }
        /// <summary>True if every field of the account has a value.</summary>
        public static bool Connected(string account)
        {
            var d = GetDef(account);
            if (d == null) return false;
            foreach (var f in d.Fields) if (string.IsNullOrEmpty(Get(account, f.Key))) return false;
            return true;
        }

        public static string Protect(string s)
        {
            try { return "enc:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser)); }
            catch { return s; }
        }
        public static string Unprotect(string s)
        {
            if (s == null || !s.StartsWith("enc:")) return s;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s.Substring(4)), null, DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }
    }

    /// <summary>Small persistent JSON documents (todos, notes, laps, screen time…). One file per key.</summary>
    public static class StateStore
    {
        public static Dictionary<string, object> Load(string key)
        {
            try
            {
                var f = Path.Combine(Paths.StateDir, Safe(key) + ".json");
                if (File.Exists(f)) { var d = J.ParseObj(File.ReadAllText(f, Encoding.UTF8)); if (d != null) return d; }
            }
            catch { }
            return new Dictionary<string, object>();
        }
        public static void Save(string key, Dictionary<string, object> data)
        {
            try { File.WriteAllText(Path.Combine(Paths.StateDir, Safe(key) + ".json"), J.Stringify(data), Encoding.UTF8); } catch { }
        }
        static string Safe(string k) { foreach (var c in Path.GetInvalidFileNameChars()) k = k.Replace(c, '_'); return k; }
    }
}
