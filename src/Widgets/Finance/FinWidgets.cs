// Finance + Analytics widgets. FinMetricWidget draws everything; each widget only names its provider and variant.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>Shared drawing bits (brand badge, sample badge, change pill, chart with hover).</summary>
    public static class FinDraw
    {
        public static void Badge(DrawCtx c, FinProvider p, RectangleF r)
        {
            var bg = p.Brand; double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255;
            if (c.T.Dark && lum < 0.18) { bg = c.T.Text; lum = 1 - lum; }           // dark logos (Dub) invert on dark themes
            var fg = lum > 0.62 ? Color.FromArgb(255, 20, 20, 24) : Color.White;
            if (bg.R == c.T.Text.R && bg.G == c.T.Text.G && bg.B == c.T.Text.B) fg = c.T.Dark ? Color.FromArgb(255, 18, 18, 22) : Color.White;
            c.G.FillEllipse(Ui.B(bg), r);
            Ui.Text(c.G, p.Letter, Ui.Font(r.Height * 0.56f, true), fg, new RectangleF(r.X, r.Y + r.Height * 0.02f, r.Width, r.Height), 0);
        }
        /// <summary>Tiny "SAMPLE" capsule; returns its width.</summary>
        public static float SampleTag(DrawCtx c, float x, float cy, bool draw = true)
        {
            var f = c.FS(8.5f); float w = Ui.MeasureW(c.G, "Sample", f) + c.S(8), h = c.S(12);
            if (draw)
            {
                var r = new RectangleF(x, cy - h / 2, w, h);
                Ui.FillRound(c.G, r, h / 2, c.T.Dark ? Color.FromArgb(40, c.T.Warn) : Color.FromArgb(46, c.T.Warn));
                Ui.Text(c.G, "Sample", f, c.T.Dark ? c.T.Warn : Ui.Mix(c.T.Warn, Color.Black, 0.35), new RectangleF(r.X, r.Y + c.S(0.5f), r.Width, r.Height), 0);
            }
            return w;
        }
        public static Color ChangeColor(DrawCtx c, double ch) { return double.IsNaN(ch) ? c.T.TextDim : ch >= 0 ? c.T.Good : c.T.Bad; }
        /// <summary>Colored pill "▲ 8.2%". Returns width.</summary>
        public static float ChangePill(DrawCtx c, double ch, float x, float cy, float px, bool draw = true)
        {
            var txt = (double.IsNaN(ch) ? "" : (ch >= 0 ? "▲ " : "▼ ")) + FinFmt.Pct(ch).TrimStart('+', '−');
            var f = c.FS(px); float w = Ui.MeasureW(c.G, txt, f) + c.S(12), h = c.S(px + 8);
            if (draw)
            {
                var col = ChangeColor(c, ch); var r = new RectangleF(x, cy - h / 2, w, h);
                Ui.FillRound(c.G, r, h / 2, Color.FromArgb(c.T.Dark ? 38 : 30, col));
                Ui.Text(c.G, txt, f, c.T.Dark ? col : Ui.Mix(col, Color.Black, 0.15), r, 0);
            }
            return w;
        }

        /// <summary>Panel chart: bars (money) or area (counts), axis labels, hover tooltip.</summary>
        public static void Chart(DrawCtx c, RectangleF r, FinSeries s, int days, bool money, string cur, bool bars)
        {
            var vals = s.Tail(days); if (vals.Length < 2) { Ui.Text(c.G, "No data yet", c.F(11.5f), c.T.TextFaint, r, 0); return; }
            double max = Math.Max(1e-6, vals.Max()) * 1.08;
            // faint gridlines
            using (var pen = new Pen(Color.FromArgb(c.T.Dark ? 22 : 18, c.T.Text), 1f) { DashStyle = DashStyle.Dot })
                for (int i = 1; i <= 2; i++) { float y = r.Bottom - r.Height * i / 3f; c.G.DrawLine(pen, r.X, y, r.Right, y); }
            int hov = -1;
            if (c.IsHover(new RectangleF(r.X, r.Y - c.S(6), r.Width, r.Height + c.S(18))))
                hov = Math.Max(0, Math.Min(vals.Length - 1, (int)((c.Mouse.X - r.X) / r.Width * vals.Length)));
            if (bars)
            {
                Ui.Bars(c.G, r, vals, Color.FromArgb(c.T.Dark ? 120 : 110, c.T.Accent), c.S(2.2f), max, hov >= 0 ? hov : vals.Length - 1, c.T.Accent);
            }
            else
            {
                Ui.Sparkline(c.G, r, vals, c.T.Accent, true, c.S(2f), 0, max);
                if (hov >= 0)
                {
                    float x = r.X + r.Width * hov / (vals.Length - 1), y = r.Bottom - (float)(vals[hov] / max) * r.Height;
                    using (var pen = new Pen(Color.FromArgb(60, c.T.Text), 1f)) c.G.DrawLine(pen, x, r.Y, x, r.Bottom);
                    Ui.FillCircle(c.G, new PointF(x, y), c.S(4f), c.T.Accent); Ui.FillCircle(c.G, new PointF(x, y), c.S(1.8f), c.T.PanelBg.A > 200 ? c.T.PanelBg : Color.White);
                }
            }
            var first = s.DayOf(s.Length - vals.Length); var fl = c.F(10);
            Ui.Text(c.G, FinFmt.Day(first), fl, c.T.TextFaint, new RectangleF(r.X, r.Bottom + c.S(4), r.Width / 2, c.S(13)));
            Ui.Text(c.G, s.End == DateTime.Today ? "Today" : FinFmt.Day(s.End), fl, c.T.TextFaint, new RectangleF(r.X + r.Width / 2, r.Bottom + c.S(4), r.Width / 2, c.S(13)), 1);
            if (hov >= 0)
            {
                var txt = FinFmt.Day(s.DayOf(s.Length - vals.Length + hov)) + "  ·  " + FinFmt.Value(vals[hov], money, cur, true);
                var f = c.FS(11); float w = Ui.MeasureW(c.G, txt, f) + c.S(14), h = c.S(20);
                float cx = r.X + r.Width * (hov + 0.5f) / vals.Length;
                var tr = new RectangleF(Math.Max(r.X, Math.Min(r.Right - w, cx - w / 2)), r.Y - c.S(10), w, h);
                Ui.FillRound(c.G, tr, h / 2, c.T.Dark ? Ui.Mix(c.T.Input, Color.White, 0.06) : Color.FromArgb(255, 30, 30, 36));
                Ui.Text(c.G, txt, f, Color.White, tr, 0);
            }
        }

        public static void Stat(DrawCtx c, RectangleF r, string label, string value, Color? vc = null)
        {
            Ui.Text(c.G, label, c.F(10.5f), c.T.TextDim, new RectangleF(r.X, r.Y, r.Width, c.S(14)));
            Ui.Text(c.G, value, c.FS(14), vc ?? c.T.Text, new RectangleF(r.X, r.Y + c.S(15), r.Width, c.S(20)));
        }
        public static void Divider(DrawCtx c, float x1, float x2, float y)
        {
            using (var pen = new Pen(Color.FromArgb(c.T.Dark ? 26 : 22, c.T.Text), 1f)) c.G.DrawLine(pen, x1, y, x2, y);
        }
        public static void OpenUrl(string url) { try { if (!string.IsNullOrEmpty(url)) System.Diagnostics.Process.Start(url); } catch { } }
    }

    /// <summary>
    /// Base for all provider widgets. Number tile: badge · "Stripe · 30d" / "$12.4k  +8%".
    /// Chart tile: badge · name+change / week total · 14-day sparkline. Panel: 30-day chart, stats, recent, connect/refresh.
    /// </summary>
    public abstract class FinMetricWidget : Widget
    {
        protected abstract string ProviderId { get; }
        protected virtual bool ChartTile { get { return false; } }
        /// <summary>Window of the number tile (days).</summary>
        protected virtual int Period { get { return P.Money ? 30 : 7; } }
        /// <summary>Number tile also shows a tiny trend line.</summary>
        protected virtual bool TrendInTile { get { return false; } }
        protected FinProvider P { get { return FinProviders.Get(ProviderId); } }
        protected FinSeries Data { get { return FinStore.Peek(P); } }
        int TileDays { get { return ChartTile ? 7 : Period; } }
        string Label { get { return ChartTile ? Meta.Name.Replace(" Chart", "") : P.Name; } }

        public override void OnStart() { FinStore.Ensure(P); Hub.Subscribe(P.HubKey, this); }
        public override void OnStop() { Hub.Unsubscribe(P.HubKey, this); }

        void Numbers(FinSeries s, int days, out double cur, out double ch)
        {
            if (s == null || s.Length == 0) { cur = double.NaN; ch = double.NaN; return; }
            cur = s.Sum(days); ch = s.ChangeOver(days);
        }
        string ValueText(FinSeries s, double v)
        {
            if (double.IsNaN(v)) return s != null && s.Error != null ? "Error" : "…";
            return FinFmt.Value(v, P.Money, s.Currency);
        }
        string SubLabel { get { return Label + " · " + (ChartTile ? "7d" : Period + "d"); } }

        // ---------------- tile ----------------
        public override int Measure(DrawCtx c)
        {
            var s = Data; double v, ch; Numbers(s, TileDays, out v, out ch);
            float pad = c.S(10), badge = c.S(18), gap = c.S(8);
            float l1 = Ui.MeasureW(c.G, SubLabel, c.F(10)) + (s != null && s.Sample ? c.S(4) + FinDraw.SampleTag(c, 0, 0, false) : 0);
            float l2 = Ui.MeasureW(c.G, ValueText(s, v), c.FS(13.5f)) + (double.IsNaN(v) ? 0 : c.S(5) + Ui.MeasureW(c.G, FinFmt.Pct(ch), c.FS(10.5f)));
            float w = pad + badge + gap + Math.Max(l1, l2) + pad;
            if (ChartTile) w += c.S(66);
            else if (TrendInTile) w += c.S(40);
            return (int)Math.Ceiling(w);
        }

        public override void Draw(DrawCtx c)
        {
            var s = Data; var b = c.Bounds; double v, ch; Numbers(s, TileDays, out v, out ch);
            float x = b.X + c.S(10), cy = b.Y + b.Height / 2f;
            FinDraw.Badge(c, P, new RectangleF(x, cy - c.S(9), c.S(18), c.S(18)));
            x += c.S(26);
            float right = b.Right - c.S(10) - (ChartTile ? c.S(66) : TrendInTile ? c.S(40) : 0);
            // line 1: label (+ sample tag)
            var f1 = c.F(10); float y1 = cy - c.S(7.5f);
            Ui.Text(c.G, SubLabel, f1, c.T.TextDim, new RectangleF(x, y1 - c.S(7), right - x, c.S(14)));
            if (s != null && s.Sample) FinDraw.SampleTag(c, x + Ui.MeasureW(c.G, SubLabel, f1) + c.S(4), y1);
            else if (s != null && s.Stale) Ui.FillCircle(c.G, new PointF(x + Ui.MeasureW(c.G, SubLabel, f1) + c.S(6), y1), c.S(2.5f), c.T.Warn);
            // line 2: value + change
            float y2 = cy + c.S(6.5f); var vt = ValueText(s, v); var f2 = c.FS(13.5f);
            Ui.Text(c.G, vt, f2, s != null && s.Error != null && double.IsNaN(v) ? c.T.Warn : c.T.Text, new RectangleF(x, y2 - c.S(9), right - x, c.S(18)));
            if (!double.IsNaN(v))
            {
                float vx = x + Ui.MeasureW(c.G, vt, f2) + c.S(5);
                Ui.Text(c.G, FinFmt.Pct(ch), c.FS(10.5f), FinDraw.ChangeColor(c, ch), new RectangleF(vx, y2 - c.S(8), right - vx + c.S(40), c.S(17)));
            }
            // trend
            if ((ChartTile || TrendInTile) && s != null && s.Length >= 2)
            {
                float w = ChartTile ? c.S(58) : c.S(34);
                var r = new RectangleF(b.Right - c.S(10) - w, b.Y + c.S(8), w, b.Height - c.S(16));
                var vals = s.Tail(ChartTile ? 14 : 7);
                var col = double.IsNaN(ch) || ch >= 0 ? c.T.Accent : c.T.Bad;
                Ui.Sparkline(c.G, r, vals, col, true, c.S(1.6f), 0, double.NaN);
                double mx = Math.Max(1e-6, vals.Max());
                Ui.FillCircle(c.G, new PointF(r.Right, r.Bottom - (float)(vals[vals.Length - 1] / mx) * r.Height), c.S(2.2f), col);
            }
        }

        // ---------------- panel ----------------
        bool HasTxns { get { return P.Money; } }
        public override Size PanelSize(float scale) { return new Size((int)(340 * scale), (int)((HasTxns ? 412 : 318) * scale)); }
        public override void OnPanelOpen() { var s = Data; if (s != null && HasAccount && (DateTime.Now - s.Fetched).TotalMinutes > 5) Hub.Refresh(P.HubKey); }

        public override void DrawPanel(DrawCtx c)
        {
            var s = Data; var b = c.Bounds; float L = b.X + c.S(16), R = b.Right - c.S(16);
            int days = P.Money ? 30 : Math.Min(28, s != null ? s.Length : 28);
            int win = ChartTile ? 7 : Period;
            // header
            FinDraw.Badge(c, P, new RectangleF(L, b.Y + c.S(14), c.S(22), c.S(22)));
            Ui.Text(c.G, P.Name, c.FS(14), c.T.Text, new RectangleF(L + c.S(30), b.Y + c.S(14), c.S(180), c.S(22)));
            if (s != null && s.Sample) { var tw = FinDraw.SampleTag(c, 0, 0, false); FinDraw.SampleTag(c, R - tw, b.Y + c.S(25)); }
            else if (s != null) Ui.Text(c.G, s.Stale ? "Offline · cached" : "Updated " + FinFmt.Ago(s.Fetched), c.F(10.5f), s.Stale ? c.T.Warn : c.T.TextFaint, new RectangleF(R - c.S(140), b.Y + c.S(14), c.S(140), c.S(22)), 1);

            if (s == null || s.Length == 0)
            {
                var msg = s == null ? "Loading…" : s.Error ?? "No data";
                Ui.Icon(c.G, s == null ? Icons.Refresh : Icons.Warning, new RectangleF(b.X, b.Y + c.S(90), b.Width, c.S(30)), s == null ? c.T.TextDim : c.T.Warn, c.S(22));
                Ui.TextWrap(c.G, msg, c.F(12), c.T.TextDim, new RectangleF(L + c.S(20), b.Y + c.S(130), R - L - c.S(40), c.S(60)));
                Footer(c, s); return;
            }

            // headline
            double v = s.Sum(win), ch = s.ChangeOver(win);
            float y = b.Y + c.S(48);
            var big = FinFmt.Value(v, P.Money, s.Currency, true); var bf = c.F(26, true);
            Ui.Text(c.G, big, bf, c.T.Text, new RectangleF(L, y, R - L, c.S(34)));
            FinDraw.ChangePill(c, ch, L + Ui.MeasureW(c.G, big, bf) + c.S(10), y + c.S(18), 11);
            string what = P.Money ? (P.Id == "gumroad" ? "kept after fees" : "net revenue") : P.Unit;
            string period = win == 7 ? "this week" : "last " + win + " days";
            Ui.Text(c.G, Cap(what) + " · " + period + " vs previous", c.F(11), c.T.TextDim, new RectangleF(L, y + c.S(34), R - L, c.S(16)));
            if (s.Currency != "USD" && P.Money) Ui.Text(c.G, s.Currency, c.F(10.5f, true), c.T.TextFaint, new RectangleF(L, y + c.S(34), R - L, c.S(16)), 1);

            // chart
            var cr = new RectangleF(L, b.Y + c.S(118), R - L, c.S(86));
            FinDraw.Chart(c, cr, s, days, P.Money, s.Currency, P.Money);

            // stats
            float sy = b.Y + c.S(226), cw = (R - L) / 3f;
            var tail = s.Tail(days); double best = tail.Length > 0 ? tail.Max() : 0; int bi = Array.IndexOf(tail, best);
            if (P.Money)
            {
                FinDraw.Stat(c, new RectangleF(L, sy, cw, c.S(36)), "Avg / day", FinFmt.Money(s.Sum(30) / 30, s.Currency));
                FinDraw.Stat(c, new RectangleF(L + cw, sy, cw, c.S(36)), "Best day", FinFmt.Money(best, s.Currency));
                FinDraw.Stat(c, new RectangleF(L + cw * 2, sy, cw, c.S(36)), Cap(P.Unit) + " · 30d", s.Count >= 0 ? FinFmt.CountFull(s.Count) : "—");
            }
            else
            {
                double wk = s.Sum(7), pw = s.Sum(7, 7);
                FinDraw.Stat(c, new RectangleF(L, sy, cw, c.S(36)), win == 7 ? "Last week" : "This week", FinFmt.Count(win == 7 ? pw : wk));
                FinDraw.Stat(c, new RectangleF(L + cw, sy, cw, c.S(36)), "Daily avg", FinFmt.Count(Math.Round(s.Sum(days) / days)));
                FinDraw.Stat(c, new RectangleF(L + cw * 2, sy, cw, c.S(36)), "Best day", FinFmt.Count(best) + (bi >= 0 ? "  " + s.DayOf(s.Length - tail.Length + bi).ToString("MMM d") : ""));
            }

            // recent transactions
            if (HasTxns)
            {
                float ry = b.Y + c.S(274);
                FinDraw.Divider(c, L, R, ry - c.S(6));
                Ui.Text(c.G, "Recent", c.F(10.5f), c.T.TextDim, new RectangleF(L, ry, R - L, c.S(16)));
                ry += c.S(18);
                if (s.Recent.Count == 0) Ui.Text(c.G, "No recent transactions", c.F(11.5f), c.T.TextFaint, new RectangleF(L, ry, R - L, c.S(22)));
                foreach (var t in s.Recent.Take(3))
                {
                    var amt = FinFmt.MoneyFull(t.Amount, s.Currency); var af = c.FS(12); float aw = Ui.MeasureW(c.G, amt, af);
                    var ago = FinFmt.Ago(t.When); var gf = c.F(10.5f); float gw = Ui.MeasureW(c.G, ago, gf);
                    Ui.Text(c.G, t.Label, c.F(12), c.T.Text, new RectangleF(L, ry, R - L - aw - gw - c.S(20), c.S(22)));
                    Ui.Text(c.G, ago, gf, c.T.TextFaint, new RectangleF(R - aw - gw - c.S(10), ry, gw + c.S(2), c.S(22)), 1);
                    Ui.Text(c.G, amt, af, t.Amount < 0 ? c.T.Bad : c.T.Text, new RectangleF(R - aw - c.S(2), ry, aw + c.S(2), c.S(22)), 1);
                    ry += c.S(22);
                }
            }
            Footer(c, s);
        }

        void Footer(DrawCtx c, FinSeries s)
        {
            var b = c.Bounds; float L = b.X + c.S(16), R = b.Right - c.S(16), y = b.Bottom - c.S(44);
            FinDraw.Divider(c, L, R, y - c.S(4));
            if (!HasAccount)
            {
                Ui.Text(c.G, "Showing sample data", c.F(11), c.T.TextDim, new RectangleF(L, y + c.S(2), c.S(150), c.S(28)));
                c.Button(new RectangleF(R - c.S(150), y + c.S(2), c.S(150), c.S(28)), "Connect in Settings", () => Host.OpenSettings("accounts"), true);
                return;
            }
            string note = s != null && s.Error != null ? s.Error : null;
            if (note != null) Ui.Text(c.G, note, c.F(10.5f), c.T.Warn, new RectangleF(L, y + c.S(2), R - L - c.S(150), c.S(28)));
            else Ui.Text(c.G, "Refreshes every 15 min", c.F(10.5f), c.T.TextFaint, new RectangleF(L, y + c.S(2), R - L - c.S(150), c.S(28)));
            c.IconButton(new RectangleF(R - c.S(140), y + c.S(2), c.S(28), c.S(28)), Icons.Refresh, () => Hub.Refresh(P.HubKey), false, 13);
            var url = P.Dashboard; c.Button(new RectangleF(R - c.S(106), y + c.S(2), c.S(106), c.S(28)), "Dashboard", () => FinDraw.OpenUrl(url), false, Icons.Link);
        }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
    }

    // ================================================================ Finance
    [Widget("polar", "Polar", "Finance", "Net revenue over the last 30 days from Polar.", RefName = "Polar", Account = "polar", Data = DataKind.NeedsAccount)]
    public class FinPolarWidget : FinMetricWidget { protected override string ProviderId { get { return "polar"; } } }
    [Widget("polar-chart", "Polar Chart", "Finance", "Your Polar revenue as a 14-day line.", RefName = "Polar Chart", Account = "polar", Data = DataKind.NeedsAccount)]
    public class FinPolarChartWidget : FinMetricWidget { protected override string ProviderId { get { return "polar"; } } protected override bool ChartTile { get { return true; } } }

    [Widget("stripe", "Stripe", "Finance", "Net revenue over the last 30 days from Stripe.", RefName = "Stripe", Account = "stripe", Data = DataKind.NeedsAccount)]
    public class FinStripeWidget : FinMetricWidget { protected override string ProviderId { get { return "stripe"; } } }
    [Widget("stripe-chart", "Stripe Chart", "Finance", "Your Stripe revenue as a 14-day line.", RefName = "Stripe Chart", Account = "stripe", Data = DataKind.NeedsAccount)]
    public class FinStripeChartWidget : FinMetricWidget { protected override string ProviderId { get { return "stripe"; } } protected override bool ChartTile { get { return true; } } }

    [Widget("dodo", "Dodo Payments", "Finance", "Net revenue over the last 30 days from Dodo Payments.", RefName = "Dodo Payments", Account = "dodo", Data = DataKind.NeedsAccount)]
    public class FinDodoWidget : FinMetricWidget { protected override string ProviderId { get { return "dodo"; } } }
    [Widget("dodo-chart", "Dodo Chart", "Finance", "Your Dodo Payments revenue as a 14-day line.", RefName = "Dodo Chart", Account = "dodo", Data = DataKind.NeedsAccount)]
    public class FinDodoChartWidget : FinMetricWidget { protected override string ProviderId { get { return "dodo"; } } protected override bool ChartTile { get { return true; } } }

    [Widget("shopify", "Shopify", "Finance", "Your store's net sales over the last 30 days.", RefName = "Shopify", Account = "shopify", Data = DataKind.NeedsAccount)]
    public class FinShopifyWidget : FinMetricWidget { protected override string ProviderId { get { return "shopify"; } } }
    [Widget("shopify-chart", "Shopify Chart", "Finance", "This week's Shopify sales and how they moved, over 14 days.", RefName = "Shopify Chart", Account = "shopify", Data = DataKind.NeedsAccount)]
    public class FinShopifyChartWidget : FinMetricWidget { protected override string ProviderId { get { return "shopify"; } } protected override bool ChartTile { get { return true; } } }

    [Widget("lemonsqueezy", "Lemon Squeezy", "Finance", "Revenue after tax and refunds over 30 days.", RefName = "Lemon Squeezy", Account = "lemonsqueezy", Data = DataKind.NeedsAccount)]
    public class FinLemonWidget : FinMetricWidget { protected override string ProviderId { get { return "lemonsqueezy"; } } }
    [Widget("lemonsqueezy-chart", "Lemon Squeezy Chart", "Finance", "This week's Lemon Squeezy revenue over a 14-day line.", RefName = "Lemon Squeezy Chart", Account = "lemonsqueezy", Data = DataKind.NeedsAccount)]
    public class FinLemonChartWidget : FinMetricWidget { protected override string ProviderId { get { return "lemonsqueezy"; } } protected override bool ChartTile { get { return true; } } }

    [Widget("paddle", "Paddle", "Finance", "What you earned from Paddle transactions over 30 days.", RefName = "Paddle", Account = "paddle", Data = DataKind.NeedsAccount)]
    public class FinPaddleWidget : FinMetricWidget { protected override string ProviderId { get { return "paddle"; } } }
    [Widget("paddle-chart", "Paddle Chart", "Finance", "This week's Paddle revenue over a 14-day line.", RefName = "Paddle Chart", Account = "paddle", Data = DataKind.NeedsAccount)]
    public class FinPaddleChartWidget : FinMetricWidget { protected override string ProviderId { get { return "paddle"; } } protected override bool ChartTile { get { return true; } } }

    [Widget("gumroad", "Gumroad", "Finance", "What you kept from Gumroad sales over 30 days, after the fee.", RefName = "Gumroad", Account = "gumroad", Data = DataKind.NeedsAccount)]
    public class FinGumroadWidget : FinMetricWidget { protected override string ProviderId { get { return "gumroad"; } } }
    [Widget("gumroad-chart", "Gumroad Chart", "Finance", "This week's Gumroad sales over a 14-day line.", RefName = "Gumroad Chart", Account = "gumroad", Data = DataKind.NeedsAccount)]
    public class FinGumroadChartWidget : FinMetricWidget { protected override string ProviderId { get { return "gumroad"; } } protected override bool ChartTile { get { return true; } } }

    // ================================================================ Analytics
    [Widget("searchconsole", "Search Console", "Analytics", "Clicks from Google Search over the last 28 days and how they changed.", RefName = "Search Console", Account = "searchconsole", Data = DataKind.NeedsAccount)]
    public class FinSearchConsoleWidget : FinMetricWidget { protected override string ProviderId { get { return "searchconsole"; } } protected override int Period { get { return 28; } } }

    [Widget("datafast-chart", "DataFast Chart", "Analytics", "Your site's daily visitors over 14 days, with the week's total and its change.", RefName = "DataFast Chart", Account = "datafast", Data = DataKind.NeedsAccount)]
    public class FinDataFastChartWidget : FinMetricWidget { protected override string ProviderId { get { return "datafast"; } } protected override bool ChartTile { get { return true; } } }

    [Widget("plausible", "Plausible", "Analytics", "Visitors this week from Plausible, with the trend.", RefName = "Plausible", Account = "plausible", Data = DataKind.NeedsAccount)]
    public class FinPlausibleWidget : FinMetricWidget { protected override string ProviderId { get { return "plausible"; } } protected override bool TrendInTile { get { return true; } } }

    [Widget("dub", "Dub", "Analytics", "Clicks on your Dub links this week.", RefName = "Dub", Account = "dub", Data = DataKind.NeedsAccount)]
    public class FinDubWidget : FinMetricWidget { protected override string ProviderId { get { return "dub"; } } }

    // ================================================================ Total Revenue
    [Widget("totalrevenue", "Total Revenue", "Finance", "Every connected store's last 30 days added up, with a monthly goal and its pace.", RefName = "Total Revenue", Data = DataKind.NeedsAccount)]
    public class FinTotalRevenueWidget : Widget
    {
        // The registry runs every widget's static ctor at startup: this defines all Finance/Analytics accounts.
        static FinTotalRevenueWidget() { FinProviders.Init(); }
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Number("goal", "Monthly goal (USD)", 10000, 0, 1e9) }; } }
        double Goal { get { return Math.Max(0, SNum("goal")); } }
        /// <summary>Goal used for display: sample mode uses a goal that fits the sample numbers.</summary>
        double GoalFor(Combined cmb) { return cmb.Sample && Goal > 0 ? 35000 : Goal; }

        public override void OnStart() { foreach (var p in FinProviders.Revenue) { FinStore.Ensure(p); Hub.Subscribe(p.HubKey, this); } }
        public override void OnStop() { foreach (var p in FinProviders.Revenue) Hub.Unsubscribe(p.HubKey, this); }

        class Combined { public double[] Values = new double[60]; public bool Sample; public List<KeyValuePair<FinProvider, FinSeries>> Parts = new List<KeyValuePair<FinProvider, FinSeries>>(); public bool Mixed, Stale, Loading; public FinSeries AsSeries; }
        Combined Compute()
        {
            var cmb = new Combined();
            var connected = FinProviders.Revenue.Where(p => Accounts.Connected(p.Account)).ToList();
            cmb.Sample = connected.Count == 0;
            var use = cmb.Sample ? FinProviders.Revenue.ToList() : connected;
            int n = cmb.Values.Length; var today = DateTime.Today;
            foreach (var p in use)
            {
                var s = FinStore.Peek(p);
                if (s == null) { cmb.Loading = true; continue; }
                if (s.Length == 0) { cmb.Stale = true; continue; }
                cmb.Parts.Add(new KeyValuePair<FinProvider, FinSeries>(p, s));
                if (s.Stale) cmb.Stale = true;
                if (s.Currency != "USD") cmb.Mixed = true;
                for (int i = 0; i < n; i++) cmb.Values[i] += s.ValueOn(today.AddDays(i - (n - 1)));
            }
            cmb.AsSeries = new FinSeries { Values = cmb.Values, End = today, Sample = cmb.Sample };
            return cmb;
        }
        static double MonthToDate(FinSeries s) { var t = DateTime.Today; double m = 0; for (var d = new DateTime(t.Year, t.Month, 1); d <= t; d = d.AddDays(1)) m += s.ValueOn(d); return m; }
        static double MonthFrac() { var n = DateTime.Now; int dim = DateTime.DaysInMonth(n.Year, n.Month); return Math.Max(0.02, ((n.Day - 1) + n.TimeOfDay.TotalHours / 24) / dim); }

        public override int Measure(DrawCtx c)
        {
            var cmb = Compute(); double v = cmb.AsSeries.Sum(30), ch = cmb.AsSeries.ChangeOver(30);
            float l1 = Ui.MeasureW(c.G, "Total · 30d", c.F(10)) + (cmb.Sample ? c.S(4) + FinDraw.SampleTag(c, 0, 0, false) : 0);
            float l2 = Ui.MeasureW(c.G, FinFmt.Money(v), c.FS(13.5f)) + c.S(5) + Ui.MeasureW(c.G, FinFmt.Pct(ch), c.FS(10.5f));
            return (int)Math.Ceiling(c.S(10) + c.S(18) + c.S(8) + Math.Max(l1, l2) + (Goal > 0 ? c.S(32) : 0) + c.S(10));
        }

        public override void Draw(DrawCtx c)
        {
            var cmb = Compute(); var s = cmb.AsSeries; var b = c.Bounds;
            double v = s.Sum(30), ch = s.ChangeOver(30);
            float x = b.X + c.S(10), cy = b.Y + b.Height / 2f;
            var br = new RectangleF(x, cy - c.S(9), c.S(18), c.S(18));
            c.G.FillEllipse(Ui.B(c.T.AccentA(c.T.Dark ? 60 : 44)), br);
            Ui.Icon(c.G, Icons.Money, br, c.T.Dark ? Ui.Mix(c.T.Accent, Color.White, 0.35) : c.T.Accent, c.S(11));
            x += c.S(26);
            var f1 = c.F(10); float y1 = cy - c.S(7.5f); const string lab = "Total · 30d";
            Ui.Text(c.G, lab, f1, c.T.TextDim, new RectangleF(x, y1 - c.S(7), c.S(120), c.S(14)));
            if (cmb.Sample) FinDraw.SampleTag(c, x + Ui.MeasureW(c.G, lab, f1) + c.S(4), y1);
            else if (cmb.Stale) Ui.FillCircle(c.G, new PointF(x + Ui.MeasureW(c.G, lab, f1) + c.S(6), y1), c.S(2.5f), c.T.Warn);
            float y2 = cy + c.S(6.5f); var vt = cmb.Parts.Count == 0 && cmb.Loading ? "…" : FinFmt.Money(v); var f2 = c.FS(13.5f);
            Ui.Text(c.G, vt, f2, c.T.Text, new RectangleF(x, y2 - c.S(9), c.S(120), c.S(18)));
            float vx = x + Ui.MeasureW(c.G, vt, f2) + c.S(5);
            Ui.Text(c.G, FinFmt.Pct(ch), c.FS(10.5f), FinDraw.ChangeColor(c, ch), new RectangleF(vx, y2 - c.S(8), c.S(60), c.S(17)));
            if (Goal > 0)
            {
                // goal ring: month-to-date vs goal; tick = where you should be today
                double mtd = MonthToDate(s), frac = mtd / GoalFor(cmb), pace = MonthFrac();
                var rr = new RectangleF(b.Right - c.S(10) - c.S(22), cy - c.S(11), c.S(22), c.S(22));
                Ui.Ring(c.G, rr, Math.Min(1, frac), frac >= pace ? c.T.Good : c.T.Accent, c.T.Track, c.S(3));
                double ang = (pace * 360 - 90) * Math.PI / 180; float rad = c.S(11) - c.S(1.5f);
                var cc = new PointF(rr.X + rr.Width / 2, rr.Y + rr.Height / 2);
                Ui.FillCircle(c.G, new PointF(cc.X + (float)Math.Cos(ang) * rad, cc.Y + (float)Math.Sin(ang) * rad), c.S(1.6f), c.T.Text);
                Ui.Text(c.G, Math.Min(999, Math.Round(frac * 100)).ToString("0"), c.FS(7.5f), c.T.TextDim, rr, 0);
            }
        }

        public override Size PanelSize(float scale)
        {
            var cmb = Compute(); int rows = Math.Max(1, cmb.Parts.Count);
            return new Size((int)(340 * scale), (int)((260 + rows * 26) * scale));
        }

        public override void DrawPanel(DrawCtx c)
        {
            var cmb = Compute(); var s = cmb.AsSeries; var b = c.Bounds; float L = b.X + c.S(16), R = b.Right - c.S(16);
            float y = c.PanelHeader("Total Revenue", null, Icons.Money);
            if (cmb.Sample) { var tw = FinDraw.SampleTag(c, 0, 0, false); FinDraw.SampleTag(c, R - tw, b.Y + c.S(23)); }
            else Ui.Text(c.G, cmb.Parts.Count + (cmb.Parts.Count == 1 ? " store" : " stores"), c.F(10.5f), c.T.TextFaint, new RectangleF(R - c.S(120), b.Y + c.S(14), c.S(120), c.S(18)), 1);

            double v = s.Sum(30), ch = s.ChangeOver(30);
            y = b.Y + c.S(46);
            var big = FinFmt.MoneyFull(v); var bf = c.F(26, true);
            Ui.Text(c.G, big, bf, c.T.Text, new RectangleF(L, y, R - L, c.S(34)));
            FinDraw.ChangePill(c, ch, L + Ui.MeasureW(c.G, big, bf) + c.S(10), y + c.S(18), 11);
            Ui.Text(c.G, "Last 30 days vs previous" + (cmb.Mixed ? " · currencies not converted" : ""), c.F(11), c.T.TextDim, new RectangleF(L, y + c.S(34), R - L, c.S(16)));

            // goal + pace
            float gy = b.Y + c.S(110);
            var now = DateTime.Now; double mtd = MonthToDate(s), goal = GoalFor(cmb), pace = MonthFrac();
            Ui.Text(c.G, now.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture) + " goal", c.FS(12), c.T.Text, new RectangleF(L, gy, R - L, c.S(18)));
            if (goal > 0)
            {
                Ui.Text(c.G, FinFmt.MoneyFull(mtd) + " / " + FinFmt.MoneyFull(goal), c.F(11.5f), c.T.TextDim, new RectangleF(L, gy, R - L, c.S(18)), 1);
                var pr = new RectangleF(L, gy + c.S(26), R - L, c.S(8)); double frac = mtd / goal;
                Ui.Progress(c.G, pr, frac, frac >= pace ? c.T.Good : c.T.Accent, c.T.Track);
                float px = pr.X + pr.Width * (float)Math.Min(1, pace);
                Ui.FillRound(c.G, new RectangleF(px - c.S(1.5f), pr.Y - c.S(4), c.S(3), pr.Height + c.S(8)), c.S(1.5f), c.T.Text);
                double proj = mtd / pace; double diff = mtd - goal * pace;
                string line = "On pace for " + FinFmt.Money(proj) + " · " + (diff >= 0 ? FinFmt.Money(diff) + " ahead" : FinFmt.Money(-diff) + " behind");
                Ui.Text(c.G, line, c.F(11), diff >= 0 ? c.T.Good : c.T.TextDim, new RectangleF(L, gy + c.S(40), R - L, c.S(16)));
                Ui.Text(c.G, Math.Round(frac * 100) + "%", c.FS(11), c.T.TextDim, new RectangleF(L, gy + c.S(40), R - L, c.S(16)), 1);
            }
            else Ui.Text(c.G, "Set a monthly goal in the widget's settings", c.F(11), c.T.TextFaint, new RectangleF(L, gy + c.S(22), R - L, c.S(16)));

            // per-store breakdown
            float ly = b.Y + c.S(186);
            FinDraw.Divider(c, L, R, ly - c.S(6));
            Ui.Text(c.G, "By store", c.F(10.5f), c.T.TextDim, new RectangleF(L, ly, R - L, c.S(16)));
            Ui.Text(c.G, "30 days", c.F(10.5f), c.T.TextFaint, new RectangleF(L, ly, R - L, c.S(16)), 1);
            ly += c.S(22);
            var parts = cmb.Parts.Select(kv => new { P = kv.Key, S = kv.Value, V = kv.Value.Sum(30) }).OrderByDescending(x => x.V).ToList();
            double top = parts.Count > 0 ? Math.Max(1e-6, parts.Max(x => x.V)) : 1;
            if (parts.Count == 0) Ui.Text(c.G, cmb.Loading ? "Loading…" : "No data from your stores yet", c.F(11.5f), c.T.TextFaint, new RectangleF(L, ly, R - L, c.S(22)));
            foreach (var x in parts)
            {
                FinDraw.Badge(c, x.P, new RectangleF(L, ly + c.S(3), c.S(16), c.S(16)));
                Ui.Text(c.G, x.P.Name, c.F(12), c.T.Text, new RectangleF(L + c.S(24), ly, c.S(110), c.S(22)));
                var amt = FinFmt.MoneyFull(x.V, x.S.Currency); var af = c.FS(12);
                Ui.Text(c.G, amt, af, c.T.Text, new RectangleF(R - c.S(80), ly, c.S(80), c.S(22)), 1);
                var bar = new RectangleF(L + c.S(130), ly + c.S(8.5f), R - L - c.S(130) - c.S(90), c.S(5));
                Ui.Progress(c.G, bar, x.V / top, c.T.AccentA(x.S.Stale ? 90 : 200), c.T.Track);
                ly += c.S(26);
            }

            // footer
            float fy = b.Bottom - c.S(44);
            FinDraw.Divider(c, L, R, fy - c.S(4));
            if (cmb.Sample)
            {
                Ui.Text(c.G, "Showing sample data", c.F(11), c.T.TextDim, new RectangleF(L, fy + c.S(2), c.S(150), c.S(28)));
                c.Button(new RectangleF(R - c.S(150), fy + c.S(2), c.S(150), c.S(28)), "Connect in Settings", () => Host.OpenSettings("accounts"), true);
            }
            else
            {
                Ui.Text(c.G, cmb.Stale ? "Some stores couldn't refresh" : "Add more stores in Settings", c.F(10.5f), cmb.Stale ? c.T.Warn : c.T.TextFaint, new RectangleF(L, fy + c.S(2), R - L - c.S(110), c.S(28)));
                c.Button(new RectangleF(R - c.S(100), fy + c.S(2), c.S(100), c.S(28)), "Accounts", () => Host.OpenSettings("accounts"), false, Icons.Settings);
            }
        }
    }
}
