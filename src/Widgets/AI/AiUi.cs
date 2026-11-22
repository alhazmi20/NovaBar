// Drawing for AI widgets: tiles (rings / value chips) and the shared panel layout.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace NovaBar
{
    public class AiPanelOpts
    {
        public int MaxWins = 4; public bool Stats = true, Chart = true, Models = true, Big = true;
        public string BigLabel;   // caption under the big value
    }

    public static class AiUi
    {
        /// <summary>Small rounded brand chip with the provider initial.</summary>
        public static void Chip(DrawCtx c, RectangleF r, string key)
        {
            var b = AiBrand.Of(key, c.T);
            Ui.FillRound(c.G, r, r.Width * 0.3f, Color.FromArgb(c.T.IsGlass ? (c.T.Dark ? 72 : 52) : (c.T.Dark ? 46 : 34), b));
            Mark(c, r, key, r.Height * 0.52f, c.T.IsGlass && c.T.Dark ? Ui.Mix(b, Color.White, 0.3) : b);
        }
        /// <summary>Four-point sparkle (concave sides), Gemini's mark.</summary>
        public static void Sparkle(Graphics g, PointF o, float rad, Color col)
        {
            float k = rad * 0.12f;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                var top = new PointF(o.X, o.Y - rad); var right = new PointF(o.X + rad, o.Y); var bot = new PointF(o.X, o.Y + rad); var left = new PointF(o.X - rad, o.Y);
                path.AddBezier(top, new PointF(o.X + k, o.Y - k), new PointF(o.X + k, o.Y - k), right);
                path.AddBezier(right, new PointF(o.X + k, o.Y + k), new PointF(o.X + k, o.Y + k), bot);
                path.AddBezier(bot, new PointF(o.X - k, o.Y + k), new PointF(o.X - k, o.Y + k), left);
                path.AddBezier(left, new PointF(o.X - k, o.Y - k), new PointF(o.X - k, o.Y - k), top);
                path.CloseFigure(); g.FillPath(Ui.B(col), path);
            }
        }
        /// <summary>Provider mark: a letter (or sparkle for Gemini), distinct per provider.</summary>
        public static void Mark(DrawCtx c, RectangleF r, string key, float px, Color col)
        {
            if (key == "gemini") { Sparkle(c.G, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), px * 0.62f, col); return; }
            string l = "C";
            switch (key) { case "chatgpt": l = "G"; break; case "grok": l = "X"; break; case "deepseek": l = "D"; break; case "glm": l = "Z"; break; case "opencode": l = "O"; break; case "cursor": l = "Cu"; px *= 0.82f; break; }
            Ui.Text(c.G, l ?? "?", Ui.FontSemi(px), col, r, 0);
        }

        /// <summary>Tiny "sample data" marker for tiles (top-right dot).</summary>
        public static void SampleDot(DrawCtx c)
        {
            var b = c.Bounds; Ui.FillCircle(c.G, new PointF(b.Right - c.S(5), b.Y + c.S(5)), c.S(2.2f), Color.FromArgb(200, c.T.Warn));
        }

        public static void Ring(DrawCtx c, RectangleF r, double frac, Color col, float thick)
        {
            Ui.Ring(c.G, r, frac < 0 ? 0 : Math.Max(frac, 0.015), col, c.T.Track, thick);
        }

        // ------------------------------------------------------------------ tile: ring + two lines
        /// <summary>Single ring tile block. dual = concentric second ring for p.Wins[1]. Returns width used (also for measuring when draw=false).</summary>
        public static float RingBlock(DrawCtx c, float x, AiProv p, bool dual, bool compact, bool draw, string extra = null, bool label0 = false)
        {
            var b = c.Bounds; float rs = c.S(compact ? 22 : 24);
            var w0 = p.Wins.Count > 0 ? p.Wins[0] : null; var w1 = dual && p.Wins.Count > 1 ? p.Wins[1] : null;
            string l1a = w0 == null ? (p.Big ?? "—") : (label0 ? Short(w0.Label) + " " : "") + AiFmt.Pct(w0.Frac);
            string l1b = compact ? "" : (w1 != null ? "  ·  " + Short(w1.Label) + " " + AiFmt.Pct(w1.Frac) : "");
            string l2 = w0 == null ? (p.Small ?? "") : (compact ? AiFmt.Left(w0.Reset) : AiFmt.Resets(w0.Reset));
            if (l2 == "") l2 = compact ? Short(w0 == null ? "" : w0.Label) : (w0 != null ? w0.Label : "");
            if (!string.IsNullOrEmpty(extra)) l2 += "  ·  " + extra;
            var f1 = c.FS(compact ? 12.5f : 13); var f1b = c.F(11); var f2 = c.F(compact ? 10 : 10.5f);
            float tw = Math.Max(Ui.MeasureW(c.G, l1a, f1) + (l1b == "" ? 0 : Ui.MeasureW(c.G, l1b, f1b)), Ui.MeasureW(c.G, l2, f2));
            float width = rs + c.S(7) + tw;
            if (!draw) return width;
            var rr = new RectangleF(x, b.Y + (b.Height - rs) / 2f, rs, rs);
            if (w0 != null)
            {
                Ring(c, rr, w0.Frac, AiBrand.Usage(p.Key, w0.Frac, c.T), c.S(3f));
                if (w1 != null) Ring(c, Ui.Inset(rr, c.S(5), c.S(5)), w1.Frac, Ui.Mix(AiBrand.Usage(p.Key, w1.Frac, c.T), c.T.Text, 0.0), c.S(2.4f));
                else Mark(c, rr, p.Key, c.S(9), AiBrand.Of(p.Key, c.T));
            }
            else Chip(c, Ui.Inset(rr, c.S(1), c.S(1)), p.Key);
            float tx = rr.Right + c.S(7), cy = b.Y + b.Height / 2f;
            float h1 = c.S(16), h2 = c.S(13);
            float top = cy - (h1 + h2) / 2f + c.S(0.5f);
            int w1a = Ui.MeasureW(c.G, l1a, f1);
            Ui.Text(c.G, l1a, f1, c.T.Text, new RectangleF(tx, top, w1a + c.S(2), h1));
            if (l1b != "") Ui.Text(c.G, l1b, f1b, c.T.TextDim, new RectangleF(tx + w1a, top + c.S(0.5f), tw - w1a + c.S(4), h1));
            Ui.Text(c.G, l2, f2, c.T.TextDim, new RectangleF(tx, top + h1, tw + c.S(4), h2));
            return width;
        }
        public static string Short(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            if (label.StartsWith("Weekly")) return "wk"; if (label.StartsWith("Session")) return "5h";
            if (label.StartsWith("Flash")) return "Flash"; if (label.StartsWith("Pro")) return "Pro";
            if (label.StartsWith("5-hour")) return "5h";
            int i = label.IndexOf(' '); return i > 0 ? label.Substring(0, i) : label;
        }

        // ------------------------------------------------------------------ tile: chip + value
        public static float ValueBlock(DrawCtx c, float x, AiProv p, string big, string small, bool draw)
        {
            var b = c.Bounds; float cs = c.S(22);
            var f1 = c.FS(13); var f2 = c.F(10.5f);
            float tw = Math.Max(Ui.MeasureW(c.G, big, f1), Ui.MeasureW(c.G, small, f2));
            if (!draw) return cs + c.S(8) + tw;
            var cr = new RectangleF(x, b.Y + (b.Height - cs) / 2f, cs, cs);
            Chip(c, cr, p.Key);
            float tx = cr.Right + c.S(8), cy = b.Y + b.Height / 2f, h1 = c.S(16), h2 = c.S(13), top = cy - (h1 + h2) / 2f + c.S(0.5f);
            Ui.Text(c.G, big, f1, c.T.Text, new RectangleF(tx, top, tw + c.S(4), h1));
            Ui.Text(c.G, small, f2, c.T.TextDim, new RectangleF(tx, top + h1, tw + c.S(4), h2));
            return cs + c.S(8) + tw;
        }

        // ------------------------------------------------------------------ panel
        public static int PanelHeight(AiProv p, AiPanelOpts o)
        {
            float h = 14 + 40;                                      // header
            int nw = Math.Min(o.MaxWins, p.Wins.Count);
            for (int i = 0; i < nw; i++) h += string.IsNullOrEmpty(p.Wins[i].Detail) ? 40 : 54;
            if (o.Big && p.Wins.Count == 0 && p.Big != null) h += 52;
            if (o.Stats && p.Stats.Count > 0) h += 56;
            if (o.Chart && p.Week != null) h += 108;
            if (o.Models && p.Models.Count > 0) h += 26 + Math.Min(4, p.Models.Count) * 22 + 6;
            if (!string.IsNullOrEmpty(p.Note)) h += 30;
            if (p.NeedsAccount) h += 44;
            h += 8;
            return (int)h;
        }

        public static void Badge(DrawCtx c, float right, float cy, string text, Color col)
        {
            var f = c.FS(9.5f); float w = Ui.MeasureW(c.G, text, f) + c.S(14), h = c.S(17);
            var r = new RectangleF(right - w, cy - h / 2, w, h);
            Ui.FillRound(c.G, r, h / 2, Color.FromArgb(c.T.Dark ? 40 : 30, col));
            Ui.Text(c.G, text, f, col, r, 0);
        }

        public static void Panel(DrawCtx c, AiProv p, AiPanelOpts o, Action connect)
        {
            var g = c.G; var b = c.Bounds; var T = c.T;
            float x = b.X + c.S(16), w = b.Width - c.S(32), y = b.Y + c.S(14);
            var brand = AiBrand.Of(p.Key, T);
            // header
            Chip(c, new RectangleF(x, y + c.S(2), c.S(30), c.S(30)), p.Key);
            Ui.Text(g, p.Name, c.FS(14.5f), T.Text, new RectangleF(x + c.S(40), y, w - c.S(110), c.S(18)));
            Ui.Text(g, p.Source, c.F(11), T.TextDim, new RectangleF(x + c.S(40), y + c.S(18), w - c.S(40), c.S(16)));
            if (p.IsSample) Badge(c, x + w, y + c.S(9), "Sample", T.Warn);
            else Badge(c, x + w, y + c.S(9), "Live", T.Good);
            y += c.S(46);

            // windows
            int nw = Math.Min(o.MaxWins, p.Wins.Count);
            for (int i = 0; i < nw; i++)
            {
                var win = p.Wins[i];
                var col = AiBrand.Usage(p.Key, win.Frac, T);
                Ui.Text(g, win.Label, c.FS(12), T.Text, new RectangleF(x, y, w * 0.5f, c.S(18)));
                string pct = AiFmt.Pct(win.Frac), rs = AiFmt.Resets(win.Reset);
                var fp = c.FS(12.5f); var fr = c.F(11);
                float rw = rs == "" ? 0 : Ui.MeasureW(g, "  ·  " + rs, fr);
                if (rs != "") Ui.Text(g, "  ·  " + rs, fr, T.TextDim, new RectangleF(x + w - rw - c.S(2), y, rw + c.S(2), c.S(18)), 1);
                Ui.Text(g, pct, fp, win.Frac >= 0.75 ? col : T.Text, new RectangleF(x, y, w - rw, c.S(18)), 1);
                Ui.Progress(g, new RectangleF(x, y + c.S(23), w, c.S(6)), Math.Max(0, win.Frac), col, T.Track);
                y += c.S(40);
                if (!string.IsNullOrEmpty(win.Detail)) { Ui.Text(g, win.Detail, c.F(10.5f), T.TextFaint, new RectangleF(x, y - c.S(6), w, c.S(14))); y += c.S(14); }
            }

            // big value (balances / token counts)
            if (o.Big && p.Wins.Count == 0 && p.Big != null)
            {
                Ui.Text(g, p.Big, c.F(26, true), T.Text, new RectangleF(x - c.S(1), y, w, c.S(32)));
                Ui.Text(g, o.BigLabel ?? p.Small, c.F(11), T.TextDim, new RectangleF(x, y + c.S(32), w, c.S(14)));
                y += c.S(52);
            }

            // stats
            if (o.Stats && p.Stats.Count > 0)
            {
                int n = Math.Min(4, p.Stats.Count); float gap = c.S(6), cw = (w - gap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                {
                    var r = new RectangleF(x + i * (cw + gap), y, cw, c.S(44));
                    Ui.FillRound(g, r, c.S(9), T.Tile);
                    Ui.Text(g, p.Stats[i].Label, c.F(9.5f), T.TextDim, new RectangleF(r.X + c.S(9), r.Y + c.S(6), r.Width - c.S(14), c.S(14)));
                    Ui.Text(g, p.Stats[i].Value, c.FS(13), T.Text, new RectangleF(r.X + c.S(9), r.Y + c.S(21), r.Width - c.S(14), c.S(18)));
                }
                y += c.S(56);
            }

            // 7-day chart
            if (o.Chart && p.Week != null)
            {
                double tot = p.Week.Sum();
                Ui.Text(g, p.WeekLabel, c.FS(11.5f), T.TextDim, new RectangleF(x, y, w * 0.6f, c.S(16)));
                Ui.Text(g, (p.WeekMoney ? AiFmt.Money(tot, p.Cur) : AiFmt.Tok(tot)) + " · 7d", c.F(11), T.TextDim, new RectangleF(x, y, w, c.S(16)), 1);
                var cr = new RectangleF(x, y + c.S(24), w, c.S(58));
                using (var pen = new Pen(Color.FromArgb(T.Dark ? 22 : 30, T.Text), 1f))
                {
                    g.DrawLine(pen, cr.X, cr.Bottom + 0.5f, cr.Right, cr.Bottom + 0.5f);
                    g.DrawLine(pen, cr.X, cr.Y + cr.Height / 2, cr.Right, cr.Y + cr.Height / 2);
                }
                double mx = Math.Max(1e-9, p.Week.Max());
                float gap = c.S(10), bw = (cr.Width - gap * 6) / 7;
                var labels = AiDays.Labels();
                for (int i = 0; i < 7; i++)
                {
                    float bh = (float)(p.Week[i] / mx * (cr.Height - c.S(2)));
                    var br = new RectangleF(cr.X + i * (bw + gap), cr.Bottom - Math.Max(c.S(2), bh), bw, Math.Max(c.S(2), bh));
                    var col = i == 6 ? brand : Color.FromArgb(T.Dark ? 110 : 120, brand);
                    if (p.Week[i] <= 0) col = T.Track;
                    Ui.FillRound(g, br, Math.Min(bw / 2, c.S(4)), col);
                    Ui.Text(g, labels[i], c.F(9.5f), i == 6 ? T.Text : T.TextFaint, new RectangleF(br.X - c.S(4), cr.Bottom + c.S(4), bw + c.S(8), c.S(13)), 0);
                    if (c.IsHover(new RectangleF(br.X - gap / 2, cr.Y, bw + gap, cr.Height)) && p.Week[i] > 0)
                    {
                        var tip = p.WeekMoney ? AiFmt.Money(p.Week[i], p.Cur) : AiFmt.Tok(p.Week[i]);
                        var tf = c.FS(10); float tw2 = Ui.MeasureW(g, tip, tf) + c.S(10);
                        var tr = new RectangleF(Math.Min(cr.Right - tw2, Math.Max(cr.X, br.X + bw / 2 - tw2 / 2)), br.Y - c.S(19), tw2, c.S(16));
                        Ui.FillRound(g, tr, c.S(5), T.Text); Ui.Text(g, tip, tf, T.PanelBg.A > 200 ? T.PanelBg : (T.Dark ? Color.Black : Color.White), tr, 0);
                    }
                }
                if (tot <= 0) Ui.Text(g, "No activity this week", c.F(11), T.TextFaint, cr, 0);
                y += c.S(108);
            }

            // models
            if (o.Models && p.Models.Count > 0)
            {
                Ui.Text(g, p.ModelsLabel, c.FS(11.5f), T.TextDim, new RectangleF(x, y, w, c.S(16)));
                y += c.S(24);
                double tot = Math.Max(1e-9, p.Models.Sum(m => m.Tokens));
                int i = 0;
                foreach (var m in p.Models.Take(4))
                {
                    var col = i == 0 ? brand : T.Series[(i + 1) % T.Series.Length];
                    Ui.FillCircle(g, new PointF(x + c.S(4), y + c.S(9)), c.S(3.5f), col);
                    Ui.Text(g, m.Name, c.F(12), T.Text, new RectangleF(x + c.S(14), y, w * 0.45f, c.S(18)));
                    var bar = new RectangleF(x + w * 0.5f, y + c.S(7), w * 0.24f, c.S(4));
                    Ui.Progress(g, bar, m.Tokens / tot, Color.FromArgb(200, col), T.Track);
                    string v = AiFmt.Tok(m.Tokens) + (m.Cost > 0 && m.Count > 0 ? "" : "");
                    Ui.Text(g, v, c.FS(11.5f), T.Text, new RectangleF(x, y, w, c.S(18)), 1);
                    y += c.S(22); i++;
                }
                y += c.S(6);
            }

            if (!string.IsNullOrEmpty(p.Note))
            {
                Ui.Icon(g, Icons.Info, new RectangleF(x, y + c.S(2), c.S(14), c.S(14)), T.TextFaint, c.S(11));
                Ui.Text(g, p.Note, c.F(10.5f), T.TextDim, new RectangleF(x + c.S(19), y, w - c.S(19), c.S(18)));
                y += c.S(30);
            }
            if (p.NeedsAccount && connect != null)
            {
                c.Button(new RectangleF(x, y, w, c.S(32)), "Connect in Settings", connect, true, Icons.Link);
            }
        }
    }
}
