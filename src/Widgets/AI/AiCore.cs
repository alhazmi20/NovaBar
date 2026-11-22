// AI usage widgets — shared model, formatting, brand colours, path discovery and incremental file reading.
// All local-log samplers only read bytes appended since the last pass (per-file offset + length + mtime).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NovaBar
{
    /// <summary>One limit window (5-hour, weekly, daily…). Frac &lt; 0 = unknown.</summary>
    public class AiWin
    {
        public string Label; public double Frac = -1; public DateTime Reset; public string Detail;
        public AiWin(string label, double frac, DateTime reset, string detail) { Label = label; Frac = frac; Reset = reset; Detail = detail; }
    }
    public class AiRow { public string Name; public double Tokens; public int Count; public double Cost; }
    public class AiStat { public string Label, Value; public AiStat(string l, string v) { Label = l; Value = v; } }

    /// <summary>Display model for one provider (built from a hub snapshot + per-widget options).</summary>
    public class AiProv
    {
        public string Key, Name, Source;
        public bool IsSample, NeedsAccount;
        public string Note;
        public List<AiWin> Wins = new List<AiWin>();
        public List<AiStat> Stats = new List<AiStat>();
        public double[] Week = new double[7];      // oldest .. today
        public string WeekLabel = "Tokens per day"; public bool WeekMoney; public string Cur = "$";
        public List<AiRow> Models = new List<AiRow>();
        public string ModelsLabel = "Models · 7 days";
        public string Big, Small;                  // tile headline when there is no limit window (balances, token counts)
        public DateTime Updated;
    }

    /// <summary>Base snapshot with value equality via a signature string (Hub only repaints on change).</summary>
    public class AiSnap
    {
        public string Sig = "";
        public override bool Equals(object o) { var s = o as AiSnap; return s != null && s.GetType() == GetType() && s.Sig == Sig; }
        public override int GetHashCode() { return Sig.GetHashCode(); }
    }

    /// <summary>Settings shared by the global samplers (set by widgets from their per-instance settings).</summary>
    public static class AiCfg
    {
        public static volatile bool ScanWsl = true;
        public static volatile string ClaudeExtra = "", CodexExtra = "", GeminiExtra = "", OpenCodeExtra = "";
        public static volatile string GlmRegion = "Global (z.ai)";
    }

    public static class AiFmt
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string Tok(double n)
        {
            double a = Math.Abs(n);
            if (a >= 1e9) return (n / 1e9).ToString(a >= 1e10 ? "0" : "0.#", Inv) + "B";
            if (a >= 1e6) return (n / 1e6).ToString(a >= 1e7 ? "0" : "0.#", Inv) + "M";
            if (a >= 1e3) return (n / 1e3).ToString(a >= 1e4 ? "0" : "0.#", Inv) + "K";
            return Math.Round(n).ToString("0", Inv);
        }
        public static string Money(double v, string cur)
        {
            if (string.IsNullOrEmpty(cur)) cur = "$";
            return (v < 0 ? "-" : "") + cur + Math.Abs(v).ToString(Math.Abs(v) >= 1000 ? "#,0" : "#,0.00", Inv);
        }
        public static string Pct(double f) { return f < 0 ? "—" : Math.Round(Math.Max(0, f) * 100).ToString("0", Inv) + "%"; }
        /// <summary>"2h 14m", "3d 4h", "12m", "now".</summary>
        public static string Left(DateTime resetUtc)
        {
            if (resetUtc == DateTime.MinValue) return "";
            var t = resetUtc - DateTime.UtcNow;
            if (t.TotalSeconds <= 30) return "now";
            if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            return Math.Max(1, (int)Math.Ceiling(t.TotalMinutes)) + "m";
        }
        public static string Resets(DateTime resetUtc) { var l = Left(resetUtc); return l == "" ? "" : l == "now" ? "resets now" : "resets " + l; }
        public static string Ago(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "never";
            var t = DateTime.UtcNow - utc;
            if (t.TotalMinutes < 1) return "just now";
            if (t.TotalHours < 1) return (int)t.TotalMinutes + "m ago";
            if (t.TotalDays < 1) return (int)t.TotalHours + "h ago";
            return (int)t.TotalDays + "d ago";
        }
        public static DateTime ParseUtc(string s)
        {
            DateTime d;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)) return d;
            return DateTime.MinValue;
        }
        public static DateTime FromEpoch(double sec) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sec); }
        public static DateTime FromEpochMs(double ms) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms); }
        public static string N(double v) { return v.ToString("0.####", Inv); }

        /// <summary>"claude-opus-5-5" → "Opus 5.5", "gpt-5-codex" → "GPT-5 Codex", "gemini-2.5-pro" → "Gemini 2.5 Pro".</summary>
        public static string Model(string m)
        {
            if (string.IsNullOrEmpty(m)) return "Unknown";
            var s = m; int slash = s.LastIndexOf('/'); if (slash >= 0) s = s.Substring(slash + 1);
            var parts = s.Split('-', '_').Where(p => p.Length > 0 && !(p.Length == 8 && p.All(char.IsDigit))).ToList();
            if (parts.Count > 0 && parts[0].Equals("claude", StringComparison.OrdinalIgnoreCase))
            {
                parts.RemoveAt(0);
                var words = parts.Where(p => !char.IsDigit(p[0])).ToList(); var nums = parts.Where(p => char.IsDigit(p[0])).ToList();
                return string.Join(" ", words.Select(Cap)) + (nums.Count > 0 ? " " + string.Join(".", nums) : "");
            }
            if (parts.Count > 0 && parts[0].Equals("gpt", StringComparison.OrdinalIgnoreCase) && parts.Count > 1)
                return "GPT-" + parts[1] + (parts.Count > 2 ? " " + string.Join(" ", parts.Skip(2).Select(Cap)) : "");
            return string.Join(" ", parts.Select(Cap));
        }
        static string Cap(string p) { return p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1); }
    }

    public static class AiBrand
    {
        public static Color Of(string key, Theme t)
        {
            switch (key)
            {
                case "claude": return Color.FromArgb(217, 119, 87);
                case "chatgpt": return t.Dark ? Color.FromArgb(25, 195, 145) : Color.FromArgb(16, 163, 127);
                case "gemini": return Color.FromArgb(79, 140, 247);
                case "grok": return t.Dark ? Color.FromArgb(229, 229, 229) : Color.FromArgb(17, 17, 17);
                case "deepseek": return t.Dark ? Color.FromArgb(110, 135, 255) : Color.FromArgb(77, 107, 254);
                case "glm": return t.Dark ? Color.FromArgb(104, 132, 255) : Color.FromArgb(59, 91, 255);
                case "opencode": return t.Dark ? Color.FromArgb(250, 178, 90) : Color.FromArgb(214, 128, 24);
                default: return t.TextDim; // cursor: neutral
            }
        }
        /// <summary>Usage colour: brand until it gets tight, then warn / bad.</summary>
        public static Color Usage(string key, double frac, Theme t)
        {
            if (frac >= 0.9) return t.Bad; if (frac >= 0.75) return t.Warn; return Of(key, t);
        }
    }

    /// <summary>Discovers tool data folders on Windows and (cheaply) inside running WSL distros.</summary>
    public static class AiPaths
    {
        public static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
        static List<string> wslHomes = new List<string>(); static DateTime wslAt = DateTime.MinValue; static readonly object lk = new object();

        /// <summary>Home folders inside WSL distros (\\wsl.localhost\Distro\home\user, \root). Only probed while the WSL VM is
        /// already running (vmmemWSL), so we never boot WSL just to look. Cached for 10 minutes.</summary>
        public static List<string> WslHomes()
        {
            lock (lk)
            {
                if (!AiCfg.ScanWsl) return new List<string>();
                if ((DateTime.UtcNow - wslAt).TotalMinutes < 10) return wslHomes;
                wslAt = DateTime.UtcNow;
                var res = new List<string>();
                try
                {
                    bool running = false;
                    foreach (var n in new[] { "vmmemWSL", "vmmem" }) { var ps = Process.GetProcessesByName(n); if (ps.Length > 0) running = true; foreach (var p in ps) p.Dispose(); if (running) break; }
                    if (running)
                    {
                        foreach (var d in Distros())
                        {
                            var root = @"\\wsl.localhost\" + d;
                            try
                            {
                                var home = Path.Combine(root, "home");
                                if (Directory.Exists(home)) res.AddRange(Directory.GetDirectories(home));
                                res.Add(Path.Combine(root, "root"));
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                wslHomes = res; return res;
            }
        }
        static IEnumerable<string> Distros()
        {
            var l = new List<string>();
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss"))
                    if (k != null) foreach (var sub in k.GetSubKeyNames())
                            using (var s = k.OpenSubKey(sub))
                            {
                                var n = s == null ? null : s.GetValue("DistributionName") as string;
                                if (!string.IsNullOrEmpty(n) && !n.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)) l.Add(n);
                            }
            }
            catch { }
            return l;
        }

        /// <summary>Existing folders for a tool: %USERPROFILE%\sub, the same under WSL homes, env override, extra user paths.</summary>
        public static List<string> Find(string[] subs, string envVar, string extra)
        {
            var c = new List<string>();
            if (!string.IsNullOrEmpty(envVar)) { var e = Environment.GetEnvironmentVariable(envVar); if (!string.IsNullOrEmpty(e)) c.Add(e); }
            foreach (var s in subs) c.Add(Path.Combine(Home, s));
            foreach (var h in WslHomes()) foreach (var s in subs) c.Add(Path.Combine(h, s));
            if (!string.IsNullOrEmpty(extra)) foreach (var p in extra.Split(';', '|')) if (p.Trim().Length > 0) c.Add(Environment.ExpandEnvironmentVariables(p.Trim()));
            var res = new List<string>();
            foreach (var p in c.Distinct(StringComparer.OrdinalIgnoreCase)) { try { if (Directory.Exists(p)) res.Add(p); } catch { } }
            return res;
        }

        /// <summary>Files under dir matching pattern written since cutoff (one directory walk, no extra stat calls).</summary>
        public static List<FileInfo> Recent(string dir, string pattern, DateTime cutoffUtc)
        {
            var l = new List<FileInfo>();
            try
            {
                var di = new DirectoryInfo(dir); if (!di.Exists) return l;
                foreach (var f in di.EnumerateFiles(pattern, SearchOption.AllDirectories)) if (f.LastWriteTimeUtc >= cutoffUtc) l.Add(f);
            }
            catch { }
            return l;
        }
    }

    /// <summary>Per-file read cursor.</summary>
    public class AiCursor { public long Offset, Len; public DateTime Mtime; public string Tag; public object Data; }

    public static class AiIo
    {
        const long MaxChunk = 48L << 20;
        /// <summary>Feeds complete lines appended since the cursor. Returns false if the file was truncated/replaced (cursor reset → caller should drop data for it).</summary>
        public static bool ReadNew(FileInfo fi, AiCursor cur, Action<string> onLine)
        {
            bool intact = true;
            if (fi.Length == cur.Len && fi.LastWriteTimeUtc == cur.Mtime) return true;
            if (fi.Length < cur.Offset) { cur.Offset = 0; intact = false; }
            try
            {
                using (var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
                {
                    long len = fs.Length, start = cur.Offset;
                    if (len - start > MaxChunk) start = len - MaxChunk;   // huge backlog: only the tail matters
                    if (len > start)
                    {
                        fs.Seek(start, SeekOrigin.Begin);
                        var buf = new byte[len - start]; int got = 0;
                        while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                        int last = Array.LastIndexOf(buf, (byte)'\n', got - 1);
                        if (last >= 0)
                        {
                            int ls = 0;
                            for (int i = 0; i <= last; i++)
                                if (buf[i] == '\n')
                                {
                                    if (i - ls > 2) { try { onLine(Encoding.UTF8.GetString(buf, ls, i - ls)); } catch { } }
                                    ls = i + 1;
                                }
                            cur.Offset = start + last + 1;
                        }
                    }
                    cur.Len = fi.Length; cur.Mtime = fi.LastWriteTimeUtc;
                }
            }
            catch { }
            return intact;
        }
        public static string ReadAll(string path)
        {
            try { using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var r = new StreamReader(fs, Encoding.UTF8)) return r.ReadToEnd(); }
            catch { return null; }
        }

        // ---- cheap field extraction from a JSON line without full parsing (Claude lines can be 100s of KB) ----
        public static string StrAfter(string s, string key, int from = 0)
        {
            int i = s.IndexOf("\"" + key + "\":\"", from, StringComparison.Ordinal); if (i < 0) return null;
            i += key.Length + 4; int j = s.IndexOf('"', i); return j < 0 ? null : s.Substring(i, j - i);
        }
        public static string LastStr(string s, string key)
        {
            int i = s.LastIndexOf("\"" + key + "\":\"", StringComparison.Ordinal); if (i < 0) return null;
            i += key.Length + 4; int j = s.IndexOf('"', i); return j < 0 ? null : s.Substring(i, j - i);
        }
        public static long NumAfter(string s, string key, int from, int until)
        {
            int i = s.IndexOf("\"" + key + "\":", from, StringComparison.Ordinal); if (i < 0 || (until > 0 && i > until)) return 0;
            i += key.Length + 3; long v = 0; while (i < s.Length && s[i] == ' ') i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { v = v * 10 + (s[i] - '0'); i++; }
            return v;
        }
    }

    /// <summary>Day-bucket helpers (local days, 7 bins ending today).</summary>
    public static class AiDays
    {
        public static int Index(DateTime utc)
        {
            var d = (DateTime.Now.Date - utc.ToLocalTime().Date).Days; return d < 0 || d > 6 ? -1 : 6 - d;
        }
        public static string[] Labels()
        {
            var l = new string[7]; for (int i = 0; i < 7; i++) l[i] = DateTime.Now.Date.AddDays(i - 6).ToString("ddd", AiFmt.Inv).Substring(0, 2); return l;
        }
    }
}
