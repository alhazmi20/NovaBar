// Developer widgets: Resend (emails this week + delivered), Linear (assigned issues), Supabase (API requests + health).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace NovaBar
{
    // =====================================================================================
    [Widget("resend", "Resend", "Developer", "Emails sent this week and how many were delivered.",
        RefName = "Resend", Account = "resend", Data = DataKind.NeedsAccount)]
    public class OnlResendWidget : OnlWidget
    {
        static OnlResendWidget()
        {
            Accounts.Define("resend", "Resend", "Create an API key with Full access (listing emails needs it). Stored encrypted on this PC.",
                "https://resend.com/api-keys", Accounts.F("key", "API key", true, "re_…"));
        }
        protected override string HubKey { get { return "onl.resend"; } }
        protected override bool IsConnected { get { return Accounts.Connected("resend"); } }
        protected override int IntervalMs { get { return 15 * 60 * 1000; } }

        static DateTime WeekStartUtc()
        {
            var t = DateTime.Today; int diff = ((int)t.DayOfWeek + 6) % 7; // Monday
            return t.AddDays(-diff).ToUniversalTime();
        }
        static bool Delivered(string ev) { return ev == "delivered" || ev == "opened" || ev == "clicked" || ev == "complained"; }
        static bool Bounced(string ev) { return ev == "bounced" || ev == "failed"; }

        protected override OnlData Fetch()
        {
            var key = Accounts.Get("resend", "key").Trim();
            var since = WeekStartUtc(); var weekAgo = DateTime.Today.AddDays(-6).ToUniversalTime();
            var from = since < weekAgo ? since : weekAgo;
            var all = new List<object>(); string after = null;
            for (int page = 0; page < 10; page++)
            {
                var url = "https://api.resend.com/emails?limit=100" + (after != null ? "&after=" + Uri.EscapeDataString(after) : "");
                var o = OnlNet.Json(url, "Authorization: Bearer " + key);
                var arr = OnlUtil.Arr(o, "data");
                if (arr.Count == 0) break;
                bool older = false;
                foreach (var e in arr) { all.Add(e); if (OnlUtil.ParseTime(J.Str(e, "created_at")) < from) older = true; }
                if (older || !J.Bool(o, "has_more")) break;
                after = J.Str(arr[arr.Count - 1], "id");
                System.Threading.Thread.Sleep(600); // Resend: 2 req/s
            }
            var d = new OnlData();
            int sent = 0, deliv = 0, bounced = 0, opened = 0;
            var perDay = new double[7];
            foreach (var e in all)
            {
                var t = OnlUtil.ParseTime(J.Str(e, "created_at")); var ev = J.Str(e, "last_event");
                if (t >= since) { sent++; if (Delivered(ev)) deliv++; if (Bounced(ev)) bounced++; if (ev == "opened" || ev == "clicked") opened++; }
                int idx = 6 - (int)(DateTime.Today - t.ToLocalTime().Date).TotalDays;
                if (idx >= 0 && idx < 7) perDay[idx]++;
            }
            d.N["sent"] = sent; d.N["delivered"] = deliv; d.N["bounced"] = bounced; d.N["opened"] = opened;
            d.Series = perDay;
            foreach (var e in all.Take(5))
            {
                var to = J.Get(e, "to"); var toS = to is System.Collections.IList ? string.Join(", ", ((System.Collections.IList)to).Cast<object>().Select(x => J.AsStr(x))) : J.AsStr(to);
                d.Items.Add(new OnlItem { Title = J.Str(e, "subject", "(no subject)"), Sub = toS, Tag = J.Str(e, "last_event"), When = J.Str(e, "created_at"), Url = "https://resend.com/emails/" + J.Str(e, "id") });
            }
            return d;
        }

        protected override OnlData MakeSample()
        {
            var d = new OnlData(); var rng = new OnlUtil.Rng(OnlUtil.DaySeed("resend"));
            var perDay = new double[7];
            for (int i = 0; i < 7; i++) { var dow = DateTime.Today.AddDays(i - 6).DayOfWeek; perDay[i] = (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) ? rng.I(4, 14) : rng.I(22, 61); }
            int diff = ((int)DateTime.Today.DayOfWeek + 6) % 7;
            double sent = perDay.Skip(6 - diff).Sum();
            d.N["sent"] = sent; d.N["delivered"] = Math.Round(sent * 0.972); d.N["bounced"] = Math.Max(1, Math.Round(sent * 0.012)); d.N["opened"] = Math.Round(sent * 0.46);
            d.Series = perDay;
            var now = DateTime.UtcNow;
            d.Items.Add(new OnlItem { Title = "Confirm your email address", Sub = "maya@example.com", Tag = "delivered", When = OnlUtil.Iso(now.AddMinutes(-6)), Url = "https://resend.com/emails" });
            d.Items.Add(new OnlItem { Title = "Your receipt #2041", Sub = "jordan@example.org", Tag = "opened", When = OnlUtil.Iso(now.AddMinutes(-38)), Url = "https://resend.com/emails" });
            d.Items.Add(new OnlItem { Title = "Reset your password", Sub = "old-address@example.net", Tag = "bounced", When = OnlUtil.Iso(now.AddHours(-2)), Url = "https://resend.com/emails" });
            d.Items.Add(new OnlItem { Title = "Weekly digest", Sub = "sam@example.com", Tag = "delivered", When = OnlUtil.Iso(now.AddHours(-5)), Url = "https://resend.com/emails" });
            return d;
        }

        bool NoData(OnlData d) { return Loading(d) || (d.Error != null && !d.Has("sent")); }
        string Rate(OnlData d) { var s = d.Num("sent"); return s > 0 ? Math.Round(d.Num("delivered") / s * 100) + "%" : null; }

        public override int Measure(DrawCtx c) { var d = Data; return OnlDraw.MeasureStat(c, NoData(d) ? "—" : Ui.Compact(d.Num("sent")), "sent", d.Sample, NoData(d) ? null : Rate(d)); }
        public override void Draw(DrawCtx c)
        {
            var d = Data; var rate = NoData(d) ? null : Rate(d);
            var frac = d.Num("sent") > 0 ? d.Num("delivered") / d.Num("sent") : 1;
            OnlDraw.Stat(c, "resend", NoData(d) ? "—" : Ui.Compact(d.Num("sent")), "sent", d.Sample, rate, frac >= 0.95 ? c.T.Good : frac >= 0.85 ? c.T.Warn : c.T.Bad);
        }

        public override Size PanelSize(float s) { return new Size((int)(360 * s), (int)(470 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            float y = OnlDraw.Header(c, "resend", "Resend", "This week (since Monday)", d);
            double sent = d.Num("sent"), del = d.Num("delivered");
            Func<double, string> V = v => NoData(d) ? "\u2014" : Ui.Compact(v);
            y = OnlDraw.Cards(c, y, new[] { V(sent), V(del), V(d.Num("bounced")) },
                new[] { "Sent", "Delivered" + (sent > 0 ? " · " + Math.Round(del / sent * 100) + "%" : ""), "Bounced" }, new[] { c.T.Accent, c.T.Good, c.T.Bad });
            // delivery bar
            var pr = new RectangleF(b.X + c.S(16), y - c.S(4), b.Width - c.S(32), c.S(6));
            Ui.Progress(c.G, pr, sent > 0 ? del / sent : 0, c.T.Good, c.T.Track);
            y += c.S(14);
            y = OnlDraw.Section(c, y, "Last 7 days", Ui.Compact(d.Series.Sum()) + " emails");
            var chart = new RectangleF(b.X + c.S(20), y, b.Width - c.S(40), c.S(52));
            if (d.Series.Length == 7)
            {
                Ui.Bars(c.G, chart, d.Series, c.T.AccentA(c.T.Dark ? 150 : 165), c.S(10), double.NaN, 6, c.T.Accent);
                float bw = (chart.Width - c.S(10) * 6) / 7;
                for (int i = 0; i < 7; i++)
                    Ui.Text(c.G, DateTime.Today.AddDays(i - 6).ToString("ddd", CultureInfo.InvariantCulture).Substring(0, 2), c.F(9.5f), i == 6 ? c.T.Text : c.T.TextFaint,
                        new RectangleF(chart.X + i * (bw + c.S(10)) - c.S(4), chart.Bottom + c.S(2), bw + c.S(8), c.S(14)), 0);
            }
            y = chart.Bottom + c.S(22);
            y = OnlDraw.ErrorLine(c, y, d);
            y = OnlDraw.Section(c, y, "Recent");
            float bottom = b.Bottom - OnlDraw.FooterH(c.Scale);
            if (d.Items.Count == 0) OnlDraw.Empty(c, y, "No emails yet.", 36);
            foreach (var it in d.Items)
            {
                if (y + c.S(40) > bottom) break;
                var ev = it.Tag;
                var col = Delivered(ev) ? c.T.Good : Bounced(ev) ? c.T.Bad : ev == "delivery_delayed" ? c.T.Warn : c.T.TextDim;
                y = OnlDraw.Row(c, y, it.Title, it.Sub + " · " + OnlUtil.Ago(it.When), ev.Replace('_', ' '), col, it.Url, col);
            }
            OnlDraw.Footer(c, d, "Open Resend", "https://resend.com/emails", RefreshNow, Host);
        }
    }

    // =====================================================================================
    [Widget("linear", "Linear", "Developer", "Issues assigned to you and what's in progress.",
        RefName = "Linear", Account = "linear", Data = DataKind.NeedsAccount)]
    public class OnlLinearWidget : OnlWidget
    {
        static OnlLinearWidget()
        {
            Accounts.Define("linear", "Linear", "Create a personal API key in Linear → Settings → Security & access → Personal API keys (read access is enough).",
                "https://linear.app/settings/account/security", Accounts.F("key", "Personal API key", true, "lin_api_…"));
        }
        protected override string HubKey { get { return "onl.linear"; } }
        protected override bool IsConnected { get { return Accounts.Connected("linear"); } }
        protected override int IntervalMs { get { return 10 * 60 * 1000; } }

        // Kind: 0 backlog, 1 unstarted (todo), 2 started (in progress), 3 triage
        static int KindOf(string type) { return type == "started" ? 2 : type == "unstarted" ? 1 : type == "triage" ? 3 : 0; }
        static readonly string[] Prio = { "", "Urgent", "High", "Medium", "Low" };

        protected override OnlData Fetch()
        {
            const string q = "query{viewer{name assignedIssues(first:100,orderBy:updatedAt,filter:{state:{type:{nin:[\"completed\",\"canceled\"]}}}){nodes{identifier title url priority dueDate updatedAt state{name type}}}}}";
            var o = OnlNet.PostJson("https://api.linear.app/graphql", J.Stringify(new Dictionary<string, object> { { "query", q } }), "Authorization: " + Accounts.Get("linear", "key").Trim());
            var nodes = J.Arr(o, "data.viewer.assignedIssues.nodes");
            if (nodes == null) throw new OnlErr(J.Get(o, "errors") != null ? J.Str(o, "errors.0.message", "API error") : "Unexpected response");
            var d = new OnlData(); d.S["name"] = J.Str(o, "data.viewer.name");
            var items = new List<OnlItem>();
            int prog = 0, todo = 0, urgent = 0, overdue = 0;
            foreach (var n in nodes)
            {
                int k = KindOf(J.Str(n, "state.type")); int p = (int)J.Num(n, "priority");
                if (k == 2) prog++; if (k == 1) todo++; if (p == 1 || p == 2) urgent++;
                var due = J.Str(n, "dueDate");
                DateTime dd; if (!string.IsNullOrEmpty(due) && DateTime.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.None, out dd) && dd.Date < DateTime.Today) overdue++;
                items.Add(new OnlItem { Title = J.Str(n, "title"), Sub = J.Str(n, "identifier") + " · " + J.Str(n, "state.name"), Url = J.Str(n, "url"), Kind = k, Val = p, When = J.Str(n, "updatedAt"), Tag = due });
            }
            d.N["assigned"] = nodes.Count; d.N["progress"] = prog; d.N["todo"] = todo; d.N["urgent"] = urgent; d.N["overdue"] = overdue;
            d.Items = Sort(items).Take(10).ToList();
            return d;
        }
        static IEnumerable<OnlItem> Sort(List<OnlItem> l)
        {
            return l.OrderByDescending(x => x.Kind == 2 ? 3 : x.Kind == 1 ? 2 : x.Kind == 3 ? 1 : 0).ThenBy(x => x.Val == 0 ? 9 : x.Val);
        }

        protected override OnlData MakeSample()
        {
            var d = new OnlData(); d.S["name"] = "You"; var now = DateTime.UtcNow;
            Func<string, string, string, int, int, OnlItem> it = (id, t, st, k, p) => new OnlItem { Title = t, Sub = id + " · " + st, Kind = k, Val = p, Url = "https://linear.app", When = OnlUtil.Iso(now) };
            var l = new List<OnlItem> {
                it("ENG-1423", "Migrate auth service to the new session store", "In Progress", 2, 2),
                it("ENG-1407", "Bar flickers when switching themes", "In Progress", 2, 1),
                it("ENG-1398", "Add CSV export to the billing page", "In Review", 2, 3),
                it("ENG-1431", "Write onboarding checklist copy", "Todo", 1, 3),
                it("ENG-1380", "Investigate slow cold start on Windows", "Todo", 1, 2),
                it("ENG-1322", "Settings: remember last opened tab", "Backlog", 0, 4),
            };
            d.Items = l; d.N["assigned"] = 11; d.N["progress"] = 3; d.N["todo"] = 5; d.N["urgent"] = 3; d.N["overdue"] = 1;
            return d;
        }

        bool NoData(OnlData d) { return Loading(d) || (d.Error != null && !d.Has("assigned")); }
        string Extra(OnlData d) { return NoData(d) || d.Num("progress") == 0 ? null : d.Num("progress") + " active"; }
        public override int Measure(DrawCtx c) { var d = Data; return OnlDraw.MeasureStat(c, NoData(d) ? "—" : Ui.Compact(d.Num("assigned")), "assigned", d.Sample, Extra(d)); }
        public override void Draw(DrawCtx c) { var d = Data; OnlDraw.Stat(c, "linear", NoData(d) ? "—" : Ui.Compact(d.Num("assigned")), "assigned", d.Sample, Extra(d), c.T.Accent); }

        public static void StateGlyph(DrawCtx c, RectangleF r, int kind)
        {
            var cr = Ui.Inset(r, c.S(2), c.S(2));
            float pw = c.S(1.5f);
            switch (kind)
            {
                case 2: // in progress: ring with half pie
                    using (var p = new Pen(c.T.Warn, pw)) c.G.DrawEllipse(p, cr);
                    { var pr = Ui.Inset(cr, c.S(2.5f), c.S(2.5f)); c.G.FillPie(Ui.B(c.T.Warn), pr.X, pr.Y, pr.Width, pr.Height, -90f, 180f); }
                    break;
                case 1: using (var p = new Pen(c.T.TextDim, pw)) c.G.DrawEllipse(p, cr); break;
                case 3: using (var p = new Pen(c.T.Accent, pw)) c.G.DrawEllipse(p, cr); Ui.FillCircle(c.G, new PointF(cr.X + cr.Width / 2, cr.Y + cr.Height / 2), c.S(2), c.T.Accent); break;
                default:
                    using (var p = new Pen(c.T.TextFaint, pw) { DashPattern = new[] { 1.6f, 1.6f } }) c.G.DrawEllipse(p, cr);
                    break;
            }
        }

        public override Size PanelSize(float s) { return new Size((int)(372 * s), (int)(424 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            float y = OnlDraw.Header(c, "linear", "Linear", "My issues", d);
            Func<double, string> V = v => NoData(d) ? "\u2014" : Ui.Compact(v);
            y = OnlDraw.Cards(c, y, new[] { V(d.Num("assigned")), V(d.Num("progress")), V(d.Num("urgent")) },
                new[] { "Assigned", "In progress", "Urgent / high" }, new[] { c.T.Accent, c.T.Warn, c.T.Bad });
            y = OnlDraw.ErrorLine(c, y, d);
            y = OnlDraw.Section(c, y, "Up next", d.Num("overdue") > 0 ? d.Num("overdue") + " overdue" : null);
            float bottom = b.Bottom - OnlDraw.FooterH(c.Scale);
            if (d.Items.Count == 0) OnlDraw.Empty(c, y + c.S(20), d.Error == null ? "Nothing assigned — enjoy the calm." : "No issues loaded.");
            foreach (var it in d.Items)
            {
                if (y + c.S(42) > bottom) break;
                int p = (int)it.Val, k = it.Kind;
                y = OnlDraw.Row(c, y, it.Title, it.Sub, p > 0 && p < Prio.Length ? Prio[p] : "", Color.Empty, it.Url, p == 1 ? c.T.Bad : p == 2 ? c.T.Warn : c.T.TextFaint, 42, r => StateGlyph(c, r, k));
            }
            OnlDraw.Footer(c, d, "Open Linear", "https://linear.app", RefreshNow, Host);
        }
    }

    // =====================================================================================
    [Widget("supabase", "Supabase", "Developer", "API requests on your project in the last 24 hours, and its health.",
        RefName = "Supabase", Account = "supabase", Data = DataKind.NeedsAccount)]
    public class OnlSupabaseWidget : OnlWidget
    {
        static OnlSupabaseWidget()
        {
            Accounts.Define("supabase", "Supabase", "Personal access token (Account → Access Tokens) and your project ref (the id in your dashboard URL).",
                "https://supabase.com/dashboard/account/tokens", Accounts.F("token", "Access token", true, "sbp_…"), Accounts.F("ref", "Project ref", false, "abcdefghijklmnopqrst"));
        }
        protected override string HubKey { get { return "onl.supabase:" + (Accounts.Get("supabase", "ref") ?? ""); } }
        protected override bool IsConnected { get { return Accounts.Connected("supabase"); } }
        protected override int IntervalMs { get { return 10 * 60 * 1000; } }
        static readonly string[] Svc = { "rest", "auth", "storage", "realtime" };

        protected override OnlData Fetch()
        {
            var tok = Accounts.Get("supabase", "token").Trim(); var rf = Accounts.Get("supabase", "ref").Trim();
            var h = "Authorization: Bearer " + tok; var api = "https://api.supabase.com/v1/projects/" + Uri.EscapeDataString(rf);
            var d = new OnlData();
            var p = OnlNet.Json(api, h);
            d.S["name"] = J.Str(p, "name", rf); d.S["region"] = J.Str(p, "region"); d.S["status"] = J.Str(p, "status");
            d.S["ref"] = rf;
            // service health (best effort)
            try
            {
                var hl = OnlNet.Json(api + "/health?services=auth&services=db&services=rest&services=realtime&services=storage", h) as System.Collections.IList;
                if (hl != null) foreach (var s in hl)
                        d.Items.Add(new OnlItem { Title = J.Str(s, "name"), Tag = J.Str(s, "status"), Kind = J.Bool(s, "healthy") ? 1 : 0 });
            }
            catch (OnlErr) { }
            // request counts, last 24h
            try
            {
                var u = OnlNet.Json(api + "/analytics/endpoints/usage.api-counts?interval=1day", h);
                var rows = OnlUtil.Arr(u, "result").Cast<object>().OrderBy(x => J.Str(x, "timestamp")).ToList();
                var ser = new List<double>(); var tot = new double[4];
                foreach (var r in rows)
                {
                    double sum = 0;
                    for (int i = 0; i < 4; i++) { var v = J.Num(r, "total_" + Svc[i] + "_requests"); tot[i] += v; sum += v; }
                    ser.Add(sum);
                }
                d.Series = ser.ToArray();
                for (int i = 0; i < 4; i++) d.N[Svc[i]] = tot[i];
                d.N["requests"] = tot.Sum();
            }
            catch (OnlErr) { d.N["requests"] = -1; }
            d.N["healthy"] = Healthy(d) ? 1 : 0;
            return d;
        }
        static bool Healthy(OnlData d)
        {
            var st = d.Str("status");
            if (st != "" && st != "ACTIVE_HEALTHY") return false;
            return d.Items.All(x => x.Kind == 1);
        }

        protected override OnlData MakeSample()
        {
            var d = new OnlData(); var rng = new OnlUtil.Rng(OnlUtil.DaySeed("supabase"));
            d.S["name"] = "acme-production"; d.S["region"] = "eu-central-1"; d.S["status"] = "ACTIVE_HEALTHY"; d.S["ref"] = "abcdefghijklmnop";
            var ser = new double[24]; int hourNow = DateTime.Now.Hour;
            for (int i = 0; i < 24; i++) { int hr = (hourNow - 23 + i + 24) % 24; ser[i] = Math.Round(380 + 900 * Math.Max(0, Math.Sin((hr - 6) / 24.0 * Math.PI * 2 - 0.3)) + rng.I(0, 220)); }
            d.Series = ser; double tot = ser.Sum();
            d.N["rest"] = Math.Round(tot * 0.71); d.N["auth"] = Math.Round(tot * 0.17); d.N["storage"] = Math.Round(tot * 0.08); d.N["realtime"] = tot - d.N["rest"] - d.N["auth"] - d.N["storage"];
            d.N["requests"] = tot; d.N["healthy"] = 1;
            foreach (var s in new[] { "db", "rest", "auth", "storage", "realtime" }) d.Items.Add(new OnlItem { Title = s, Tag = "ACTIVE_HEALTHY", Kind = 1 });
            return d;
        }

        bool NoData(OnlData d) { return Loading(d) || (d.Error != null && !d.Has("requests")) || d.Num("requests") < 0; }
        string Val(OnlData d) { return NoData(d) ? "—" : Ui.Compact(d.Num("requests")); }

        public override int Measure(DrawCtx c) { var d = Data; return OnlDraw.MeasureStat(c, Val(d), "req", d.Sample) + c.Si(12); }
        public override void Draw(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            var cb = c.Bounds; c.Bounds = new Rectangle(b.X, b.Y, b.Width - c.Si(12), b.Height);
            OnlDraw.Stat(c, "supabase", Val(d), "req", d.Sample);
            c.Bounds = cb;
            // health dot at the right edge
            bool loading = Loading(d) || (d.Error != null && !d.Has("healthy"));
            var col = loading ? c.T.TextFaint : d.Num("healthy") > 0 ? c.T.Good : c.T.Bad;
            var pt = new PointF(b.Right - c.S(14), b.Y + b.Height / 2f);
            Ui.FillCircle(c.G, pt, c.S(5.5f), Color.FromArgb(50, col));
            Ui.FillCircle(c.G, pt, c.S(3.2f), col);
        }

        public override Size PanelSize(float s) { return new Size((int)(360 * s), (int)(384 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Data; var b = c.Bounds;
            var sub = d.Str("name"); if (d.Str("region") != "") sub += " · " + d.Str("region");
            float y = OnlDraw.Header(c, "supabase", "Supabase", sub == "" ? null : sub, d);
            y = OnlDraw.Cards(c, y, new[] { Val(d), NoData(d) ? "\u2014" : Ui.Compact(d.Num("rest")), NoData(d) ? "\u2014" : Ui.Compact(d.Num("auth")) }, new[] { "Requests, 24h", "REST", "Auth" }, new[] { c.T.Accent, c.T.Series[1], c.T.Series[2] });
            y = OnlDraw.Section(c, y, "Requests over 24h", "storage " + Ui.Compact(d.Num("storage")) + " · realtime " + Ui.Compact(d.Num("realtime")));
            var chart = new RectangleF(b.X + c.S(18), y + c.S(2), b.Width - c.S(36), c.S(64));
            if (d.Series.Length >= 2) Ui.Sparkline(c.G, chart, d.Series, c.T.Accent, true, c.S(1.8f), 0);
            else OnlDraw.Empty(c, chart.Y, "No traffic data yet.", 64);
            y = chart.Bottom + c.S(14);
            y = OnlDraw.ErrorLine(c, y, d);
            y = OnlDraw.Section(c, y, "Health", d.Str("status").Replace('_', ' ').ToLowerInvariant());
            // services as pills in a wrap row
            float x = b.X + c.S(16), ph = c.S(26);
            foreach (var it in d.Items)
            {
                var f = c.F(11.5f); float w = Ui.MeasureW(c.G, it.Title, f) + c.S(28);
                if (x + w > b.Right - c.S(16)) { x = b.X + c.S(16); y += ph + c.S(8); }
                var r = new RectangleF(x, y, w, ph);
                var col = it.Kind == 1 ? c.T.Good : c.T.Bad;
                Ui.FillRound(c.G, r, ph / 2, OnlDraw.CardBg(c.T));
                Ui.FillCircle(c.G, new PointF(r.X + c.S(11), r.Y + ph / 2), c.S(3.5f), col);
                Ui.Text(c.G, it.Title, f, c.T.Text, new RectangleF(r.X + c.S(19), r.Y, w - c.S(20), ph));
                x += w + c.S(8);
            }
            if (d.Items.Count == 0) OnlDraw.Empty(c, y, "Health unavailable.", 26);
            var rf = d.Str("ref");
            OnlDraw.Footer(c, d, "Open dashboard", "https://supabase.com/dashboard/project/" + rf, RefreshNow, Host);
        }
    }
}
