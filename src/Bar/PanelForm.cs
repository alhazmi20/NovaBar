// Popup card for a widget panel. Activates (keyboard input), closes on deactivate / Esc / tile click.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NovaBar
{
    class PanelForm : Form
    {
        readonly BarForm bar;
        readonly BarTile tile;
        readonly Widget w;
        readonly BarBuffer buf = new BarBuffer();
        readonly DrawCtx ctx = new DrawCtx();
        readonly Dictionary<string, PanelInput> inputs = new Dictionary<string, PanelInput>();
        readonly HashSet<string> seen = new HashSet<string>();
        readonly List<string> stale = new List<string>();
        readonly Action<InputSpec> sink;
        Point mouse = new Point(-1, -1);
        int hoverHit = -1;
        Size cur;
        bool closed, focusedFirst;
        // ---- shell style: fused to the bar, slides out of it ----
        bool shell { get { return AppConfig.Current.IsShell && !bar.Vertical; } }
        readonly FilletPair joins = new FilletPair();
        Rectangle finalB; bool anim; int animStart; Timer animTimer;
        const int AnimMs = 190;
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
        [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);

        class PanelInput { public TextBox Box; public InputSpec Spec; public string LastInitial; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        const int EM_SETCUEBANNER = 0x1501;

        public PanelForm(BarForm bar, BarTile tile)
        {
            this.bar = bar; this.tile = tile; w = tile.W;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; TopMost = true; KeyPreview = true; Text = w.Meta.Name; MinimumSize = new Size(1, 1);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            sink = OnInput;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                if (!shell) cp.ClassStyle |= 0x20000; // CS_DROPSHADOW (shell panels are fused to the bar: no shadow)
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            BarShell.ApplyBackdrop(Handle, bar.T, true, false);
        }

        public void ShowPanel()
        {
            Reposition();
            CreateHandle();
            if (shell)
            {   // start collapsed against the bar, then grow out of it
                anim = true; animStart = Environment.TickCount;
                ApplyAnimFrame(0.0);
                animTimer = new Timer { Interval = 8 };
                animTimer.Tick += (o, e) =>
                {
                    double t = Math.Min(1.0, (Environment.TickCount - animStart) / (double)AnimMs);
                    ApplyAnimFrame(t);
                    if (t >= 1.0) { animTimer.Stop(); anim = false; Bounds = finalB; ShapeShell(finalB.Size); Render(); }
                };
            }
            Render();
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            if (animTimer != null) animTimer.Start();
        }

        /// <summary>Animation frame t∈[0,1]: ease-out (back-ish overshoot-free cubic), height grows away from the bar.</summary>
        void ApplyAnimFrame(double t)
        {
            double e = 1 - Math.Pow(1 - t, 3);
            int h = Math.Max(1, (int)Math.Round(finalB.Height * e));
            var b = bar.IsTop ? new Rectangle(finalB.X, finalB.Y, finalB.Width, h) : new Rectangle(finalB.X, finalB.Bottom - h, finalB.Width, h);
            Bounds = b;
            ShapeShell(b.Size);
            if (IsHandleCreated && buf.W > 0) BlitAnim();
        }
        void BlitAnim()
        {
            // content stays glued to the leading (far-from-bar) edge, so it slides out of the bar
            var dc = BarNative.GetDC(Handle);
            try { buf.BlitFrom(dc, ClientSize.Width, ClientSize.Height, 0, bar.IsTop ? buf.H - ClientSize.Height : 0); }
            finally { BarNative.ReleaseDC(Handle, dc); }
        }
        /// <summary>Round only the corners away from the bar; the side touching the bar stays square.</summary>
        void ShapeShell(Size s)
        {
            if (!shell || !IsHandleCreated) return;
            int d = (int)Math.Round(2 * 12 * bar.M.Dpi);
            var rr = CreateRoundRectRgn(0, 0, s.Width + 1, s.Height + 1, d, d);
            var sq = bar.IsTop ? CreateRectRgn(0, 0, s.Width, Math.Min(s.Height, d)) : CreateRectRgn(0, Math.Max(0, s.Height - d), s.Width, s.Height);
            CombineRgn(rr, rr, sq, 2 /*RGN_OR*/); Native.DeleteObject(sq);
            SetWindowRgn(Handle, rr, true);
        }

        void Reposition()
        {
            var m = bar.M;
            var sz = BarRenderer.SafePanelSize(w, m.Scale);
            if (sz.Width < 40) sz.Width = 40; if (sz.Height < 40) sz.Height = 40;
            cur = sz;
            var tileScr = bar.RectangleToScreen(tile.R);
            var barR = bar.BarScreenRect;
            var mon = bar.Scr.Bounds;
            int gap = shell ? 0 : (int)Math.Round(8 * m.Scale), edge = shell ? 0 : (int)Math.Round(8 * m.Scale);
            int x, y;
            if (bar.Vertical)
            {   // beside the sidebar, vertically centred on the tile
                x = bar.LeftEdge ? barR.Right + gap : barR.Left - gap - sz.Width;
                y = tileScr.Y + Math.Min(tileScr.Height, m.TileH) / 2 - sz.Height / 2;
            }
            else
            {
                x = tileScr.X + tileScr.Width / 2 - sz.Width / 2;
                y = bar.IsTop ? barR.Bottom + gap : barR.Top - gap - sz.Height;
            }
            x = Math.Max(mon.Left + edge, Math.Min(mon.Right - edge - sz.Width, x));
            y = Math.Max(mon.Top + edge, Math.Min(mon.Bottom - edge - sz.Height, y));
            finalB = new Rectangle(x, y, sz.Width, sz.Height);
            if (!anim) Bounds = finalB;
            if (shell)
            {
                if (!anim) ShapeShell(finalB.Size);
                int R = bar.FilletR;
                // concave joins between the panel's sides and the bar (skipped where the panel touches a screen edge)
                Rectangle l = x - R >= mon.Left ? new Rectangle(x - R, bar.IsTop ? barR.Bottom : barR.Top - R, R, R) : Rectangle.Empty;
                Rectangle r = x + sz.Width + R <= mon.Right ? new Rectangle(x + sz.Width, bar.IsTop ? barR.Bottom : barR.Top - R, R, R) : Rectangle.Empty;
                if (bar.IsTop) joins.Set(l, 0, R, r, R, R, bar.T.ShellBg);
                else joins.Set(l, 0, 0, r, R, 0, bar.T.ShellBg);
            }
        }

        internal void Render()
        {
            if (closed || IsDisposed) return;
            var sz = BarRenderer.SafePanelSize(w, bar.M.Scale);
            if (!sz.IsEmpty && sz != cur) Reposition();
            int cw = anim ? cur.Width : ClientSize.Width, ch = anim ? cur.Height : ClientSize.Height;
            var g = buf.Begin(cw, ch);
            var r = new Rectangle(0, 0, cw, ch);
            BarRenderer.PaintCard(g, bar.T, r, 0);
            seen.Clear();
            BarRenderer.PaintPanel(g, bar.T, bar.M, w, ctx, r, r.Contains(mouse), mouse, sink);
            // drop inputs that were not declared this frame
            stale.Clear();
            foreach (var k in inputs.Keys) if (!seen.Contains(k)) stale.Add(k);
            foreach (var k in stale) { var pi = inputs[k]; inputs.Remove(k); Controls.Remove(pi.Box); pi.Box.Dispose(); }
            if (IsHandleCreated) { if (anim) BlitAnim(); else buf.BlitTo(Handle); }
        }

        void OnInput(InputSpec spec)
        {
            if (spec == null || spec.Key == null) return;
            seen.Add(spec.Key);
            var t = bar.T; float s = bar.M.Scale;
            var g = buf.G;
            // host-painted field background (rounded); the real TextBox sits inside it
            if (g != null) Ui.FillRound(g, spec.Rect, Math.Min(spec.Rect.Height / 2f, 8 * s), t.Input);
            PanelInput pi;
            if (!inputs.TryGetValue(spec.Key, out pi))
            {
                pi = new PanelInput { Box = new TextBox { BorderStyle = BorderStyle.None, Multiline = spec.Multiline, AcceptsReturn = spec.Multiline, WordWrap = true } };
                pi.Spec = spec;
                if (spec.Multiline) pi.Box.ScrollBars = ScrollBars.None;
                var box = pi.Box; var holder = pi;
                box.KeyDown += (o, e) =>
                {
                    if (e.KeyCode == Keys.Enter && (!holder.Spec.Multiline || e.Control))
                    {
                        e.SuppressKeyPress = true; e.Handled = true;
                        var text = box.Text;
                        // run the submit with the typed text BEFORE clearing: clearing fires TextChanged → onChange(""),
                        // which made e.g. Commands recompute an empty-query list and launch its first suggestion (Lock).
                        try { if (holder.Spec.OnSubmit != null) holder.Spec.OnSubmit(text); } catch (Exception ex) { BarShell.Log(w.Meta.Id + " input submit", ex); }
                        if (!holder.Spec.Multiline && !box.IsDisposed) box.Text = "";
                        bar.Invalidate(w);
                    }
                };
                box.TextChanged += (o, e) =>
                {
                    try { if (holder.Spec.OnChange != null) holder.Spec.OnChange(box.Text); } catch (Exception ex) { BarShell.Log(w.Meta.Id + " input change", ex); }
                    bar.Invalidate(w);
                };
                inputs[spec.Key] = pi;
                Controls.Add(box);
                if (spec.Initial != null) box.Text = spec.Initial;
                pi.LastInitial = spec.Initial;
                if (!string.IsNullOrEmpty(spec.Placeholder) && !spec.Multiline) SendMessage(box.Handle, EM_SETCUEBANNER, new IntPtr(1), spec.Placeholder);
                if (t.IsGlass)
                {
                    // layered child = its own opaque DWM surface, so GDI text isn't rendered see-through on the glass frame
                    int ex = Native.GetWindowLong(box.Handle, Native.GWL_EXSTYLE);
                    Native.SetWindowLong(box.Handle, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
                    BarNative.SetLayeredWindowAttributes(box.Handle, 0, 255, BarNative.LWA_ALPHA);
                }
                if (!focusedFirst) { focusedFirst = true; ActiveControl = box; }
            }
            pi.Spec = spec;
            // a changed Initial value lets widgets set/clear the text programmatically
            if (spec.Initial != pi.LastInitial) { pi.LastInitial = spec.Initial; if (spec.Initial != null && spec.Initial != pi.Box.Text) pi.Box.Text = spec.Initial; }
            var font = Ui.Font(12.5f * s);
            if (pi.Box.Font != font) pi.Box.Font = font;
            if (pi.Box.BackColor != t.Input) pi.Box.BackColor = t.Input;
            if (pi.Box.ForeColor != t.Text) pi.Box.ForeColor = t.Text;
            int pad = (int)Math.Round(8 * s);
            Rectangle br;
            if (spec.Multiline) br = new Rectangle(spec.Rect.X + pad, spec.Rect.Y + pad / 2 + 2, spec.Rect.Width - 2 * pad, spec.Rect.Height - pad - 4);
            else { int h = pi.Box.PreferredHeight; br = new Rectangle(spec.Rect.X + pad, spec.Rect.Y + (spec.Rect.Height - h) / 2, spec.Rect.Width - 2 * pad, h); }
            if (pi.Box.Bounds != br) pi.Box.Bounds = br;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (anim) { BlitAnim(); return; }
            if (buf.W != ClientSize.Width || buf.H != ClientSize.Height) { Render(); return; }
            var hdc = e.Graphics.GetHdc();
            try { buf.Blit(hdc, ClientSize.Width, ClientSize.Height); } finally { e.Graphics.ReleaseHdc(hdc); }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!closed) BeginInvoke((Action)(() => { if (!closed && !IsDisposed) Close(); }));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
            base.OnKeyDown(e);
        }

        static int HitIndex(DrawCtx c, Point p) { for (int i = c.Hits.Count - 1; i >= 0; i--) if (c.Hits[i].Key.Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool wasIn = ClientRectangle.Contains(mouse);
            mouse = e.Location;
            int hh = HitIndex(ctx, mouse);
            if (hh != hoverHit || !wasIn) { hoverHit = hh; bar.Invalidate(w); }
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            mouse = new Point(-1, -1); hoverHit = -1; bar.Invalidate(w);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var a = BarRenderer.HitAt(ctx, e.Location);
            if (a == null) return;
            try { a(); } catch (Exception ex) { BarShell.Log(w.Meta.Id + " panel click", ex); }
            if (!closed) bar.Invalidate(w);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var a = BarRenderer.WheelAt(ctx, e.Location);
            if (a == null) return;
            try { a(e.Delta > 0 ? 1 : -1); } catch (Exception ex) { BarShell.Log(w.Meta.Id + " panel wheel", ex); }
            bar.Invalidate(w);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closed = true;
            if (animTimer != null) { animTimer.Stop(); animTimer.Dispose(); animTimer = null; }
            joins.Dispose();
            base.OnFormClosed(e);
            bar.OnPanelClosed(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { buf.Dispose(); foreach (var pi in inputs.Values) pi.Box.Dispose(); inputs.Clear(); }
            base.Dispose(disposing);
        }
    }
}
