// Drawing helpers shared by the Online widgets: brand marks, tile stat layout, panel header/cards/rows/footer,
// contribution heat-map grid.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

namespace NovaBar
{
    public static class OnlDraw
    {
        static Color C(int r, int g, int b) { return Color.FromArgb(255, r, g, b); }

        // ------------------------------------------------------------------ brand marks
        /// <summary>Small app-icon style brand mark (rounded square). Brand colors only inside the mark.</summary>
        public static void Logo(Graphics g, string brand, RectangleF r, Theme t)
        {
            float rad = r.Width * 0.27f, w = r.Width;
            Color white = Color.White;
            switch (brand)
            {
                case "github":
                    {
                        var bg = t.Dark ? C(240, 240, 244) : C(36, 41, 47); var fg = t.Dark ? C(24, 24, 28) : white;
                        Ui.FillRound(g, r, rad, bg);
                        GitHubMark(g, Ui.Inset(r, w * 0.17f, w * 0.17f), fg, bg);
                        break;
                    }
                case "gmail":
                    {
                        Ui.FillRound(g, r, rad, t.Dark ? C(250, 250, 252) : C(255, 255, 255));
                        if (!t.Dark) Ui.StrokeRound(g, r, rad, Color.FromArgb(30, 0, 0, 0), 1f);
                        float x0 = r.X + w * 0.2f, x1 = r.Right - w * 0.2f, y0 = r.Y + w * 0.3f, y1 = r.Bottom - w * 0.28f, mid = r.X + w / 2;
                        using (var p = Ui.P(C(234, 67, 53), w * 0.11f)) g.DrawLines(p, new[] { new PointF(x0, y1), new PointF(x0, y0), new PointF(mid, r.Y + w * 0.53f), new PointF(x1, y0), new PointF(x1, y1) });
                        using (var p = Ui.P(C(66, 133, 244), w * 0.11f)) g.DrawLine(p, x0, y0 + w * 0.02f, x0, y1);
                        using (var p = Ui.P(C(52, 168, 83), w * 0.11f)) g.DrawLine(p, x1, y0 + w * 0.02f, x1, y1);
                        break;
                    }
                case "linear":
                    {
                        var bg = C(94, 106, 210);
                        Ui.FillRound(g, r, rad, bg);
                        var cr = Ui.Inset(r, w * 0.2f, w * 0.2f);
                        g.FillEllipse(Ui.B(white), cr);
                        var st = g.Save();
                        g.SetClip(cr);
                        using (var p = new Pen(bg, w * 0.07f))
                            for (int i = 0; i < 3; i++)
                            {
                                float o = cr.Width * (0.18f + 0.2f * i);
                                g.DrawLine(p, cr.X - 2, cr.Bottom - o - 2 - cr.Width, cr.X + o + cr.Width + 2, cr.Bottom + 2);
                            }
                        g.Restore(st);
                        break;
                    }
                case "supabase":
                    {
                        Ui.FillRound(g, r, rad, t.Dark ? C(36, 36, 40) : C(28, 28, 28));
                        var c = C(62, 207, 142);
                        var pts = new[] { new PointF(r.X + w * 0.56f, r.Y + w * 0.14f), new PointF(r.X + w * 0.24f, r.Y + w * 0.58f), new PointF(r.X + w * 0.5f, r.Y + w * 0.58f),
                                          new PointF(r.X + w * 0.44f, r.Bottom - w * 0.14f), new PointF(r.X + w * 0.76f, r.Y + w * 0.42f), new PointF(r.X + w * 0.5f, r.Y + w * 0.42f) };
                        g.FillPolygon(Ui.B(c), pts);
                        break;
                    }
                case "resend":
                    {
                        var bg = t.Dark ? C(245, 245, 247) : C(18, 18, 20); var fg = t.Dark ? C(18, 18, 20) : white;
                        Ui.FillRound(g, r, rad, bg);
                        Ui.Text(g, "R", Ui.FontSemi(w * 0.66f), fg, new RectangleF(r.X, r.Y + w * 0.02f, r.Width, r.Height), 0);
                        break;
                    }
                case "leetcode":
                    {
                        Ui.FillRound(g, r, rad, t.Dark ? C(40, 40, 44) : C(255, 255, 255));
                        if (!t.Dark) Ui.StrokeRound(g, r, rad, Color.FromArgb(30, 0, 0, 0), 1f);
                        float th = w * 0.11f;
                        using (var p = Ui.P(C(255, 161, 22), th))
                            g.DrawLines(p, new[] { new PointF(r.X + w * 0.6f, r.Y + w * 0.2f), new PointF(r.X + w * 0.3f, r.Y + w * 0.5f), new PointF(r.X + w * 0.6f, r.Bottom - w * 0.2f) });
                        using (var p = Ui.P(t.Dark ? C(200, 200, 205) : C(70, 70, 75), th)) g.DrawLine(p, r.X + w * 0.48f, r.Y + w * 0.5f, r.X + w * 0.76f, r.Y + w * 0.5f);
                        break;
                    }
                case "x":
                    {
                        var bg = t.Dark ? C(245, 245, 247) : C(0, 0, 0); var fg = t.Dark ? C(0, 0, 0) : white;
                        Ui.FillRound(g, r, rad, bg);
                        float a = w * 0.27f;
                        using (var p = new Pen(fg, w * 0.13f)) g.DrawLine(p, r.X + a, r.Y + a, r.Right - a, r.Bottom - a);
                        using (var p = new Pen(fg, w * 0.07f)) g.DrawLine(p, r.Right - a, r.Y + a, r.X + a, r.Bottom - a);
                        break;
                    }
                case "tiktok":
                    {
                        Ui.FillRound(g, r, rad, C(12, 12, 14));
                        float px = w * 0.62f;
                        var ir = new RectangleF(r.X, r.Y + w * 0.02f, r.Width, r.Height);
                        Ui.Icon(g, Icons.Music, new RectangleF(ir.X - w * 0.05f, ir.Y - w * 0.04f, ir.Width, ir.Height), C(37, 244, 238), px);
                        Ui.Icon(g, Icons.Music, new RectangleF(ir.X + w * 0.05f, ir.Y + w * 0.04f, ir.Width, ir.Height), C(254, 44, 85), px);
                        Ui.Icon(g, Icons.Music, ir, white, px);
                        break;
                    }
                case "youtube":
                    {
                        Ui.FillRound(g, r, rad, C(255, 0, 51));
                        float cx = r.X + w * 0.53f, cy = r.Y + w / 2, s = w * 0.2f;
                        g.FillPolygon(Ui.B(white), new[] { new PointF(cx - s * 0.85f, cy - s), new PointF(cx + s, cy), new PointF(cx - s * 0.85f, cy + s) });
                        break;
                    }
                case "instagram":
                    {
                        using (var path = Ui.Round(r, rad))
                        using (var br = new LinearGradientBrush(new PointF(r.X, r.Bottom), new PointF(r.Right, r.Y), C(254, 196, 60), C(193, 53, 132)))
                        {
                            var cb = new ColorBlend(3); cb.Colors = new[] { C(254, 196, 60), C(236, 50, 92), C(140, 58, 180) }; cb.Positions = new[] { 0f, 0.5f, 1f };
                            br.InterpolationColors = cb;
                            g.FillPath(br, path);
                        }
                        var ir = Ui.Inset(r, w * 0.22f, w * 0.22f);
                        Ui.StrokeRound(g, ir, ir.Width * 0.3f, white, w * 0.08f);
                        var cc = new PointF(r.X + w / 2, r.Y + w / 2);
                        using (var p = new Pen(white, w * 0.08f)) g.DrawEllipse(p, cc.X - w * 0.13f, cc.Y - w * 0.13f, w * 0.26f, w * 0.26f);
                        Ui.FillCircle(g, new PointF(ir.Right - w * 0.12f, ir.Y + w * 0.12f), w * 0.04f, white);
                        break;
                    }
                default:
                    Ui.FillRound(g, r, rad, t.Accent);
                    break;
            }
        }

