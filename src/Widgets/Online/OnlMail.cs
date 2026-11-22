// Gmail (unread inbox via IMAP + app password) and LeetCode Stats (public GraphQL, username only).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NovaBar
{
    [Widget("gmail", "Gmail", "Online & Mail", "Unread mail in your inbox, with the latest unread senders.",
        RefName = "Gmail", Account = "gmail", Data = DataKind.NeedsAccount)]
    public class OnlGmailWidget : OnlWidget
    {
        static OnlGmailWidget()
        {
            Accounts.Define("gmail", "Gmail", "Use your Gmail address and an App password (Google Account → Security → 2-Step Verification → App passwords). NovaBar only reads unread counts and headers over IMAP; nothing is changed or marked read.",
                "https://myaccount.google.com/apppasswords", Accounts.F("email", "Gmail address", false, "you@gmail.com"), Accounts.F("password", "App password", true, "abcd efgh ijkl mnop"));
        }
        protected override string HubKey { get { return "onl.gmail:" + (Accounts.Get("gmail", "email") ?? "").ToLowerInvariant(); } }
        protected override bool IsConnected { get { return Accounts.Connected("gmail"); } }
        protected override int IntervalMs { get { return 5 * 60 * 1000; } }

        protected override OnlData Fetch()
        {
            var email = Accounts.Get("gmail", "email").Trim(); var pw = Accounts.Get("gmail", "password").Replace(" ", "");
            var d = new OnlData(); d.S["email"] = email;
            using (var im = new OnlImap("imap.gmail.com", 993))
            {
                im.Cmd("LOGIN " + OnlImap.Quote(email) + " " + OnlImap.Quote(pw), "Sign-in failed — check the address and app password");
                foreach (var l in im.Cmd("STATUS INBOX (MESSAGES UNSEEN)"))
                {
                    var m = Regex.Match(l, "MESSAGES (\\d+)"); if (m.Success) d.N["total"] = double.Parse(m.Groups[1].Value);
                    m = Regex.Match(l, "UNSEEN (\\d+)"); if (m.Success) d.N["unread"] = double.Parse(m.Groups[1].Value);
                }
                if (!d.Has("unread")) throw new OnlErr("Couldn't read the inbox");
                if (d.Num("unread") > 0)
                {
                    im.Cmd("EXAMINE INBOX"); // read-only: never changes \Seen
                    var uids = new List<long>();
                    foreach (var l in im.Cmd("UID SEARCH UNSEEN"))
                        if (l.StartsWith("* SEARCH")) foreach (var p in l.Substring(8).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) { long u; if (long.TryParse(p, out u)) uids.Add(u); }
                    var last = uids.OrderByDescending(x => x).Take(6).ToList();
                    if (last.Count > 0)
                    {
                        var msgs = new List<OnlItem>();
                        foreach (var l in im.Cmd("UID FETCH " + string.Join(",", last) + " (UID X-GM-THRID BODY.PEEK[HEADER.FIELDS (FROM SUBJECT DATE)])"))
                        {
                            if (!l.Contains("FETCH")) continue;
                            int a = l.IndexOf(OnlImap.LitOpen), b = l.IndexOf(OnlImap.LitClose);
                            var hdr = OnlImap.ParseHeaders(a >= 0 && b > a ? l.Substring(a + 1, b - a - 1) : "");
                            var th = Regex.Match(l, "X-GM-THRID (\\d+)");
                            string url = "https://mail.google.com/mail/u/0/#inbox";
                            ulong tid; if (th.Success && ulong.TryParse(th.Groups[1].Value, out tid)) url += "/" + tid.ToString("x");
                            string subj, from, date;
                            hdr.TryGetValue("Subject", out subj); hdr.TryGetValue("From", out from); hdr.TryGetValue("Date", out date);
                            var dt = OnlImap.ParseDate(date);
                            msgs.Add(new OnlItem { Title = string.IsNullOrEmpty(subj) ? "(no subject)" : subj, Sub = OnlImap.DisplayName(from), Url = url, When = dt == DateTime.MinValue ? "" : OnlUtil.Iso(dt) });
                        }
                        d.Items = msgs.OrderByDescending(x => x.WhenUtc).ToList();
                    }
                }
            }
            return d;
        }

        protected override OnlData MakeSample()
        {
            var d = new OnlData(); var now = DateTime.UtcNow;
            d.S["email"] = "you@gmail.com"; d.N["unread"] = 4; d.N["total"] = 2318;
            Func<string, string, double, OnlItem> it = (f, s, m) => new OnlItem { Sub = f, Title = s, When = OnlUtil.Iso(now.AddMinutes(-m)), Url = "https://mail.google.com/mail/u/0/#inbox" };
            d.Items = new List<OnlItem> { it("Maya Chen", "Re: Q4 planning doc — comments inline", 9), it("GitHub", "[acme/web-app] Review requested on #482", 47), it("Figma", "Jordan invited you to “Dock widgets v3”", 160), it("Stripe", "Your weekly payments summary", 610) };
            return d;
        }

        bool NoData(OnlData d) { return Loading(d) || (d.Error != null && !d.Has("unread")); }
        string Val(OnlData d) { return NoData(d) ? "—" : Ui.Compact(d.Num("unread")); }
        public override int Measure(DrawCtx c) { var d = Data; return OnlDraw.MeasureStat(c, Val(d), "unread", d.Sample); }
        public override void Draw(DrawCtx c) { var d = Data; OnlDraw.Stat(c, "gmail", Val(d), "unread", d.Sample, null, null, d.Num("unread") > 0 ? (Color?)null : c.T.TextDim); }

        public override Size PanelSize(float s) { return new Size((int)(360 * s), (int)(386 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            float y = OnlDraw.Header(c, "gmail", "Gmail", d.Str("email", Accounts.Get("gmail", "email") ?? ""), d);
            y = OnlDraw.Cards(c, y, new[] { Val(d), NoData(d) ? "—" : Ui.Compact(d.Num("total")) }, new[] { "Unread in Inbox", "Inbox conversations" }, new[] { c.T.Bad, c.T.TextFaint });
            y = OnlDraw.ErrorLine(c, y, d);
            y = OnlDraw.Section(c, y, "Latest unread");
            float bottom = b.Bottom - OnlDraw.FooterH(c.Scale);
            if (d.Items.Count == 0) OnlDraw.Empty(c, y + c.S(20), NoData(d) ? "Not loaded yet." : "No unread mail. Nice.");
            foreach (var it in d.Items)
            {
                if (y + c.S(44) > bottom) break;
                y = OnlDraw.Row(c, y, it.Sub, it.Title, OnlUtil.Ago(it.When), c.T.Accent, it.Url, null, 44);
            }
            OnlDraw.Footer(c, d, "Open Gmail", "https://mail.google.com/mail/u/0/#inbox", RefreshNow, Host);
        }
    }

    // =====================================================================================
    [Widget("leetcode", "LeetCode Stats", "Online & Mail", "Problems solved by difficulty and your streak. Only needs your public username.",
        RefName = "LeetCode Stats", Account = "leetcode", Data = DataKind.NeedsAccount)]
    public class OnlLeetCodeWidget : OnlWidget
    {
        static OnlLeetCodeWidget()
        {
            Accounts.Define("leetcode", "LeetCode", "Just your public LeetCode username — no password or key needed.", "https://leetcode.com/profile/", Accounts.F("user", "Username", false, "your-handle"));
        }
        static string User { get { var u = Accounts.Get("leetcode", "user"); return string.IsNullOrEmpty(u) ? null : u.Trim().TrimStart('@'); } }
        protected override string HubKey { get { return "onl.leetcode:" + (User ?? "").ToLowerInvariant(); } }
        protected override bool IsConnected { get { return User != null; } }
        protected override int IntervalMs { get { return 30 * 60 * 1000; } }
        static readonly string[] Diff = { "Easy", "Medium", "Hard" };

        protected override OnlData Fetch()
        {
            const string q = "query($u:String!){allQuestionsCount{difficulty count} matchedUser(username:$u){username profile{ranking realName} submitStatsGlobal{acSubmissionNum{difficulty count}} userCalendar{streak totalActiveDays submissionCalendar}}}";
            var body = J.Stringify(new Dictionary<string, object> { { "query", q }, { "variables", new Dictionary<string, object> { { "u", User } } } });
            var o = OnlNet.PostJson("https://leetcode.com/graphql", body, "Referer: https://leetcode.com/", "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) NovaBar/1.0");
            var mu = J.Obj(o, "data.matchedUser");
            if (mu == null) throw new OnlErr("User not found");
            var d = new OnlData(); d.S["user"] = J.Str(mu, "username");
            foreach (var x in OnlUtil.Arr(o, "data.allQuestionsCount")) d.N["all" + J.Str(x, "difficulty")] = J.Num(x, "count");
            foreach (var x in OnlUtil.Arr(mu, "submitStatsGlobal.acSubmissionNum")) d.N["ac" + J.Str(x, "difficulty")] = J.Num(x, "count");
            d.N["rank"] = J.Num(mu, "profile.ranking"); d.N["maxStreak"] = J.Num(mu, "userCalendar.streak"); d.N["active"] = J.Num(mu, "userCalendar.totalActiveDays");
            // submission calendar: {"<unix day>": count}
            var cal = J.ParseObj(J.Str(mu, "userCalendar.submissionCalendar", "{}")) ?? new Dictionary<string, object>();
            var days = 182; var today = DateTime.UtcNow.Date; var counts = new double[days];
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            foreach (var kv in cal)
            {
                long ts; if (!long.TryParse(kv.Key, out ts)) continue;
                int idx = days - 1 - (int)(today - epoch.AddSeconds(ts).Date).TotalDays;
                if (idx >= 0 && idx < days) counts[idx] += J.AsNum(kv.Value);
            }
            Finish(d, counts, today.AddDays(-(days - 1)));
            return d;
        }
        static void Finish(OnlData d, double[] counts, DateTime start)
        {
            d.Series = counts; d.Levels = OnlDraw.LevelsFor(counts);
            d.S["start"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            int cur, lng; OnlDraw.Streaks(counts, out cur, out lng);
            d.N["streak"] = cur; if (lng > d.Num("maxStreak")) d.N["maxStreak"] = lng;
        }

        protected override OnlData MakeSample()
        {
            var d = new OnlData(); d.S["user"] = "neetcoder";
            d.N["allAll"] = 3384; d.N["allEasy"] = 852; d.N["allMedium"] = 1773; d.N["allHard"] = 759;
            d.N["acEasy"] = 186; d.N["acMedium"] = 241; d.N["acHard"] = 47; d.N["acAll"] = 474;
            d.N["rank"] = 118402; d.N["active"] = 203; d.N["maxStreak"] = 41;
            var counts = OnlDraw.SampleDays(OnlUtil.DaySeed("leet"), 182, 0.55, 0.7).Select(x => Math.Min(x, 6)).ToArray();
            Finish(d, counts, DateTime.Today.AddDays(-181));
            return d;
        }

        bool NoData(OnlData d) { return Loading(d) || (d.Error != null && !d.Has("acAll")); }
        string Val(OnlData d) { return NoData(d) ? "—" : Ui.Compact(d.Num("acAll")); }

        void SegRing(DrawCtx c, RectangleF r, OnlData d, float thick)
        {
            var rr = Ui.Inset(Ui.Square(r), thick / 2, thick / 2);
            using (var p = new Pen(c.T.Track, thick)) c.G.DrawEllipse(p, rr);
            double all = Math.Max(1, d.Num("allAll"));
            Color[] cols = { c.T.Good, c.T.Warn, c.T.Bad };
            float a = -90;
            for (int i = 0; i < 3; i++)
            {
                float sweep = (float)(360 * d.Num("ac" + Diff[i]) / all);
                if (sweep > 0.5f) using (var p = new Pen(cols[i], thick)) c.G.DrawArc(p, rr, a, sweep);
                a += sweep;
            }
        }

        public override int Measure(DrawCtx c)
        {
            var d = Data;
            float w = c.S(10) + c.S(20) + c.S(7) + Ui.MeasureW(c.G, Val(d), OnlDraw.ValFont(c)) + c.S(4) + Ui.MeasureW(c.G, "solved", OnlDraw.LblFont(c));
            if (!NoData(d) && d.Num("streak") > 0) w += c.S(8) + c.S(12) + Ui.MeasureW(c.G, ((int)d.Num("streak")).ToString(), c.FS(11));
            if (d.Sample) w += c.S(6) + OnlDraw.BadgeW(c);
            return (int)Math.Ceiling(w + c.S(11));
        }
        public override void Draw(DrawCtx c)
        {
            var d = Data; var b = c.Bounds; float x = b.X + c.S(10), cy = b.Y + b.Height / 2f, rs = c.S(20);
            SegRing(c, new RectangleF(x, cy - rs / 2, rs, rs), d, c.S(3.2f));
            x += rs + c.S(7);
            var vf = OnlDraw.ValFont(c); var v = Val(d); float vw = Ui.MeasureW(c.G, v, vf);
            Ui.Text(c.G, v, vf, c.T.Text, new RectangleF(x, b.Y, vw + c.S(2), b.Height)); x += vw + c.S(4);
            var lf = OnlDraw.LblFont(c); float lw = Ui.MeasureW(c.G, "solved", lf);
            Ui.Text(c.G, "solved", lf, c.T.TextDim, new RectangleF(x, b.Y + c.S(0.5f), lw + c.S(2), b.Height)); x += lw;
            if (!NoData(d) && d.Num("streak") > 0)
            {
                x += c.S(8);
                Ui.Icon(c.G, Icons.Sparkle, new RectangleF(x, b.Y, c.S(12), b.Height), c.T.Warn, c.S(10.5f)); x += c.S(12);
                var s = ((int)d.Num("streak")).ToString(); var sf = c.FS(11); float sw = Ui.MeasureW(c.G, s, sf);
                Ui.Text(c.G, s, sf, c.T.Warn, new RectangleF(x, b.Y + c.S(0.5f), sw + c.S(2), b.Height)); x += sw;
            }
            if (d.Sample) OnlDraw.Badge(c, x + c.S(6), cy);
        }

        public override Size PanelSize(float s) { return new Size((int)(372 * s), (int)(386 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            var user = d.Str("user", User ?? "");
            float y = OnlDraw.Header(c, "leetcode", "LeetCode", user != "" ? "@" + user + (d.Num("rank") > 0 ? " · rank " + d.Num("rank").ToString("#,0") : "") : null, d);
            // ring + difficulty bars
            float rs = c.S(96);
            var rr = new RectangleF(b.X + c.S(18), y, rs, rs);
            SegRing(c, rr, d, c.S(8));
            Ui.Text(c.G, Val(d), c.FS(22), c.T.Text, new RectangleF(rr.X, rr.Y + c.S(24), rr.Width, c.S(28)), 0);
            Ui.Text(c.G, "/ " + Ui.Compact(d.Num("allAll")), c.F(10.5f), c.T.TextDim, new RectangleF(rr.X, rr.Y + c.S(52), rr.Width, c.S(16)), 0);
            float bx = rr.Right + c.S(20), bw = b.Right - c.S(18) - bx, by = y + c.S(4);
            Color[] cols = { c.T.Good, c.T.Warn, c.T.Bad };
            for (int i = 0; i < 3; i++)
            {
                double ac = d.Num("ac" + Diff[i]), all = Math.Max(1, d.Num("all" + Diff[i]));
                Ui.Text(c.G, Diff[i], c.FS(11.5f), cols[i], new RectangleF(bx, by, bw, c.S(16)));
                Ui.Text(c.G, Ui.Compact(ac) + " / " + Ui.Compact(all), c.F(11), c.T.TextDim, new RectangleF(bx, by, bw, c.S(16)), 1);
                Ui.Progress(c.G, new RectangleF(bx, by + c.S(19), bw, c.S(5)), ac / all, cols[i], c.T.Track);
                by += c.S(32);
            }
            y += rs + c.S(14);
            y = OnlDraw.Cards(c, y, new[] { ((int)d.Num("streak")) + "d", ((int)d.Num("maxStreak")) + "d", Ui.Compact(d.Num("active")) },
                new[] { "Current streak", "Max streak", "Active days" }, new[] { c.T.Warn, c.T.Accent, c.T.Good }, 52);
            y = OnlDraw.Section(c, y, "Submissions, last 26 weeks");
            var gr = new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(56));
            int hover; RectangleF used;
            DateTime start; DateTime.TryParseExact(d.Str("start"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start);
            if (d.Levels.Length > 0)
            {
                float pitch = (float)Math.Floor(Math.Min(gr.Height / 7f, gr.Width / 26f));
                var g2 = new RectangleF(gr.X + (gr.Width - pitch * 26) / 2, gr.Y, pitch * 26, pitch * 7);
                OnlDraw.Grid(c, g2, d.Levels, (int)start.DayOfWeek, 26, out hover, out used);
                if (hover >= 0)
                {
                    var n = (int)d.Series[hover];
                    Ui.Text(c.G, (n == 0 ? "No" : n.ToString()) + (n == 1 ? " submission" : " submissions") + " on " + start.AddDays(hover).ToString("MMM d", CultureInfo.InvariantCulture),
                        c.F(10.5f), c.T.Text, new RectangleF(b.X + c.S(16), y - c.S(20), b.Width - c.S(32), c.S(14)), 1);
                }
            }
            OnlDraw.ErrorLine(c, gr.Bottom + c.S(4), d);
            OnlDraw.Footer(c, d, "Open profile", "https://leetcode.com/u/" + user + "/", RefreshNow, Host);
        }
    }
}
