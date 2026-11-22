// Productivity group — shared helpers (formatting, app-kind classification, sun times, flash overlay, pomodoro log).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace NovaBar
{
    public static class ProdUtil
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long Unix(DateTime t) { return (long)(t.ToUniversalTime() - Epoch).TotalSeconds; }
        public static long UnixNow() { return Unix(DateTime.UtcNow); }
        public static DateTime FromUnix(double s) { return Epoch.AddSeconds(s).ToLocalTime(); }

        /// <summary>m:ss or h:mm:ss</summary>
        public static string MMSS(double sec)
        {
            if (sec < 0) sec = 0;
            long s = (long)Math.Ceiling(sec - 0.0001);
            if (s >= 3600) return (s / 3600) + ":" + ((s / 60) % 60).ToString("00") + ":" + (s % 60).ToString("00");
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
        /// <summary>Compact duration from seconds: "5h 12m", "42m", "0m".</summary>
        public static string Dur(double sec)
        {
            long m = (long)(sec / 60);
            if (m >= 60) return (m / 60) + "h " + (m % 60) + "m";
            return m + "m";
        }
        /// <summary>"now", "in 25 min", "in 2h 5m", "tomorrow", "in 3 days".</summary>
        public static string Until(DateTime start)
        {
            var d = start - DateTime.Now;
            if (d.TotalSeconds <= 0) return "now";
            if (d.TotalMinutes < 1) return "in <1 min";
            if (d.TotalMinutes < 60) return "in " + (int)Math.Ceiling(d.TotalMinutes) + " min";
            if (d.TotalHours < 24 && start.Date == DateTime.Today) { int m = (int)Math.Round(d.TotalMinutes); return "in " + (m / 60) + "h" + (m % 60 > 0 ? " " + (m % 60) + "m" : ""); }
            if (start.Date == DateTime.Today.AddDays(1)) return "tomorrow";
            return "in " + (int)Math.Round((start.Date - DateTime.Today).TotalDays) + " days";
        }
        public static string DayLabel(DateTime d)
        {
            if (d.Date == DateTime.Today) return "Today";
            if (d.Date == DateTime.Today.AddDays(1)) return "Tomorrow";
            if (d.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
            return d.ToString("dddd, MMM d", Inv);
        }

        public static List<Dictionary<string, object>> DictList(object o)
        {
            var l = new List<Dictionary<string, object>>();
            var a = o as System.Collections.IList;
            if (a != null) foreach (var x in a) { var d = x as Dictionary<string, object>; if (d != null) l.Add(d); }
            return l;
        }

        // ---------- flash overlay (phase end of pomodoro / timer) ----------
        public static bool Flashing(DateTime until) { return DateTime.Now < until; }
        public static void Flash(DrawCtx c, DateTime until, Color col)
        {
            if (!Flashing(until)) return;
            int ms = (int)(until - DateTime.Now).TotalMilliseconds;
            bool on = (ms / 500) % 2 == 0;
            if (!on) return;
            var r = new RectangleF(c.Bounds.X, c.Bounds.Y, c.Bounds.Width, c.Bounds.Height);
            Ui.FillRound(c.G, r, c.S(10), Color.FromArgb(c.T.Dark ? 70 : 60, col));
            Ui.StrokeRound(c.G, r, c.S(10), Color.FromArgb(200, col), c.S(1.5f));
        }
        public static void Chime() { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } }

        /// <summary>Badge pill (e.g. "Sample", "in 25 min").</summary>
        public static float Badge(DrawCtx c, string text, float rightX, float cy, Color fg, Color bg, float px = 10f)
        {
            var f = c.FS(px); float w = Ui.MeasureW(c.G, text, f) + c.S(12), h = c.S(px + 7);
            var r = new RectangleF(rightX - w, cy - h / 2, w, h);
            Ui.FillRound(c.G, r, h / 2, bg); Ui.Text(c.G, text, f, fg, r, 0);
            return r.X;
        }
        public static void SampleBadge(DrawCtx c, float rightX, float cy) { Badge(c, "Sample", rightX, cy, c.T.Warn, Color.FromArgb(c.T.Dark ? 40 : 34, c.T.Warn)); }

        // ---------- kinds of apps ----------
        public const string KindChars = "wbcmgo";
        public static readonly string[] KindNames = { "Work & dev", "Browser", "Communication", "Media", "Games", "Other" };
        public static int KindIndex(char k) { int i = KindChars.IndexOf(k); return i < 0 ? 5 : i; }
        public static Color KindColor(Theme t, char k)
        {
            switch (k)
            {
                case 'w': return t.Accent;
                case 'b': return t.Series[1];
                case 'c': return t.Series[2];
                case 'm': return t.Series[4];
                case 'g': return t.Series[3];
                default: return t.Dark ? Color.FromArgb(255, 120, 120, 135) : Color.FromArgb(255, 150, 152, 165);
            }
        }
        static readonly Dictionary<string, char> kindMap = BuildKinds();
        static Dictionary<string, char> BuildKinds()
        {
            var d = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
            Action<char, string> add = (k, list) => { foreach (var n in list.Split(',')) d[n.Trim()] = k; };
            add('w', "code,code - insiders,cursor,windsurf,zed,devenv,rider64,idea64,pycharm64,webstorm64,clion64,goland64,phpstorm64,rubymine64,datagrip64,studio64,fleet,sublime_text,notepad++,notepad,windowsterminal,wt,cmd,powershell,pwsh,conhost,openconsole,wsl,wslhost,mintty,alacritty,wezterm-gui,winword,excel,powerpnt,onenote,onenoteim,msaccess,mspub,visio,winproj,acrobat,acrord32,soffice,soffice.bin,figma,photoshop,illustrator,indesign,afterfx,adobe premiere pro,blender,unity,unity hub,unrealeditor,postman,insomnia,githubdesktop,sourcetree,gitkraken,fork,docker desktop,obsidian,notion,logseq,typora,xmind,dbeaver,ssms,tableplus,heidisql,filezilla,winscp,putty,mobaxterm,ssh,claude,chatgpt,lens,godot,matlab,rstudio,spyder,jupyter-lab,anki,todoist,ticktick,linear");
            add('b', "chrome,msedge,firefox,brave,opera,opera_gx,vivaldi,arc,iexplore,librewolf,waterfox,thorium,chromium,zen,floorp,yandex,whale,sidekick,duckduckgo");
            add('c', "teams,ms-teams,msteams,slack,discord,discordptb,discordcanary,zoom,skype,whatsapp,whatsapp.root,telegram,signal,outlook,olk,hxoutlook,thunderbird,mailspring,webex,ciscowebexstart,element,messenger,viber,line,wechat,weixin,mattermost,rocket.chat,em client,mailbird,spark,beeper,lync");
            add('m', "spotify,vlc,mpc-hc,mpc-hc64,mpc-be64,potplayer,potplayermini,potplayermini64,wmplayer,music.ui,video.ui,microsoft.media.player,applemusic,itunes,foobar2000,mpv,netflix,obs64,obs32,audacity,plex,plexamp,kodi,stremio,tidal,deezer,amazon music,youtube music,photos,microsoft.photos,davinci resolve,resolve,premiere,handbrake,musicbee,aimp,winamp,primevideo,disney+,twitch");
            add('g', "steam,epicgameslauncher,battle.net,riotclientservices,riotclientux,leagueclient,league of legends,valorant,valorant-win64-shipping,minecraft,minecraftlauncher,javaw,robloxplayerbeta,gog galaxy,galaxyclient,xboxapp,xboxpcapp,gamingservices,upc,ubisoftconnect,eadesktop,origin,cs2,csgo,dota2,fortniteclient-win64-shipping,r5apex,overwatch,gta5,gtav,rdr2,eldenring,witcher3,cyberpunk2077,genshinimpact,starrail,zenlesszonezero,playnite.desktopapp,rocketleague,fifa,fc24,fc25,fc26,destiny2,warframe.x64,pathofexile,poe2,eurotrucks2,factorio,terraria,stardew valley,hades,hollow_knight,baldur's gate 3,bg3,bg3_dx11,helldivers2,palworld-win64-shipping");
            return d;
        }
        /// <summary>Classify a process (name without .exe, plus optional full path) into a kind char.</summary>
        public static char Classify(string proc, string path)
        {
            if (string.IsNullOrEmpty(proc)) return 'o';
            char k; if (kindMap.TryGetValue(proc, out k)) return k;
            var p = (path ?? "").ToLowerInvariant();
            if (p.Contains("\\steamapps\\") || p.Contains("\\epic games\\") || p.Contains("\\riot games\\") || p.Contains("\\xboxgames\\") || p.Contains("\\gog galaxy\\games\\") || p.Contains("\\ubisoft game launcher\\games\\") || p.Contains("\\ea games\\")) return 'g';
            var lp = proc.ToLowerInvariant();
            if (lp.EndsWith("-win64-shipping") || lp.EndsWith("_shipping")) return 'g';
            if (lp.StartsWith("jetbrains") || lp.EndsWith("64") && (lp.StartsWith("idea") || lp.StartsWith("pycharm"))) return 'w';
            return 'o';
        }

        // ---------- sun (NOAA general solar position equations) ----------
        static DateTime sunDay; static DateTime? sunRise, sunSet; static double sunLat = double.NaN, sunLon = double.NaN;
        /// <summary>Local sunrise/sunset for a date at the configured location (null in polar day/night). Cached per day.</summary>
        public static void SunTimes(DateTime date, out DateTime? rise, out DateTime? set)
        {
            double lat = AppConfig.Current.Lat, lon = AppConfig.Current.Lon;
            if (sunDay != date.Date || lat != sunLat || lon != sunLon)
            {
                sunRise = SunEvent(date.Date, lat, lon, true); sunSet = SunEvent(date.Date, lat, lon, false);
                sunDay = date.Date; sunLat = lat; sunLon = lon;
            }
            rise = sunRise; set = sunSet;
        }
        /// <summary>NOAA: fractional year γ → equation of time + declination → hour angle for zenith 90.833°.</summary>
        public static DateTime? SunEvent(DateTime date, double lat, double lon, bool rise)
        {
            // first pass at local noon, second pass refined at the event time
            double minutes = 720;
            for (int pass = 0; pass < 2; pass++)
            {
                int doy = date.DayOfYear; int daysInYear = DateTime.IsLeapYear(date.Year) ? 366 : 365;
                double hour = minutes / 60.0;
                double g = 2 * Math.PI / daysInYear * (doy - 1 + (hour - 12) / 24.0);
                double eqtime = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
                double decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g) + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
                double latR = lat * Math.PI / 180;
                double cosHa = Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(latR) * Math.Cos(decl)) - Math.Tan(latR) * Math.Tan(decl);
                if (cosHa > 1 || cosHa < -1) return null;
                double ha = Math.Acos(cosHa) * 180 / Math.PI;
                minutes = rise ? 720 - 4 * (lon + ha) - eqtime : 720 - 4 * (lon - ha) - eqtime; // UTC minutes from midnight UTC
            }
            var utc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);
            var local = utc.ToLocalTime();
            // keep the event on the requested local date (far east/west longitudes)
            if (local.Date > date.Date) local = local.AddDays(-1); else if (local.Date < date.Date) local = local.AddDays(1);
            return local;
        }
    }

    /// <summary>Completed pomodoro focus rounds (shared by Pomodoro + Day Timeline). Persisted under state key "prod.pomodoro".</summary>
    public static class ProdPomoLog
    {
        public class Round { public DateTime Start, End; }
        static List<Round> rounds;
        static readonly object lk = new object();
        public const string Key = "prod.pomodoro.log";
        static void Load()
        {
            if (rounds != null) return;
            rounds = new List<Round>();
            var d = StateStore.Load(Key);
            foreach (var r in ProdUtil.DictList(J.Get(d, "rounds")))
                rounds.Add(new Round { Start = ProdUtil.FromUnix(J.Num(r, "s")), End = ProdUtil.FromUnix(J.Num(r, "e")) });
        }
        public static void Add(DateTime start, DateTime end)
        {
            lock (lk)
            {
                Load();
                rounds.Add(new Round { Start = start, End = end });
                var cutoff = DateTime.Now.AddDays(-35); rounds.RemoveAll(r => r.End < cutoff);
                var arr = rounds.Select(r => (object)new Dictionary<string, object> { { "s", ProdUtil.Unix(r.Start) }, { "e", ProdUtil.Unix(r.End) } }).ToArray();
                var copy = arr; System.Threading.ThreadPool.QueueUserWorkItem(_ => StateStore.Save(Key, new Dictionary<string, object> { { "rounds", copy } }));
            }
        }
        public static List<Round> ForDay(DateTime day)
        {
            lock (lk) { Load(); return rounds.Where(r => r.Start.Date == day.Date || r.End.Date == day.Date).ToList(); }
        }
    }
}
