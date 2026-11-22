// Screen Time — foreground-app tracker (Hub key "prod.screentime", 5 s sampling) + widget.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>One day of screen time. Seconds per hour, per app, app kinds, and 288 five-minute slots of the dominant kind ('.' = away).</summary>
    public class ProdDayRec
    {
        public DateTime Day;
        public int[] Hours = new int[24];
        public Dictionary<string, int> Apps = new Dictionary<string, int>();
        public Dictionary<string, char> Kinds = new Dictionary<string, char>();
        public char[] Slots = Enumerable.Repeat('.', 288).ToArray();
        public int Total { get { return Hours.Sum(); } }
        public ProdDayRec Clone()
        {
            return new ProdDayRec { Day = Day, Hours = (int[])Hours.Clone(), Apps = new Dictionary<string, int>(Apps), Kinds = new Dictionary<string, char>(Kinds), Slots = (char[])Slots.Clone() };
        }
        /// <summary>Seconds per kind index (KindChars order).</summary>
        public int[] ByKind()
        {
            var k = new int[6];
            foreach (var a in Apps) { char c; if (!Kinds.TryGetValue(a.Key, out c)) c = 'o'; k[ProdUtil.KindIndex(c)] += a.Value; }
            return k;
        }
    }

    /// <summary>Immutable snapshot published on the Hub. A new instance only when the minute total / slots change.</summary>
    public class ProdScreenSnap
    {
        public ProdDayRec Today;
        public bool Active;
        public string CurrentApp;
    }

    public static class ProdScreenTracker
    {
        public const string Key = "prod.screentime";
        const string StateKeyName = "prod.screentime";
        const int KeepDays = 35, IdleLimitMs = 120000;
        static readonly object lk = new object();
        static Dictionary<DateTime, ProdDayRec> days;
        static int[,] slotCounts = new int[288, 6]; static DateTime slotDay;
        static DateTime lastSample = DateTime.MinValue, lastSave = DateTime.Now;
        static bool dirty, locked;
        static ProdScreenSnap snap;
        static bool hooked;

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        delegate bool EnumChildProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChildProc cb, IntPtr l);

        class ProcInfo { public string Name, Path; }
        static readonly Dictionary<uint, ProcInfo> pidCache = new Dictionary<uint, ProcInfo>();
        static readonly Dictionary<string, string> friendlyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static DateTime pidCacheReset = DateTime.Now;

        public static void Define()
        {
            Hub.Define(Key, 5000, Sample);
            if (!hooked)
            {
                hooked = true;
                try { Microsoft.Win32.SystemEvents.SessionSwitch += (s, e) => { if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) locked = true; else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) locked = false; }; } catch { }
            }
        }

        // ------------- persistence -------------
        static void Load()
        {
            if (days != null) return;
            days = new Dictionary<DateTime, ProdDayRec>();
            try
            {
                var st = StateStore.Load(StateKeyName);
                var dd = J.Obj(st, "days");
                if (dd != null) foreach (var kv in dd)
                    {
                        DateTime d; if (!DateTime.TryParseExact(kv.Key, "yyyy-MM-dd", ProdUtil.Inv, System.Globalization.DateTimeStyles.None, out d)) continue;
                        var rec = new ProdDayRec { Day = d };
                        var h = J.Arr(kv.Value, "h"); if (h != null) for (int i = 0; i < 24 && i < h.Count; i++) rec.Hours[i] = (int)J.AsNum(h[i]);
                        var a = J.Obj(kv.Value, "a"); if (a != null) foreach (var x in a) rec.Apps[x.Key] = (int)J.AsNum(x.Value);
                        var k = J.Obj(kv.Value, "k"); if (k != null) foreach (var x in k) { var s = J.AsStr(x.Value); if (s.Length > 0) rec.Kinds[x.Key] = s[0]; }
                        var sl = J.Str(kv.Value, "s"); if (sl.Length == 288) rec.Slots = sl.ToCharArray();
                        days[d] = rec;
                    }
            }
            catch { }
        }
        static void Save()
        {
            Dictionary<string, object> outD;
            lock (lk)
            {
                if (days == null) return;
                var dd = new Dictionary<string, object>();
                foreach (var r in days.Values)
                    dd[r.Day.ToString("yyyy-MM-dd")] = new Dictionary<string, object> {
                        { "h", r.Hours.Cast<object>().ToArray() },
                        { "a", r.Apps.ToDictionary(x => x.Key, x => (object)x.Value) },
                        { "k", r.Kinds.ToDictionary(x => x.Key, x => (object)x.Value.ToString()) },
                        { "s", new string(r.Slots) } };
                outD = new Dictionary<string, object> { { "v", 1 }, { "days", dd } };
                dirty = false; lastSave = DateTime.Now;
            }
            StateStore.Save(StateKeyName, outD);
        }
        /// <summary>Persist now (called when a subscribing widget stops).</summary>
        public static void Flush() { if (dirty) Save(); }

        // ------------- queries (cheap, in-memory copies) -------------
        /// <summary>True while fewer than 10 minutes were ever recorded (first run): widgets show a sample day instead of empty charts.</summary>
        public static bool FewData { get { lock (lk) { Load(); int t = 0; foreach (var r in days.Values) { t += r.Total; if (t >= 600) return false; } return true; } } }
        public static ProdDayRec GetDay(DateTime d)
        {
            lock (lk) { Load(); ProdDayRec r; return days.TryGetValue(d.Date, out r) ? r.Clone() : new ProdDayRec { Day = d.Date }; }
        }
        /// <summary>Totals per day for [from, from+n).</summary>
        public static int[] Totals(DateTime from, int n)
        {
            var t = new int[n];
            lock (lk) { Load(); for (int i = 0; i < n; i++) { ProdDayRec r; if (days.TryGetValue(from.Date.AddDays(i), out r)) t[i] = r.Total; } }
            return t;
        }
        /// <summary>Per-app totals across a date range, sorted desc.</summary>
        public static List<KeyValuePair<string, int>> AppsRange(DateTime from, int n, out Dictionary<string, char> kinds)
        {
            var acc = new Dictionary<string, int>(); kinds = new Dictionary<string, char>();
            lock (lk)
            {
                Load();
                for (int i = 0; i < n; i++)
                {
                    ProdDayRec r; if (!days.TryGetValue(from.Date.AddDays(i), out r)) continue;
                    foreach (var a in r.Apps) { int v; acc.TryGetValue(a.Key, out v); acc[a.Key] = v + a.Value; }
                    foreach (var k in r.Kinds) kinds[k.Key] = k.Value;
                }
            }
            return acc.OrderByDescending(x => x.Value).ToList();
        }

        // ------------- sampling -------------
        static object Sample()
        {
            var now = DateTime.Now;
            bool active = false; string app = null;
            lock (lk)
            {
                Load();
                double elapsed = lastSample == DateTime.MinValue ? 5 : (now - lastSample).TotalSeconds;
                if (elapsed > 15 || elapsed < 0) elapsed = 5; // after sleep / clock change count one sample
                lastSample = now;

                if (!locked && Native.IdleMs() < IdleLimitMs)
                {
                    char kind; string friendly;
                    if (Foreground(out friendly, out kind))
                    {
                        active = true; app = friendly;
                        var rec = Day(now.Date);
                        int sec = (int)Math.Round(elapsed);
                        rec.Hours[now.Hour] += sec;
                        int v; rec.Apps.TryGetValue(friendly, out v); rec.Apps[friendly] = v + sec;
                        rec.Kinds[friendly] = kind;
                        if (slotDay != now.Date) { slotCounts = new int[288, 6]; slotDay = now.Date; for (int i = 0; i < 288; i++) if (rec.Slots[i] != '.') slotCounts[i, ProdUtil.KindIndex(rec.Slots[i])] = 1; }
                        int slot = (now.Hour * 60 + now.Minute) / 5, ki = ProdUtil.KindIndex(kind);
                        slotCounts[slot, ki] += sec;
                        int best = 0; for (int i = 1; i < 6; i++) if (slotCounts[slot, i] > slotCounts[slot, best]) best = i;
                        rec.Slots[slot] = ProdUtil.KindChars[best];
                        dirty = true;
                    }
                }
                // prune
                if (days.Count > KeepDays) { var cut = now.Date.AddDays(-KeepDays + 1); foreach (var k in days.Keys.Where(d => d < cut).ToList()) days.Remove(k); dirty = true; }
                if ((now - pidCacheReset).TotalMinutes > 60) { pidCache.Clear(); pidCacheReset = now; }
            }
            if (dirty && (now - lastSave).TotalSeconds >= 60) Save();

            ProdDayRec today; lock (lk) { ProdDayRec r; today = days.TryGetValue(now.Date, out r) ? r : null; }
            int mins = today == null ? 0 : today.Total / 60;
            var s = snap;
            bool changed = s == null || s.Today.Day != now.Date || s.Today.Total / 60 != mins || s.Active != active || s.CurrentApp != app;
            if (!changed && today != null) changed = !s.Today.Slots.SequenceEqual(today.Slots);
            if (changed)
            {
                ProdDayRec copy; lock (lk) copy = today != null ? today.Clone() : new ProdDayRec { Day = now.Date };
                snap = new ProdScreenSnap { Today = copy, Active = active, CurrentApp = app };
            }
            return snap;
        }
        static ProdDayRec Day(DateTime d)
        {
            ProdDayRec r; if (!days.TryGetValue(d, out r)) days[d] = r = new ProdDayRec { Day = d };
            return r;
        }

        /// <summary>Foreground window → friendly app name + kind. False when nothing meaningful is in front (lock screen, desktop gone).</summary>
        static bool Foreground(out string friendly, out char kind)
        {
            friendly = null; kind = 'o';
            var h = Native.GetForegroundWindow(); if (h == IntPtr.Zero) return false;
            uint pid; Native.GetWindowThreadProcessId(h, out pid); if (pid == 0) return false;
            var pi = Proc(pid); if (pi == null) return false;
            if (pi.Name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                // UWP app hosted in a frame: the real app owns a child window
                uint inner = 0;
                EnumChildWindows(h, (c, l) => { uint p; Native.GetWindowThreadProcessId(c, out p); if (p != pid && p != 0) { inner = p; return false; } return true; }, IntPtr.Zero);
                if (inner != 0) { var ip = Proc(inner); if (ip != null) pi = ip; }
            }
            var n = pi.Name.ToLowerInvariant();
            if (n == "lockapp" || n == "logonui" || n == "screenclippinghost") return false;
            if (n == "explorer" && Native.WindowClass(h) == "WorkerW") { friendly = "Desktop"; kind = 'o'; return true; }
            friendly = Friendly(pi); kind = ProdUtil.Classify(pi.Name, pi.Path);
            return true;
        }
        static ProcInfo Proc(uint pid)
        {
            ProcInfo pi; if (pidCache.TryGetValue(pid, out pi)) return pi;
            string path = null;
            var hp = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
            if (hp != IntPtr.Zero)
            {
                try { var sb = new StringBuilder(1024); int sz = sb.Capacity; if (QueryFullProcessImageName(hp, 0, sb, ref sz)) path = sb.ToString(); }
                finally { CloseHandle(hp); }
            }
            string name = path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : null;
            if (name == null) { try { using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) name = p.ProcessName; } catch { return null; } }
            pi = new ProcInfo { Name = name, Path = path };
            pidCache[pid] = pi; return pi;
        }
        static string Friendly(ProcInfo pi)
        {
            string f; if (friendlyCache.TryGetValue(pi.Name, out f)) return f;
            f = null;
            try
            {
                string path = pi.Path;
                if (path == null) { var ps = System.Diagnostics.Process.GetProcessesByName(pi.Name); if (ps.Length > 0) { try { path = ps[0].MainModule.FileName; } catch { } foreach (var p in ps) p.Dispose(); } }
                if (path != null)
                {
                    var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                    var d = (vi.FileDescription ?? "").Trim();
                    if (d.Length >= 2 && d.Length <= 40 && !d.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) f = d;
                    else if (!string.IsNullOrEmpty(vi.ProductName) && vi.ProductName.Length <= 40) f = vi.ProductName.Trim();
                }
            }
            catch { }
            if (string.IsNullOrEmpty(f)) f = pi.Name.Length > 0 ? char.ToUpper(pi.Name[0]) + pi.Name.Substring(1) : "Unknown";
            // tidy common verbose descriptions
            if (f == "Windows Explorer") f = "File Explorer";
            if (f.StartsWith("Microsoft Edge", StringComparison.Ordinal)) f = "Microsoft Edge";
            friendlyCache[pi.Name] = f; return f;
        }

        /// <summary>Realistic sample day (used when the tracker has no data yet, e.g. the very first minutes / render harness).</summary>
        public static ProdDayRec SampleDay(DateTime day)
        {
            var r = new ProdDayRec { Day = day.Date };
            var apps = new[] { new { n = "Visual Studio Code", k = 'w', s = 9600 }, new { n = "Google Chrome", k = 'b', s = 6200 }, new { n = "Slack", k = 'c', s = 2900 }, new { n = "Windows Terminal", k = 'w', s = 2400 }, new { n = "Spotify", k = 'm', s = 1300 }, new { n = "Figma", k = 'w', s = 1100 }, new { n = "File Explorer", k = 'o', s = 600 } };
            foreach (var a in apps) { r.Apps[a.n] = a.s; r.Kinds[a.n] = a.k; }
            int[] hrs = { 0, 0, 0, 0, 0, 0, 0, 0, 1500, 3100, 3300, 2800, 1200, 2600, 3400, 3200, 2500, 1300, 300, 0, 900, 1400, 600, 0 };
            int nowH = day.Date == DateTime.Today ? DateTime.Now.Hour : 24;
            for (int i = 0; i < 24; i++) r.Hours[i] = i <= nowH ? hrs[i] : 0;
            // scale per-app totals to the hours elapsed so the numbers agree
            double scale = r.Hours.Sum() / (double)Math.Max(1, apps.Sum(a => a.s));
            foreach (var a in apps) r.Apps[a.n] = (int)(a.s * scale);
            string pattern = "wwwbwwcwwwbbwwwwcwwbbmwwwwwwowwcwwwbbwww";
            int nowSlot = day.Date == DateTime.Today ? (DateTime.Now.Hour * 60 + DateTime.Now.Minute) / 5 : 288;
            for (int i = 0; i < 288 && i <= nowSlot; i++)
            {
                int h = i / 12; if (hrs[h] == 0) continue;
                if (hrs[h] < 2000 && (i % 12) > 6) continue;
                r.Slots[i] = h >= 20 ? (i % 5 == 0 ? 'b' : 'm') : pattern[(i * 7 + h) % pattern.Length];
            }
            return r;
        }
    }

    [Widget("screentime", "Screen Time", "Productivity", "Today's time in front of the PC, hour by hour, and every app's, by day and by week.", RefName = "Screen Time", Data = DataKind.Live)]
    public class ProdScreenTimeWidget : Widget
    {
        ProdScreenSnap snap;
        int view; // 0 day, 1 week
        int dayOffset; // 0 = today, -1 yesterday...

        public override void OnStart() { ProdScreenTracker.Define(); Hub.Subscribe(ProdScreenTracker.Key, this); }
        public override void OnStop() { Hub.Unsubscribe(ProdScreenTracker.Key, this); ProdScreenTracker.Flush(); }

        ProdDayRec Today(out bool sample)
        {
            var s = Hub.Get<ProdScreenSnap>(ProdScreenTracker.Key);
            sample = false;
            if (ProdScreenTracker.FewData) { sample = true; return ProdScreenTracker.SampleDay(DateTime.Today); }
            return s != null ? s.Today : ProdScreenTracker.GetDay(DateTime.Today);
        }

        public override int Measure(DrawCtx c) { return c.Si(150); }
        public override void Draw(DrawCtx c)
        {
            bool sample; var d = Today(out sample);
            var b = c.Bounds; float x = b.X + c.S(10);
            Ui.Icon(c.G, "", new RectangleF(x, b.Y, c.S(18), b.Height), c.T.Accent, c.S(15)); // hourglass-ish (stopwatch) glyph
            x += c.S(24);
            float textW = c.S(56);
            Ui.Text(c.G, ProdUtil.Dur(d.Total), c.FS(13), c.T.Text, new RectangleF(x, b.Y + c.S(3), textW + c.S(10), c.S(16)));
            Ui.Text(c.G, sample ? "sample" : "today", c.F(10), sample ? c.T.Warn : c.T.TextDim, new RectangleF(x, b.Y + c.S(18), textW, c.S(13)));
            // hour bars 7..23 (waking hours) — compact
            var br = new RectangleF(x + textW + c.S(2), b.Y + c.S(8), b.Right - c.S(10) - (x + textW + c.S(2)), b.Height - c.S(16));
            int from = 6; var vals = new List<double>(); for (int i = from; i < 24; i++) vals.Add(d.Hours[i]);
            int hl = DateTime.Now.Hour - from;
            Ui.Bars(c.G, br, vals, Ui.WithA(c.T.Accent, 120), c.S(1.5f), 3600, hl, c.T.Accent);
        }

        public override Size PanelSize(float s) { return new Size((int)(360 * s), (int)(430 * s)); }
        public override void OnPanelOpen() { dayOffset = 0; }

        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var g = c.G;
            bool sample; var today = Today(out sample);
            c.PanelHeader("Screen Time", null, "");
            // view tabs (right of header)
            float tw = c.S(54), th = c.S(22), ty = b.Y + c.S(12);
            var tWeek = new RectangleF(b.Right - c.S(14) - tw, ty, tw, th); var tDay = new RectangleF(tWeek.X - tw - c.S(2), ty, tw, th);
            Ui.FillRound(g, new RectangleF(tDay.X - c.S(2), ty - c.S(2), tw * 2 + c.S(6), th + c.S(4)), c.S(8), c.T.Tile);
            Ui.FillRound(g, view == 0 ? tDay : tWeek, c.S(7), c.T.Accent);
            Ui.Text(g, "Day", c.FS(11.5f), view == 0 ? Color.White : c.T.TextDim, tDay, 0); c.Hit(tDay, () => { view = 0; Invalidate(); });
            Ui.Text(g, "Week", c.FS(11.5f), view == 1 ? Color.White : c.T.TextDim, tWeek, 0); c.Hit(tWeek, () => { view = 1; Invalidate(); });
            if (sample) ProdUtil.SampleBadge(c, tDay.X - c.S(8), ty + th / 2);

            float x = b.X + c.S(16), w = b.Width - c.S(32), y = b.Y + c.S(46);
            List<KeyValuePair<string, int>> apps; Dictionary<string, char> kinds;
            if (view == 0)
            {
                var day = dayOffset == 0 ? today : (sample ? ProdScreenTracker.SampleDay(DateTime.Today.AddDays(dayOffset)) : ProdScreenTracker.GetDay(DateTime.Today.AddDays(dayOffset)));
                // nav row
                var prev = new RectangleF(x - c.S(4), y, c.S(24), c.S(24)); var next = new RectangleF(b.Right - c.S(12) - c.S(24), y, c.S(24), c.S(24));
                c.IconButton(prev, Icons.ChevronLeft, () => { if (dayOffset > -34) dayOffset--; Invalidate(); }, false, 11);
                if (dayOffset < 0) c.IconButton(next, Icons.ChevronRight, () => { dayOffset++; Invalidate(); }, false, 11);
                Ui.Text(g, ProdUtil.DayLabel(DateTime.Today.AddDays(dayOffset)), c.F(11.5f), c.T.TextDim, new RectangleF(prev.Right + c.S(4), y, w, c.S(24)));
                y += c.S(28);
                Ui.Text(g, ProdUtil.Dur(day.Total), c.F(26, true), c.T.Text, new RectangleF(x, y, w, c.S(32)));
                // compare with the 7-day average
                var tot = ProdScreenTracker.Totals(DateTime.Today.AddDays(dayOffset - 7), 7); var nz = tot.Where(t => t > 0).ToList();
                if (nz.Count > 0 && !sample)
                {
                    double avg = nz.Average(); double diff = day.Total - avg;
                    Ui.Text(g, (diff >= 0 ? "▲ " : "▼ ") + ProdUtil.Dur(Math.Abs(diff)) + " vs 7-day avg", c.F(11), diff >= 0 ? c.T.Warn : c.T.Good, new RectangleF(x, y, w, c.S(32)), 1);
                }
                else if (sample) Ui.Text(g, "Tracking starts now", c.F(11), c.T.TextDim, new RectangleF(x, y, w, c.S(32)), 1);
                y += c.S(38);
                // hour bars 0..23
                var chart = new RectangleF(x, y, w, c.S(70));
                using (var p = new Pen(c.T.Track, 1f)) { g.DrawLine(p, chart.X, chart.Bottom + 0.5f, chart.Right, chart.Bottom + 0.5f); g.DrawLine(p, chart.X, chart.Y, chart.Right, chart.Y); }
                Ui.Text(g, "60m", c.F(9), c.T.TextFaint, new RectangleF(chart.Right - c.S(30), chart.Y - c.S(12), c.S(30), c.S(11)), 1);
                var vals = day.Hours.Select(h => (double)h).ToList();
                Ui.Bars(g, chart, vals, Ui.WithA(c.T.Accent, 140), c.S(2.5f), 3600, dayOffset == 0 ? DateTime.Now.Hour : -1, c.T.Accent);
                y = chart.Bottom + c.S(3);
                for (int h = 0; h <= 24; h += 6)
                {
                    float hx = chart.X + chart.Width * h / 24f;
                    Ui.Text(g, h == 24 ? "" : (h.ToString("00") + ":00"), c.F(9), c.T.TextFaint, new RectangleF(hx - (h == 0 ? 0 : c.S(18)), y, c.S(36), c.S(12)), h == 0 ? -1 : 0);
                }
                y += c.S(20);
                apps = day.Apps.OrderByDescending(a => a.Value).ToList(); kinds = day.Kinds;
                KindStrip(c, day.ByKind(), new RectangleF(x, y, w, c.S(6))); y += c.S(30);
            }
            else
            {
                var start = DateTime.Today.AddDays(-6);
                var tot = sample ? new[] { 22100, 25400, 19800, 27100, 23900, 9600, today.Total } : ProdScreenTracker.Totals(start, 7);
                if (!sample) tot[6] = today.Total;
                int total = tot.Sum(); int nzc = Math.Max(1, tot.Count(t => t > 0));
                Ui.Text(g, ProdUtil.Dur(total / (double)nzc), c.F(26, true), c.T.Text, new RectangleF(x, y + c.S(2), w, c.S(32)));
                Ui.Text(g, "daily average · " + ProdUtil.Dur(total) + " total", c.F(11), c.T.TextDim, new RectangleF(x, y + c.S(2), w, c.S(32)), 1);
                y += c.S(44);
                var chart = new RectangleF(x, y, w, c.S(84));
                double mx = Math.Max(3600 * 4, tot.Max());
                using (var p = new Pen(c.T.Track, 1f)) g.DrawLine(p, chart.X, chart.Bottom + 0.5f, chart.Right, chart.Bottom + 0.5f);
                // average line
                float ay = chart.Bottom - (float)(total / (double)nzc / mx) * chart.Height;
                using (var p = new Pen(Ui.WithA(c.T.Good, 160), c.S(1)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawLine(p, chart.X, ay, chart.Right, ay);
                Ui.Bars(g, chart, tot.Select(t => (double)t).ToList(), Ui.WithA(c.T.Accent, 140), c.S(10), mx, 6, c.T.Accent);
                y = chart.Bottom + c.S(3);
                float bw = (chart.Width - c.S(10) * 6) / 7;
                for (int i = 0; i < 7; i++)
                {
                    var dd = start.AddDays(i);
                    Ui.Text(g, dd.ToString("ddd", ProdUtil.Inv), c.F(9.5f), i == 6 ? c.T.Text : c.T.TextFaint, new RectangleF(chart.X + i * (bw + c.S(10)), y, bw, c.S(12)), 0);
                }
                y += c.S(20);
                if (sample) { var sd = ProdScreenTracker.SampleDay(DateTime.Today); apps = sd.Apps.Select(a => new KeyValuePair<string, int>(a.Key, a.Value * 5)).OrderByDescending(a => a.Value).ToList(); kinds = sd.Kinds; }
                else apps = ProdScreenTracker.AppsRange(start, 7, out kinds);
                var bk = new int[6]; foreach (var a in apps) { char k; if (!kinds.TryGetValue(a.Key, out k)) k = 'o'; bk[ProdUtil.KindIndex(k)] += a.Value; }
                KindStrip(c, bk, new RectangleF(x, y, w, c.S(6))); y += c.S(30);
            }

            // per-app list
            Ui.Text(g, "APPS", c.FS(9.5f), c.T.TextFaint, new RectangleF(x, y, w, c.S(14))); y += c.S(18);
            int maxV = apps.Count > 0 ? apps[0].Value : 1;
            int rows = (int)((b.Bottom - c.S(12) - y) / c.S(30));
            if (apps.Count == 0) Ui.Text(g, "No activity recorded", c.F(12), c.T.TextDim, new RectangleF(x, y, w, c.S(20)));
            for (int i = 0; i < apps.Count && i < rows; i++)
            {
                var a = apps[i]; char k; if (!kinds.TryGetValue(a.Key, out k)) k = 'o';
                var col = ProdUtil.KindColor(c.T, k);
                Ui.FillCircle(g, new PointF(x + c.S(4), y + c.S(8)), c.S(3.5f), col);
                Ui.Text(g, a.Key, c.F(12), c.T.Text, new RectangleF(x + c.S(14), y, w - c.S(80), c.S(16)));
                Ui.Text(g, ProdUtil.Dur(a.Value), c.FS(11.5f), c.T.TextDim, new RectangleF(x, y, w, c.S(16)), 1);
                Ui.Progress(g, new RectangleF(x + c.S(14), y + c.S(19), w - c.S(14), c.S(4)), a.Value / (double)maxV, Ui.WithA(col, 200), c.T.Track);
                y += c.S(30);
            }
        }

        static void KindStrip(DrawCtx c, int[] byKind, RectangleF r)
        {
            int tot = byKind.Sum(); var g = c.G;
            Ui.FillRound(g, r, r.Height / 2, c.T.Track);
            if (tot <= 0) return;
            using (var clip = Ui.Round(r, r.Height / 2))
            {
                var old = g.Clip; g.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
                float x = r.X;
                for (int i = 0; i < 6; i++)
                {
                    if (byKind[i] <= 0) continue;
                    float w = r.Width * byKind[i] / tot;
                    g.FillRectangle(Ui.B(ProdUtil.KindColor(c.T, ProdUtil.KindChars[i])), x, r.Y, w + 0.5f, r.Height);
                    x += w;
                }
                g.Clip = old;
            }
            // legend under the strip (only kinds with time)
            float lx = r.X, ly = r.Bottom + c.S(3);
            for (int i = 0; i < 6; i++)
            {
                if (byKind[i] <= 0) continue;
                string label = ProdUtil.KindNames[i].Split(' ')[0] + " " + Ui.Pct(byKind[i] / (double)tot);
                float lw = Ui.MeasureW(g, label, c.F(9.5f)) + c.S(16);
                if (lx + lw > r.Right) break;
                Ui.FillCircle(g, new PointF(lx + c.S(3), ly + c.S(6)), c.S(3), ProdUtil.KindColor(c.T, ProdUtil.KindChars[i]));
                Ui.Text(g, label, c.F(9.5f), c.T.TextDim, new RectangleF(lx + c.S(9), ly, lw, c.S(12)));
                lx += lw;
            }
        }
    }
}
