// "»" overflow tile: shown at the end of a pill bar when its widgets don't fit the screen.
// Its panel draws the hidden widgets as normal, fully interactive tiles.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>Not registered in the widget gallery (no [Widget] attribute); created by the bar itself.</summary>
    public class BarOverflowWidget : Widget
    {
        internal readonly List<BarTile> Hidden = new List<BarTile>();
        internal BarMetrics M;
        public BarOverflowWidget() { Meta = new WidgetAttribute("overflow", "More widgets", "Hidden", "Widgets that don't fit on this screen"); Meta.RefName = "-"; }

        public override int Measure(DrawCtx c) { return c.Si(34); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds;
            Ui.Icon(c.G, "", new RectangleF(b.X, b.Y, b.Width, b.Height), c.T.Text, c.S(12));   // chevron
            Ui.Icon(c.G, "", new RectangleF(b.X + c.S(5), b.Y, b.Width, b.Height), c.T.TextDim, c.S(12));
            if (Hidden.Count > 0)
            {
                string n = Hidden.Count.ToString(); var f = c.F(8.5f, true);
                float bw = Math.Max(c.S(13), Ui.Measure(c.G, n, f).Width + c.S(6)), bh = c.S(13);
                var br = new RectangleF(b.Right - bw - c.S(1), b.Y + c.S(1), bw, bh);
                Ui.FillRound(c.G, br, bh / 2, c.T.Accent);
                Ui.Text(c.G, n, f, Color.White, br, 0);
            }
        }

        // ---- panel: hidden widgets as tiles, wrapped in rows ----
        const float MaxW = 560, PadX = 14, Head = 44;
        List<Rectangle> LayoutRows(float s, out Size size)
        {
            var rects = new List<Rectangle>();
            int gap = Math.Max(4, (int)(8 * s)), x0 = (int)(PadX * s), y0 = (int)(Head * s);
            int maxW = (int)(MaxW * s), th = M != null ? M.TileH : (int)(34 * s);
            int x = x0, y = y0, right = 0;
            foreach (var t in Hidden)
            {
                int w = Math.Max(th, t.Width);
                if (x > x0 && x + w > maxW - x0) { x = x0; y += th + gap; }
                rects.Add(new Rectangle(x, y, w, th)); x += w + gap; right = Math.Max(right, x - gap);
            }
            size = new Size(Math.Max((int)(240 * s), right + x0), y + th + (int)(PadX * s));
            return rects;
        }
        public override Size PanelSize(float scale)
        {
            if (Hidden.Count == 0) return new Size((int)(240 * scale), (int)(90 * scale));
            Size sz; LayoutRows(scale, out sz); return sz;
        }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds;
            Ui.Text(c.G, "More widgets", c.FS(13.5f), c.T.Text, new RectangleF(b.X + c.S(PadX), b.Y + c.S(12), b.Width, c.S(20)));
            Ui.Text(c.G, "don't fit on this screen", c.F(11), c.T.TextFaint, new RectangleF(b.X + c.S(PadX), b.Y + c.S(12), b.Width - c.S(2 * PadX), c.S(20)), 1);
            if (Hidden.Count == 0) { Ui.Text(c.G, "Everything fits again", c.F(12), c.T.TextDim, new RectangleF(b.X, b.Y + c.S(40), b.Width, c.S(30)), 0); return; }
            Size sz; var rects = LayoutRows(c.Scale, out sz);
            var saveB = c.Bounds; bool saveP = c.IsPanel, saveH = c.Hover;
            for (int i = 0; i < Hidden.Count && i < rects.Count; i++)
            {
                var t = Hidden[i]; if (t.W == null) continue;
                var r = rects[i]; r.Offset(b.X, b.Y);
                bool hov = r.Contains(c.Mouse);
                Ui.FillRound(c.G, r, M != null ? M.TileRadius : c.S(8), hov ? c.T.TileHover : c.T.Tile);
                Ui.StrokeRound(c.G, r, M != null ? M.TileRadius : c.S(8), c.T.TileBorder, 1f);
                var w = t.W;
                c.Hit(r, () => { try { w.OnClick(MouseButtons.Left); } catch { } });   // tile fallback; the widget's own hits (added below) win
                c.Bounds = r; c.IsPanel = false; c.Hover = hov;
                var st = c.G.Save();
                try { c.G.SetClip(r); w.Draw(c); } catch { }
                finally { c.G.Restore(st); c.Bounds = saveB; c.IsPanel = saveP; c.Hover = saveH; }
            }
        }
    }
}
