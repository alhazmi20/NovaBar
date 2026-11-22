// Settings app: renders real widgets (via WidgetRegistry + their own Draw code) for live previews.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    class SetPreview : IWidgetHost, IDisposable
    {
        readonly SetWindow win;
        readonly Dictionary<string, Widget> map = new Dictionary<string, Widget>();
        readonly HashSet<Widget> started = new HashSet<Widget>();
        readonly HashSet<Widget> broken = new HashSet<Widget>();
        readonly Dictionary<Widget, int> lastTick = new Dictionary<Widget, int>();
        public SetPreview(SetWindow w) { win = w; }

        /// <summary>Get (or create) a preview widget. key distinguishes gallery copies from bar instances.</summary>
        public Widget Get(string key, string id, WidgetInstance inst, bool start)
        {
            Widget w;
            if (!map.TryGetValue(key, out w))
            {
                try { w = WidgetRegistry.Create(id, inst); } catch { w = null; }
                if (w == null) return null;
                w.Host = this; map[key] = w;
            }
            if (start && !started.Contains(w) && !broken.Contains(w))
            {
                started.Add(w);
                try { w.OnStart(); } catch { broken.Add(w); }
            }
            return w;
        }
        public void Forget(string key)
        {
            Widget w; if (!map.TryGetValue(key, out w)) return;
            Stop(w); map.Remove(key);
        }
        void Stop(Widget w)
        {
            if (started.Remove(w)) { try { w.OnStop(); } catch { } try { Hub.UnsubscribeAll(w); } catch { } }
        }
        /// <summary>Stop every widget whose key is not in keep (they stay cached but idle).</summary>
        public void StopExcept(ICollection<string> keep)
        {
            foreach (var kv in map.ToList()) if (!keep.Contains(kv.Key)) Stop(kv.Value);
        }
        public void Dispose()
        {
            foreach (var w in map.Values.ToList()) Stop(w);
            map.Clear();
        }
        /// <summary>Called by the window every ~500 ms: drives widget OnTick for running clocks/timers.</summary>
        public bool Tick()
        {
            bool any = false; int now = Environment.TickCount;
            foreach (var w in started.ToList())
            {
                int ms; try { ms = w.RefreshMs; } catch { continue; }
                if (ms <= 0) continue;
                int last; lastTick.TryGetValue(w, out last);
                if (now - last >= Math.Max(500, ms)) { lastTick[w] = now; try { w.OnTick(); } catch { } any = true; }
            }
            return any;
        }

        // ---- IWidgetHost ----
        public void Invalidate(Widget w) { if (win != null) win.RequestRepaint(); }
        public void Post(Action a) { if (win != null && win.IsHandleCreated) try { win.BeginInvoke(a); } catch { } }
        public void OpenPanel(Widget w) { }
        public void ClosePanel() { }
        public bool IsPanelOpen(Widget w) { return false; }
        public void OpenSettings(string page) { if (win != null) Post(() => win.Navigate(page)); }
        public Rectangle ScreenBoundsOf(Widget w) { return Rectangle.Empty; }

        // ---- rendering ----
        DrawCtx Ctx(Graphics g, Theme t, Rectangle b, float scale)
        {
            return new DrawCtx { G = g, T = t, Bounds = b, Scale = scale, BarHeight = (int)Math.Round(44 * scale), Hover = false };
        }
        public int Measure(Graphics g, Theme t, Widget w, float scale)
        {
            if (w == null || broken.Contains(w)) return (int)(80 * scale);
            int th = (int)Math.Round(34 * scale);
            var old = Theme.Current; Theme.Current = t;
            try { int x = w.Measure(Ctx(g, t, new Rectangle(0, 0, (int)(120 * scale), th), scale)); return Math.Max((int)(20 * scale), Math.Min((int)(900 * scale), x)); }
            catch { broken.Add(w); return (int)(80 * scale); }
            finally { Theme.Current = old; }
        }
        /// <summary>Draws the tile background (like the bar host) plus the widget's own content.</summary>
        public void DrawTile(Graphics g, Theme t, Widget w, Rectangle r, float scale, bool tileBg = true)
        {
            if (tileBg)
            {
                Ui.FillRound(g, r, 9 * scale, t.Tile);
                Ui.StrokeRound(g, r, 9 * scale, t.TileBorder, 1f);
            }
            if (w == null) return;
            var st = g.Save(); var old = Theme.Current;
            try
            {
                g.SetClip(r, CombineMode.Intersect);
                Theme.Current = t;
                if (broken.Contains(w)) throw new Exception();
                w.Draw(Ctx(g, t, r, scale));
            }
            catch
            {
                broken.Add(w);
                g.Restore(st); st = g.Save(); g.SetClip(r, CombineMode.Intersect);
                Ui.Icon(g, Icons.Warning, new RectangleF(r.X, r.Y, Math.Min(r.Width, 34 * scale), r.Height), t.Warn, 13 * scale);
                Ui.Text(g, w.Meta.Name, Ui.Font(11 * scale), t.TextDim, new RectangleF(r.X + 30 * scale, r.Y, r.Width - 34 * scale, r.Height));
            }
            finally { Theme.Current = old; g.Restore(st); }
        }

        // ---- bar mockups ----
        
        /// <summary>Draw a bar with three zones of widgets into area (mirrors the bar host layout: pad 5, gap 4, zone gap 14).
        /// The bar is scaled down to fit if needed. Lists may contain nulls (rendered as mock tiles). Returns the bar rect.</summary>
        public RectangleF DrawBar(Graphics g, Theme t, RectangleF area, IList<Widget>[] zones, float scale, bool pill, bool center = true)
        {
            float sc = scale; float[][] ws = null; float total = 0;
            for (int it = 0; it < 14; it++)
            {
                ws = new float[3][]; total = 10 * sc; int nz = 0;
                for (int z = 0; z < 3; z++)
                {
                    ws[z] = new float[zones[z].Count];
                    for (int i = 0; i < zones[z].Count; i++) { var w = zones[z][i]; ws[z][i] = w == null ? 70 * sc : Measure(g, t, w, sc); total += ws[z][i]; }
                    if (zones[z].Count > 0) { total += (zones[z].Count - 1) * 4 * sc; nz++; }
                }
                if (nz > 1) total += (nz - 1) * 14 * sc;
                if (total <= area.Width || sc < 0.28f) break;
                sc *= 0.92f;
            }
            float bh = 44 * sc, th = 34 * sc, pad = 5 * sc, gap = 4 * sc;
            float bw = pill ? Math.Max(bh, Math.Min(area.Width, total)) : area.Width;
            var bar = new RectangleF(area.X + (area.Width - bw) / 2, area.Y + (center ? (area.Height - bh) / 2 : 0), bw, bh);
            FillBarBg(g, t, bar, pill ? 8 * sc : 0);
            float ty = bar.Y + (bh - th) / 2;
            var st = g.Save(); g.SetClip(bar, CombineMode.Intersect);
            if (pill || total > bar.Width + 1)
            {
                float x = bar.X + pad; bool any = false;
                for (int z = 0; z < 3; z++)
                {
                    if (zones[z].Count == 0) continue;
                    if (any) x += 14 * sc - gap; any = true;
                    for (int i = 0; i < zones[z].Count; i++) { DrawOne(g, t, zones[z][i], x, ty, ws[z][i], th, sc); x += ws[z][i] + gap; }
                }
            }
            else
            {
                float x = bar.X + pad;
                for (int i = 0; i < zones[0].Count; i++) { DrawOne(g, t, zones[0][i], x, ty, ws[0][i], th, sc); x += ws[0][i] + gap; }
                float cw = ws[1].Sum() + Math.Max(0, zones[1].Count - 1) * gap, rw = ws[2].Sum() + Math.Max(0, zones[2].Count - 1) * gap;
                float cx = bar.X + (bar.Width - cw) / 2;
                float rStart = bar.Right - pad - rw - (zones[2].Count > 0 ? 14 * sc : 0);
                if (cx + cw > rStart) cx = rStart - cw;
                if (cx < x + 14 * sc - gap && zones[1].Count > 0) cx = x + 14 * sc - gap;
                for (int i = 0; i < zones[1].Count; i++) { DrawOne(g, t, zones[1][i], cx, ty, ws[1][i], th, sc); cx += ws[1][i] + gap; }
                float rx = bar.Right - pad - rw; if (rx < cx + 14 * sc - gap && zones[1].Count > 0) rx = cx + 14 * sc - gap;
                for (int i = 0; i < zones[2].Count; i++) { DrawOne(g, t, zones[2][i], rx, ty, ws[2][i], th, sc); rx += ws[2][i] + gap; }
            }
            g.Restore(st);
            return bar;
        }
        void DrawOne(Graphics g, Theme t, Widget w, float x, float y, float ww, float th, float sc)
        {
            if (w == null) { SetMock.Tile(g, t, new RectangleF(x, y, ww, th), sc, (int)x); return; }
            DrawTile(g, t, w, new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(ww), (int)Math.Round(th)), sc);
        }

        public static void FillBarBg(Graphics g, Theme t, RectangleF bar, float rad)
        {
            if (t.Id == "glass") Ui.FillRound(g, bar, rad, Color.FromArgb(70, 30, 30, 42));
            else if (t.Id == "glasslight") Ui.FillRound(g, bar, rad, Color.FromArgb(120, 255, 255, 255));
            Ui.FillRound(g, bar, rad, t.BarBg);
            Ui.StrokeRound(g, bar, rad, t.BarBorder, 1f);
            if (t.IsGlass) // specular top highlight like liquid glass
                using (var p = Ui.Round(new RectangleF(bar.X + 1, bar.Y + 1, bar.Width - 2, bar.Height * 0.5f), Math.Max(0, rad - 1)))
                using (var br = new LinearGradientBrush(new RectangleF(bar.X, bar.Y, bar.Width, bar.Height * 0.5f + 1), Color.FromArgb(t.Dark ? 22 : 60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    g.FillPath(br, p);
        }

        // ---- wallpaper backdrop for previews (blurred colorful gradient) ----
        static Bitmap wall, wallDark, wallLight;
        static Bitmap MakeWall(int variant)
        {
            var bmp = new Bitmap(64, 36, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color a, b; Color[] blobs;
                if (variant == 1) { a = Color.FromArgb(28, 24, 52); b = Color.FromArgb(12, 40, 70); blobs = new[] { Color.FromArgb(230, 90, 60, 200), Color.FromArgb(220, 230, 80, 140), Color.FromArgb(200, 40, 170, 200), Color.FromArgb(180, 250, 160, 70) }; }
                else if (variant == 2) { a = Color.FromArgb(200, 214, 245); b = Color.FromArgb(248, 214, 226); blobs = new[] { Color.FromArgb(200, 140, 170, 255), Color.FromArgb(200, 255, 170, 200), Color.FromArgb(180, 150, 230, 220), Color.FromArgb(170, 255, 220, 150) }; }
                else { a = Color.FromArgb(70, 40, 140); b = Color.FromArgb(20, 90, 160); blobs = new[] { Color.FromArgb(255, 255, 110, 90), Color.FromArgb(255, 240, 70, 160), Color.FromArgb(230, 40, 200, 210), Color.FromArgb(220, 255, 190, 60) }; }
                using (var br = new LinearGradientBrush(new Rectangle(-24, -24, 112, 84), a, b, 30f)) g.FillRectangle(br, 0, 0, 64, 36);
                var pos = new[] { new RectangleF(-10, -12, 40, 34), new RectangleF(30, 14, 42, 30), new RectangleF(34, -16, 30, 30), new RectangleF(2, 20, 30, 24) };
                for (int i = 0; i < blobs.Length; i++)
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(pos[i]);
                        using (var pg = new PathGradientBrush(path) { CenterColor = blobs[i], SurroundColors = new[] { Color.FromArgb(0, blobs[i]) } }) g.FillPath(pg, path);
                    }
            }
            return bmp;
        }
        /// <summary>variant: 0 vivid, 1 dark, 2 light</summary>
        public static void Wallpaper(Graphics g, RectangleF r, int variant = 0)
        {
            Bitmap b;
            if (variant == 1) b = wallDark ?? (wallDark = MakeWall(1)); else if (variant == 2) b = wallLight ?? (wallLight = MakeWall(2)); else b = wall ?? (wall = MakeWall(0));
            var im = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                var dst = new[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.X, r.Bottom) };
                g.DrawImage(b, dst, new RectangleF(2f, 2f, b.Width - 4, b.Height - 4), GraphicsUnit.Pixel, ia);
            }
            g.InterpolationMode = im;
        }
        public static int WallFor(Theme t) { return t.Id == "glasslight" || t.Id == "white" ? 2 : t.Id == "midnight" ? 1 : 0; }
    }

    /// <summary>Placeholder tiles used only when no real widget is available (e.g. stripped-down builds).</summary>
    static class SetMock
    {
        static readonly string[][] items = {
            new[] { Icons.Clock, "12:45" }, new[] { Icons.Cpu, "23%" }, new[] { Icons.PartlyCloudy, "24°" },
            new[] { Icons.Music, "Lo-fi" }, new[] { Icons.Battery, "86%" }, new[] { Icons.Wifi, "Wi-Fi" } };
        public static void Tile(Graphics g, Theme t, RectangleF r, float sc, int seed)
        {
            Ui.FillRound(g, r, 9 * sc, t.Tile); Ui.StrokeRound(g, r, 9 * sc, t.TileBorder, 1f);
            var it = items[Math.Abs(seed / 7) % items.Length];
            Ui.Icon(g, it[0], new RectangleF(r.X + 8 * sc, r.Y, 18 * sc, r.Height), t.Accent, 13 * sc);
            Ui.Text(g, it[1], Ui.FontSemi(12.5f * sc), t.Text, new RectangleF(r.X + 29 * sc, r.Y, r.Width - 32 * sc, r.Height));
        }
    }
}
