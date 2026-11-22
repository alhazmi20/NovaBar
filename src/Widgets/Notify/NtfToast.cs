// NovaBar toast banners: borderless no-activate topmost windows stacked at the bottom-right above the bar
// (top-right under it when the bar is at the top). Painted on a PARGB back buffer like the bar; glass themes get the
// same acrylic accent backdrop. Timers run only while toasts are on screen (15 ms only while sliding).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>Toast card painting shared by the live window and the render preview.</summary>
    public static class NtfDraw
    {
        public const float W = 360, Pad = 14;

        static float BodyH(Graphics g, NtfItem it, float s, int maxLines, float width)
        {
            if (string.IsNullOrEmpty(it.Body)) return 0;
            var f = Ui.Font(13 * s);
            float lh = f.GetHeight(g);
            var sz = g.MeasureString(it.Body, f, (int)width, StringFormat.GenericTypographic);
            int lines = Math.Max(1, Math.Min(maxLines, (int)Math.Ceiling(sz.Height / lh - 0.05)));
            return lines * lh;
        }

        public static Size ToastSize(Graphics g, NtfItem it, float s)
        {
            float w = W * s, inner = w - 2 * Pad * s;
            float h = Pad * s + 26 * s + 20 * s;
            float bh = BodyH(g, it, s, 3, inner);
            if (bh > 0) h += 3 * s + bh;
            h += Pad * s + 2 * s;
            return new Size((int)Math.Ceiling(w), (int)Math.Ceiling(h));
        }

        /// <summary>Paint the toast content in r (background already painted). Returns the close-button rect.</summary>
        public static Rectangle PaintToast(Graphics g, Theme t, Rectangle r, NtfItem it, float s, bool hover, Point mouse)
        {
            float x = r.X + Pad * s, y = r.Y + Pad * s, right = r.Right - Pad * s;
            var ir = new RectangleF(x, y, 18 * s, 18 * s);
            NtfIcons.Draw(g, it, ir, t);
            var close = new Rectangle((int)(r.Right - 10 * s - 26 * s), (int)(r.Y + 8 * s), (int)(26 * s), (int)(26 * s));
            var tf = Ui.Font(11 * s);
            string time = NtfItem.Ago(it.Time);
            float tw = Ui.Measure(g, time, tf).Width;
            float timeRight = hover ? close.X - 2 * s : right;
            Ui.Text(g, time, tf, t.TextFaint, new RectangleF(timeRight - tw - 2, y, tw + 2, 18 * s), 1);
            Ui.Text(g, it.App, Ui.FontSemi(12 * s), t.TextDim, new RectangleF(ir.Right + 8 * s, y, timeRight - tw - 8 * s - ir.Right - 8 * s, 18 * s));
            if (hover)
            {
                bool ch = close.Contains(mouse);
                if (ch) Ui.FillRound(g, close, close.Width / 2f, t.TileHover);
                Ui.Icon(g, "", close, ch ? t.Text : t.TextDim, 10.5f * s);
            }
            y += 26 * s;
            Ui.Text(g, it.Title, Ui.FontSemi(14 * s), t.Text, new RectangleF(x, y, right - x, 20 * s));
            y += 23 * s;
            float bh = BodyH(g, it, s, 3, right - x);
            if (bh > 0) Ui.TextWrap(g, it.Body, Ui.Font(13 * s), t.TextDim, new RectangleF(x, y, right - x, bh + 1));
            return close;
        }

        public static void PaintBackground(Graphics g, Theme t, Rectangle r, float radius)
        {
            if (radius <= 0)
            {
                g.FillRectangle(Ui.B(t.PanelBg), r);
                using (var p = new Pen(t.BarBorder, 1f)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            else { Ui.FillRound(g, r, radius, t.PanelBg); Ui.StrokeRound(g, r, radius, t.BarBorder, 1f); }
        }
    }

    /// <summary>Back buffer (same technique as the bar's: 32bpp premultiplied DIB + BitBlt).</summary>
    sealed class NtfBuffer : IDisposable
    {
        IntPtr mdc, hbmp, oldBmp, bits; Bitmap bmp; Graphics g; public int W, H;
        public Graphics Begin(int w, int h)
        {
            if (w < 1) w = 1; if (h < 1) h = 1;
            if (g == null || w != W || h != H)
            {
                Free(); W = w; H = h;
                var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                var sdc = NtfNative.GetDC(IntPtr.Zero);
                mdc = Native.CreateCompatibleDC(sdc);
                hbmp = Native.CreateDIBSection(sdc, ref bi, 0, out bits, IntPtr.Zero, 0);
                NtfNative.ReleaseDC(IntPtr.Zero, sdc);
                oldBmp = Native.SelectObject(mdc, hbmp);
                bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
                g = Graphics.FromImage(bmp); Ui.Hq(g);
            }
            g.Clear(Color.Transparent);
            return g;
        }
        public void Blit(IntPtr hdc) { if (g == null) return; g.Flush(FlushIntention.Sync); Native.BitBlt(hdc, 0, 0, W, H, mdc, 0, 0, Native.SRCCOPY); }
        public void BlitTo(IntPtr hwnd) { var dc = NtfNative.GetDC(hwnd); try { Blit(dc); } finally { NtfNative.ReleaseDC(hwnd, dc); } }
        void Free()
        {
            if (g != null) { g.Dispose(); g = null; }
            if (bmp != null) { bmp.Dispose(); bmp = null; }
            if (mdc != IntPtr.Zero) { Native.SelectObject(mdc, oldBmp); Native.DeleteDC(mdc); mdc = IntPtr.Zero; }
            if (hbmp != IntPtr.Zero) { Native.DeleteObject(hbmp); hbmp = IntPtr.Zero; }
        }
        public void Dispose() { Free(); }
    }

    class NtfToastForm : Form
    {
        public readonly NtfItem Item;
        readonly NtfBuffer buf = new NtfBuffer();
        readonly Theme t; readonly float s;
        public bool Hovered; public int RemainingMs;
        Point mouse = new Point(-1, -1); Rectangle closeR;
        // animation
        public Point Target; Point animFrom; int animStart = -1; public const int AnimMs = 200;
        public bool Animating { get { return animStart >= 0; } }

        public NtfToastForm(NtfItem it, Theme theme, float scale, Size size)
        {
            Item = it; t = theme; s = scale;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; TopMost = true; Text = "NovaBar notification"; MinimumSize = new Size(1, 1);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            Size = size;
            RemainingMs = (int)(NtfNative.ExtraNum("notify.duration", 6) * 1000);
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST; cp.ClassStyle |= 0x20000; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NtfNative.WM_MOUSEACTIVATE) { m.Result = new IntPtr(NtfNative.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); NtfNative.Backdrop(Handle, t); }

        public void MoveTo(Point target, bool slideIn)
        {
            Target = target;
            animFrom = slideIn ? new Point(target.X + (int)(48 * s), target.Y) : Location;
            if (animFrom == target) { animStart = -1; Location = target; return; }
            animStart = Environment.TickCount;
            if (slideIn) Location = animFrom;
        }
        /// <summary>Advance the animation; returns true while still animating.</summary>
        public bool Step()
        {
            if (animStart < 0) return false;
            double p = Math.Min(1, (Environment.TickCount - animStart) / (double)AnimMs);
            double e = 1 - Math.Pow(1 - p, 3);
            Location = new Point((int)Math.Round(animFrom.X + (Target.X - animFrom.X) * e), (int)Math.Round(animFrom.Y + (Target.Y - animFrom.Y) * e));
            if (p >= 1) { animStart = -1; return false; }
            return true;
        }

        public void ShowNoActivate()
        {
            CreateHandle(); Render();
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void Render()
        {
            if (IsDisposed) return;
            var g = buf.Begin(ClientSize.Width, ClientSize.Height);
            var r = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            NtfDraw.PaintBackground(g, t, r, 0);
            try { closeR = NtfDraw.PaintToast(g, t, r, Item, s, Hovered, mouse); } catch (Exception ex) { NtfNative.Log("toast paint", ex); }
            if (IsHandleCreated) buf.BlitTo(Handle);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (buf.W != ClientSize.Width || buf.H != ClientSize.Height) { Render(); return; }
            var hdc = e.Graphics.GetHdc(); try { buf.Blit(hdc); } finally { e.Graphics.ReleaseHdc(hdc); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool wasClose = closeR.Contains(mouse);
            mouse = e.Location;
            if (!Hovered) { Hovered = true; Render(); }
            else if (wasClose != closeR.Contains(mouse)) Render();
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hovered = false; mouse = new Point(-1, -1); Render(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || closeR.Contains(e.Location)) { NtfToasts.Close(Item.Key); return; }   // × → stays in the center
            if (e.Button == MouseButtons.Left) NtfService.Activate(Item);
        }
        protected override void Dispose(bool disposing) { if (disposing) buf.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Toast stack manager (UI thread only).</summary>
    public static class NtfToasts
    {
        const int MaxVisible = 3;
        static readonly List<NtfToastForm> visible = new List<NtfToastForm>();   // [0] = newest (closest to the bar)
        static readonly Queue<NtfItem> pending = new Queue<NtfItem>();
        static Timer timer; static int lastTick;

        public static int Count { get { return visible.Count; } }

        public static void Show(NtfItem it)
        {
            if (visible.Any(v => v.Item.Key == it.Key)) return;
            if (visible.Count >= MaxVisible) { pending.Enqueue(it); return; }
            Form bar; Rectangle area; bool top; float s; Anchor(out bar, out area, out top, out s);
            Size sz;
            using (var bmp = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bmp)) { Ui.Hq(g); sz = NtfDraw.ToastSize(g, it, s); }
            var f = new NtfToastForm(it, Theme.Current, s, sz);
            visible.Insert(0, f);
            Restack(f);
            f.ShowNoActivate();
            EnsureTimer();
        }

        public static void Close(string key)
        {
            var f = visible.FirstOrDefault(x => x.Item.Key == key);
            if (f == null) { var keep = pending.Where(p => p.Key != key).ToList(); pending.Clear(); foreach (var p in keep) pending.Enqueue(p); return; }
            visible.Remove(f);
            try { f.Close(); f.Dispose(); } catch { }
            while (visible.Count < MaxVisible && pending.Count > 0)
            {
                var p = pending.Dequeue();
                if ((DateTime.Now - p.Time).TotalMinutes < 2 && NtfService.Items.Any(i => i.Key == p.Key)) { Show(p); return; }
            }
            Restack(null);
        }

        public static void CloseAll()
        {
            pending.Clear();
            foreach (var f in visible.ToList()) { try { f.Close(); f.Dispose(); } catch { } }
            visible.Clear();
            if (timer != null) timer.Stop();
        }

        /// <summary>Where the stack lives: next to the NovaBar bar (found among open forms), else the work area corner.</summary>
        static void Anchor(out Form bar, out Rectangle area, out bool top, out float s)
        {
            bar = null;
            foreach (Form f in Application.OpenForms) if (f.GetType().Name == "BarForm" && f.Visible && f.Width > 50) { bar = f; break; }
            var scr = bar != null ? Screen.FromHandle(bar.Handle) : Screen.PrimaryScreen;
            uint dpi = 96;
            try { dpi = bar != null ? Native.GetDpiForWindow(bar.Handle) : Native.GetDpiForSystem(); } catch { }
            s = Math.Max(1f, dpi / 96f);
            if (bar != null && bar.Height > bar.Width)
            {   // vertical sidebar: bottom corner on the side away from it
                top = false; var b = scr.Bounds;
                bool barLeft = bar.Left + bar.Width / 2 < b.Left + b.Width / 2;
                area = barLeft ? Rectangle.FromLTRB(bar.Right, b.Top, b.Right, b.Bottom) : Rectangle.FromLTRB(b.Left, b.Top, bar.Left, b.Bottom);
            }
            else if (bar != null)
            {
                top = bar.Top + bar.Height / 2 < scr.Bounds.Top + scr.Bounds.Height / 2;
                var b = scr.Bounds;
                area = top ? new Rectangle(b.Left, bar.Bottom, b.Width, b.Bottom - bar.Bottom) : new Rectangle(b.Left, b.Top, b.Width, bar.Top - b.Top);
                // keep the stack right-aligned with the bar's right edge (pill bars don't span the screen)
                if (bar.Right < b.Right - 4) area.Width = bar.Right - b.Left;
            }
            else { top = false; area = scr.WorkingArea; }
        }

        static void Restack(NtfToastForm fresh)
        {
            if (visible.Count == 0) return;
            Form bar; Rectangle area; bool top; float s; Anchor(out bar, out area, out top, out s);
            int gap = (int)(10 * s), edge = (int)(12 * s);
            int y = top ? area.Top + edge : area.Bottom - edge;
            foreach (var f in visible)
            {
                int x = area.Right - edge - f.Width;
                var target = top ? new Point(x, y) : new Point(x, y - f.Height);
                if (f == fresh) f.MoveTo(target, true); else if (f.Target != target) f.MoveTo(target, false);
                y = top ? y + f.Height + gap : y - f.Height - gap;
            }
            EnsureTimer();
        }

        static void EnsureTimer()
        {
            if (timer == null) { timer = new Timer(); timer.Tick += (o, e) => Tick(); }
            bool anim = visible.Any(f => f.Animating);
            timer.Interval = anim ? 15 : 200;
            if (!timer.Enabled) { lastTick = Environment.TickCount; timer.Start(); }
        }

        static void Tick()
        {
            int now = Environment.TickCount, dt = now - lastTick; lastTick = now;
            bool anim = false;
            foreach (var f in visible) anim |= f.Step();
            // auto-hide countdown pauses while the pointer is over any toast (like Windows)
            bool anyHover = visible.Any(f => f.Hovered || f.Bounds.Contains(Control.MousePosition));
            if (!anyHover)
                foreach (var f in visible.ToList())
                {
                    f.RemainingMs -= dt;
                    if (f.RemainingMs <= 0) { Close(f.Item.Key); anim = true; }
                }
            if (visible.Count == 0) { timer.Stop(); return; }
            int want = anim || visible.Any(f => f.Animating) ? 15 : 200;
            if (timer.Interval != want) timer.Interval = want;
        }

        public static string Describe() { return string.Join("; ", visible.Select(f => f.Bounds + " vis=" + Native.IsWindowVisible(f.Handle))); }
        /// <summary>Screen rect covering the stack (live test screenshots).</summary>
        public static Rectangle StackBounds()
        {
            if (visible.Count == 0) return Rectangle.Empty;
            var r = visible[0].Bounds; foreach (var f in visible) r = Rectangle.Union(r, new Rectangle(f.Target, f.Size));
            return r;
        }
    }

    /// <summary>Render-harness extras: a PNG of the toast stack in each theme (and, with NOVABAR_NOTIFY_LIVE=1, a short real on-screen test).</summary>
    public static class NtfPreview
    {
        static bool done;
        public static void MaybeRender()
        {
            if (done || !NtfNative.RenderMode) return; done = true;
            var outPng = NtfNative.Arg("--render"); if (string.IsNullOrEmpty(outPng)) return;
            try
            {
                var full = Path.GetFullPath(outPng);
                var dest = Path.Combine(Path.GetDirectoryName(full), Path.GetFileNameWithoutExtension(full) + "-toasts.png");
                var themes = (NtfNative.Arg("--themes") ?? string.Join(",", Theme.Ids)).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                Render(dest, themes);
                if (NtfNative.Env("NOVABAR_NOTIFY_LIVE")) Live(Path.Combine(Path.GetDirectoryName(full), Path.GetFileNameWithoutExtension(full) + "-live.png"));
            }
            catch (Exception ex) { NtfNative.Log("preview", ex); }
        }

        static void Render(string dest, string[] themes)
        {
            float s = 1f; var items = NtfDemo.Samples(DateTime.Now).Take(3).ToArray();
            int colW = (int)(NtfDraw.W * s) + 60, W = colW * themes.Length + 20, H = 560;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                Ui.Hq(g); g.Clear(Color.FromArgb(255, 30, 30, 34));
                for (int i = 0; i < themes.Length; i++)
                {
                    var th = Theme.Make(themes[i], AppConfig.Current.Accent); var keep = Theme.Current; Theme.Current = th;
                    var area = new Rectangle(10 + i * colW, 40, colW - 10, H - 50);
                    Ui.Text(g, th.Name, Ui.Font(15, true), Color.FromArgb(235, 235, 240), new RectangleF(area.X + 6, 8, area.Width, 26));
                    Backdrop(g, th, area);
                    // bar strip at the bottom
                    var bar = new Rectangle(area.X + 10, area.Bottom - 54, area.Width - 20, 44);
                    if (th.IsGlass) Ui.FillRound(g, bar, 8, Tint(th));
                    Ui.FillRound(g, bar, 8, th.BarBg); Ui.StrokeRound(g, bar, 8, th.BarBorder);
                    int y = bar.Top - 10;
                    for (int k = 0; k < items.Length; k++)
                    {
                        var sz = NtfDraw.ToastSize(g, items[k], s);
                        var r = new Rectangle(area.Right - 12 - sz.Width, y - sz.Height, sz.Width, sz.Height);
                        if (th.IsGlass) Ui.FillRound(g, r, 8, Tint(th));
                        NtfDraw.PaintBackground(g, th, r, 8);
                        bool hover = k == 1;
                        NtfDraw.PaintToast(g, th, r, items[k], s, hover, hover ? new Point(r.Right - 20, r.Y + 20) : new Point(-1, -1));
                        y = r.Top - 10;
                    }
                    Theme.Current = keep;
                }
                bmp.Save(dest, ImageFormat.Png);
            }
        }
        static Color Tint(Theme th) { return th.Dark ? Color.FromArgb(150, 32, 32, 36) : Color.FromArgb(150, 238, 238, 240); }
        static void Backdrop(Graphics g, Theme th, Rectangle r)
        {
            if (!th.IsGlass) { Ui.FillRound(g, r, 10, th.Dark ? Color.FromArgb(255, 72, 74, 80) : Color.FromArgb(255, 205, 207, 212)); return; }
            using (var path = Ui.Round(r, 10))
            {
                var clip = g.Clip; g.SetClip(path);
                using (var lg = new LinearGradientBrush(r, Color.FromArgb(255, 40, 70, 160), Color.FromArgb(255, 200, 70, 120), 60f)) g.FillRectangle(lg, r);
                var blobs = new[] { new PointF(0.2f, 0.25f), new PointF(0.8f, 0.45f), new PointF(0.35f, 0.8f) };
                var cols = new[] { Color.FromArgb(255, 255, 170, 60), Color.FromArgb(255, 60, 220, 200), Color.FromArgb(255, 130, 80, 255) };
                for (int i = 0; i < blobs.Length; i++)
                {
                    float rad = r.Width * 0.55f;
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(r.X + blobs[i].X * r.Width - rad, r.Y + blobs[i].Y * r.Height - rad, rad * 2, rad * 2);
                        using (var pb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb(210, cols[i]), SurroundColors = new[] { Color.FromArgb(0, cols[i]) } }) g.FillPath(pb, gp);
                    }
                }
                g.Clip = clip; clip.Dispose();
            }
        }

        /// <summary>Show the demo toasts for real, screenshot them after the slide-in, then close everything.</summary>
        static void Live(string dest)
        {
            foreach (var it in NtfDemo.Samples(DateTime.Now).Take(3).Reverse()) NtfToasts.Show(it);
            var t = new Timer { Interval = 1200 };
            t.Tick += (o, e) =>
            {
                t.Stop(); t.Dispose();
                try
                {
                    var r = NtfToasts.StackBounds(); r.Inflate(24, 24);
                    NtfNative.Log("live stack " + r + " " + NtfToasts.Describe(), null);
                    using (var bmp = new Bitmap(r.Width, r.Height))
                    using (var g = Graphics.FromImage(bmp)) { g.CopyFromScreen(r.Location, Point.Empty, r.Size); bmp.Save(dest, ImageFormat.Png); }
                }
                catch (Exception ex) { NtfNative.Log("live shot", ex); }
                NtfToasts.CloseAll();
            };
            t.Start();
        }
    }
}
