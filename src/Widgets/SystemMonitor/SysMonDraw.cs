// Shared drawing helpers + base class for System Monitor widgets.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace NovaBar
{
    /// <summary>Base: subscribes to Hub keys on start, unsubscribes on stop.</summary>
    public abstract class SysMonWidget : Widget
    {
        protected virtual string[] Keys { get { return new string[0]; } }
        public override void OnStart() { SysMon.Ensure(); foreach (var k in Keys) Hub.Subscribe(k, this); }
        public override void OnStop() { Hub.UnsubscribeAll(this); }
        protected float Pad(DrawCtx c) { return c.S(10); }
    }

    public static class SysMonDraw
    {
        /// <summary>Small dim label over a bold value, vertically centred in r.</summary>
        public static void LabelValue(DrawCtx c, RectangleF r, string label, string value, Color valueColor, int align = -1)
        {
            float mid = r.Y + r.Height / 2;
            Ui.Text(c.G, label, c.F(9.5f), c.T.TextDim, new RectangleF(r.X, mid - c.S(13), r.Width, c.S(12)), align);
            Ui.Text(c.G, value, c.FS(13), valueColor, new RectangleF(r.X, mid - c.S(1.5f), r.Width, c.S(15)), align);
        }
        public static int LabelValueW(DrawCtx c, string label, string value)
        {
            return Math.Max(Ui.MeasureW(c.G, label, c.F(9.5f)), Ui.MeasureW(c.G, value, c.FS(13)));
        }

        /// <summary>Ring + label/value tile used by CPU and RAM.</summary>
        public static void RingTile(DrawCtx c, double frac, bool valid, string label, string value, Color col)
        {
            var b = c.Bounds; float pad = c.S(10), d = c.S(22);
            var rr = new RectangleF(b.X + pad, b.Y + (b.Height - d) / 2f, d, d);
            Ui.Ring(c.G, rr, valid ? frac : 0, col, c.T.Track, c.S(3.4f));
            LabelValue(c, new RectangleF(rr.Right + c.S(8), b.Y, b.Right - rr.Right - c.S(8) - pad + c.S(2), b.Height), label, valid ? value : "—", c.T.Text);
        }
        public static int RingTileW(DrawCtx c, string label)
        {
            return (int)(c.S(10) + c.S(22) + c.S(8) + LabelValueW(c, label, "100%") + c.S(10));
        }

        /// <summary>Card-like inset area inside panels.</summary>
        public static void Card(DrawCtx c, RectangleF r)
        {
            Ui.FillRound(c.G, r, c.S(10), c.T.Tile);
        }

        /// <summary>Chart with faint grid. vals 0..max.</summary>
        public static void Chart(DrawCtx c, RectangleF r, double[] vals, double max, Color col, bool fill = true, float width = 1.6f, bool grid = true)
        {
            if (grid)
                using (var p = new Pen(Ui.WithA(c.T.TextFaint, 45), Math.Max(1f, c.S(0.8f))) { DashStyle = DashStyle.Dot })
                    for (int i = 1; i <= 3; i++) { float y = r.Y + r.Height * i / 4f; c.G.DrawLine(p, r.X, y, r.Right, y); }
            Ui.Sparkline(c.G, r, vals, col, fill, c.S(width), 0, Math.Max(max, 1e-9));
        }
        static readonly double[] none = new double[0];
        /// <summary>Chart only the newest `count` samples, right-aligned (history still filling up).</summary>
        public static void ChartN(DrawCtx c, RectangleF r, double[] vals, int count, double max, Color col, bool fill = true, float width = 1.6f, bool grid = true)
        {
            if (grid) Chart(c, r, none, max, col, false, width, true);
            count = Math.Min(count, vals.Length);
            if (count < 2) return;
            float w = r.Width * (count - 1) / (vals.Length - 1);
            Ui.Sparkline(c.G, new RectangleF(r.Right - w, r.Y, w, r.Height), new ArraySegment<double>(vals, vals.Length - count, count), col, fill, c.S(width), 0, Math.Max(max, 1e-9));
        }

        /// <summary>Label left, value right row (panel stats).</summary>
        public static void Row(DrawCtx c, float x, float y, float w, string label, string value, Color? vcol = null)
        {
            Ui.Text(c.G, label, c.F(11.5f), c.T.TextDim, new RectangleF(x, y, w * 0.45f, c.S(18)));
            Ui.Text(c.G, value, c.FS(12), vcol ?? c.T.Text, new RectangleF(x + w * 0.3f, y, w * 0.7f, c.S(18)), 1);
        }
        public static void Stat(DrawCtx c, RectangleF r, string label, string value, Color? vcol = null, int align = -1)
        {
            Ui.Text(c.G, label, c.F(10.5f), c.T.TextDim, new RectangleF(r.X, r.Y, r.Width, c.S(14)), align);
            Ui.Text(c.G, value, c.FS(13.5f), vcol ?? c.T.Text, new RectangleF(r.X, r.Y + c.S(15), r.Width, c.S(18)), align);
        }

        public static void Divider(DrawCtx c, float x, float y, float w)
        {
            c.G.FillRectangle(Ui.B(Ui.WithA(c.T.TextFaint, 40)), x, y, w, Math.Max(1f, c.S(0.8f)));
        }

        // -------------------- battery visuals (size-relative, used by tiles and panels) --------------------
        public static Color BatColor(Theme t, SysMonPowerSnap p, bool neutral)
        {
            if (p == null || !p.HasBattery) return t.TextFaint;
            if (p.Charging) return t.Good;
            if (p.Pct <= 15) return t.Bad;
            if (p.Pct <= 30) return t.Warn;
            return neutral ? t.Text : t.Accent;
        }

        /// <summary>Classic battery body with fill; r is the body+cap area.</summary>
        public static void BatteryShape(DrawCtx c, RectangleF r, SysMonPowerSnap p)
        {
            var g = c.G; var t = c.T;
            float capW = Math.Max(1.5f, r.Width * 0.08f), stroke = Math.Max(1.2f, r.Height * 0.11f);
            var body = new RectangleF(r.X, r.Y, r.Width - capW - stroke * 0.6f, r.Height);
            float rad = r.Height * 0.3f;
            var outline = p != null && p.HasBattery ? Ui.WithA(t.Text, 150) : Ui.WithA(t.TextDim, 150);
            using (var path = Ui.Round(Ui.Inset(body, stroke / 2, stroke / 2), rad)) using (var pen = new Pen(outline, stroke)) g.DrawPath(pen, path);
            var cap = new RectangleF(body.Right + stroke * 0.6f, r.Y + r.Height * 0.32f, capW, r.Height * 0.36f);
            Ui.FillRound(g, cap, capW / 2, outline);
            var inner = Ui.Inset(body, stroke * 1.9f, stroke * 1.9f);
            if (p != null && p.HasBattery)
            {
                float w = Math.Max(inner.Height * 0.5f, inner.Width * p.Pct / 100f);
                Ui.FillRound(g, new RectangleF(inner.X, inner.Y, w, inner.Height), Math.Max(1, rad - stroke * 1.4f), BatColor(t, p, true));
                if (p.Charging) Bolt(g, Ui.Inset(body, 0, -r.Height * 0.05f), t.Dark ? Color.FromArgb(255, 20, 20, 24) : Color.White, outline);
            }
            else Plug(g, body, t.TextDim, r.Height * 0.62f);
        }

        /// <summary>Mains plug drawn as shapes (prongs, head, cord), centred in r, size = height in px.</summary>
        public static void Plug(Graphics g, RectangleF r, Color col, float size)
        {
            float h = size, w = h * 0.62f, x = r.X + (r.Width - w) / 2, y = r.Y + (r.Height - h) / 2;
            float pw = Math.Max(1.2f, w * 0.13f), ph = h * 0.24f;
            Ui.FillRound(g, new RectangleF(x + w * 0.24f, y, pw, ph + 1), pw / 2, col);
            Ui.FillRound(g, new RectangleF(x + w * 0.76f - pw, y, pw, ph + 1), pw / 2, col);
            var head = new RectangleF(x, y + ph, w, h * 0.38f);
            using (var path = new GraphicsPath())
            {
                float rr = w * 0.22f;
                path.AddLine(head.X, head.Y, head.Right, head.Y);
                path.AddArc(head.Right - 2 * rr, head.Bottom - 2 * rr, 2 * rr, 2 * rr, 0, 90);
                path.AddArc(head.X, head.Bottom - 2 * rr, 2 * rr, 2 * rr, 90, 90);
                path.CloseFigure(); g.FillPath(Ui.B(col), path);
            }
            using (var pen = Ui.P(col, Math.Max(1.2f, w * 0.15f))) g.DrawLine(pen, x + w / 2, head.Bottom, x + w / 2, y + h);
        }

        /// <summary>Lightning bolt polygon centred in r with outline for contrast.</summary>
        public static void Bolt(Graphics g, RectangleF r, Color fill, Color edge)
        {
            float h = r.Height * 0.86f, w = h * 0.56f, x = r.X + (r.Width - w) / 2, y = r.Y + (r.Height - h) / 2;
            var pts = new[] {
                new PointF(x + w * 0.62f, y), new PointF(x + w * 0.08f, y + h * 0.56f), new PointF(x + w * 0.46f, y + h * 0.56f),
                new PointF(x + w * 0.36f, y + h), new PointF(x + w * 0.92f, y + h * 0.42f), new PointF(x + w * 0.54f, y + h * 0.42f) };
            using (var path = new GraphicsPath()) { path.AddPolygon(pts); using (var pen = new Pen(edge, Math.Max(1f, h * 0.09f)) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path); g.FillPath(Ui.B(fill), path); }
        }

        /// <summary>Ring round the number. sparkAngle (deg) &gt;= 0 draws a spark dot.</summary>
        public static void BatteryRing(DrawCtx c, RectangleF r, SysMonPowerSnap p, double sparkAngle, float thick, Font numFont)
        {
            var g = c.G; var t = c.T; var sq = Ui.Square(r);
            bool has = p != null && p.HasBattery;
            var col = BatColor(t, p, false);
            Ui.Ring(g, sq, has ? p.Pct / 100.0 : 0, col, t.Track, thick);
            if (!has) { Plug(g, sq, t.TextDim, sq.Height * 0.4f); return; }
            Ui.Text(g, p.Pct.ToString(), numFont, t.Text, new RectangleF(sq.X - c.S(4), sq.Y, sq.Width + c.S(8), sq.Height + c.S(0.5f)), 0);
            if (sparkAngle >= 0)
            {
                float rad = (sq.Width - thick) / 2, cx = sq.X + sq.Width / 2, cy = sq.Y + sq.Height / 2;
                double a = (sparkAngle - 90) * Math.PI / 180;
                var pt = new PointF(cx + (float)(Math.Cos(a) * rad), cy + (float)(Math.Sin(a) * rad));
                // short comet tail
                for (int i = 4; i >= 1; i--)
                {
                    double at = (sparkAngle - i * 9 - 90) * Math.PI / 180;
                    Ui.FillCircle(g, new PointF(cx + (float)(Math.Cos(at) * rad), cy + (float)(Math.Sin(at) * rad)), thick * (0.55f - i * 0.07f), Ui.WithA(Color.White, 150 - i * 30));
                }
                Ui.FillCircle(g, pt, thick * 1.25f, Ui.WithA(col, 90));
                Ui.FillCircle(g, pt, thick * 0.72f, Color.White);
            }
        }

        /// <summary>Energy core: 12 arc cells around a glowing centre.</summary>
        public static void BatteryCore(DrawCtx c, RectangleF r, SysMonPowerSnap p)
        {
            var g = c.G; var t = c.T; var sq = Ui.Square(r);
            bool has = p != null && p.HasBattery;
            int lit = has ? (int)Math.Ceiling(p.Pct / 100.0 * 12 - 0.01) : 0;
            var col = BatColor(t, p, false);
            float thick = sq.Width * 0.15f, cx = sq.X + sq.Width / 2, cy = sq.Y + sq.Height / 2;
            var ar = Ui.Inset(sq, thick / 2, thick / 2);
            for (int i = 0; i < 12; i++)
            {
                var cc = i < lit ? col : t.Track;
                using (var pen = new Pen(cc, thick)) g.DrawArc(pen, ar, -90 + i * 30 + 3.5f, 23f);
            }
            float coreR = sq.Width * 0.22f;
            if (has)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(cx - coreR * 1.6f, cy - coreR * 1.6f, coreR * 3.2f, coreR * 3.2f);
                    using (var br = new PathGradientBrush(path) { CenterColor = Ui.WithA(col, 120), SurroundColors = new[] { Ui.WithA(col, 0) } }) g.FillPath(br, path);
                }
                Ui.FillCircle(g, new PointF(cx, cy), coreR, col);
                Ui.FillCircle(g, new PointF(cx - coreR * 0.25f, cy - coreR * 0.25f), coreR * 0.38f, Ui.WithA(Color.White, 110));
                if (p.Charging) Bolt(g, new RectangleF(cx - coreR, cy - coreR, coreR * 2, coreR * 2), Color.White, Ui.WithA(col, 200));
            }
            else Plug(g, new RectangleF(cx - coreR * 2, cy - coreR * 2, coreR * 4, coreR * 4), t.TextDim, coreR * 1.7f);
        }

        // 3x5 dot-matrix glyphs: rows top->bottom, 3 bits each (MSB = left)
        static readonly Dictionary<char, int[]> glyphs = new Dictionary<char, int[]>
        {
            {'0', new[]{7,5,5,5,7}}, {'1', new[]{2,6,2,2,7}}, {'2', new[]{7,1,7,4,7}}, {'3', new[]{7,1,7,1,7}}, {'4', new[]{5,5,7,1,1}},
            {'5', new[]{7,4,7,1,7}}, {'6', new[]{7,4,7,5,7}}, {'7', new[]{7,1,2,2,2}}, {'8', new[]{7,5,7,5,7}}, {'9', new[]{7,5,7,1,7}},
            {'A', new[]{2,5,7,5,5}}, {'C', new[]{7,4,4,4,7}}, {'%', new[]{5,1,2,4,5}}, {'-', new[]{0,0,7,0,0}},
        };
        /// <summary>Draws text as 3x5 dot glyphs; returns width used. pitch = dot spacing.</summary>
        public static float DotText(Graphics g, string s, float x, float y, float pitch, Color on, Color off)
        {
            float dot = pitch * 0.74f, cx = x;
            foreach (var ch in s)
            {
                int[] rows; if (!glyphs.TryGetValue(ch, out rows)) { cx += pitch * 2; continue; }
                for (int ry = 0; ry < 5; ry++)
                    for (int rx = 0; rx < 3; rx++)
                    {
                        bool lit = (rows[ry] & (4 >> rx)) != 0;
                        var col = lit ? on : off; if (col.A == 0) continue;
                        g.FillEllipse(Ui.B(col), cx + rx * pitch + (pitch - dot) / 2, y + ry * pitch + (pitch - dot) / 2, dot, dot);
                    }
                cx += pitch * 4;
            }
            return cx - x - pitch;
        }
        public static float DotTextW(string s, float pitch) { return s.Length * pitch * 4 - pitch; }

        /// <summary>Dot battery: cols x 3 dots + cap, lit column-wise to the charge.</summary>
        public static void DotBattery(Graphics g, float x, float y, float pitch, int cols, SysMonPowerSnap p, Color on, Color off)
        {
            bool has = p != null && p.HasBattery;
            int lit = has ? (int)Math.Round(p.Pct / 100.0 * cols) : 0;
            if (has && p.Pct > 0 && lit == 0) lit = 1;
            float dot = pitch * 0.74f;
            for (int cx = 0; cx < cols; cx++)
                for (int ry = 0; ry < 3; ry++)
                    g.FillEllipse(Ui.B(cx < lit ? on : off), x + cx * pitch + (pitch - dot) / 2, y + ry * pitch + (pitch - dot) / 2, dot, dot);
            g.FillEllipse(Ui.B(lit >= cols ? on : off), x + cols * pitch + (pitch - dot) / 2, y + pitch + (pitch - dot) / 2, dot, dot);
        }

        /// <summary>Fuel gauge: half-dial E..F with a needle. r = full gauge rect (width ≈ 2*height).</summary>
        public static void FuelGauge(DrawCtx c, RectangleF r, SysMonPowerSnap p, bool big)
        {
            var g = c.G; var t = c.T;
            bool has = p != null && p.HasBattery;
            float R = Math.Min(r.Width / 2, r.Height * 0.92f), cx = r.X + r.Width / 2, cy = r.Y + R + (r.Height - R) * 0.5f;
            float thick = R * (big ? 0.11f : 0.16f);
            var arc = new RectangleF(cx - R + thick / 2, cy - R + thick / 2, 2 * R - thick, 2 * R - thick);
            // dial: red reserve zone then track
            using (var pen = Ui.P(t.Track, thick)) g.DrawArc(pen, arc, 180, 180);
            using (var pen = new Pen(Ui.WithA(t.Bad, has ? 220 : 90), thick)) g.DrawArc(pen, arc, 180, 27);
            if (has) using (var pen = Ui.P(BatColor(t, p, false), thick)) g.DrawArc(pen, arc, 180, (float)(180 * p.Pct / 100.0));
            // ticks
            int ticks = big ? 9 : 5;
            using (var tp = new Pen(Ui.WithA(t.TextDim, 170), Math.Max(1f, R * 0.035f)))
                for (int i = 0; i < ticks; i++)
                {
                    double a = Math.PI + Math.PI * i / (ticks - 1);
                    float r1 = R - thick * 1.5f, r2 = r1 - R * (i % 2 == 0 ? 0.14f : 0.08f);
                    g.DrawLine(tp, cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1, cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2);
                }
            // E / F
            var lf = Ui.Font(Math.Max(6.5f, R * (big ? 0.17f : 0.30f)), true);
            float lw = R * 0.5f;
            Ui.Text(g, "E", lf, t.Bad, new RectangleF(cx - R + thick * 0.2f - lw / 2 + R * 0.06f, cy - R * 0.02f, lw, R * 0.42f), 0);
            Ui.Text(g, "F", lf, t.TextDim, new RectangleF(cx + R - thick * 0.2f - lw / 2 - R * 0.06f, cy - R * 0.02f, lw, R * 0.42f), 0);
            if (has)
            {
                double a = Math.PI + Math.PI * p.Pct / 100.0;
                float nl = R * 0.78f;
                var tip = new PointF(cx + (float)Math.Cos(a) * nl, cy + (float)Math.Sin(a) * nl);
                using (var pen = Ui.P(t.Text, Math.Max(1.4f, R * 0.07f))) g.DrawLine(pen, cx, cy, tip.X, tip.Y);
                Ui.FillCircle(g, new PointF(cx, cy), Math.Max(2f, R * 0.13f), t.Text);
                Ui.FillCircle(g, new PointF(cx, cy), Math.Max(0.8f, R * 0.05f), t.Accent);
                if (p.Charging) Bolt(g, new RectangleF(cx - R * 0.22f, cy - R * 0.62f, R * 0.44f, R * 0.4f), t.Good, Ui.WithA(t.Good, 90));
            }
            else
                Plug(g, new RectangleF(cx - R * 0.5f, cy - R * 0.66f, R, R * 0.56f), t.TextDim, R * 0.42f);
        }

        /// <summary>Semi-circular temperature gauge.</summary>
        public static void TempGauge(DrawCtx c, RectangleF r, double celsius, float thick)
        {
            var g = c.G; var t = c.T;
            float R = Math.Min(r.Width / 2, r.Height), cx = r.X + r.Width / 2, cy = r.Y + r.Height;
            var arc = new RectangleF(cx - R + thick / 2, cy - R + thick / 2, 2 * R - thick, 2 * R - thick);
            using (var pen = Ui.P(t.Track, thick)) g.DrawArc(pen, arc, 180, 180);
            if (double.IsNaN(celsius)) return;
            double frac = Math.Max(0.02, Math.Min(1, (celsius - 20) / 80.0));
            using (var pen = Ui.P(TempColor(t, celsius), thick)) g.DrawArc(pen, arc, 180, (float)(180 * frac));
        }
        public static Color TempColor(Theme t, double c) { return double.IsNaN(c) ? t.TextFaint : c >= 85 ? t.Bad : c >= 70 ? t.Warn : c >= 55 ? Ui.Mix(t.Accent, t.Warn, (c - 55) / 15) : t.Accent; }
    }
}
