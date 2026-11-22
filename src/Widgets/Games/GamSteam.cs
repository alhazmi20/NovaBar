// Steam (local data, no key): registry (SteamPath / RunningAppID / ActiveUser), libraryfolders.vdf, appmanifest_*.acf,
// userdata\<id>\config\localconfig.vdf (LastPlayed / Playtime) and appcache\librarycache header images.
// Hub "gam.steam.run": 5 s registry poll of RunningAppID (one DWORD read; same snapshot object while nothing changes).
// Hub "gam.steam.lib": manifest scan every 10 min, plus on panel open and shortly after a game exits.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NovaBar
{
    public sealed class GamGame
    {
        public int AppId; public string Name = "", Lib = "";
        public DateTime LastPlayed = DateTime.MinValue, LastUpdated = DateTime.MinValue;
        public long Size, Flags, BytesToDl, BytesDl, BytesToStage, BytesStaged;
        public double PlayMin;
        public bool Installed;
        public bool Updating { get { return (Flags & (256 | 1024)) != 0; } }
        public bool Downloading { get { return (Flags & 256) != 0; } }
        public double DlFrac
        {
            get
            {
                long tot = BytesToDl + BytesToStage, done = BytesDl + BytesStaged;
                return tot > 0 ? Math.Max(0, Math.Min(1, (double)done / tot)) : 0;
            }
        }
        public string Run { get { return "steam://rungameid/" + AppId; } }
    }

    public sealed class GamLib
    {
        public bool Found;                    // Steam install detected
        public string SteamPath = "", User = "", Sig = "";
        public List<GamGame> Games = new List<GamGame>();   // installed, tools excluded
        public List<GamGame> Recent = new List<GamGame>();  // installed, by last played (fallback last updated)
        public GamGame Download;              // an update/download in progress (or queued/paused)
        public long TotalSize;
        public GamGame Find(int id) { return Games.FirstOrDefault(g => g.AppId == id); }
        public override bool Equals(object o) { var x = o as GamLib; return x != null && x.Sig == Sig; }
        public override int GetHashCode() { return Sig.GetHashCode(); }
    }

    public sealed class GamRun
    {
        public int AppId; public string Name = ""; public DateTime Since = DateTime.UtcNow;
        public bool Playing { get { return AppId != 0; } }
    }

    public static class GamSteam
    {
        public const string RunKey = "gam.steam.run", LibKey = "gam.steam.lib";
        static GamRun lastRun = new GamRun();
        static readonly GamRun noRun = new GamRun();

        static GamSteam()
        {
            Hub.Define(RunKey, 5000, SampleRun);
            Hub.Define(LibKey, 10 * 60 * 1000, () => Scan());
        }
        public static void Init() { }   // touch the static ctor

        public static GamLib Lib { get { return Hub.Get<GamLib>(LibKey) ?? Empty; } }
        public static GamRun Running { get { return Hub.Get<GamRun>(RunKey) ?? noRun; } }
        static readonly GamLib Empty = new GamLib();

        static string Demo { get { return Environment.GetEnvironmentVariable("NOVABAR_STEAM_DEMO"); } }

        public static string SteamPath()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var p = k != null ? k.GetValue("SteamPath") as string : null;
                    if (!string.IsNullOrEmpty(p)) { p = Path.GetFullPath(p.Replace('/', '\\')); if (Directory.Exists(p)) return p; }
                }
            }
            catch { }
            var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            return Directory.Exists(def) ? def : null;
        }

        static int RegDword(string sub, string name)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(sub)) { var v = k != null ? k.GetValue(name) : null; return v is int ? (int)v : 0; } }
            catch { return 0; }
        }

        // ---------------- running game (cheap poll) ----------------
        static object SampleRun()
        {
            int id = RegDword(@"Software\Valve\Steam", "RunningAppID");
            var d = Demo; if (d == "play") id = 990080;
            var prev = lastRun;
            if (id == prev.AppId) return prev;          // unchanged → same reference, no repaint
            GamRun n;
            if (id == 0)
            {
                n = noRun;
                // the game just closed: Steam writes LastPlayed/Playtime a moment later
                var t = new System.Threading.Timer(_ => Hub.Refresh(LibKey)); t.Change(6000, Timeout.Infinite); keep = t;
            }
            else
            {
                var g = Lib.Find(id);
                n = new GamRun { AppId = id, Name = g != null ? g.Name : ManifestName(id) };
                if (n.Name == "") n.Name = "App " + id;
                // if we started while the game was already running, Steam's own LastPlayed is the launch time
                var lp = LaunchTime(id);
                if (lp != DateTime.MinValue && lp <= DateTime.UtcNow && (DateTime.UtcNow - lp).TotalHours < 24) n.Since = lp;
                if (d == "play") n.Since = DateTime.UtcNow.AddMinutes(-72);
            }
            lastRun = n; return n;
        }
        static System.Threading.Timer keep;

        static DateTime LaunchTime(int id)
        {
            var g = Lib.Find(id);
            // fresh read: Steam updates LastPlayed in the manifest / localconfig at launch
            var sp = SteamPath(); if (sp == null) return DateTime.MinValue;
            DateTime best = g != null ? g.LastPlayed : DateTime.MinValue;
            foreach (var lib in Libraries(sp))
            {
                var f = Path.Combine(lib, "steamapps", "appmanifest_" + id + ".acf");
                if (!File.Exists(f)) continue;
                var m = GamVdf.ParseFile(f);
                var t = GamUtil.FromUnix(GamVdf.Num(m, "AppState/LastPlayed")); if (t > best) best = t;
            }
            return best;
        }
        static string ManifestName(int id)
        {
            var sp = SteamPath(); if (sp == null) return "";
            foreach (var lib in Libraries(sp))
            {
                var f = Path.Combine(lib, "steamapps", "appmanifest_" + id + ".acf");
                if (File.Exists(f)) return GamVdf.Str(GamVdf.ParseFile(f), "AppState/name");
            }
            return "";
        }

        // ---------------- library scan ----------------
        public static List<string> Libraries(string sp)
        {
            var l = new List<string> { sp };
            var v = GamVdf.ParseFile(Path.Combine(sp, "steamapps", "libraryfolders.vdf"));
            var root = GamVdf.Obj(v, "libraryfolders") ?? GamVdf.Obj(v, "LibraryFolders");
            if (root != null)
                foreach (var kv in root)
                {
                    string p = kv.Value is string ? (string)kv.Value : GamVdf.Str(kv.Value, "path");
                    if (string.IsNullOrEmpty(p)) continue;
                    try { p = Path.GetFullPath(p); } catch { continue; }
                    if (!l.Any(x => string.Equals(x.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) && Directory.Exists(p)) l.Add(p);
                }
            return l;
        }

        static bool IsTool(int id, string name)
        {
            if (id == 228980 || id == 1070560 || id == 1391110 || id == 1628350 || id == 1493710 || id == 250820) return true; // redist, runtimes, SteamVR
            var n = name.ToLowerInvariant();
            return n.Contains("redistributable") || n.Contains("steam linux runtime") || n.StartsWith("proton ") || n.Contains("connect services") || n.EndsWith(" dedicated server") || n.Contains("steamworks");
        }

        public static string UserDir(string sp)
        {
            var ud = Path.Combine(sp, "userdata"); if (!Directory.Exists(ud)) return null;
            int active = RegDword(@"Software\Valve\Steam\ActiveProcess", "ActiveUser");
            if (active != 0) { var d = Path.Combine(ud, ((uint)active).ToString()); if (Directory.Exists(d)) return d; }
            string best = null; DateTime bt = DateTime.MinValue;
            try
            {
                foreach (var d in Directory.GetDirectories(ud))
                {
                    var f = Path.Combine(d, "config", "localconfig.vdf");
                    if (!File.Exists(f)) continue;
                    var t = File.GetLastWriteTimeUtc(f); if (t > bt) { bt = t; best = d; }
                }
            }
            catch { }
            return best;
        }

        public static GamLib Scan()
        {
            if (Demo == "1" || Demo == "play") return DemoLib();
            var r = new GamLib();
            var sp = SteamPath();
            if (sp == null) { r.Sig = "none"; return r; }
            r.Found = true; r.SteamPath = sp;
            // localconfig: LastPlayed + Playtime (minutes) per app
            var lc = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var udir = UserDir(sp);
            if (udir != null)
            {
                var v = GamVdf.ParseFile(Path.Combine(udir, "config", "localconfig.vdf"));
                var apps = GamVdf.Obj(v, "UserLocalConfigStore/Software/Valve/Steam/apps") ?? GamVdf.Obj(v, "UserLocalConfigStore/Software/Valve/Steam/Apps");
                if (apps != null) lc = apps;
                r.User = GamVdf.Str(v, "UserLocalConfigStore/friends/PersonaName");
                if (r.User == "") { var fr = GamVdf.Obj(v, "UserLocalConfigStore/friends"); if (fr != null) r.User = GamVdf.Str(fr, "PersonaName"); }
            }
            foreach (var lib in Libraries(sp))
            {
                string[] files;
                try { files = Directory.GetFiles(Path.Combine(lib, "steamapps"), "appmanifest_*.acf"); } catch { continue; }
                foreach (var f in files)
                {
                    var m = GamVdf.Obj(GamVdf.ParseFile(f), "AppState"); if (m == null) continue;
                    var g = new GamGame { Lib = lib, Installed = true };
                    g.AppId = (int)GamVdf.Num(m, "appid"); g.Name = GamVdf.Str(m, "name");
                    if (g.AppId == 0 || g.Name == "" || IsTool(g.AppId, g.Name)) continue;
                    if (r.Games.Any(x => x.AppId == g.AppId)) continue;
                    g.Flags = GamVdf.Num(m, "StateFlags"); g.Size = GamVdf.Num(m, "SizeOnDisk");
                    g.BytesToDl = GamVdf.Num(m, "BytesToDownload"); g.BytesDl = GamVdf.Num(m, "BytesDownloaded");
                    g.BytesToStage = GamVdf.Num(m, "BytesToStage"); g.BytesStaged = GamVdf.Num(m, "BytesStaged");
                    g.LastUpdated = GamUtil.FromUnix(GamVdf.Num(m, "LastUpdated"));
                    var lp = GamUtil.FromUnix(GamVdf.Num(m, "LastPlayed"));
                    object a; if (lc.TryGetValue(g.AppId.ToString(), out a))
                    {
                        var lp2 = GamUtil.FromUnix(GamVdf.Num(a, "LastPlayed")); if (lp2 > lp) lp = lp2;
                        g.PlayMin = GamVdf.Num(a, "Playtime");
                    }
                    g.LastPlayed = lp;
                    r.Games.Add(g); r.TotalSize += g.Size;
                }
            }
            Finish(r);
            return r;
        }

        static void Finish(GamLib r)
        {
            r.Recent = r.Games.OrderByDescending(g => g.LastPlayed).ThenByDescending(g => g.LastUpdated).ToList();
            r.Download = r.Games.Where(g => g.Downloading).FirstOrDefault() ?? r.Games.Where(g => g.Updating && g.BytesToDl > 0).OrderByDescending(g => g.DlFrac).FirstOrDefault();
            var sb = new StringBuilder(r.Found ? "f" : "n").Append(r.User);
            foreach (var g in r.Recent) sb.Append('|').Append(g.AppId).Append(':').Append(g.LastPlayed.Ticks).Append(':').Append(g.Flags).Append(':').Append(g.Size).Append(':').Append(g.BytesDl + g.BytesStaged).Append(':').Append(g.PlayMin);
            r.Sig = sb.ToString();
        }

        // ---------------- header images ----------------
        static string cachedPath;
        static readonly Dictionary<int, string> headerPath = new Dictionary<int, string>();
        /// <summary>Header image path for an app from the local library cache (old and new layouts). Cached; "" if none.</summary>
        public static string Header(int appId)
        {
            lock (headerPath)
            {
                string p; if (headerPath.TryGetValue(appId, out p)) return p;
                p = FindHeader(appId); headerPath[appId] = p; return p;
            }
        }
        static string FindHeader(int id)
        {
            if (cachedPath == null) cachedPath = SteamPath() ?? "";
            var sp = cachedPath; if (sp == "") return "";
            var lc = Path.Combine(sp, "appcache", "librarycache");
            try
            {
                var flat = Path.Combine(lc, id + "_header.jpg"); if (File.Exists(flat)) return flat;
                var dir = Path.Combine(lc, id.ToString());
                if (!Directory.Exists(dir)) return "";
                foreach (var n in new[] { "header.jpg", "library_header.jpg" }) { var f = Path.Combine(dir, n); if (File.Exists(f)) return f; }
                string best = ""; DateTime bt = DateTime.MinValue;
                foreach (var sub in Directory.GetDirectories(dir))
                    foreach (var n in new[] { "header.jpg", "library_header.jpg" })
                    {
                        var f = Path.Combine(sub, n);
                        if (File.Exists(f)) { var t = File.GetLastWriteTimeUtc(f); if (t > bt) { bt = t; best = f; } }
                    }
                if (best != "") return best;
                var hero = Directory.GetFiles(dir, "library_hero.jpg", SearchOption.AllDirectories).FirstOrDefault();
                return hero ?? "";
            }
            catch { return ""; }
        }

        // ---------------- demo (render harness: NOVABAR_STEAM_DEMO=1|play|none) ----------------
        static GamLib DemoLib()
        {
            var r = new GamLib { Found = true, SteamPath = SteamPath() ?? "", User = "player" };
            var now = DateTime.UtcNow;
            Func<int, string, double, double, long, GamGame> add = (id, n, agoH, play, gb) =>
            {
                var g = new GamGame { AppId = id, Name = n, LastPlayed = agoH < 0 ? DateTime.MinValue : now.AddHours(-agoH), PlayMin = play * 60, Size = gb * 1073741824L, Flags = 4, Installed = true };
                r.Games.Add(g); r.TotalSize += g.Size; return g;
            };
            add(990080, "Hogwarts Legacy", 5, 64.2, 76); add(242640, "Styx: Master of Shadows", 9, 12.5, 7);
            add(3489700, "Stellar Blade", 30, 22.1, 60); add(1496790, "Gotham Knights", 140, 18, 45);
            add(2651280, "Marvel's Spider-Man 2", 600, 41, 108); add(1030840, "Mafia: Definitive Edition", 1200, 9, 36);
            var dl = add(1091500, "Cyberpunk 2077", -1, 0, 85); dl.Flags = 4 | 256 | 1024; dl.BytesToDl = 24543345520; dl.BytesDl = 15582348688;
            Finish(r);
            return r;
        }
    }

    /// <summary>Steam logo drawn as vectors (wheel + crank), so no image resources are needed.</summary>
    public static class GamDraw
    {
        public static readonly Color SteamBlue = Color.FromArgb(255, 23, 26, 33), SteamLight = Color.FromArgb(255, 102, 192, 244);

        public static void SteamLogo(Graphics g, RectangleF r, bool onDark, Theme T)
        {
            var sq = Ui.Square(r); float d = sq.Width;
            var bg1 = Color.FromArgb(255, 42, 71, 94); var bg2 = Color.FromArgb(255, 23, 26, 33);
            using (var br = new LinearGradientBrush(new RectangleF(sq.X - 1, sq.Y - 1, d + 2, d + 2), bg1, bg2, 60f)) g.FillEllipse(br, sq);
            var fg = Color.FromArgb(255, 235, 242, 250);
            // big wheel (upper right)
            var c1 = new PointF(sq.X + d * 0.63f, sq.Y + d * 0.38f); float r1 = d * 0.19f;
            using (var p = new Pen(fg, d * 0.075f)) g.DrawEllipse(p, c1.X - r1, c1.Y - r1, r1 * 2, r1 * 2);
            Ui.FillCircle(g, c1, d * 0.09f, fg);
            // small wheel (lower left) + rod
            var c2 = new PointF(sq.X + d * 0.33f, sq.Y + d * 0.66f); float r2 = d * 0.11f;
            using (var p = Ui.P(fg, d * 0.12f)) g.DrawLine(p, c2, new PointF(c1.X - r1 * 0.85f, c1.Y + r1 * 0.55f));
            Ui.FillCircle(g, c2, r2, fg);
            Ui.FillCircle(g, c2, r2 * 0.45f, bg2);
        }
    }

    [Widget("steam", "Steam", "Games", "The game you're playing with a live timer, recently played games with art, downloads and quick launch. Reads your local Steam install.", Data = DataKind.Live)]
    public class GamSteamWidget : Widget
    {
        public override void OnStart() { GamSteam.Init(); Hub.Subscribe(GamSteam.RunKey, this); Hub.Subscribe(GamSteam.LibKey, this); }
        public override void OnStop() { Hub.Unsubscribe(GamSteam.RunKey, this); Hub.Unsubscribe(GamSteam.LibKey, this); }
        public override void OnPanelOpen() { Hub.Refresh(GamSteam.LibKey); }
        // timer text changes once a minute; tick only while a game is running
        public override int RefreshMs { get { return GamSteam.Running.Playing ? 15000 : 0; } }

        public override int Measure(DrawCtx c)
        {
            var run = GamSteam.Running; var lib = GamSteam.Lib;
            string a, b; Lines(run, lib, out a, out b);
            int tw = Math.Max(Ui.MeasureW(c.G, a, c.FS(12)), Ui.MeasureW(c.G, b, c.F(10.5f)));
            return Math.Max(c.Si(110), Math.Min(c.Si(230), c.Si(10 + 22 + 9 + 12) + tw));
        }

        static void Lines(GamRun run, GamLib lib, out string a, out string b)
        {
            if (run.Playing) { a = run.Name; b = "Playing · " + GamUtil.Session(DateTime.UtcNow - run.Since); return; }
            a = "Steam";
            if (!lib.Found && lib.Sig == "none") { b = "Not installed"; return; }
            if (lib.Download != null && lib.Download.Downloading) { b = "Downloading " + Ui.Pct(lib.Download.DlFrac); return; }
            var last = lib.Recent.FirstOrDefault(g => g.LastPlayed != DateTime.MinValue);
            if (last != null && (DateTime.UtcNow - last.LastPlayed).TotalDays < 14) { b = "Last played: " + last.Name; return; }
            b = lib.Games.Count > 0 ? lib.Games.Count + " games" : "Library";
        }

        public override void Draw(DrawCtx c)
        {
            var run = GamSteam.Running; var lib = GamSteam.Lib; var b = c.Bounds;
            float d = c.S(22);
            var lr = new RectangleF(b.X + c.S(10), b.Y + (b.Height - d) / 2, d, d);
            if (run.Playing)
            {
                // game art as a tiny rounded square with the steam logo badge
                var hr = new RectangleF(lr.X - c.S(2), b.Y + (b.Height - c.S(24)) / 2, c.S(24), c.S(24));
                GamImg.Draw(c, GamSteam.Header(run.AppId), hr, c.S(6), run.Name, this);
                var badge = new RectangleF(hr.Right - c.S(9), hr.Bottom - c.S(9), c.S(12), c.S(12));
                Ui.FillCircle(c.G, new PointF(badge.X + badge.Width / 2, badge.Y + badge.Height / 2), c.S(7), c.T.Dark ? Color.FromArgb(255, 20, 22, 28) : Color.White);
                GamDraw.SteamLogo(c.G, badge, c.T.Dark, c.T);
                lr = hr;
            }
            else GamDraw.SteamLogo(c.G, lr, c.T.Dark, c.T);
            string l1, l2; Lines(run, lib, out l1, out l2);
            float x = lr.Right + c.S(9), w = b.Right - c.S(10) - x;
            float ha = c.S(15), hb = c.S(13.5f), y = b.Y + (b.Height - ha - hb) / 2;
            Ui.Text(c.G, l1, c.FS(12), c.T.Text, new RectangleF(x, y, w, ha));
            if (run.Playing)
            {
                var gr = new RectangleF(x, y + ha, w, hb);
                Ui.FillCircle(c.G, new PointF(x + c.S(3), gr.Y + gr.Height / 2), c.S(2.6f), c.T.Good);
                Ui.Text(c.G, l2, c.F(10.5f), c.T.TextDim, new RectangleF(x + c.S(9), gr.Y, w - c.S(9), hb));
            }
            else Ui.Text(c.G, l2, c.F(10.5f), c.T.TextDim, new RectangleF(x, y + ha, w, hb));
            if (!run.Playing && lib.Download != null && lib.Download.Downloading)
                Ui.Progress(c.G, new RectangleF(x, b.Bottom - c.S(5), w, c.S(2)), lib.Download.DlFrac, SteamAccent(c.T), c.T.Track);
        }

        public static Color SteamAccent(Theme T) { return T.Dark ? GamDraw.SteamLight : Color.FromArgb(255, 26, 120, 196); }

        public override void AddMenuItems(List<KeyValuePair<string, Action>> items)
        {
            items.Add(new KeyValuePair<string, Action>("Open Steam", () => GamUtil.Launch("steam://open/main")));
            items.Add(new KeyValuePair<string, Action>("Library", () => GamUtil.Launch("steam://open/games")));
            foreach (var g in GamSteam.Lib.Recent.Where(x => x.LastPlayed != DateTime.MinValue).Take(3))
            { var gg = g; items.Add(new KeyValuePair<string, Action>("Play " + gg.Name, () => GamUtil.Launch(gg.Run))); }
        }

        // ---------------- panel ----------------
        const int ListN = 4;
        public override Size PanelSize(float scale)
        {
            var lib = GamSteam.Lib; var run = GamSteam.Running;
            float h = 50 + 150 + 12;                         // header + hero
            int n = Math.Min(ListN, Math.Max(0, lib.Recent.Count - 1));
            if (n > 0) h += 22 + n * 42;
            if (lib.Download != null) h += 52;
            h += 56;                                          // buttons
            if (!lib.Found) h = 260;
            return new Size((int)(360 * scale), (int)(h * scale));
        }

        public override void DrawPanel(DrawCtx c)
        {
            var lib = GamSteam.Lib; var run = GamSteam.Running; var b = c.Bounds; var g = c.G; var T = c.T;
            var acc = SteamAccent(T);
            // header with logo
            float pad = c.S(16);
            var lr = new RectangleF(b.X + pad, b.Y + c.S(13), c.S(22), c.S(22));
            GamDraw.SteamLogo(g, lr, T.Dark, T);
            Ui.Text(g, "Steam", c.FS(14), T.Text, new RectangleF(lr.Right + c.S(9), lr.Y, c.S(120), lr.Height));
            string sub = lib.Games.Count > 0 ? lib.Games.Count + " games · " + Ui.Bytes(lib.TotalSize, 1) : (lib.Found ? "Scanning library…" : "");
            Ui.Text(g, sub, c.F(11.5f), T.TextDim, new RectangleF(b.X, lr.Y, b.Right - pad - b.X, lr.Height), 1);
            float y = b.Y + c.S(50);
            float x0 = b.X + pad, w = b.Width - 2 * pad;

            if (!lib.Found)
            {
                Ui.Text(g, lib.Sig == "none" ? "Steam isn't installed" : "Reading your Steam library…", c.FS(14), T.Text, new RectangleF(b.X, y + c.S(40), b.Width, c.S(22)), 0);
                Ui.Text(g, lib.Sig == "none" ? "Install Steam to see your games here" : "", c.F(12), T.TextDim, new RectangleF(b.X, y + c.S(66), b.Width, c.S(18)), 0);
                if (lib.Sig == "none") c.Button(new RectangleF(b.X + b.Width / 2 - c.S(70), y + c.S(110), c.S(140), c.S(32)), "Get Steam", () => GamUtil.Launch("https://store.steampowered.com/about/"), true);
                return;
            }

            // hero: running game, else most recent
            GamGame hero = run.Playing ? (lib.Find(run.AppId) ?? new GamGame { AppId = run.AppId, Name = run.Name }) : lib.Recent.FirstOrDefault();
            var hr = new RectangleF(x0, y, w, c.S(150));
            if (hero != null)
            {
                GamImg.Draw(c, GamSteam.Header(hero.AppId), hr, c.S(12), hero.Name, this);
                // bottom scrim for legible text on any art
                var sr = new RectangleF(hr.X, hr.Y + hr.Height * 0.30f, hr.Width, hr.Height * 0.70f);
                using (var path = RoundBottom(sr, c.S(12)))
                using (var br = new LinearGradientBrush(new RectangleF(sr.X, sr.Y - 1, sr.Width, sr.Height + 2), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(235, 8, 10, 14), LinearGradientMode.Vertical))
                    g.FillPath(br, path);
                var white = Color.White; var dim = Color.FromArgb(220, 225, 230, 240);
                string chip = run.Playing ? "NOW PLAYING" : "LAST PLAYED";
                var cf = c.F(9.5f, true); float cw = Ui.MeasureW(g, chip, cf) + c.S(run.Playing ? 22 : 14);
                var cr = new RectangleF(hr.X + c.S(10), hr.Y + c.S(10), cw, c.S(18));
                Ui.FillRound(g, cr, cr.Height / 2, run.Playing ? Color.FromArgb(230, 34, 197, 94) : Color.FromArgb(150, 10, 12, 18));
                if (run.Playing) Ui.FillCircle(g, new PointF(cr.X + c.S(8), cr.Y + cr.Height / 2), c.S(2.6f), white);
                Ui.Text(g, chip, cf, white, new RectangleF(cr.X + c.S(run.Playing ? 14 : 7), cr.Y, cr.Width, cr.Height));
                float ty = hr.Bottom - c.S(48);
                var pb = new RectangleF(hr.Right - c.S(10) - c.S(36), hr.Bottom - c.S(46), c.S(36), c.S(36));
                Ui.Text(g, hero.Name, c.FS(15), white, new RectangleF(hr.X + c.S(12), ty, pb.X - hr.X - c.S(18), c.S(20)));
                string meta;
                if (run.Playing) meta = "Playing for " + GamUtil.Session(DateTime.UtcNow - run.Since) + (hero.PlayMin > 0 ? " · " + GamUtil.Hours(hero.PlayMin) + " total" : "");
                else meta = (hero.LastPlayed != DateTime.MinValue ? GamUtil.Ago(hero.LastPlayed) : "Not played yet") + (hero.PlayMin > 0 ? " · " + GamUtil.Hours(hero.PlayMin) : "");
                Ui.Text(g, meta, c.F(11.5f), dim, new RectangleF(hr.X + c.S(12), ty + c.S(21), pb.X - hr.X - c.S(18), c.S(16)));
                if (!run.Playing)
                {
                    bool h = c.IsHover(pb);
                    Ui.FillCircle(g, new PointF(pb.X + pb.Width / 2, pb.Y + pb.Height / 2), pb.Width / 2, h ? Color.FromArgb(255, 74, 222, 128) : Color.FromArgb(255, 34, 197, 94));
                    Ui.Icon(g, Icons.Play, new RectangleF(pb.X + c.S(1), pb.Y, pb.Width, pb.Height), white, c.S(14));
                    var hg = hero; c.Hit(hr, () => GamUtil.Launch(hg.Run));
                }
                else { var id = hero.AppId; c.Hit(hr, () => GamUtil.Launch("steam://nav/games/details/" + id)); }
            }
            y = hr.Bottom + c.S(12);

            // recent list
            var rest = lib.Recent.Where(x => hero == null || x.AppId != hero.AppId).Take(ListN).ToList();
            if (rest.Count > 0)
            {
                Ui.Text(g, "RECENT", c.F(10, true), T.TextFaint, new RectangleF(x0, y, w, c.S(14)));
                y += c.S(22);
                foreach (var it in rest)
                {
                    var row = new RectangleF(x0 - c.S(6), y - c.S(3), w + c.S(12), c.S(40));
                    bool h = c.IsHover(row);
                    if (h) Ui.FillRound(g, row, c.S(9), T.TileHover);
                    var ir = new RectangleF(x0, y + c.S(1), c.S(68), c.S(32));
                    GamImg.Draw(c, GamSteam.Header(it.AppId), ir, c.S(6), it.Name, this);
                    float tx = ir.Right + c.S(10), tw = row.Right - c.S(40) - tx;
                    Ui.Text(g, it.Name, c.FS(12.5f), T.Text, new RectangleF(tx, y + c.S(1), tw, c.S(17)));
                    string m2 = it.Updating ? (it.Downloading ? "Downloading " : "Update queued ") + Ui.Pct(it.DlFrac)
                        : (it.LastPlayed != DateTime.MinValue ? GamUtil.Ago(it.LastPlayed) : "Not played yet") + (it.PlayMin > 0 ? " · " + GamUtil.Hours(it.PlayMin) : "");
                    Ui.Text(g, m2, c.F(11), T.TextDim, new RectangleF(tx, y + c.S(18), tw, c.S(15)));
                    var pr = new RectangleF(row.Right - c.S(34), y + c.S(3), c.S(28), c.S(28));
                    if (h) Ui.FillCircle(g, new PointF(pr.X + pr.Width / 2, pr.Y + pr.Height / 2), pr.Width / 2, Color.FromArgb(255, 34, 197, 94));
                    Ui.Icon(g, Icons.Play, new RectangleF(pr.X + c.S(1), pr.Y, pr.Width, pr.Height), h ? Color.White : T.TextDim, c.S(11));
                    var gg = it; c.Hit(row, () => GamUtil.Launch(gg.Run));
                    y += c.S(42);
                }
            }

            // download indicator
            if (lib.Download != null)
            {
                var dl = lib.Download;
                var dr = new RectangleF(x0, y + c.S(2), w, c.S(42));
                Ui.FillRound(g, dr, c.S(10), T.Tile);
                var ic = new RectangleF(dr.X + c.S(8), dr.Y + c.S(7), c.S(28), c.S(28));
                Ui.FillCircle(g, new PointF(ic.X + ic.Width / 2, ic.Y + ic.Height / 2), ic.Width / 2, Ui.WithA(acc, 40));
                Ui.Icon(g, Icons.Download, ic, acc, c.S(13));
                float tx = ic.Right + c.S(9), tw = dr.Right - c.S(12) - tx;
                string st = dl.Downloading ? "Downloading" : "Update paused";
                Ui.Text(g, dl.Name, c.FS(12), T.Text, new RectangleF(tx, dr.Y + c.S(5), tw - c.S(90), c.S(16)));
                long tot = dl.BytesToDl, done = dl.BytesDl;
                Ui.Text(g, st + " · " + Ui.Pct(dl.DlFrac), c.F(11), T.TextDim, new RectangleF(tx, dr.Y + c.S(5), tw, c.S(16)), 1);
                Ui.Progress(g, new RectangleF(tx, dr.Y + c.S(26), tw, c.S(5)), dl.DlFrac, dl.Downloading ? acc : T.TextFaint, T.Track);
                c.Hit(dr, () => GamUtil.Launch("steam://open/downloads"));
                y = dr.Bottom + c.S(10);
            }

            // buttons
            float bh = c.S(32), gap = c.S(8), bw = (w - 2 * gap) / 3;
            float by = b.Bottom - c.S(14) - bh;
            c.Button(new RectangleF(x0, by, bw, bh), "Open Steam", () => GamUtil.Launch("steam://open/main"));
            c.Button(new RectangleF(x0 + bw + gap, by, bw, bh), "Library", () => GamUtil.Launch("steam://open/games"));
            c.Button(new RectangleF(x0 + 2 * (bw + gap), by, bw, bh), "Friends", () => GamUtil.Launch("steam://open/friends"));
        }

        static GraphicsPath RoundBottom(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            p.AddLine(r.X, r.Y, r.Right, r.Y);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }
}
