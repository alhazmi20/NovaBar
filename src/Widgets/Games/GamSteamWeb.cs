// Steam Web API widgets (account "steam": Web API key + SteamID64 or vanity name).
// steam-friends: ISteamUser/GetFriendList + GetPlayerSummaries every 5 min. steam-playtime: IPlayerService/GetRecentlyPlayedGames every 15 min.
// Not connected → deterministic sample data with a "Sample" badge (no network at all).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace NovaBar
{
    public sealed class GamFriend { public string Name = "", Game = "", Avatar = ""; public int State; public long GameId; }
    public sealed class GamPlayItem { public int AppId; public string Name = ""; public double Min2w, MinTotal; }

    /// <summary>Immutable web snapshot for both widgets. Equality by signature (no repaint when nothing changed).</summary>
    public sealed class GamWebSnap
    {
        public bool Sample; public string Error; public DateTime At = DateTime.UtcNow;
        public int Total;
        public List<GamFriend> Friends = new List<GamFriend>();     // online only, in-game first
        public List<GamPlayItem> Plays = new List<GamPlayItem>();
        public string Sig = "";
        public int Online { get { return Friends.Count; } }
        public int InGame { get { return Friends.Count(f => f.Game != ""); } }
        public double Min2w { get { return Plays.Sum(p => p.Min2w); } }
        public void Seal()
        {
            var sb = new StringBuilder(Sample ? "s" : "l").Append(Error).Append('|').Append(Total);
            foreach (var f in Friends) sb.Append('|').Append(f.Name).Append(':').Append(f.State).Append(':').Append(f.Game);
            foreach (var p in Plays) sb.Append('|').Append(p.AppId).Append(':').Append(p.Min2w).Append(':').Append(p.MinTotal);
            Sig = sb.ToString();
        }
        public override bool Equals(object o) { var x = o as GamWebSnap; return x != null && x.Sig == Sig; }
        public override int GetHashCode() { return Sig.GetHashCode(); }
    }

    public static class GamWeb
    {
        static GamWeb()
        {
            Accounts.Define("steam", "Steam",
                "Optional — the Steam widget works without it. For friends and 2-week playtime: get a free Web API key at steamcommunity.com/dev/apikey (any domain name works), " +
                "and paste your SteamID64 (17 digits, shown in Steam → Account details) or your custom profile URL name. Your profile's game details / friends list must be visible to the key owner (you).",
                "https://steamcommunity.com/dev/apikey",
                Accounts.F("key", "Web API key"), Accounts.F("steamid", "SteamID64 or profile name", false, "7656119…"));
        }
        public static void Init() { }

        static readonly Dictionary<string, string> vanity = new Dictionary<string, string>();

        static string Get(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 15000; req.ReadWriteTimeout = 15000; req.UserAgent = "NovaBar/1.0"; req.AutomaticDecompression = DecompressionMethods.GZip;
                using (var resp = (HttpWebResponse)req.GetResponse()) using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
            }
            catch (WebException we)
            {
                var hr = we.Response as HttpWebResponse;
                if (hr == null) throw new Exception("Offline");
                int code = (int)hr.StatusCode; try { hr.Close(); } catch { }
                if (code == 401) throw new Exception("Profile is private or key invalid");
                if (code == 403) throw new Exception("Invalid Web API key");
                if (code == 429) throw new Exception("Rate limited");
                throw new Exception("Steam error " + code);
            }
        }

        static string SteamId(string key)
        {
            var id = (Accounts.Get("steam", "steamid") ?? "").Trim().TrimEnd('/');
            int slash = id.LastIndexOf('/'); if (slash >= 0) id = id.Substring(slash + 1);   // pasted profile URL
            if (id.Length == 17 && id.All(char.IsDigit)) return id;
            lock (vanity) { string v; if (vanity.TryGetValue(id, out v)) return v; }
            var o = J.Parse(Get("https://api.steampowered.com/ISteamUser/ResolveVanityURL/v1/?key=" + Http.Esc(key) + "&vanityurl=" + Http.Esc(id)));
            var sid = J.Str(o, "response.steamid");
            if (sid == "") throw new Exception("SteamID not found");
            lock (vanity) vanity[id] = sid;
            return sid;
        }

        public static GamWebSnap FetchFriends()
        {
            var key = Accounts.Get("steam", "key"); var sid = SteamId(key);
            var fl = J.Parse(Get("https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key=" + Http.Esc(key) + "&steamid=" + sid + "&relationship=friend"));
            var ids = J.Arr(fl, "friendslist.friends") ?? new object[0];
            var all = ids.Cast<object>().Select(x => J.Str(x, "steamid")).Where(x => x != "").ToList();
            var s = new GamWebSnap { Total = all.Count };
            for (int i = 0; i < all.Count && i < 500; i += 100)
            {
                var o = J.Parse(Get("https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=" + Http.Esc(key) + "&steamids=" + string.Join(",", all.Skip(i).Take(100))));
                foreach (var p in J.Arr(o, "response.players") ?? new object[0])
                {
                    int st = (int)J.Num(p, "personastate");
                    var f = new GamFriend { Name = J.Str(p, "personaname"), State = st, Game = J.Str(p, "gameextrainfo"), Avatar = J.Str(p, "avatarmedium"), GameId = (long)J.Num(p, "gameid") };
                    if (st != 0 || f.Game != "") s.Friends.Add(f);
                }
            }
            s.Friends = s.Friends.OrderBy(f => f.Game == "" ? 1 : 0).ThenBy(f => f.State == 1 ? 0 : 1).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            s.Seal(); return s;
        }

        public static GamWebSnap FetchPlaytime()
        {
            var key = Accounts.Get("steam", "key"); var sid = SteamId(key);
            var o = J.Parse(Get("https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/?key=" + Http.Esc(key) + "&steamid=" + sid + "&count=12"));
            var s = new GamWebSnap();
            foreach (var g in J.Arr(o, "response.games") ?? new object[0])
                s.Plays.Add(new GamPlayItem { AppId = (int)J.Num(g, "appid"), Name = J.Str(g, "name"), Min2w = J.Num(g, "playtime_2weeks"), MinTotal = J.Num(g, "playtime_forever") });
            s.Plays = s.Plays.OrderByDescending(p => p.Min2w).ToList();
            s.Seal(); return s;
        }

        public static GamWebSnap SampleFriends()
        {
            var s = new GamWebSnap { Sample = true, Total = 38 };
            string[,] f = { { "Nova", "Hogwarts Legacy" }, { "kestrel", "Counter-Strike 2" }, { "Mira_", "Baldur's Gate 3" }, { "atlas", "" }, { "Juno", "" }, { "sable", "" }, { "Orion", "" } };
            int[] st = { 1, 1, 1, 1, 3, 1, 3 };
            for (int i = 0; i < f.GetLength(0); i++) s.Friends.Add(new GamFriend { Name = f[i, 0], Game = f[i, 1], State = st[i] });
            s.Seal(); return s;
        }
        public static GamWebSnap SamplePlaytime()
        {
            var s = new GamWebSnap { Sample = true };
            s.Plays.Add(new GamPlayItem { AppId = 990080, Name = "Hogwarts Legacy", Min2w = 742, MinTotal = 3852 });
            s.Plays.Add(new GamPlayItem { AppId = 3489700, Name = "Stellar Blade", Min2w = 415, MinTotal = 1326 });
            s.Plays.Add(new GamPlayItem { AppId = 242640, Name = "Styx: Master of Shadows", Min2w = 188, MinTotal = 750 });
            s.Plays.Add(new GamPlayItem { AppId = 1496790, Name = "Gotham Knights", Min2w = 96, MinTotal = 1080 });
            s.Plays.Add(new GamPlayItem { AppId = 2651280, Name = "Marvel's Spider-Man 2", Min2w = 41, MinTotal = 2460 });
            s.Seal(); return s;
        }
    }

    /// <summary>Binds a Hub key that depends on whether the "steam" account is connected; rebinds on config changes.</summary>
    public abstract class GamWebWidget : Widget
    {
        protected abstract string Key { get; }
        protected abstract int Interval { get; }
        protected abstract GamWebSnap Fetch();
        protected abstract GamWebSnap MakeSample();
        string bound, fp;
        GamWebSnap last;

        static string Fingerprint() { return (Accounts.Get("steam", "key") ?? "") + "|" + (Accounts.Get("steam", "steamid") ?? ""); }
        public override void OnStart() { GamWeb.Init(); Bind(); AppConfig.Changed += OnCfg; }
        public override void OnStop() { AppConfig.Changed -= OnCfg; if (bound != null) Hub.Unsubscribe(bound, this); bound = null; }
        void OnCfg() { if (Fingerprint() == fp) return; if (bound != null) Hub.Unsubscribe(bound, this); Bind(); Hub.Refresh(bound); Invalidate(); }
        void Bind()
        {
            fp = Fingerprint();
            bool conn = HasAccount;
            bound = Key + (conn ? "" : "#sample");
            var self = this;
            Hub.Define(bound, conn ? Interval : 24 * 3600 * 1000, () =>
            {
                if (!conn) return self.MakeSample();
                try { var s = self.Fetch(); self.last = s; return s; }
                catch (Exception e)
                {
                    var prev = self.last;
                    var s = new GamWebSnap { Error = e.Message, Total = prev != null ? prev.Total : 0 };
                    if (prev != null) { s.Friends = prev.Friends; s.Plays = prev.Plays; }
                    s.Seal(); return s;
                }
            });
            Hub.Subscribe(bound, this);
        }
        protected GamWebSnap Data
        {
            get
            {
                var d = bound != null ? Hub.Get<GamWebSnap>(bound) : null;
                if (d != null) return d;
                return HasAccount ? null : MakeSample();
            }
        }
        protected void RefreshNow() { if (bound != null) Hub.Refresh(bound); }

        protected void Badge(DrawCtx c, GamWebSnap d, RectangleF anchorRight)
        {
            if (d == null) return;
            string t = d.Sample ? "Sample" : d.Error != null ? "Error" : null; if (t == null) return;
            var f = c.F(10); float w = Ui.MeasureW(c.G, t, f) + c.S(12);
            var r = new RectangleF(anchorRight.Right - w, anchorRight.Y, w, c.S(17));
            Ui.FillRound(c.G, r, r.Height / 2, d.Sample ? c.T.Track : Ui.WithA(c.T.Bad, 50));
            Ui.Text(c.G, t, f, d.Sample ? c.T.TextDim : c.T.Bad, r, 0);
        }
        protected void Footer(DrawCtx c, GamWebSnap d)
        {
            var b = c.Bounds; float y = b.Bottom - c.S(44);
            if (d != null && d.Sample)
            {
                Ui.Text(c.G, "Showing sample data", c.F(11), c.T.TextDim, new RectangleF(b.X + c.S(16), y, b.Width - c.S(170), c.S(30)));
                c.Button(new RectangleF(b.Right - c.S(16) - c.S(140), y, c.S(140), c.S(30)), "Connect in Settings", () => Host.OpenSettings("accounts"), true);
            }
            else
            {
                string s = d == null ? "Loading…" : d.Error != null ? d.Error : "Updated " + GamUtil.Ago(d.At);
                Ui.Text(c.G, s, c.F(11), d != null && d.Error != null ? c.T.Bad : c.T.TextFaint, new RectangleF(b.X + c.S(16), y, b.Width - c.S(110), c.S(30)));
                c.Button(new RectangleF(b.Right - c.S(16) - c.S(84), y, c.S(84), c.S(30)), "Refresh", RefreshNow, false, Icons.Refresh);
            }
        }
    }

    [Widget("steam-friends", "Steam Friends", "Games", "How many Steam friends are online and who's in a game.", Account = "steam", Data = DataKind.NeedsAccount)]
    public class GamSteamFriendsWidget : GamWebWidget
    {
        static GamSteamFriendsWidget() { GamWeb.Init(); }
        protected override string Key { get { return "gam.steam.friends"; } }
        protected override int Interval { get { return 5 * 60 * 1000; } }
        protected override GamWebSnap Fetch() { return GamWeb.FetchFriends(); }
        protected override GamWebSnap MakeSample() { return GamWeb.SampleFriends(); }

        public override int Measure(DrawCtx c) { return c.Si(128); }
        public override void Draw(DrawCtx c)
        {
            var d = Data; var b = c.Bounds; var T = c.T;
            float ic = c.S(22);
            var ir = new RectangleF(b.X + c.S(10), b.Y + (b.Height - ic) / 2, ic, ic);
            GamDraw.SteamLogo(c.G, ir, T.Dark, T);
            float x = ir.Right + c.S(9), w = b.Right - c.S(10) - x;
            string a = d == null ? "Friends" : d.Online + " online";
            string s2 = d == null ? "Loading…" : d.InGame > 0 ? d.InGame + " in game" : (d.Error != null ? "Offline data" : "Nobody in game");
            float ha = c.S(15), hb = c.S(13.5f), y = b.Y + (b.Height - ha - hb) / 2;
            Ui.Text(c.G, a, c.FS(12), T.Text, new RectangleF(x, y, w, ha));
            if (d != null && d.InGame > 0) Ui.FillCircle(c.G, new PointF(x + c.S(3), y + ha + hb / 2), c.S(2.6f), T.Good);
            float o = d != null && d.InGame > 0 ? c.S(9) : 0;
            Ui.Text(c.G, s2, c.F(10.5f), T.TextDim, new RectangleF(x + o, y + ha, w - o, hb));
            if (d != null && d.Sample) Ui.FillCircle(c.G, new PointF(b.Right - c.S(7), b.Y + c.S(7)), c.S(2.5f), T.Warn);
        }

        public override Size PanelSize(float scale) { return new Size((int)(330 * scale), (int)(392 * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds; var g = c.G; var T = c.T;
            float y = c.PanelHeader("Steam Friends", null, Icons.People);
            Badge(c, d, new RectangleF(b.X, b.Y + c.S(14), b.Width - c.S(16), c.S(18)));
            float x0 = b.X + c.S(16), w = b.Width - c.S(32);
            if (d == null) { Ui.Text(g, "Loading…", c.F(12), T.TextDim, new RectangleF(b.X, y + c.S(60), b.Width, c.S(20)), 0); Footer(c, d); return; }
            // stats row
            float sw = (w - c.S(16)) / 3;
            Stat(c, new RectangleF(x0, y, sw, c.S(50)), d.Online.ToString(), "Online", T.Good);
            Stat(c, new RectangleF(x0 + sw + c.S(8), y, sw, c.S(50)), d.InGame.ToString(), "In game", GamSteamWidget.SteamAccent(T));
            Stat(c, new RectangleF(x0 + 2 * (sw + c.S(8)), y, sw, c.S(50)), d.Total.ToString(), "Friends", T.TextDim);
            y += c.S(62);
            float bottom = b.Bottom - c.S(54);
            if (d.Friends.Count == 0) Ui.Text(g, "No friends online right now", c.F(12), T.TextDim, new RectangleF(b.X, y + c.S(30), b.Width, c.S(20)), 0);
            foreach (var f in d.Friends)
            {
                if (y + c.S(36) > bottom) break;
                var av = new RectangleF(x0, y + c.S(2), c.S(30), c.S(30));
                if (f.Avatar != "") GamImg.Draw(c, f.Avatar, av, c.S(8), f.Name, this);
                else GamImg.Draw(c, null, av, c.S(8), f.Name, this);
                var sc = f.Game != "" ? GamSteamWidget.SteamAccent(T) : f.State == 1 ? T.Good : T.Warn;
                var dot = new PointF(av.Right - c.S(2), av.Bottom - c.S(2));
                Ui.FillCircle(g, dot, c.S(5), T.PanelBg.A > 200 ? T.PanelBg : (T.Dark ? Color.FromArgb(255, 24, 24, 30) : Color.White));
                Ui.FillCircle(g, dot, c.S(3.4f), sc);
                float tx = av.Right + c.S(10), tw = b.Right - c.S(16) - tx;
                Ui.Text(g, f.Name, c.FS(12.5f), T.Text, new RectangleF(tx, y + c.S(1), tw, c.S(17)));
                string st = f.Game != "" ? "Playing " + f.Game : StateName(f.State);
                Ui.Text(g, st, c.F(11), f.Game != "" ? sc : T.TextDim, new RectangleF(tx, y + c.S(18), tw, c.S(15)));
                y += c.S(38);
            }
            Footer(c, d);
        }
        static string StateName(int s) { switch (s) { case 1: return "Online"; case 2: return "Busy"; case 3: return "Away"; case 4: return "Snooze"; case 5: return "Looking to trade"; case 6: return "Looking to play"; default: return "Offline"; } }
        static void Stat(DrawCtx c, RectangleF r, string v, string label, Color col)
        {
            Ui.FillRound(c.G, r, c.S(10), c.T.Tile);
            Ui.Text(c.G, v, c.FS(17), col == c.T.TextDim ? c.T.Text : col, new RectangleF(r.X, r.Y + c.S(6), r.Width, c.S(22)), 0);
            Ui.Text(c.G, label, c.F(10.5f), c.T.TextDim, new RectangleF(r.X, r.Y + c.S(29), r.Width, c.S(14)), 0);
        }
        public override void AddMenuItems(List<KeyValuePair<string, Action>> items)
        {
            items.Add(new KeyValuePair<string, Action>("Open Friends list", () => GamUtil.Launch("steam://open/friends")));
        }
    }

    [Widget("steam-playtime", "Steam Playtime", "Games", "Hours played in the last two weeks with a bar per game.", Account = "steam", Data = DataKind.NeedsAccount)]
    public class GamSteamPlaytimeWidget : GamWebWidget
    {
        static GamSteamPlaytimeWidget() { GamWeb.Init(); }
        protected override string Key { get { return "gam.steam.playtime"; } }
        protected override int Interval { get { return 15 * 60 * 1000; } }
        protected override GamWebSnap Fetch() { return GamWeb.FetchPlaytime(); }
        protected override GamWebSnap MakeSample() { return GamWeb.SamplePlaytime(); }

        public override int Measure(DrawCtx c) { return c.Si(150); }
        public override void Draw(DrawCtx c)
        {
            var d = Data; var b = c.Bounds; var T = c.T;
            float ic = c.S(22);
            var ir = new RectangleF(b.X + c.S(10), b.Y + (b.Height - ic) / 2, ic, ic);
            GamDraw.SteamLogo(c.G, ir, T.Dark, T);
            // mini bars at the right
            var vals = d == null ? new List<double>() : d.Plays.Take(5).Select(p => p.Min2w).ToList();
            float bw = c.S(28);
            var br = new RectangleF(b.Right - c.S(10) - bw, b.Y + c.S(9), bw, b.Height - c.S(18));
            if (vals.Count > 0) Ui.Bars(c.G, br, vals, GamSteamWidget.SteamAccent(T), c.S(2), double.NaN, 0, T.Good);
            float x = ir.Right + c.S(9), w = br.X - c.S(8) - x;
            float ha = c.S(15), hb = c.S(13.5f), y = b.Y + (b.Height - ha - hb) / 2;
            Ui.Text(c.G, d == null ? "Playtime" : GamUtil.Hours(d.Min2w), c.FS(12), T.Text, new RectangleF(x, y, w, ha));
            Ui.Text(c.G, d == null ? "Loading…" : "last 2 weeks", c.F(10.5f), T.TextDim, new RectangleF(x, y + ha, w, hb));
            if (d != null && d.Sample) Ui.FillCircle(c.G, new PointF(b.Right - c.S(5), b.Y + c.S(5)), c.S(2.5f), T.Warn);
        }

        public override Size PanelSize(float scale) { return new Size((int)(340 * scale), (int)(380 * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds; var g = c.G; var T = c.T;
            float y = c.PanelHeader("Steam Playtime", null, Icons.Game);
            Badge(c, d, new RectangleF(b.X, b.Y + c.S(14), b.Width - c.S(16), c.S(18)));
            float x0 = b.X + c.S(16), w = b.Width - c.S(32);
            if (d == null) { Ui.Text(g, "Loading…", c.F(12), T.TextDim, new RectangleF(b.X, y + c.S(60), b.Width, c.S(20)), 0); Footer(c, d); return; }
            Ui.Text(g, GamUtil.Hours(d.Min2w), c.FS(26), T.Text, new RectangleF(x0, y, w, c.S(32)));
            Ui.Text(g, "played in the last 2 weeks · " + d.Plays.Count + (d.Plays.Count == 1 ? " game" : " games"), c.F(11.5f), T.TextDim, new RectangleF(x0, y + c.S(32), w, c.S(16)));
            y += c.S(60);
            double mx = Math.Max(1, d.Plays.Select(p => p.Min2w).DefaultIfEmpty(1).Max());
            var acc = GamSteamWidget.SteamAccent(T);
            float bottom = b.Bottom - c.S(54);
            if (d.Plays.Count == 0) Ui.Text(g, "No games played in the last two weeks", c.F(12), T.TextDim, new RectangleF(b.X, y + c.S(30), b.Width, c.S(20)), 0);
            int i = 0;
            foreach (var p in d.Plays)
            {
                if (y + c.S(40) > bottom) break;
                var ir = new RectangleF(x0, y + c.S(2), c.S(60), c.S(28));
                GamImg.Draw(c, GamSteam.Header(p.AppId), ir, c.S(5), p.Name, this);
                float tx = ir.Right + c.S(10), tw = b.Right - c.S(16) - tx;
                Ui.Text(g, p.Name, c.FS(12), T.Text, new RectangleF(tx, y, tw - c.S(56), c.S(16)));
                Ui.Text(g, GamUtil.Hours(p.Min2w), c.F(11.5f, true), T.Text, new RectangleF(tx, y, tw, c.S(16)), 1);
                var pr = new RectangleF(tx, y + c.S(21), tw, c.S(6));
                Ui.Progress(g, pr, p.Min2w / mx, i == 0 ? acc : Ui.WithA(acc, 170), T.Track);
                var id = p.AppId; c.Hit(new RectangleF(x0, y, w, c.S(34)), () => GamUtil.Launch("steam://nav/games/details/" + id));
                y += c.S(40); i++;
            }
            Footer(c, d);
        }
    }
}