        /// <summary>Simplified Octocat silhouette (circle head with ears + tail gap) — legible from 12 px up.</summary>
        static void GitHubMark(Graphics g, RectangleF r, Color fg, Color bg)
        {
            float w = r.Width;
            g.FillEllipse(Ui.B(fg), r);
            // cat head
            float hx = r.X + w * 0.5f, hy = r.Y + w * 0.47f;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(hx - w * 0.25f, hy - w * 0.2f, w * 0.5f, w * 0.42f);
                path.AddPolygon(new[] { new PointF(hx - w * 0.25f, hy - w * 0.05f), new PointF(hx - w * 0.23f, hy - w * 0.3f), new PointF(hx - w * 0.08f, hy - w * 0.17f) });
                path.AddPolygon(new[] { new PointF(hx + w * 0.25f, hy - w * 0.05f), new PointF(hx + w * 0.23f, hy - w * 0.3f), new PointF(hx + w * 0.08f, hy - w * 0.17f) });
                path.FillMode = FillMode.Winding;
                g.FillPath(Ui.B(bg), path);
            }
            // neck / body
            g.FillRectangle(Ui.B(bg), hx - w * 0.1f, hy + w * 0.15f, w * 0.2f, w * 0.38f);
        }

        /// <summary>git pull-request glyph drawn with lines (no font glyph exists for it).</summary>
        public static void PrGlyph(Graphics g, RectangleF r, Color c)
        {
            float s = Math.Min(r.Width, r.Height), x = r.X + (r.Width - s) / 2, y = r.Y + (r.Height - s) / 2;
            float pw = Math.Max(1f, s * 0.11f), cr = s * 0.13f;
            using (var p = Ui.P(c, pw))
            {
                float lx = x + s * 0.25f, rx = x + s * 0.75f, ty = y + s * 0.18f, by = y + s * 0.82f;
                g.DrawEllipse(p, lx - cr, ty - cr, cr * 2, cr * 2);
                g.DrawEllipse(p, lx - cr, by - cr, cr * 2, cr * 2);
                g.DrawEllipse(p, rx - cr, by - cr, cr * 2, cr * 2);
                g.DrawLine(p, lx, ty + cr, lx, by - cr);
                g.DrawLine(p, rx, by - cr, rx, ty + s * 0.12f);
                g.DrawLine(p, rx, ty + s * 0.12f, x + s * 0.5f, ty + s * 0.12f);
                g.DrawLines(p, new[] { new PointF(x + s * 0.6f, ty - s * 0.02f), new PointF(x + s * 0.48f, ty + s * 0.12f), new PointF(x + s * 0.6f, ty + s * 0.26f) });
            }
        }

