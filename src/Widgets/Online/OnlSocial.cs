// Social widgets: X Profile, TikTok, YouTube, Instagram. All follow one pattern (OnlSocialWidget):
// fetch the follower count with the user's own official token/key, keep a small daily history on disk
// (for the 30-day sparkline and +today / +7d deltas), and show realistic sample data until connected.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NovaBar
{
    public abstract class OnlSocialWidget : OnlWidget
    {
        protected abstract string Brand { get; }
        protected abstract string Unit { get; }            // "followers" / "subscribers"
        protected abstract string[] SecondaryKeys { get; }
        protected abstract string[] SecondaryLabels { get; }
        protected abstract OnlData FetchStats();           // must set N["followers"], S["name"], S["handle"], S["url"]
        protected abstract int SampleBase { get; }
        protected virtual string FallbackUrl { get { return "https://" + Brand + ".com"; } }
        protected override int IntervalMs { get { return 15 * 60 * 1000; } }

        protected override OnlData Fetch()
        {
            var d = FetchStats();
            ApplyHistory(d, UpdateHistory(HubKey, d.Num("followers")));
            return d;
        }

        static SortedDictionary<DateTime, double> UpdateHistory(string key, double value)
        {
            var f = Path.Combine(Paths.CacheDir, "onl-hist-" + key.Replace(':', '_').Replace('/', '_').Replace('\\', '_') + ".json");
            var h = new SortedDictionary<DateTime, double>();
            try
            {
                if (File.Exists(f))
                {
                    var o = J.ParseObj(File.ReadAllText(f, Encoding.UTF8));
                    if (o != null) foreach (var kv in o) { DateTime dt; if (DateTime.TryParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) h[dt] = J.AsNum(kv.Value); }
                }
            }
            catch { }
            h[DateTime.Today] = value;
            foreach (var k in h.Keys.Where(x => x < DateTime.Today.AddDays(-120)).ToList()) h.Remove(k);
            try { File.WriteAllText(f, J.Stringify(h.ToDictionary(x => x.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x => (object)x.Value)), Encoding.UTF8); } catch { }
            return h;
        }

        static void ApplyHistory(OnlData d, SortedDictionary<DateTime, double> h)
        {
            var today = DateTime.Today; double now = d.Num("followers");
            Func<int, double?> at = days => { var k = h.Keys.Where(x => x <= today.AddDays(-days)).ToList(); return k.Count == 0 ? (double?)null : h[k[k.Count - 1]]; };
            var a1 = at(1); var a7 = at(7); var a30 = at(30);
            if (a1.HasValue) d.N["d1"] = now - a1.Value;
            if (a7.HasValue) d.N["d7"] = now - a7.Value;
            if (a30.HasValue) d.N["d30"] = now - a30.Value;
            d.Series = h.Where(x => x.Key > today.AddDays(-30)).Select(x => x.Value).ToArray();
        }

        protected override OnlData MakeSample()
        {
            var rng = new OnlUtil.Rng(OnlUtil.DaySeed(Brand));
            var ser = new double[30]; double v = SampleBase;
            for (int i = 29; i >= 0; i--) { ser[i] = Math.Round(v); v -= SampleBase * (0.0009 + rng.D() * 0.0022) - (rng.D() < 0.12 ? SampleBase * 0.001 : 0); }
            var d = SampleExtra();
            d.Series = ser; d.N["followers"] = ser[29];
            d.N["d1"] = ser[29] - ser[28]; d.N["d7"] = ser[29] - ser[22]; d.N["d30"] = ser[29] - ser[0];
            return d;
        }
        protected abstract OnlData SampleExtra();

        bool NoData(OnlData d) { return Loading(d) || (d.Error != null && !d.Has("followers")); }
        string Val(OnlData d) { return NoData(d) ? "—" : Ui.Compact(d.Num("followers")); }
        string Delta(OnlData d) { if (NoData(d) || !d.Has("d1") || d.Num("d1") == 0) return null; return OnlUtil.Signed(d.Num("d1")); }
        string ShortUnit { get { return Unit == "subscribers" ? "subs" : Unit; } }

        public override int Measure(DrawCtx c) { var d = Data; return OnlDraw.MeasureStat(c, Val(d), ShortUnit, d.Sample, Delta(d)); }
        public override void Draw(DrawCtx c)
        {
            var d = Data;
            OnlDraw.Stat(c, Brand, Val(d), ShortUnit, d.Sample, Delta(d), d.Num("d1") >= 0 ? c.T.Good : c.T.Bad);
        }

        public override Size PanelSize(float s) { return new Size((int)(360 * s), (int)(344 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            var handle = d.Str("handle");
            float y = OnlDraw.Header(c, Brand, d.Str("name", Meta.Name), handle != "" ? (handle.StartsWith("@") ? handle : "@" + handle) : null, d);
            // big number + unit
            var bf = c.FS(34); var v = Val(d); float vw = Ui.MeasureW(c.G, v, bf);
            float x = b.X + c.S(16);
            Ui.Text(c.G, v, bf, c.T.Text, new RectangleF(x - c.S(1), y - c.S(4), vw + c.S(4), c.S(42)));
            Ui.Text(c.G, Unit, c.F(12), c.T.TextDim, new RectangleF(x + vw + c.S(6), y + c.S(14), c.S(110), c.S(18)));
            // delta chips (right aligned)
            float cx = b.Right - c.S(16);
            foreach (var kv in new[] { new KeyValuePair<string, string>("d7", "7d"), new KeyValuePair<string, string>("d1", "today") })
            {
                if (!d.Has(kv.Key)) continue;
                var dv = d.Num(kv.Key); var txt = OnlUtil.Signed(dv) + " " + kv.Value;
                var f = c.FS(10.5f); float w = Ui.MeasureW(c.G, txt, f) + c.S(14), h = c.S(20);
                var col = dv > 0 ? c.T.Good : dv < 0 ? c.T.Bad : c.T.TextDim;
                var r = new RectangleF(cx - w, y + c.S(10), w, h);
                Ui.FillRound(c.G, r, h / 2, Color.FromArgb(c.T.Dark ? 38 : 28, col));
                Ui.Text(c.G, txt, f, c.T.Dark ? Ui.Mix(col, Color.White, 0.15) : Ui.Mix(col, Color.Black, 0.2), r, 0);
                cx -= w + c.S(6);
            }
            y += c.S(46);
            // 30-day sparkline
            var chart = new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(70));
            Ui.FillRound(c.G, chart, c.S(10), OnlDraw.CardBg(c.T));
            var inner = new RectangleF(chart.X + c.S(10), chart.Y + c.S(22), chart.Width - c.S(20), chart.Height - c.S(30));
            Ui.Text(c.G, "Last 30 days", c.F(10.5f), c.T.TextDim, new RectangleF(chart.X + c.S(10), chart.Y + c.S(5), c.S(120), c.S(15)));
            if (d.Has("d30")) Ui.Text(c.G, OnlUtil.Signed(d.Num("d30")), c.FS(10.5f), d.Num("d30") >= 0 ? c.T.Good : c.T.Bad, new RectangleF(chart.X, chart.Y + c.S(5), chart.Width - c.S(10), c.S(15)), 1);
            if (d.Series.Length >= 2) Ui.Sparkline(c.G, inner, d.Series, c.T.Accent, true, c.S(1.8f));
            else Ui.Text(c.G, "History builds up as NovaBar checks daily.", c.F(11), c.T.TextFaint, inner, 0);
            y = chart.Bottom + c.S(12);
            // secondary stats
            var keys = SecondaryKeys; var labels = SecondaryLabels;
            y = OnlDraw.Cards(c, y, keys.Select(k => NoData(d) || !d.Has(k) ? "—" : Ui.Compact(d.Num(k))).ToArray(), labels, null, 56);
            OnlDraw.ErrorLine(c, y - c.S(4), d);
            var url = d.Str("url", FallbackUrl);
            OnlDraw.Footer(c, d, "Open profile", url, RefreshNow, Host);
        }
    }

    // =====================================================================================
    [Widget("xprofile", "X Profile", "Social", "Followers on your X account, with daily change.",
        RefName = "X Profile", Account = "x", Data = DataKind.NeedsAccount)]
    public class OnlXWidget : OnlSocialWidget
    {
        static OnlXWidget()
        {
            Accounts.Define("x", "X (Twitter)", "Your @username and an app Bearer token from the X developer portal (Projects & Apps → Keys and tokens).",
                "https://developer.x.com/en/portal/dashboard", Accounts.F("user", "Username", false, "@handle"), Accounts.F("token", "Bearer token", true, "AAAA…"));
        }
        static string User { get { var u = Accounts.Get("x", "user"); return string.IsNullOrEmpty(u) ? "" : u.Trim().TrimStart('@'); } }
        protected override string HubKey { get { return "onl.x:" + User.ToLowerInvariant(); } }
        protected override bool IsConnected { get { return Accounts.Connected("x"); } }
        protected override string Brand { get { return "x"; } }
        protected override string Unit { get { return "followers"; } }
        protected override string[] SecondaryKeys { get { return new[] { "following", "posts", "listed" }; } }
        protected override string[] SecondaryLabels { get { return new[] { "Following", "Posts", "Listed" }; } }
        protected override int SampleBase { get { return 12840; } }
        protected override string FallbackUrl { get { return "https://x.com/" + User; } }
        protected override OnlData FetchStats()
        {
            var o = OnlNet.Json("https://api.twitter.com/2/users/by/username/" + Uri.EscapeDataString(User) + "?user.fields=public_metrics,name", "Authorization: Bearer " + Accounts.Get("x", "token").Trim());
            if (J.Obj(o, "data") == null) throw new OnlErr(J.Str(o, "errors.0.detail", "User not found"));
            var d = new OnlData();
            d.N["followers"] = J.Num(o, "data.public_metrics.followers_count");
            d.N["following"] = J.Num(o, "data.public_metrics.following_count");
            d.N["posts"] = J.Num(o, "data.public_metrics.tweet_count");
            d.N["listed"] = J.Num(o, "data.public_metrics.listed_count");
            d.S["name"] = J.Str(o, "data.name"); d.S["handle"] = J.Str(o, "data.username"); d.S["url"] = "https://x.com/" + J.Str(o, "data.username");
            return d;
        }
        protected override OnlData SampleExtra()
        {
            var d = new OnlData(); d.N["following"] = 412; d.N["posts"] = 3871; d.N["listed"] = 96;
            d.S["name"] = "Your Name"; d.S["handle"] = "yourhandle"; d.S["url"] = "https://x.com"; return d;
        }
    }

    // =====================================================================================
    [Widget("tiktok", "TikTok", "Social", "Followers on your TikTok profile.",
        RefName = "TikTok", Account = "tiktok", Data = DataKind.NeedsAccount)]
    public class OnlTikTokWidget : OnlSocialWidget
    {
        static OnlTikTokWidget()
        {
            Accounts.Define("tiktok", "TikTok", "A user access token from the TikTok Display API (scopes user.info.basic, user.info.stats). Tokens expire — paste a fresh one when the widget says so.",
                "https://developers.tiktok.com/doc/display-api-get-started", Accounts.F("token", "Access token", true, "act.…"));
        }
        protected override string HubKey { get { return "onl.tiktok"; } }
        protected override bool IsConnected { get { return Accounts.Connected("tiktok"); } }
        protected override string Brand { get { return "tiktok"; } }
        protected override string Unit { get { return "followers"; } }
        protected override string[] SecondaryKeys { get { return new[] { "following", "likes", "videos" }; } }
        protected override string[] SecondaryLabels { get { return new[] { "Following", "Likes", "Videos" }; } }
        protected override int SampleBase { get { return 48200; } }
        protected override string FallbackUrl { get { return "https://www.tiktok.com"; } }
        protected override OnlData FetchStats()
        {
            var o = OnlNet.Json("https://open.tiktokapis.com/v2/user/info/?fields=open_id,display_name,username,follower_count,following_count,likes_count,video_count,profile_deep_link",
                "Authorization: Bearer " + Accounts.Get("tiktok", "token").Trim());
            var code = J.Str(o, "error.code", "ok");
            if (code != "ok") throw new OnlErr(code == "access_token_invalid" ? "Token expired — paste a new one" : J.Str(o, "error.message", code));
            var u = J.Obj(o, "data.user"); if (u == null) throw new OnlErr("Unexpected response");
            var d = new OnlData();
            d.N["followers"] = J.Num(u, "follower_count"); d.N["following"] = J.Num(u, "following_count"); d.N["likes"] = J.Num(u, "likes_count"); d.N["videos"] = J.Num(u, "video_count");
            d.S["name"] = J.Str(u, "display_name"); d.S["handle"] = J.Str(u, "username");
            d.S["url"] = J.Str(u, "username") != "" ? "https://www.tiktok.com/@" + J.Str(u, "username") : J.Str(u, "profile_deep_link", "https://www.tiktok.com");
            return d;
        }
        protected override OnlData SampleExtra()
        {
            var d = new OnlData(); d.N["following"] = 318; d.N["likes"] = 1260000; d.N["videos"] = 214;
            d.S["name"] = "Your Name"; d.S["handle"] = "yourhandle"; d.S["url"] = "https://www.tiktok.com"; return d;
        }
    }

    // =====================================================================================
    [Widget("youtube", "YouTube", "Social", "Subscribers on your YouTube channel, via your own API key.",
        RefName = "YouTube", Account = "youtube", Data = DataKind.NeedsAccount)]
    public class OnlYouTubeWidget : OnlSocialWidget
    {
        static OnlYouTubeWidget()
        {
            Accounts.Define("youtube", "YouTube", "A YouTube Data API v3 key (Google Cloud console → Credentials) and your channel @handle or channel id (UC…).",
                "https://console.cloud.google.com/apis/library/youtube.googleapis.com", Accounts.F("key", "API key", true, "AIza…"), Accounts.F("channel", "Channel @handle or id", false, "@yourchannel"));
        }
        static string Channel { get { var c = Accounts.Get("youtube", "channel"); return string.IsNullOrEmpty(c) ? "" : c.Trim(); } }
        protected override string HubKey { get { return "onl.youtube:" + Channel.ToLowerInvariant(); } }
        protected override bool IsConnected { get { return Accounts.Connected("youtube"); } }
        protected override string Brand { get { return "youtube"; } }
        protected override string Unit { get { return "subscribers"; } }
        protected override string[] SecondaryKeys { get { return new[] { "views", "videos" }; } }
        protected override string[] SecondaryLabels { get { return new[] { "Total views", "Videos" }; } }
        protected override int SampleBase { get { return 23900; } }
        protected override string FallbackUrl { get { return "https://www.youtube.com/" + (Channel.StartsWith("@") ? Channel : "channel/" + Channel); } }
        protected override OnlData FetchStats()
        {
            var ch = Channel;
            if (ch.Contains("youtube.com/")) ch = ch.Substring(ch.IndexOf("youtube.com/") + 12).Replace("channel/", "").Trim('/');
            bool isId = ch.StartsWith("UC") && ch.Length == 24;
            var url = "https://www.googleapis.com/youtube/v3/channels?part=statistics,snippet&" + (isId ? "id=" + Uri.EscapeDataString(ch) : "forHandle=" + Uri.EscapeDataString(ch.StartsWith("@") ? ch : "@" + ch))
                      + "&key=" + Uri.EscapeDataString(Accounts.Get("youtube", "key").Trim());
            var o = OnlNet.Json(url);
            var it = J.Obj(o, "items.0"); if (it == null) throw new OnlErr("Channel not found");
            var d = new OnlData();
            d.N["followers"] = J.Num(it, "statistics.subscriberCount"); d.N["views"] = J.Num(it, "statistics.viewCount"); d.N["videos"] = J.Num(it, "statistics.videoCount");
            if (J.Bool(it, "statistics.hiddenSubscriberCount")) d.S["hidden"] = "1";
            d.S["name"] = J.Str(it, "snippet.title"); d.S["handle"] = J.Str(it, "snippet.customUrl");
            d.S["url"] = d.S["handle"] != "" ? "https://www.youtube.com/" + d.S["handle"] : "https://www.youtube.com/channel/" + J.Str(it, "id");
            return d;
        }
        protected override OnlData SampleExtra()
        {
            var d = new OnlData(); d.N["views"] = 3480000; d.N["videos"] = 186;
            d.S["name"] = "Your Channel"; d.S["handle"] = "@yourchannel"; d.S["url"] = "https://www.youtube.com"; return d;
        }
    }

    // =====================================================================================
    [Widget("instagram", "Instagram", "Social", "Followers on your Instagram account, with an official token.",
        RefName = "Instagram", Account = "instagram", Data = DataKind.NeedsAccount)]
    public class OnlInstagramWidget : OnlSocialWidget
    {
        static OnlInstagramWidget()
        {
            Accounts.Define("instagram", "Instagram", "A long-lived access token from the Instagram API with Instagram Login (professional account, scope instagram_business_basic). Long-lived tokens last 60 days.",
                "https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login", Accounts.F("token", "Access token", true, "IGAA…"));
        }
        protected override string HubKey { get { return "onl.instagram"; } }
        protected override bool IsConnected { get { return Accounts.Connected("instagram"); } }
        protected override string Brand { get { return "instagram"; } }
        protected override string Unit { get { return "followers"; } }
        protected override string[] SecondaryKeys { get { return new[] { "following", "posts" }; } }
        protected override string[] SecondaryLabels { get { return new[] { "Following", "Posts" }; } }
        protected override int SampleBase { get { return 8640; } }
        protected override string FallbackUrl { get { return "https://www.instagram.com"; } }
        protected override OnlData FetchStats()
        {
            var o = OnlNet.Json("https://graph.instagram.com/v21.0/me?fields=user_id,username,name,followers_count,follows_count,media_count&access_token=" + Uri.EscapeDataString(Accounts.Get("instagram", "token").Trim()));
            if (J.Get(o, "followers_count") == null) throw new OnlErr(J.Str(o, "error.message", "Token lacks follower access"));
            var d = new OnlData();
            d.N["followers"] = J.Num(o, "followers_count"); d.N["following"] = J.Num(o, "follows_count"); d.N["posts"] = J.Num(o, "media_count");
            d.S["name"] = J.Str(o, "name", J.Str(o, "username")); d.S["handle"] = J.Str(o, "username"); d.S["url"] = "https://www.instagram.com/" + J.Str(o, "username");
            return d;
        }
        protected override OnlData SampleExtra()
        {
            var d = new OnlData(); d.N["following"] = 521; d.N["posts"] = 312;
            d.S["name"] = "Your Name"; d.S["handle"] = "yourhandle"; d.S["url"] = "https://www.instagram.com"; return d;
        }
    }
}
