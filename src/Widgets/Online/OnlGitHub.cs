// GitHub widgets: GitHub Activity (contribution grid + streak), GitHub (unread notifications), GitHub Overview.
// One shared account "github": username (public) + personal access token.
// GitHub Activity works with the username alone (scrapes the public contributions calendar); with a token it uses GraphQL.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NovaBar
{
    public static class OnlGh
    {
        public static void Define()
        {
            Accounts.Define("github", "GitHub",
                "Username is enough for the contribution grid. Add a personal access token (classic, scopes: notifications + repo, or a fine-grained token with read access) for notifications, pull requests and stars.",
                "https://github.com/settings/tokens",
                Accounts.F("user", "Username", false, "octocat"), Accounts.F("token", "Personal access token", true, "ghp_\u2026"));
        }
        public static string User { get { var u = Accounts.Get("github", "user"); return string.IsNullOrEmpty(u) ? null : u.Trim().TrimStart('@'); } }
        public static string Token { get { var t = Accounts.Get("github", "token"); return string.IsNullOrEmpty(t) ? null : t.Trim(); } }
        public static string[] H()
        {
            return new[] { "Authorization: Bearer " + Token, "Accept: application/vnd.github+json", "X-GitHub-Api-Version: 2022-11-28" };
        }

        /// <summary>API subject URL → browser URL.</summary>
        public static string HtmlUrl(string apiUrl, string repoHtml)
        {
            if (string.IsNullOrEmpty(apiUrl)) return string.IsNullOrEmpty(repoHtml) ? "https://github.com/notifications" : repoHtml;
            var u = apiUrl.Replace("https://api.github.com/repos/", "https://github.com/");
            u = u.Replace("/pulls/", "/pull/").Replace("/commits/", "/commit/");
            if (u.Contains("/releases/")) u = (repoHtml ?? u) + "/releases";
            return u;
        }

        public static List<OnlItem> Notifications()
        {
            var arr = OnlNet.Json("https://api.github.com/notifications?per_page=50", H()) as System.Collections.IList;
            if (arr == null) throw new OnlErr("Unexpected response");
            var l = new List<OnlItem>();
            foreach (var n in arr)
            {
                var type = J.Str(n, "subject.type");
                l.Add(new OnlItem
                {
                    Title = J.Str(n, "subject.title"),
                    Sub = J.Str(n, "repository.full_name") + " \u00b7 " + Pretty(type),
                    Url = HtmlUrl(J.Str(n, "subject.url"), J.Str(n, "repository.html_url")),
                    When = J.Str(n, "updated_at"),
                    Tag = J.Str(n, "reason"),
                    Kind = KindOf(type)
                });
            }
            return l;
        }
        static string Pretty(string type)
        {
            switch (type) { case "PullRequest": return "Pull request"; case "CheckSuite": return "Checks"; case "RepositoryVulnerabilityAlert": return "Security alert"; default: return type; }
        }
        public static int KindOf(string type)
        {
            switch (type) { case "PullRequest": return 1; case "Issue": return 2; case "Release": return 3; case "CheckSuite": return 4; case "Discussion": return 5; default: return 0; }
        }
        public static void KindLead(DrawCtx c, RectangleF r, int kind)
        {
            Color col = kind == 1 ? c.T.Good : kind == 2 ? c.T.Accent : kind == 4 ? c.T.Bad : kind == 3 ? c.T.Warn : c.T.TextDim;
            if (kind == 1) { OnlDraw.PrGlyph(c.G, Ui.Inset(r, c.S(1), c.S(1)), col); return; }
            string g = kind == 2 ? "\uE91F" : kind == 3 ? Icons.Flag : kind == 4 ? Icons.Warning : kind == 5 ? Icons.Chat : Icons.Bell;
            if (kind == 2) { using (var p = new Pen(col, c.S(1.4f))) c.G.DrawEllipse(p, Ui.Inset(r, c.S(2), c.S(2))); Ui.FillCircle(c.G, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), c.S(1.6f), col); return; }
            Ui.Icon(c.G, g, r, col, c.S(12));
        }

        public static List<OnlItem> SampleNotifications()
        {
            var now = DateTime.UtcNow;
            Func<string, string, int, string, double, OnlItem> it = (t, repo, k, url, minsAgo) => new OnlItem { Title = t, Sub = repo + " \u00b7 " + (k == 1 ? "Pull request" : k == 2 ? "Issue" : k == 3 ? "Release" : k == 4 ? "Checks" : "Discussion"), Kind = k, Url = url, When = OnlUtil.Iso(now.AddMinutes(-minsAgo)), Tag = "review_requested" };
            return new List<OnlItem>
            {
                it("Add keyboard navigation to the command palette", "acme/web-app", 1, "https://github.com/notifications", 14),
                it("Crash when opening settings with an empty profile", "acme/desktop", 2, "https://github.com/notifications", 52),
                it("CI failed on main: build-windows", "acme/desktop", 4, "https://github.com/notifications", 95),
                it("v2.4.0", "vercel/next.js", 3, "https://github.com/notifications", 260),
                it("Bump typescript from 5.6.2 to 5.7.1", "acme/web-app", 1, "https://github.com/notifications", 410),
                it("RFC: plugin API for custom widgets", "acme/desktop", 5, "https://github.com/notifications", 1500),
                it("Dark mode colors are off in the sidebar", "acme/web-app", 2, "https://github.com/notifications", 2900),
            };
        }
    }

    // =====================================================================================
    [Widget("github-activity", "GitHub Activity", "Online & Mail", "Your contribution grid and current streak. Only needs your public username.",
        RefName = "GitHub Activity", Account = "github", Data = DataKind.NeedsAccount)]
    public class OnlGitHubActivityWidget : OnlWidget
    {
        static OnlGitHubActivityWidget() { OnlGh.Define(); }
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Number("weeks", "Weeks shown on the bar", 16, 6, 30) }; } }
        protected override string HubKey { get { return "onl.gh.contrib:" + (OnlGh.User ?? "").ToLowerInvariant() + (OnlGh.Token != null ? ":t" : ""); } }
        protected override bool IsConnected { get { return OnlGh.User != null; } }
        protected override int IntervalMs { get { return 15 * 60 * 1000; } }

        protected override OnlData Fetch()
        {
            var user = OnlGh.User;
            var dates = new List<DateTime>(); var counts = new List<double>(); var levels = new List<int>();
            if (OnlGh.Token != null)
            {
                const string q = "query($login:String!){user(login:$login){contributionsCollection{contributionCalendar{totalContributions weeks{contributionDays{date contributionCount contributionLevel}}}}}}";
                var body = J.Stringify(new Dictionary<string, object> { { "query", q }, { "variables", new Dictionary<string, object> { { "login", user } } } });
                var o = OnlNet.PostJson("https://api.github.com/graphql", body, OnlGh.H());
                var weeks = J.Arr(o, "data.user.contributionsCollection.contributionCalendar.weeks");
                if (weeks == null) throw new OnlErr(J.Get(o, "errors") != null ? "User not found" : "Unexpected response");
                foreach (var w in weeks) foreach (var d in OnlUtil.Arr(w, "contributionDays"))
                    {
                        DateTime dt; if (!DateTime.TryParseExact(J.Str(d, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) continue;
                        dates.Add(dt); counts.Add(J.Num(d, "contributionCount"));
                        var lv = J.Str(d, "contributionLevel");
                        levels.Add(lv == "FIRST_QUARTILE" ? 1 : lv == "SECOND_QUARTILE" ? 2 : lv == "THIRD_QUARTILE" ? 3 : lv == "FOURTH_QUARTILE" ? 4 : 0);
                    }
            }
            else
            {
                var html = OnlNet.Req("GET", "https://github.com/users/" + Uri.EscapeDataString(user) + "/contributions", null, "Accept: text/html");
                var tips = new Dictionary<string, double>();
                foreach (Match m in Regex.Matches(html, "<tool-tip[^>]*?for=\"([^\"]+)\"[^>]*>([^<]*)</tool-tip>"))
                {
                    var mm = Regex.Match(m.Groups[2].Value, "^\\s*(\\d[\\d,]*)");
                    tips[m.Groups[1].Value] = mm.Success ? double.Parse(mm.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture) : 0;
                }
                var rows = new List<KeyValuePair<DateTime, KeyValuePair<double, int>>>();
                foreach (Match m in Regex.Matches(html, "<td[^>]*ContributionCalendar-day[^>]*>"))
                {
                    var tag = m.Value;
                    var dm = Regex.Match(tag, "data-date=\"(\\d{4}-\\d{2}-\\d{2})\""); if (!dm.Success) continue;
                    var im = Regex.Match(tag, "\\sid=\"([^\"]+)\""); var lm = Regex.Match(tag, "data-level=\"(\\d)\"");
                    DateTime dt; if (!DateTime.TryParseExact(dm.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) continue;
                    double cnt = 0; if (im.Success) tips.TryGetValue(im.Groups[1].Value, out cnt);
                    int lv = lm.Success ? lm.Groups[1].Value[0] - '0' : 0;
                    if (cnt == 0 && lv > 0) cnt = lv; // tooltip missing: keep the cell visible
                    rows.Add(new KeyValuePair<DateTime, KeyValuePair<double, int>>(dt, new KeyValuePair<double, int>(cnt, lv)));
                }
                if (rows.Count == 0) throw new OnlErr("User not found");
                foreach (var r in rows.OrderBy(x => x.Key)) { dates.Add(r.Key); counts.Add(r.Value.Key); levels.Add(r.Value.Value); }
            }
            if (dates.Count == 0) throw new OnlErr("No contribution data");
            // drop future days (GraphQL returns the full current week)
            var today = DateTime.Today.AddDays(1);
            while (dates.Count > 1 && dates[dates.Count - 1] > today) { dates.RemoveAt(dates.Count - 1); counts.RemoveAt(counts.Count - 1); levels.RemoveAt(levels.Count - 1); }
            return Build(dates[0], counts.ToArray(), levels.ToArray(), user);
        }

        static OnlData Build(DateTime start, double[] counts, int[] levels, string user)
        {
            var d = new OnlData();
            d.Series = counts; d.Levels = levels;
            d.S["start"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            d.S["user"] = user;
            int cur, lng; OnlDraw.Streaks(counts, out cur, out lng);
            d.N["total"] = counts.Sum(); d.N["cur"] = cur; d.N["long"] = lng;
            d.N["today"] = counts.Length > 0 ? counts[counts.Length - 1] : 0;
            d.N["week"] = counts.Skip(Math.Max(0, counts.Length - 7)).Sum();
            return d;
        }

        protected override OnlData MakeSample()
        {
            var counts = OnlDraw.SampleDays(OnlUtil.DaySeed("ghact"), 365, 0.62, 0.35);
            return Build(DateTime.Today.AddDays(-364), counts, OnlDraw.LevelsFor(counts), "octocat");
        }

        int Pitch(DrawCtx c) { return Math.Max(3, (int)Math.Floor((c.Bounds.Height - c.S(5)) / 7f)); }
        int Weeks { get { return (int)Math.Max(6, Math.Min(30, SNum("weeks"))); } }

        public override int Measure(DrawCtx c)
        {
            var d = Data;
            float w = c.S(10) + Pitch(c) * Weeks + c.S(9) + Ui.MeasureW(c.G, StreakVal(d), OnlDraw.ValFont(c)) + c.S(4) + Ui.MeasureW(c.G, "day streak", OnlDraw.LblFont(c));
            if (d.Sample) w += c.S(6) + OnlDraw.BadgeW(c);
            return (int)Math.Ceiling(w + c.S(11));
        }
        static string StreakVal(OnlData d) { return d.Levels.Length == 0 ? "\u2014" : ((int)d.Num("cur")).ToString(); }

        public override void Draw(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            int p = Pitch(c), weeks = Weeks;
            float gh = p * 7, gx = b.X + c.S(10), gy = (float)Math.Round(b.Y + (b.Height - gh) / 2f);
            var gr = new RectangleF((float)Math.Round(gx), gy, p * weeks, gh);
            if (d.Levels.Length > 0)
            {
                int hi; RectangleF used; var m = c.Mouse; c.Mouse = new Point(-1, -1);
                OnlDraw.Grid(c, gr, d.Levels, FirstDow(d), weeks, out hi, out used);
                c.Mouse = m;
            }
            else
            {
                for (int col = 0; col < weeks; col++) for (int row = 0; row < 7; row++)
                        c.G.FillRectangle(Ui.B(OnlDraw.Level(c.T, 0)), gr.X + col * p, gr.Y + row * p, p - 1, p - 1);
            }
            float x = gr.Right + c.S(9);
            var vf = OnlDraw.ValFont(c); var v = StreakVal(d); float vw = Ui.MeasureW(c.G, v, vf);
            Ui.Text(c.G, v, vf, d.Num("cur") > 0 ? c.T.Text : c.T.TextDim, new RectangleF(x, b.Y, vw + c.S(2), b.Height));
            x += vw + c.S(4);
            var lf = OnlDraw.LblFont(c); float lw = Ui.MeasureW(c.G, "day streak", lf);
            Ui.Text(c.G, "day streak", lf, c.T.TextDim, new RectangleF(x, b.Y + c.S(0.5f), lw + c.S(2), b.Height));
            if (d.Sample) OnlDraw.Badge(c, x + lw + c.S(6), b.Y + b.Height / 2f);
        }

        static int FirstDow(OnlData d)
        {
            DateTime s; if (!DateTime.TryParseExact(d.Str("start"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out s)) return 0;
            return (int)s.DayOfWeek;
        }

        public override Size PanelSize(float s) { return new Size((int)(440 * s), (int)(312 * s)); }

        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            var user = d.Str("user", OnlGh.User ?? "");
            float y = OnlDraw.Header(c, "github", "GitHub Activity", user.Length > 0 ? "@" + user : null, d);
            y = OnlDraw.Cards(c, y, new[] { Ui.Compact(d.Num("total")), ((int)d.Num("cur")) + (d.Num("cur") == 1 ? " day" : " days"), ((int)d.Num("long")) + (d.Num("long") == 1 ? " day" : " days") },
                new[] { "Contributions", "Current streak", "Longest streak" }, new[] { c.T.Accent, c.T.Good, c.T.Warn });

            // grid with month + weekday labels
            float left = b.X + c.S(16) + c.S(24), right = b.Right - c.S(16);
            int totalCols = 53;
            float pitch = (float)Math.Floor((right - left) / totalCols);
            float gy = y + c.S(16);
            var gr = new RectangleF(right - totalCols * pitch, gy, totalCols * pitch, 7 * pitch);
            int hover = -1; RectangleF used = gr;
            if (d.Levels.Length > 0) OnlDraw.Grid(c, gr, d.Levels, FirstDow(d), totalCols, out hover, out used);
            var lf = c.F(9.5f);
            string[] wd = { "", "Mon", "", "Wed", "", "Fri", "" };
            for (int r = 0; r < 7; r++) if (wd[r] != "") Ui.Text(c.G, wd[r], lf, c.T.TextFaint, new RectangleF(b.X + c.S(16), gy + r * pitch - c.S(2), c.S(24), pitch + c.S(4)));
            DateTime start;
            if (DateTime.TryParseExact(d.Str("start"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start) && d.Levels.Length > 0)
            {
                int fd = (int)start.DayOfWeek, cols = (d.Levels.Length + fd + 6) / 7, shown = (int)Math.Round(used.Width / pitch), startCol = cols - shown;
                float lastX = -1000; int lastMonth = -1;
                for (int col = 0; col < shown; col++)
                {
                    var day = start.AddDays((startCol + col) * 7 - fd);
                    if (day.Month != lastMonth)
                    {
                        float lx = used.X + col * pitch;
                        if (lx - lastX > c.S(26) && (col > 0 || day.Day <= 7) && lx + c.S(20) < right) { Ui.TextAt(c.G, day.ToString("MMM", CultureInfo.InvariantCulture), lf, c.T.TextFaint, lx, y); lastX = lx; }
                        lastMonth = day.Month;
                    }
                }
            }
            y = gy + 7 * pitch + c.S(10);

            // legend / hover info
            string info;
            if (hover >= 0 && hover < d.Series.Length)
            {
                var day = start.AddDays(hover); var n = (int)d.Series[hover];
                info = (n == 0 ? "No" : n.ToString()) + (n == 1 ? " contribution" : " contributions") + " on " + day.ToString("ddd, MMM d", CultureInfo.InvariantCulture);
            }
            else info = Ui.Compact(d.Num("week")) + " this week \u00b7 " + Ui.Compact(d.Num("today")) + " today";
            Ui.Text(c.G, info, c.F(11), hover >= 0 ? c.T.Text : c.T.TextDim, new RectangleF(b.X + c.S(16), y, c.S(220), c.S(16)));
            float cs = c.S(9), lgx = right - 5 * (cs + c.S(3)) - c.S(28);
            Ui.Text(c.G, "Less", lf, c.T.TextFaint, new RectangleF(lgx - c.S(28), y, c.S(26), c.S(16)), 1);
            for (int i = 0; i < 5; i++) Ui.FillRound(c.G, new RectangleF(lgx + i * (cs + c.S(3)), y + (c.S(16) - cs) / 2, cs, cs), c.S(2), OnlDraw.Level(c.T, i));
            Ui.Text(c.G, "More", lf, c.T.TextFaint, new RectangleF(right - c.S(26), y, c.S(26), c.S(16)), 1);
            y += c.S(24);
            OnlDraw.ErrorLine(c, y, d);
            OnlDraw.Footer(c, d, "Open profile", "https://github.com/" + (string.IsNullOrEmpty(user) ? "" : user), RefreshNow, Host);
        }
    }

    // =====================================================================================
    [Widget("github", "GitHub", "Online & Mail", "Unread GitHub notifications with a quick list.",
        RefName = "GitHub", Account = "github", Data = DataKind.NeedsAccount)]
    public class OnlGitHubWidget : OnlWidget
    {
        static OnlGitHubWidget() { OnlGh.Define(); }
        protected override string HubKey { get { return "onl.gh.notif"; } }
        protected override bool IsConnected { get { return OnlGh.Token != null; } }
        protected override int IntervalMs { get { return 5 * 60 * 1000; } }
        protected override OnlData Fetch()
        {
            var l = OnlGh.Notifications();
            var d = new OnlData();
            d.N["unread"] = l.Count; d.N["more"] = l.Count >= 50 ? 1 : 0;
            d.Items = l.Take(12).ToList();
            return d;
        }
        protected override OnlData MakeSample()
        {
            var d = new OnlData(); d.Items = OnlGh.SampleNotifications(); d.N["unread"] = d.Items.Count; return d;
        }
        static string Count(OnlData d) { if (Loading(d) || (d.Error != null && !d.Has("unread"))) return "\u2014"; return ((int)d.Num("unread")) + (d.Num("more") > 0 ? "+" : ""); }
        static bool Loading(OnlData d) { return d.Str("loading") == "1"; }

        public override int Measure(DrawCtx c) { var d = Data; return OnlDraw.MeasureStat(c, Count(d), "unread", d.Sample); }
        public override void Draw(DrawCtx c) { var d = Data; OnlDraw.Stat(c, "github", Count(d), "unread", d.Sample, null, null, d.Num("unread") > 0 ? (Color?)null : c.T.TextDim); }

        public override Size PanelSize(float s) { return new Size((int)(360 * s), (int)(392 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data;
            int n = (int)d.Num("unread");
            float y = OnlDraw.Header(c, "github", "Notifications", !d.Has("unread") ? null : n == 0 ? "You're all caught up" : n + (d.Num("more") > 0 ? "+" : "") + " unread", d);
            y = OnlDraw.ErrorLine(c, y, d);
            float bottom = c.Bounds.Bottom - OnlDraw.FooterH(c.Scale);
            if (d.Items.Count == 0) OnlDraw.Empty(c, y + c.S(40), d.Error == null ? "Inbox zero \u2014 nothing unread." : "No notifications loaded.");
            foreach (var it in d.Items)
            {
                if (y + c.S(44) > bottom) break;
                var k = it.Kind;
                y = OnlDraw.Row(c, y, it.Title, it.Sub, OnlUtil.Ago(it.When), Color.Empty, it.Url, null, 44, r => OnlGh.KindLead(c, r, k));
            }
            OnlDraw.Footer(c, d, "Open inbox", "https://github.com/notifications", RefreshNow, Host);
        }
    }

    // =====================================================================================
    [Widget("github-overview", "GitHub Overview", "Online & Mail", "Unread notifications, your open pull requests and total stars.",
        RefName = "GitHub Overview", Account = "github", Data = DataKind.NeedsAccount)]
    public class OnlGitHubOverviewWidget : OnlWidget
    {
        static OnlGitHubOverviewWidget() { OnlGh.Define(); }
        protected override string HubKey { get { return "onl.gh.overview"; } }
        protected override bool IsConnected { get { return OnlGh.Token != null; } }
        protected override int IntervalMs { get { return 10 * 60 * 1000; } }

        protected override OnlData Fetch()
        {
            var h = OnlGh.H();
            var d = new OnlData();
            var notif = OnlGh.Notifications();
            d.N["notif"] = notif.Count;
            var s = OnlNet.Json("https://api.github.com/search/issues?q=" + Uri.EscapeDataString("is:pr is:open author:@me archived:false") + "&sort=updated&order=desc&per_page=10", h);
            d.N["prs"] = J.Num(s, "total_count");
            foreach (var it in OnlUtil.Arr(s, "items"))
            {
                var repo = J.Str(it, "repository_url").Replace("https://api.github.com/repos/", "");
                d.Items.Add(new OnlItem { Title = J.Str(it, "title"), Sub = repo + " #" + J.Num(it, "number"), Url = J.Str(it, "html_url"), When = J.Str(it, "updated_at"), Kind = J.Bool(it, "draft") ? 1 : 0 });
            }
            double stars = 0; int repos = 0; var top = new List<OnlItem>();
            for (int page = 1; page <= 5; page++)
            {
                var arr = OnlNet.Json("https://api.github.com/user/repos?per_page=100&affiliation=owner&page=" + page, h) as System.Collections.IList;
                if (arr == null || arr.Count == 0) break;
                foreach (var r in arr)
                {
                    var st = J.Num(r, "stargazers_count"); stars += st; repos++;
                    top.Add(new OnlItem { Title = J.Str(r, "name"), Sub = J.Str(r, "language"), Url = J.Str(r, "html_url"), Val = st, Kind = J.Bool(r, "private") ? 1 : 0 });
                }
                if (arr.Count < 100) break;
            }
            d.N["stars"] = stars; d.N["repos"] = repos;
            d.Items2 = top.OrderByDescending(x => x.Val).Take(3).ToList();
            return d;
        }

        protected override OnlData MakeSample()
        {
            var d = new OnlData(); var now = DateTime.UtcNow;
            d.N["notif"] = 7; d.N["prs"] = 3; d.N["stars"] = 1284; d.N["repos"] = 42;
            d.Items.Add(new OnlItem { Title = "Add keyboard navigation to the command palette", Sub = "acme/web-app #482", Url = "https://github.com/pulls", When = OnlUtil.Iso(now.AddHours(-3)) });
            d.Items.Add(new OnlItem { Title = "Fix DPI scaling on secondary monitors", Sub = "acme/desktop #1291", Url = "https://github.com/pulls", When = OnlUtil.Iso(now.AddDays(-1)) });
            d.Items.Add(new OnlItem { Title = "WIP: plugin API", Sub = "acme/desktop #1288", Url = "https://github.com/pulls", When = OnlUtil.Iso(now.AddDays(-4)), Kind = 1 });
            d.Items2.Add(new OnlItem { Title = "dotfiles", Sub = "Shell", Val = 812, Url = "https://github.com" });
            d.Items2.Add(new OnlItem { Title = "tiny-json", Sub = "C#", Val = 301, Url = "https://github.com" });
            d.Items2.Add(new OnlItem { Title = "pixel-weather", Sub = "TypeScript", Val = 96, Url = "https://github.com" });
            return d;
        }

        bool NoData(OnlData d) { return d.Str("loading") == "1" || (d.Error != null && !d.Has("notif")); }
        string V(OnlData d, string k) { return NoData(d) ? "\u2014" : Ui.Compact(d.Num(k)); }

        // tile: [logo] [bell] n  [pr] n  [star] n
        public override int Measure(DrawCtx c)
        {
            var d = Data; var f = OnlDraw.ValFont(c);
            float w = c.S(10) + c.S(18) + c.S(4);
            foreach (var k in new[] { "notif", "prs", "stars" }) w += c.S(6) + c.S(15) + c.S(3) + Ui.MeasureW(c.G, V(d, k), f);
            if (d.Sample) w += c.S(8) + OnlDraw.BadgeW(c);
            return (int)Math.Ceiling(w + c.S(11));
        }
        public override void Draw(DrawCtx c)
        {
            var d = Data; var b = c.Bounds; var f = OnlDraw.ValFont(c);
            float x = b.X + c.S(10), cy = b.Y + b.Height / 2f, ls = c.S(18);
            OnlDraw.Logo(c.G, "github", new RectangleF(x, cy - ls / 2, ls, ls), c.T);
            x += ls + c.S(4);
            string[] keys = { "notif", "prs", "stars" };
            for (int i = 0; i < 3; i++)
            {
                x += c.S(6);
                var ir = new RectangleF(x, cy - c.S(7.5f), c.S(15), c.S(15));
                var ic = i == 0 && d.Num("notif") > 0 ? c.T.Accent : c.T.TextDim;
                if (i == 0) Ui.Icon(c.G, Icons.Bell, ir, ic, c.S(12));
                else if (i == 1) OnlDraw.PrGlyph(c.G, Ui.Inset(ir, c.S(1.5f), c.S(1.5f)), c.T.TextDim);
                else Ui.Icon(c.G, Icons.Star, ir, c.T.TextDim, c.S(12));
                x += c.S(15) + c.S(3);
                var v = V(d, keys[i]); float vw = Ui.MeasureW(c.G, v, f);
                Ui.Text(c.G, v, f, c.T.Text, new RectangleF(x, b.Y, vw + c.S(2), b.Height));
                x += vw;
            }
            if (d.Sample) OnlDraw.Badge(c, x + c.S(8), cy);
        }

        public override Size PanelSize(float s) { return new Size((int)(372 * s), (int)(462 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            var user = OnlGh.User;
            float y = OnlDraw.Header(c, "github", "GitHub Overview", user != null ? "@" + user : (d.Sample ? "@octocat" : null), d);
            y = OnlDraw.Cards(c, y, new[] { V(d, "notif"), V(d, "prs"), V(d, "stars") }, new[] { "Unread", "Open PRs", "Stars" }, new[] { c.T.Accent, c.T.Good, c.T.Warn });
            y = OnlDraw.ErrorLine(c, y, d);
            y = OnlDraw.Section(c, y, "Your open pull requests", d.Num("prs") > d.Items.Count ? "showing " + d.Items.Count : null);
            float bottom = b.Bottom - OnlDraw.FooterH(c.Scale);
            int shown = 0;
            foreach (var it in d.Items)
            {
                if (shown >= 3) break;
                bool draft = it.Kind == 1;
                y = OnlDraw.Row(c, y, it.Title, it.Sub + (draft ? " \u00b7 draft" : ""), OnlUtil.Ago(it.When), Color.Empty, it.Url, null, 42,
                    r => OnlDraw.PrGlyph(c.G, Ui.Inset(r, c.S(1), c.S(1)), draft ? c.T.TextFaint : c.T.Good));
                shown++;
            }
            if (shown == 0) y = OnlDraw.Empty(c, y, "No open pull requests.", 36);
            y += c.S(8);
            y = OnlDraw.Section(c, y, "Top repositories", d.Has("repos") ? Ui.Compact(d.Num("repos")) + " repos" : null);
            foreach (var it in d.Items2)
            {
                if (y + c.S(32) > bottom) break;
                y = OnlDraw.Row(c, y, it.Title, null, Ui.Compact(it.Val) + " \u2605", Color.Empty, it.Url, c.T.TextDim, 32,
                    r => Ui.Icon(c.G, it.Kind == 1 ? Icons.Lock : Icons.Folder, r, c.T.TextDim, c.S(12)));
            }
            OnlDraw.Footer(c, d, "Open pull requests", "https://github.com/pulls", RefreshNow, Host);
        }
    }
}