        // ------------------------------------------------------------------ tile
        public static Font ValFont(DrawCtx c) { return c.FS(13); }
        public static Font LblFont(DrawCtx c) { return c.F(11.5f); }

        public static int BadgeW(DrawCtx c) { return Ui.MeasureW(c.G, "Sample", c.FS(9)) + c.Si(10); }
        /// <summary>Small "Sample" pill; returns its width.</summary>
        public static float Badge(DrawCtx c, float x, float cy, string text = "Sample")
        {
            var f = c.FS(9);
            float w = Ui.MeasureW(c.G, text, f) + c.S(10), h = c.S(14);
            var r = new RectangleF(x, cy - h / 2, w, h);
            Ui.FillRound(c.G, r, h / 2, c.T.AccentA(c.T.Dark ? 46 : 34));
            Ui.Text(c.G, text, f, c.T.Dark ? Ui.Mix(c.T.Accent, Color.White, 0.25) : Ui.Mix(c.T.Accent, Color.Black, 0.25), new RectangleF(r.X, r.Y + c.S(0.5f), r.Width, r.Height), 0);
            return w;
        }

        /// <summary>Standard stat tile: [logo] value label [extra in color] [Sample].</summary>
        public static int MeasureStat(DrawCtx c, string value, string label, bool sample, string extra = null)
        {
            float w = c.S(10) + c.S(18) + c.S(7) + Ui.MeasureW(c.G, value, ValFont(c));
            if (!string.IsNullOrEmpty(label)) w += c.S(4) + Ui.MeasureW(c.G, label, LblFont(c));
            if (!string.IsNullOrEmpty(extra)) w += c.S(5) + Ui.MeasureW(c.G, extra, c.FS(11));
            if (sample) w += c.S(6) + BadgeW(c);
            return (int)Math.Ceiling(w + c.S(11));
        }
        public static void Stat(DrawCtx c, string brand, string value, string label, bool sample, string extra = null, Color? extraColor = null, Color? valueColor = null)
        {
            var b = c.Bounds; float x = b.X + c.S(10), cy = b.Y + b.Height / 2f;
            float ls = c.S(18);
            Logo(c.G, brand, new RectangleF(x, cy - ls / 2, ls, ls), c.T);
            x += ls + c.S(7);
            var vf = ValFont(c); float vw = Ui.MeasureW(c.G, value, vf);
            Ui.Text(c.G, value, vf, valueColor ?? c.T.Text, new RectangleF(x, b.Y, vw + c.S(2), b.Height));
            x += vw;
            if (!string.IsNullOrEmpty(label))
            {
                x += c.S(4); var lf = LblFont(c); float lw = Ui.MeasureW(c.G, label, lf);
                Ui.Text(c.G, label, lf, c.T.TextDim, new RectangleF(x, b.Y + c.S(0.5f), lw + c.S(2), b.Height));
                x += lw;
            }
            if (!string.IsNullOrEmpty(extra))
            {
                x += c.S(5); var ef = c.FS(11); float ew = Ui.MeasureW(c.G, extra, ef);
                Ui.Text(c.G, extra, ef, extraColor ?? c.T.Accent, new RectangleF(x, b.Y + c.S(0.5f), ew + c.S(2), b.Height));
                x += ew;
            }
            if (sample) Badge(c, x + c.S(6), cy);
        }

