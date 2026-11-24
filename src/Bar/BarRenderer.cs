// Shared painting code for the real bar (BarForm/PanelForm) and the render harness (RenderTest).
// Anything that changes how tiles/cards look must be done here so screenshots match the live bar.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>One widget slot on the bar.</summary>
    public class BarTile
    {
        public Widget W;
        public WidgetInstance Inst;
        public string Zone = "right";
        public Rectangle R;                 // tile rect in bar client coords
        public int Width;                   // last measured width
        public int VH;                      // vertical bars: height the widget asked for (0 = normal row)
        public float HoverT;                // animated hover amount 0..1 (live bar only)
        public readonly DrawCtx Ctx = new DrawCtx();
        public string Error;                // non-null = draw an error box instead of the widget
        public string Id { get { return W != null ? W.Meta.Id : (Inst != null ? Inst.Id : "?"); } }
    }

    /// <summary>Pixel metrics of the bar for a size (S/M/L) and DPI scale.</summary>
    public class BarMetrics
    {
        public float Scale;                 // dpi * size factor (what DrawCtx.Scale gets)
        public float Dpi = 1f;              // dpi scale alone
        public bool Vertical;               // bar docked left/right
        public string Edge = "bottom";      // top | bottom | left | right
        public int Height, TileH, Pad, Gap, ZoneGap, WindowRadius, TileRadius, PanelRadius;
        public int Si(float px) { return (int)Math.Round(px * Scale); }

        public static float SizeFactor(string size)
        {
            switch ((size ?? "M").ToUpperInvariant()) { case "S": return 0.85f; case "L": return 1.2f; case "XL": return 1.4f; case "XXL": return 1.65f; default: return 1f; }
        }
        public static BarMetrics For(string size, float dpiScale)
        {
            var m = new BarMetrics { Scale = SizeFactor(size) * dpiScale, Dpi = dpiScale };
            m.Height = m.Si(44); m.TileH = m.Si(34);
            m.Pad = (m.Height - m.TileH) / 2; m.Gap = Math.Max(2, m.Si(4)); m.ZoneGap = m.Si(14);
            m.WindowRadius = (int)Math.Round(8 * dpiScale);    // what DWMWCP_ROUND gives
            m.TileRadius = m.Si(9); m.PanelRadius = (int)Math.Round(8 * dpiScale);
            return m;
        }
    }

    public static class BarRenderer
    {
        /// <summary>Called when a widget throws while measuring/drawing (render harness logs it).</summary>
        public static Action<string, Exception> OnError;
        public static readonly string[] Zones = { "left", "center", "right" };
        static void Report(BarTile t, string where, Exception ex)
        {
            t.Error = where + ": " + ex.GetType().Name + " " + ex.Message;
            var h = OnError; if (h != null) try { h(t.Id + " [" + where + "]", ex); } catch { }
        }
        static void Report(string id, string where, Exception ex)
        {
            var h = OnError; if (h != null) try { h(id + " [" + where + "]", ex); } catch { }
        }

        static void Prep(DrawCtx c, Graphics g, Theme t, BarMetrics m, Rectangle r, bool hover, Point mouse, bool panel)
        {
            c.G = g; c.T = t; c.Bounds = r; c.Scale = m.Scale; c.Hover = hover; c.Mouse = mouse; c.IsPanel = panel; c.BarHeight = m.Height; c.Vertical = m.Vertical && !panel; c.Edge = m.Edge;
            c.Hits.Clear(); c.Wheels.Clear();
        }

        // ---------------- layout ----------------
        public static int MeasureTile(Graphics g, Theme t, BarMetrics m, BarTile tile)
        {
            int w = m.Si(140);
            if (tile.W != null && tile.Error == null)
            {
                try
                {
                    Prep(tile.Ctx, g, t, m, new Rectangle(0, 0, m.Si(120), m.TileH), false, new Point(-1, -1), false);
                    w = tile.W.Measure(tile.Ctx);
                    if (w < 0) w = 0; if (w > m.Si(900)) w = m.Si(900);
                }
                catch (Exception ex) { Report(tile, "Measure", ex); w = m.Si(220); }
            }
            else if (tile.Error != null) w = m.Si(220);
            tile.Width = w;
            return w;
        }

        public static string ZoneOf(BarTile t)
        {
            var z = (t.Zone ?? "").ToLowerInvariant();
            return z == "left" || z == "center" ? z : "right";
        }
        static bool IsZone(BarTile t, string z) { return ZoneOf(t) == z; }

        /// <summary>
        /// Measures and positions tiles. width &lt;= 0 (or pill) → compact layout, returns the content width.
        /// full style (pill=false, width &gt; 0) → left/center/right zones across width. Returns the used width.
        /// </summary>
        public static int Layout(Graphics g, Theme t, BarMetrics m, List<BarTile> tiles, int width, bool pill)
        {
            int lw = 0, cw = 0, rw = 0, ln = 0, cn = 0, rn = 0;
            foreach (var tl in tiles)
            {
                int w = MeasureTile(g, t, m, tl);
                if (IsZone(tl, "left")) { lw += w; ln++; } else if (IsZone(tl, "center")) { cw += w; cn++; } else { rw += w; rn++; }
            }
            if (ln > 1) lw += (ln - 1) * m.Gap; if (cn > 1) cw += (cn - 1) * m.Gap; if (rn > 1) rw += (rn - 1) * m.Gap;
            int y = (m.Height - m.TileH) / 2;
            if (pill || width <= 0)
            {
                int x = m.Pad; bool any = false;
                foreach (var zone in Zones)
                {
                    bool started = false;
                    foreach (var tl in tiles)
                    {
                        if (ZoneOf(tl) != zone) continue;
                        if (!started && any) x += m.ZoneGap - m.Gap;
                        started = true; any = true;
                        tl.R = new Rectangle(x, y, tl.Width, m.TileH); x += tl.Width + m.Gap;
                    }
                }
                if (any) x -= m.Gap;
                int total = x + m.Pad;
                if (total < m.Height) total = m.Height;
                return total;
            }
            else
            {
                // priority when space is short: right zone (clock/tray) > left zone > center zone.
                int zg = m.ZoneGap - m.Gap;
                int rx = width - m.Pad - rw;
                if (rx < m.Pad)
                {   // right zone alone is too wide: hide its first tiles until it fits
                    int need = m.Pad - rx;
                    foreach (var tl in tiles) if (IsZone(tl, "right") && need > 0) { need -= tl.Width + m.Gap; rx += tl.Width + m.Gap; tl.Width = 0; }
                }
                int x = m.Pad, leftLimit = (rn > 0 ? rx - zg : width - m.Pad);
                foreach (var tl in tiles)
                    if (IsZone(tl, "left"))
                    {
                        if (x + tl.Width > leftLimit) tl.Width = 0;
                        tl.R = new Rectangle(x, y, tl.Width, m.TileH); if (tl.Width > 0) x += tl.Width + m.Gap;
                    }
                int leftEnd = x - (x > m.Pad ? m.Gap : 0);
                int lo = leftEnd > m.Pad ? leftEnd + zg + m.Gap : m.Pad, hi = rn > 0 ? rx - zg - m.Gap : width - m.Pad;
                int cx = (width - cw) / 2;
                if (cx < lo) cx = lo; if (cx + cw > hi) cx = hi - cw;
                bool centerFits = cn > 0 && cx >= lo && cw <= hi - lo;
                foreach (var tl in tiles)
                    if (IsZone(tl, "center"))
                    {
                        if (!centerFits) tl.Width = 0;
                        tl.R = new Rectangle(cx, y, tl.Width, m.TileH); if (tl.Width > 0) cx += tl.Width + m.Gap;
                    }
                foreach (var tl in tiles)
                    if (IsZone(tl, "right")) { tl.R = new Rectangle(rx, y, tl.Width, m.TileH); if (tl.Width > 0) rx += tl.Width + m.Gap; }
                return width;
            }
        }

        /// <summary>Compact layout limited to maxW: tiles keep their order; the ones that don't fit are hidden (Width 0).
        /// Returns the used width. Call after Layout(...) has measured every tile.</summary>
        public static int FitCompact(BarMetrics m, List<BarTile> tiles, int maxW)
        {
            int y = (m.Height - m.TileH) / 2, x = m.Pad, limit = maxW - m.Pad; bool any = false;
            foreach (var tl in tiles)
            {
                int w = tl.Width;
                if (w <= 0) { tl.R = Rectangle.Empty; continue; }
                if (x + w > limit) { tl.R = new Rectangle(x, y, 0, m.TileH); continue; }   // overflow → hidden
                tl.R = new Rectangle(x, y, w, m.TileH); x += w + m.Gap; any = true;
            }
            if (any) x -= m.Gap;
            int used = Math.Max(m.Height, x + m.Pad), off = Math.Max(0, (maxW - used) / 2);
            if (off > 0) foreach (var tl in tiles) if (tl.R.Width > 0) tl.R.Offset(off, 0);   // keep the visible tiles centred
            return used;
        }

        /// <summary>Vertical bar: the height a tile wants at inner width w (0 = normal row).</summary>
        public static int MeasureTileV(Graphics g, Theme t, BarMetrics m, BarTile tile, int w)
        {
            tile.VH = 0;
            if (tile.W == null || tile.Error != null) return 0;
            try
            {
                Prep(tile.Ctx, g, t, m, new Rectangle(0, 0, w, m.TileH), false, new Point(-1, -1), false);
                int h = tile.W.MeasureHeight(tile.Ctx);
                if (h < 0) h = 0; if (h > m.Si(2000)) h = m.Si(2000);
                tile.VH = h;
            }
            catch (Exception ex) { Report(tile, "MeasureHeight", ex); }
            return tile.VH;
        }

        /// <summary>Vertical bar layout: tiles stacked top→bottom at one inner width (the widest single-row widget,
        /// capped at capW). Returns the bar size (width incl. padding, content height).</summary>
        public static Size LayoutV(Graphics g, Theme t, BarMetrics m, List<BarTile> tiles, int capW)
        {
            int pad = m.Si(8), gap = m.Si(8);                          // roomier than the horizontal bar
            int maxInner = Math.Max(m.TileH, capW - 2 * pad), widest = 0; bool anyRow = false;
            foreach (var tl in tiles)
            {
                int w = MeasureTile(g, t, m, tl);
                if (MeasureTileV(g, t, m, tl, maxInner) > 0) continue;   // grid widget: fills whatever width we pick
                if (w > 0) { widest = Math.Max(widest, w); anyRow = true; }
            }
            int inner = anyRow ? Math.Max(m.TileH, Math.Min(maxInner, widest)) : Math.Min(maxInner, m.Si(150));
            int y = pad;
            foreach (var tl in tiles)
            {
                int h = tl.VH > 0 ? MeasureTileV(g, t, m, tl, inner) : 0;
                if (h <= 0) { if (tl.Width <= 0) { tl.R = Rectangle.Empty; continue; } h = m.TileH; }
                tl.R = new Rectangle(pad, y, inner, h); y += h + gap;
            }
            if (y > pad) y -= gap;
            return new Size(inner + 2 * pad, Math.Max(m.Height, y + pad));
        }

        /// <summary>Vertical: keep tiles that fit in maxH (in order); the rest get Width 0 (hidden/overflow). Returns used height.</summary>
        public static int FitCompactV(BarMetrics m, List<BarTile> tiles, int maxH)
        {
            int limit = maxH - m.Si(8), used = m.Si(8);
            foreach (var tl in tiles)
            {
                if (tl.R.Width <= 0) continue;
                if (tl.R.Bottom > limit) { tl.R = new Rectangle(tl.R.X, tl.R.Y, 0, tl.R.Height); continue; }
                used = tl.R.Bottom;
            }
            return used + m.Si(8);
        }

        // ---------------- painting ----------------
        /// <summary>Bar background. radius 0 for the live window (DWM rounds it); &gt;0 in the render harness.</summary>
        public static void PaintBar(Graphics g, Theme t, Rectangle r, float radius)
        {
            if (!AppConfig.Current.SolidBar) { if (radius <= 0) g.Clear(Color.Transparent); return; }
            if (AppConfig.Current.IsShell) { if (radius <= 0) g.FillRectangle(Ui.B(t.ShellBg), r); else Ui.FillRound(g, r, radius, t.ShellBg); return; }
            if (radius <= 0)
            {
                g.FillRectangle(Ui.B(t.BarBg), r);   // no outline: the bar is just its colour
            }
            else
            {
                Ui.FillRound(g, r, radius, t.BarBg);
            }
        }

        /// <summary>Paint every tile (bg + widget content). Collects hits in tile.Ctx.</summary>
        public static void PaintTiles(Graphics g, Theme t, BarMetrics m, List<BarTile> tiles, BarTile hover, Point mouse, BarTile dragging = null)
        {
            for (int i = 0; i < tiles.Count; i++) { var tl = tiles[i]; if (tl != dragging) PaintTile(g, t, m, tl, tl == hover, mouse); }
            if (dragging != null) PaintTile(g, t, m, dragging, true, mouse);
        }

        /// <summary>Live bar sets this: tile hover visuals follow BarTile.HoverT (animated) instead of the hover flag.</summary>
        public static bool AnimHover;
        public static void PaintTile(Graphics g, Theme t, BarMetrics m, BarTile tl, bool hover, Point mouse)
        {
            var r = tl.R;
            if (r.Width <= 0) return;
            float lin = AnimHover ? Math.Max(0f, Math.Min(1f, tl.HoverT)) : (hover ? 1f : 0f);
            float amt = lin * lin * (3 - 2 * lin);                       // smoothstep
            int lift = (int)Math.Round(1.5f * m.Scale * amt);
            if (m.Vertical) lift = 0; else if (lift > 0) r.Offset(0, -lift);   // sidebars: fade only
            if (tl.Error != null || tl.W == null) { ErrorBox(g, m.Scale, r, tl.Id, tl.Error ?? "unknown widget"); tl.Ctx.Hits.Clear(); tl.Ctx.Wheels.Clear(); return; }
            if (AppConfig.Current.IsShell)
            {   // shell: widgets sit flat on the bar; a soft pill only on hover
                if (amt > 0.01f) Ui.FillRound(g, r, m.TileRadius, Color.FromArgb((int)((t.Dark ? 30 : 22) * amt), t.Dark ? 255 : 0, t.Dark ? 255 : 0, t.Dark ? 255 : 0));
                Prep(tl.Ctx, g, t, m, r, hover, hover ? mouse : new Point(-1, -1), false);
                goto content;
            }
            if (!AppConfig.Current.BarBackground)
            {
                if (t.IsGlass)
                {
                    // liquid glass: real blur sits behind (BarGlass panes); light tint + top sheen + bright rim on top
                    float rad = 8f * m.Dpi;   // match DWM's 8px window rounding
                    Ui.FillRound(g, r, rad, Color.FromArgb(t.Dark ? 70 : 90, t.BarBg));
                    using (var path = Ui.Round(r, rad))
                    using (var sheen = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                        Color.FromArgb(t.Dark ? 46 : 80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    { sheen.SetBlendTriangularShape(0.0f, 1f); g.FillPath(sheen, path); }
                    using (var path = Ui.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), rad))
                    using (var rim = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                        Color.FromArgb(t.Dark ? 110 : 170, 255, 255, 255), Color.FromArgb(t.Dark ? 25 : 60, 255, 255, 255), System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    using (var pen = new Pen(rim, 1f)) g.DrawPath(pen, path);
                    Prep(tl.Ctx, g, t, m, r, hover, hover ? mouse : new Point(-1, -1), false);
                    if (amt > 0.01f) Ui.FillRound(g, r, rad, Color.FromArgb((int)((t.Dark ? 28 : 40) * amt), 255, 255, 255));
                    goto content;
                }
                else
                {
                    // solid floating tile: soft shadow + the bar colour underneath
                    Ui.FillRound(g, new RectangleF(r.X, r.Y + 1.5f * m.Scale, r.Width, r.Height), m.TileRadius, Color.FromArgb(t.Dark ? 70 : 30, 0, 0, 0));
                    Ui.FillRound(g, r, m.TileRadius, t.BarBg);
                }
            }
            if (lift > 0 && AppConfig.Current.SolidBar)   // lifted tile casts a soft shadow onto the bar
                Ui.FillRound(g, new RectangleF(r.X + 1, r.Y + lift + 1, r.Width - 2, r.Height), m.TileRadius, Color.FromArgb((int)(55 * amt), 0, 0, 0));
            Ui.FillRound(g, r, m.TileRadius, Ui.Mix(t.Tile, t.TileHover, amt));
            Ui.StrokeRound(g, r, m.TileRadius, Ui.Mix(t.TileBorder, Ui.WithA(t.Accent, Math.Max((int)t.TileBorder.A, 90)), amt * 0.5), 1f);
            Prep(tl.Ctx, g, t, m, r, hover, hover ? mouse : new Point(-1, -1), false);
            content:
            if (m.Vertical && tl.VH == 0 && tl.Width > 0)
            {   // sidebar row: narrower widgets sit centred; wider ones are drawn scaled down to fit the column
                int inset = m.Si(4), avail = r.Width - 2 * inset;
                if (tl.Width > avail)
                {
                    float f = Math.Max(0.55f, avail / (float)tl.Width);
                    tl.Ctx.Scale = m.Scale * f;
                    int ch = (int)Math.Round(r.Height * Math.Max(f, 0.8f));
                    tl.Ctx.Bounds = new Rectangle(r.X + inset, r.Y + (r.Height - ch) / 2, avail, ch);
                }
                else tl.Ctx.Bounds = new Rectangle(r.X + (r.Width - tl.Width) / 2, r.Y, tl.Width, r.Height);
            }
            var clip = g.Clip;
            g.SetClip(r);
            try { tl.W.Draw(tl.Ctx); }
            catch (Exception ex) { Report(tl, "Draw", ex); }
            finally { g.ResetTransform(); g.ResetClip(); Ui.Hq(g); clip.Dispose(); }
            if (tl.Error != null) ErrorBox(g, m.Scale, r, tl.Id, tl.Error);
        }

        /// <summary>Panel card background (host-painted).</summary>
        public static void PaintCard(Graphics g, Theme t, Rectangle r, float radius)
        {
            if (AppConfig.Current.IsShell) { if (radius <= 0) g.FillRectangle(Ui.B(t.ShellBg), r); else Ui.FillRound(g, r, radius, t.ShellBg); return; }
            if (radius <= 0)
            {
                g.FillRectangle(Ui.B(t.PanelBg), r);
                using (var p = new Pen(t.BarBorder, 1f)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            else { Ui.FillRound(g, r, radius, t.PanelBg); Ui.StrokeRound(g, r, radius, t.BarBorder, 1f); }
        }

        /// <summary>Panel content. Returns false (and draws an error box) if the widget threw.</summary>
        public static bool PaintPanel(Graphics g, Theme t, BarMetrics m, Widget w, DrawCtx c, Rectangle r, bool hover, Point mouse, Action<InputSpec> inputSink)
        {
            Prep(c, g, t, m, r, hover, mouse, true);
            c.InputSink = inputSink;
            g.SetClip(r);
            bool ok = true;
            try { w.DrawPanel(c); }
            catch (Exception ex) { ok = false; Report(w.Meta.Id, "DrawPanel", ex); c.Hits.Clear(); c.Wheels.Clear(); ErrorBox(g, m.Scale, r, w.Meta.Id, "DrawPanel: " + ex.GetType().Name + " " + ex.Message); }
            finally { g.ResetTransform(); g.ResetClip(); Ui.Hq(g); }
            return ok;
        }

        public static Size SafePanelSize(Widget w, float scale)
        {
            try { return w.PanelSize(scale); }
            catch (Exception ex) { Report(w.Meta.Id, "PanelSize", ex); return Size.Empty; }
        }

        static readonly Color ErrBg = Color.FromArgb(255, 120, 18, 24), ErrFg = Color.FromArgb(255, 255, 220, 220);
        public static void ErrorBox(Graphics g, float scale, Rectangle r, string id, string msg)
        {
            Ui.FillRound(g, r, 6 * scale, ErrBg);
            var f = Ui.Font(11 * scale, true); var f2 = Ui.Font(10 * scale);
            if (r.Height < 50 * scale)
                Ui.Text(g, id + " ⚠ " + msg, f2, ErrFg, Ui.Inset(r, 6 * scale, 0));
            else
            {
                Ui.Text(g, "⚠ " + id, f, Color.White, new RectangleF(r.X + 8 * scale, r.Y + 6 * scale, r.Width - 16 * scale, 16 * scale));
                Ui.TextWrap(g, msg, f2, ErrFg, new RectangleF(r.X + 8 * scale, r.Y + 26 * scale, r.Width - 16 * scale, r.Height - 30 * scale));
            }
        }

        /// <summary>Hit test helpers shared by bar & panel: last registered first.</summary>
        public static Action HitAt(DrawCtx c, Point p)
        {
            for (int i = c.Hits.Count - 1; i >= 0; i--) if (c.Hits[i].Key.Contains(p)) return c.Hits[i].Value;
            return null;
        }
        public static Action<int> WheelAt(DrawCtx c, Point p)
        {
            for (int i = c.Wheels.Count - 1; i >= 0; i--) if (c.Wheels[i].Key.Contains(p)) return c.Wheels[i].Value;
            return null;
        }
    }
}
