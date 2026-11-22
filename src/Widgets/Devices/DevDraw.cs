// Device batteries — drawing helpers (vector device glyphs, level colours, battery shape, panel rows).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace NovaBar
{
    public static class DevDraw
    {
        /// <summary>Number colour, as in icon.rs: green > 50 %, orange 21-50 %, red <= 20 %, blue while charging.</summary>
        public static Color Level(Theme t, DevDevice d)
        {
            if (d.Charging) return Charge(t);
            if (d.Battery <= 20) return t.Bad;
            if (d.Battery <= 50) return t.Warn;
            return t.Good;
        }
        public static Color Charge(Theme t) { return t.Dark ? Color.FromArgb(255, 86, 182, 255) : Color.FromArgb(255, 0, 112, 218); }

        static Pen Pn(Color c, float w) { return Ui.P(c, w); }

        /// <summary>Small line-art device glyph centred in r (designed on a 16x16 grid).</summary>
        public static void Glyph(Graphics g, DevKind kind, RectangleF r, Color c, float stroke)
        {
            float s = Math.Min(r.Width, r.Height), u = s / 16f;
            float ox = r.X + (r.Width - s) / 2, oy = r.Y + (r.Height - s) / 2;
            Func<float, float> X = v => ox + v * u; Func<float, float> Y = v => oy + v * u;
            using (var p = Pn(c, stroke))
            {
                switch (kind)
                {
                    case DevKind.Keyboard:
                        {
                            var body = new RectangleF(X(0.8f), Y(3.6f), 14.4f * u, 9f * u);
                            using (var path = Ui.Round(body, 2.2f * u)) g.DrawPath(p, path);
                            float d = Math.Max(1.1f, 1.45f * u);
                            for (int row = 0; row < 2; row++)
                                for (int i = 0; i < 4; i++)
                                    g.FillRectangle(Ui.B(c), X(3.2f + i * 3.2f) - d / 2, Y(6.1f + row * 2.3f) - d / 2, d, d);
                            g.DrawLine(p, X(5.4f), Y(10.6f), X(10.6f), Y(10.6f));
                            break;
                        }
                    case DevKind.Mouse:
                        {
                            var body = new RectangleF(X(3.9f), Y(1.2f), 8.2f * u, 13.6f * u);
                            using (var path = Ui.Round(body, 4.1f * u)) g.DrawPath(p, path);
                            g.DrawLine(p, X(8), Y(3.7f), X(8), Y(6.3f));
                            break;
                        }
                    case DevKind.Headset:
                        {
                            g.DrawArc(p, X(2.2f), Y(1.6f), 11.6f * u, 11.6f * u, 180, 180);
                            g.DrawLine(p, X(2.2f), Y(7.4f), X(2.2f), Y(9.4f));
                            g.DrawLine(p, X(13.8f), Y(7.4f), X(13.8f), Y(9.4f));
                            Ui.FillRound(g, new RectangleF(X(1.2f), Y(8.4f), 3.6f * u, 6.2f * u), 1.4f * u, c);
                            Ui.FillRound(g, new RectangleF(X(11.2f), Y(8.4f), 3.6f * u, 6.2f * u), 1.4f * u, c);
                            break;
                        }
                    case DevKind.Gamepad:
                        {
                            using (var path = new GraphicsPath())
                            {
                                path.AddArc(X(0.8f), Y(4.0f), 6.4f * u, 8.8f * u, 110, 180);
                                path.AddArc(X(8.8f), Y(4.0f), 6.4f * u, 8.8f * u, 250, 180);
                                path.CloseFigure();
                                g.DrawPath(p, path);
                            }
                            using (var p2 = Pn(c, Math.Max(1f, stroke * 0.9f)))
                            {
                                g.DrawLine(p2, X(3.4f), Y(8.4f), X(6.4f), Y(8.4f));
                                g.DrawLine(p2, X(4.9f), Y(6.9f), X(4.9f), Y(9.9f));
                            }
                            float d = Math.Max(1.4f, 1.8f * u);
                            g.FillEllipse(Ui.B(c), X(11.4f) - d / 2, Y(7.3f) - d / 2, d, d);
                            g.FillEllipse(Ui.B(c), X(10.2f) - d / 2, Y(9.4f) - d / 2, d, d);
                            break;
                        }
                    default:
                        {
                            // Bluetooth rune
                            g.DrawLines(p, new[] { new PointF(X(4.6f), Y(5.0f)), new PointF(X(11.2f), Y(10.8f)), new PointF(X(8), Y(13.8f)), new PointF(X(8), Y(2.2f)), new PointF(X(11.2f), Y(5.2f)), new PointF(X(4.6f), Y(11.0f)) });
                            break;
                        }
                }
            }
        }

        /// <summary>Check mark (full battery) inside r.</summary>
        public static void Check(Graphics g, RectangleF r, Color c, float stroke)
        {
            using (var p = Pn(c, stroke))
                g.DrawLines(p, new[] { new PointF(r.X + r.Width * 0.08f, r.Y + r.Height * 0.55f), new PointF(r.X + r.Width * 0.38f, r.Y + r.Height * 0.84f), new PointF(r.X + r.Width * 0.92f, r.Y + r.Height * 0.18f) });
        }

        /// <summary>Small lightning bolt (charging).</summary>
        public static void Bolt(Graphics g, RectangleF r, Color c)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            g.FillPolygon(Ui.B(c), new[] { new PointF(x + w * 0.62f, y), new PointF(x + w * 0.08f, y + h * 0.58f), new PointF(x + w * 0.46f, y + h * 0.58f),
                new PointF(x + w * 0.36f, y + h), new PointF(x + w * 0.92f, y + h * 0.40f), new PointF(x + w * 0.54f, y + h * 0.40f) });
        }

        /// <summary>Horizontal battery outline with fill to the level; bolt overlay while charging.</summary>
        public static void Battery(DrawCtx c, RectangleF r, DevDevice d)
        {
            var t = c.T; var g = c.G;
            float nub = Math.Max(1.5f, r.Width * 0.08f);
            var body = new RectangleF(r.X, r.Y, r.Width - nub - c.S(1), r.Height);
            var outline = d == null ? t.TextFaint : d.Asleep ? Ui.WithA(t.TextDim, 150) : Ui.WithA(t.Text, 170);
            Ui.StrokeRound(g, body, c.S(3), outline, Math.Max(1f, c.S(1.2f)));
            Ui.FillRound(g, new RectangleF(body.Right + c.S(1), r.Y + r.Height * 0.32f, nub, r.Height * 0.36f), nub / 2, outline);
            if (d == null) return;
            var inner = Ui.Inset(body, c.S(2.2f), c.S(2.2f));
            float fw = Math.Max(c.S(1.5f), inner.Width * Math.Max(0, Math.Min(100, d.Battery)) / 100f);
            var col = d.Asleep ? Ui.WithA(Level(t, d), 130) : Level(t, d);
            Ui.FillRound(g, new RectangleF(inner.X, inner.Y, fw, inner.Height), c.S(1.5f), col);
            if (d.Charging)
            {
                float bh = r.Height * 0.9f, bw = bh * 0.62f;
                var br = new RectangleF(body.X + (body.Width - bw) / 2, r.Y + (r.Height - bh) / 2, bw, bh);
                using (var path = new GraphicsPath())
                {
                    // outline the bolt in the tile colour so it reads over the fill
                    float x = br.X, y = br.Y, w = br.Width, h = br.Height;
                    path.AddPolygon(new[] { new PointF(x + w * 0.62f, y), new PointF(x + w * 0.08f, y + h * 0.58f), new PointF(x + w * 0.46f, y + h * 0.58f),
                        new PointF(x + w * 0.36f, y + h), new PointF(x + w * 0.92f, y + h * 0.40f), new PointF(x + w * 0.54f, y + h * 0.40f) });
                    using (var pen = new Pen(t.Dark ? Color.FromArgb(220, 20, 20, 26) : Color.FromArgb(230, 255, 255, 255), Math.Max(1f, c.S(1.6f)))) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, path); }
                    g.FillPath(Ui.B(t.Dark ? Color.White : Color.FromArgb(255, 20, 20, 26)), path);
                }
            }
        }

        public static string Ago(DateTime at)
        {
            var span = DateTime.Now - at;
            int mins = (int)span.TotalMinutes;
            return mins <= 0 ? "just now" : mins + " min ago";
        }

        public static string StateText(DevDevice d)
        {
            if (d.Asleep) return "Asleep · last seen " + Ago(d.LastSeen);
            if (d.Charging) return "Charging";
            if (d.Battery >= 100) return "Fully charged";
            if (d.Battery <= 20) return "Low battery";
            return "Updated " + Ui.Clock(d.LastSeen);
        }

        /// <summary>One device row in a panel: glyph tile, name + type/connection, % + bar, state line.</summary>
        public static void Row(DrawCtx c, RectangleF r, DevDevice d)
        {
            var t = c.T; var g = c.G;
            var lc = Level(t, d);
            float tile = c.S(38);
            var tr = new RectangleF(r.X, r.Y + c.S(2), tile, tile);
            Ui.FillRound(g, tr, c.S(11), d.Asleep ? Ui.WithA(t.Track, t.Track.A / 2 + 10) : Ui.WithA(lc, t.Dark ? 34 : 30));
            Glyph(g, d.Kind, Ui.Inset(tr, c.S(9), c.S(9)), d.Asleep ? t.TextDim : lc, Math.Max(1.2f, c.S(1.5f)));

            float x = tr.Right + c.S(12), w = r.Right - x;
            string pct = d.Battery + "%";
            var pf = c.F(15, true);
            float pw = Ui.MeasureW(g, pct, pf) + c.S(2);
            Ui.Text(g, d.Name, c.FS(13), d.Asleep ? t.TextDim : t.Text, new RectangleF(x, r.Y, w - pw - c.S(8), c.S(19)));
            Ui.Text(g, pct, pf, d.Asleep ? t.TextDim : lc, new RectangleF(r.Right - pw - c.S(1), r.Y, pw + c.S(1), c.S(19)), 1);
            Ui.Text(g, DevKinds.Label(d.Kind) + "  ·  " + DevKinds.ConnLabel(d.Conn), c.F(11.5f), t.TextDim, new RectangleF(x, r.Y + c.S(19), w, c.S(16)));
            var bar = new RectangleF(x, r.Y + c.S(39), w, c.S(5));
            Ui.Progress(g, bar, d.Battery / 100.0, d.Asleep ? Ui.WithA(lc, 110) : lc, t.Track);

            float sy = r.Y + c.S(47);
            var sc = d.Asleep ? t.TextFaint : d.Charging ? Charge(t) : d.Battery <= 20 ? t.Bad : t.TextFaint;
            float sx = x;
            if (d.Charging && !d.Asleep) { Bolt(g, new RectangleF(sx, sy + c.S(2.5f), c.S(7), c.S(11)), sc); sx += c.S(11); }
            else if (d.Battery >= 100 && !d.Asleep) { Check(g, new RectangleF(sx, sy + c.S(3.5f), c.S(10), c.S(9)), t.Good, Math.Max(1.2f, c.S(1.6f))); sx += c.S(14); sc = t.Good; }
            else if (d.Asleep) { Ui.Icon(g, Icons.Moon, new RectangleF(sx - c.S(1), sy, c.S(12), c.S(16)), sc, c.S(10)); sx += c.S(14); }
            Ui.Text(g, StateText(d), c.F(11), sc, new RectangleF(sx, sy, r.Right - sx, c.S(16)));
        }
    }
}