        // ------------------------------------------------------------------ panel
        /// <summary>Header: brand mark, title, subtitle, and a status pill on the right. Returns next y.</summary>
        public static float Header(DrawCtx c, string brand, string title, string sub, OnlData d)
        {
            var b = c.Bounds; float x = b.X + c.S(16), y = b.Y + c.S(14), ls = c.S(28);
            Logo(c.G, brand, new RectangleF(x, y, ls, ls), c.T);
            float tx = x + ls + c.S(10);
            // status pill
            string st; Color sc;
            if (d.Sample) { st = "Sample"; sc = c.T.Accent; }
            else if (d.Str("loading") == "1") { st = "Loading…"; sc = c.T.TextDim; }
            else if (d.Error != null) { st = d.Stale ? "Offline · " + OnlUtil.Ago(d.At) : "Error"; sc = c.T.Bad; }
            else { st = "Live · " + OnlUtil.Ago(d.At); sc = c.T.Good; }
            var pf = c.FS(10.5f); float pw = Ui.MeasureW(c.G, st, pf) + c.S(20), ph = c.S(20);
            var pr = new RectangleF(b.Right - c.S(16) - pw, y + (ls - ph) / 2, pw, ph);
            Ui.FillRound(c.G, pr, ph / 2, Color.FromArgb(c.T.Dark ? 40 : 30, sc));
            Ui.FillCircle(c.G, new PointF(pr.X + c.S(8), pr.Y + ph / 2), c.S(2.5f), sc);
            Ui.Text(c.G, st, pf, c.T.Dark ? Ui.Mix(sc, Color.White, 0.2) : Ui.Mix(sc, Color.Black, 0.25), new RectangleF(pr.X + c.S(13), pr.Y, pw - c.S(15), ph));
            float tw = pr.X - tx - c.S(8);
            if (string.IsNullOrEmpty(sub)) Ui.Text(c.G, title, c.FS(14), c.T.Text, new RectangleF(tx, y, tw, ls));
            else
            {
                Ui.Text(c.G, title, c.FS(14), c.T.Text, new RectangleF(tx, y - c.S(1), tw, c.S(17)));
                Ui.Text(c.G, sub, c.F(11), c.T.TextDim, new RectangleF(tx, y + c.S(15), tw, c.S(15)));
            }
            return y + ls + c.S(14);
        }

        public static float Section(DrawCtx c, float y, string text, string right = null)
        {
            var b = c.Bounds;
            Ui.Text(c.G, text.ToUpperInvariant(), c.FS(10), c.T.TextFaint, new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(14)));
            if (right != null) Ui.Text(c.G, right, c.F(10.5f), c.T.TextFaint, new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(14)), 1);
            return y + c.S(20);
        }

        /// <summary>Row of stat cards. colors (optional) tints a small dot before each label.</summary>
        public static float Cards(DrawCtx c, float y, string[] values, string[] labels, Color[] dots = null, float h = 56)
        {
            var b = c.Bounds; int n = values.Length; float gap = c.S(8);
            float x = b.X + c.S(16), w = (b.Width - c.S(32) - gap * (n - 1)) / n, ch = c.S(h);
            for (int i = 0; i < n; i++)
            {
                var r = new RectangleF(x + i * (w + gap), y, w, ch);
                Ui.FillRound(c.G, r, c.S(10), CardBg(c.T));
                Ui.Text(c.G, values[i], c.FS(19), c.T.Text, new RectangleF(r.X + c.S(11), r.Y + c.S(8), r.Width - c.S(16), c.S(24)));
                float lx = r.X + c.S(11);
                if (dots != null && i < dots.Length && dots[i].A > 0) { Ui.FillCircle(c.G, new PointF(lx + c.S(3), r.Bottom - c.S(15)), c.S(3), dots[i]); lx += c.S(10); }
                Ui.Text(c.G, labels[i], c.F(10.5f), c.T.TextDim, new RectangleF(lx, r.Bottom - c.S(23), r.Right - lx - c.S(6), c.S(16)));
            }
            return y + ch + c.S(14);
        }
        public static Color CardBg(Theme t) { return t.IsGlass ? Color.FromArgb(t.Dark ? 26 : 110, 255, 255, 255) : (t.Dark ? Ui.Mix(t.PanelBg, Color.White, 0.06) : Color.FromArgb(255, 241, 241, 245)); }

        /// <summary>Clickable list row: dot, title, sub, right tag. Returns next y.</summary>
        public static float Row(DrawCtx c, float y, string title, string sub, string tag, Color dot, string url, Color? tagColor = null, float h = 40, Action<RectangleF> drawLead = null)
        {
            var b = c.Bounds;
            var r = new RectangleF(b.X + c.S(8), y, b.Width - c.S(16), c.S(h));
            bool hov = c.IsHover(r) && !string.IsNullOrEmpty(url);
            if (hov) Ui.FillRound(c.G, r, c.S(8), c.T.TileHover);
            float x = r.X + c.S(8);
            if (drawLead != null) { drawLead(new RectangleF(x, r.Y + (r.Height - c.S(16)) / 2, c.S(16), c.S(16))); x += c.S(24); }
            else if (dot.A > 0) { Ui.FillCircle(c.G, new PointF(x + c.S(4), r.Y + r.Height / 2), c.S(3.5f), dot); x += c.S(16); }
            float tagW = 0;
            if (!string.IsNullOrEmpty(tag))
            {
                var tf = c.F(10.5f); tagW = Ui.MeasureW(c.G, tag, tf) + c.S(4);
                Ui.Text(c.G, tag, tf, tagColor ?? c.T.TextFaint, new RectangleF(r.Right - c.S(8) - tagW, r.Y, tagW, r.Height), 1);
            }
            float tw = r.Right - c.S(8) - tagW - c.S(8) - x;
            if (string.IsNullOrEmpty(sub)) Ui.Text(c.G, title, c.F(12.5f), c.T.Text, new RectangleF(x, r.Y, tw, r.Height));
            else
            {
                Ui.Text(c.G, title, c.F(12.5f), c.T.Text, new RectangleF(x, r.Y + c.S(3), tw, r.Height / 2));
                Ui.Text(c.G, sub, c.F(10.5f), c.T.TextDim, new RectangleF(x, r.Y + r.Height / 2 - c.S(1), tw, r.Height / 2 - c.S(3)));
            }
            if (!string.IsNullOrEmpty(url)) { var u = url; c.Hit(r, () => OnlUtil.Open(u)); }
            return y + c.S(h);
        }

        public static float Empty(DrawCtx c, float y, string text, float h = 40)
        {
            var b = c.Bounds;
            Ui.Text(c.G, text, c.F(11.5f), c.T.TextFaint, new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(h)), 0);
            return y + c.S(h);
        }

        /// <summary>Error line (if any) — shown above the footer when connected but the fetch failed.</summary>
        public static float ErrorLine(DrawCtx c, float y, OnlData d)
        {
            if (d.Sample || d.Error == null) return y;
            var b = c.Bounds;
            var r = new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(26));
            Ui.FillRound(c.G, r, c.S(8), Color.FromArgb(c.T.Dark ? 34 : 24, c.T.Bad));
            Ui.Icon(c.G, Icons.Warning, new RectangleF(r.X + c.S(6), r.Y, c.S(16), r.Height), c.T.Bad, c.S(11));
            Ui.Text(c.G, d.Error, c.F(11), c.T.Text, new RectangleF(r.X + c.S(26), r.Y, r.Width - c.S(30), r.Height));
            return y + c.S(34);
        }

        /// <summary>Footer buttons at the bottom of the card: Connect (sample) or Refresh, plus an Open link.</summary>
        public static void Footer(DrawCtx c, OnlData d, string openLabel, string openUrl, Action refresh, IWidgetHost host)
        {
            var b = c.Bounds; float h = c.S(30), y = b.Bottom - c.S(14) - h, gap = c.S(8);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            if (d.Sample)
            {
                float w1 = (w - gap) * 0.55f;
                c.Button(new RectangleF(x, y, w1, h), "Connect in Settings", () => { if (host != null) host.OpenSettings("accounts"); }, true, Icons.Link);
                var u = openUrl;
                c.Button(new RectangleF(x + w1 + gap, y, w - w1 - gap, h), openLabel, () => OnlUtil.Open(u));
            }
            else
            {
                var u = openUrl;
                c.Button(new RectangleF(x, y, w - h - gap, h), openLabel, () => OnlUtil.Open(u), false, Icons.Globe);
                var rr = new RectangleF(x + w - h, y, h, h);
                Ui.FillRound(c.G, rr, h / 2, c.IsHover(rr) ? c.T.TileHover : c.T.Tile);
                Ui.Icon(c.G, Icons.Refresh, rr, c.T.Text, c.S(13));
                if (refresh != null) c.Hit(rr, refresh);
            }
        }
        public static float FooterH(float scale) { return 58 * scale; }

        // ------------------------------------------------------------------ heat-map grid
        public static Color Level(Theme t, int lv)
        {
            if (lv <= 0) return t.IsGlass ? Color.FromArgb(t.Dark ? 24 : 40, t.Dark ? 255 : 0, t.Dark ? 255 : 0, t.Dark ? 255 : 0) : t.Track;
            double[] k = { 0, 0.42, 0.64, 0.84, 1.0 };
            var baseC = t.IsGlass ? Color.FromArgb(t.Dark ? 34 : 50, t.Accent) : t.Track;
            return Ui.Mix(baseC, t.Accent, k[Math.Min(4, lv)]);
        }

        /// <summary>
        /// GitHub-style grid: days chronological, firstDow = weekday of days[0] (0 = Sunday). Shows the LAST `weeks` columns.
        /// Cells are square, sized to fit r (height decides the pitch, width truncates columns). Returns pitch used.
        /// hoverIndex (out) = index into levels under the mouse or -1.
        /// </summary>
        public static float Grid(DrawCtx c, RectangleF r, int[] levels, int firstDow, int weeks, out int hoverIndex, out RectangleF used)
        {
            hoverIndex = -1; used = RectangleF.Empty;
            int n = levels.Length; if (n == 0) return 0;
            int totalCols = (n + firstDow + 6) / 7;
            float pitch = Math.Min(r.Height / 7f, r.Width / Math.Max(1, weeks));
            int cols = Math.Min(totalCols, Math.Max(1, (int)Math.Floor(r.Width / pitch + 0.001f)));
            cols = Math.Min(cols, weeks);
            int startCol = totalCols - cols;
            if (pitch >= 3) pitch = (float)Math.Floor(pitch);
            float gap = Math.Max(1f, (float)Math.Round(pitch * 0.2f)), cell = Math.Max(1f, pitch - gap);
            float rad = pitch >= 8 ? Math.Min(cell * 0.25f, c.S(2.2f)) : pitch >= 6 ? 1f : 0f;
            float gx = (float)Math.Round(r.Right - cols * pitch), gy = (float)Math.Round(r.Y);
            used = new RectangleF(gx, gy, cols * pitch, 7 * pitch);
            for (int i = 0; i < n; i++)
            {
                int pos = i + firstDow, col = pos / 7 - startCol, row = pos % 7;
                if (col < 0) continue;
                var cr = new RectangleF(gx + col * pitch, gy + row * pitch, cell, cell);
                var col0 = Level(c.T, levels[i]);
                if (rad >= 0.8f) Ui.FillRound(c.G, cr, rad, col0); else c.G.FillRectangle(Ui.B(col0), cr);
                if (c.Mouse.X >= cr.X && c.Mouse.X < cr.X + pitch && c.Mouse.Y >= cr.Y && c.Mouse.Y < cr.Y + pitch)
                {
                    hoverIndex = i;
                    using (var p = new Pen(c.T.Text, Math.Max(1f, c.S(1)))) c.G.DrawRectangle(p, cr.X - 0.5f, cr.Y - 0.5f, cr.Width + 1, cr.Height + 1);
                }
            }
            return pitch;
        }

        /// <summary>Levels 0..4 from counts using GitHub-like quartiles of non-zero days.</summary>
        public static int[] LevelsFor(double[] counts)
        {
            var nz = counts.Where(x => x > 0).OrderBy(x => x).ToArray();
            var lv = new int[counts.Length];
            if (nz.Length == 0) return lv;
            double q1 = nz[(int)(nz.Length * 0.25)], q2 = nz[(int)(nz.Length * 0.5)], q3 = nz[Math.Min(nz.Length - 1, (int)(nz.Length * 0.75))];
            for (int i = 0; i < counts.Length; i++)
            {
                var v = counts[i];
                lv[i] = v <= 0 ? 0 : v <= q1 ? 1 : v <= q2 ? 2 : v <= q3 ? 3 : 4;
            }
            return lv;
        }

        /// <summary>Current streak (ending today or yesterday) and longest streak over a chronological day-count series ending today.</summary>
        public static void Streaks(double[] counts, out int current, out int longest)
        {
            current = 0; longest = 0; int run = 0;
            for (int i = 0; i < counts.Length; i++) { if (counts[i] > 0) { run++; if (run > longest) longest = run; } else run = 0; }
            int j = counts.Length - 1;
            if (j >= 0 && counts[j] <= 0) j--; // today without activity yet doesn't break the streak
            while (j >= 0 && counts[j] > 0) { current++; j--; }
        }

        /// <summary>Sample contribution-like series (days ending today).</summary>
        public static double[] SampleDays(int seed, int days, double activity, double weekendDip)
        {
            var rng = new OnlUtil.Rng(seed); var r = new double[days];
            double wave = rng.D() * 6;
            var start = DateTime.Today.AddDays(-(days - 1));
            for (int i = 0; i < days; i++)
            {
                var dow = start.AddDays(i).DayOfWeek;
                double p = activity * (0.75 + 0.35 * Math.Sin(i / 23.0 + wave));
                if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) p *= weekendDip;
                if (i > days - 16) p = Math.Max(p, 0.97); // healthy recent streak
                if (rng.D() < p) r[i] = 1 + Math.Floor(rng.D() * rng.D() * 14);
            }
            return r;
        }
    }
}
