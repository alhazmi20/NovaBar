// Task-list hover previews: live DWM thumbnails of every window of the hovered app group.
// DWM composites the thumbnails (zero CPU for us). A 100 ms WinForms timer runs ONLY while a preview is pending or open.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>Hover state machine shared by every Apps widget instance (one preview popup at a time).</summary>
    public static class CtlThumbs
    {
        const int OpenDelayMs = 400, CloseDelayMs = 300;

        static Widget owner;                 // widget that reported the hover
        static string key, suppressKey;      // hovered group / group whose preview was dismissed by a click
        static Rectangle icon, tile;         // screen rects of the hovered icon and its tile
        static float scale = 1f;
        static Func<List<CtlWin>> provider;
        static long pendingSince;
        static long lastInside;
        static CtlThumbForm popup;
        static Timer timer;
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static int iconRetries;

        public static bool IsOpen { get { return popup != null; } }
        /// <summary>"left" / "right" when the bar is docked to a vertical screen edge (popups open beside it), else null.</summary>
        public static string BarSide { get { return BarSideAt(System.Windows.Forms.Cursor.Position); } }
        /// <summary>Side of the vertical bar on the display containing screen point p (positions are per display), else null.</summary>
        public static string BarSideAt(Point p)
        {
            var cfg = AppConfig.Current; if (cfg == null) return null;
            string pos; try { pos = cfg.PositionFor(System.Windows.Forms.Screen.FromPoint(p).DeviceName); } catch { pos = cfg.Position; }
            return pos == "left" || pos == "right" ? pos : null;
        }
        /// <summary>Top-left of a popup of size <paramref name="sz"/> beside a vertical bar: right of the tile for a left bar,
        /// left of it for a right bar, vertically centred on <paramref name="item"/>, clamped to the monitor's work area.</summary>
        public static Point BesideBar(string side, Rectangle item, Rectangle tile, Size sz, int gap, int edge)
        {
            var wa = Screen.FromRectangle(tile).WorkingArea;
            int x = side == "left" ? tile.Right + gap : tile.Left - gap - sz.Width;
            int y = item.Y + item.Height / 2 - sz.Height / 2;
            y = Math.Max(wa.Top + edge, Math.Min(wa.Bottom - edge - sz.Height, y));
            return new Point(x, y);
        }

        /// <summary>Called from the widget's Draw while the mouse is over a group that has windows.</summary>
        public static void Hover(Widget w, string groupKey, Rectangle iconScreen, Rectangle tileScreen, float s, Func<List<CtlWin>> wins)
        {
            if (!iconScreen.Contains(Cursor.Position)) return;   // stale c.Mouse (the bar repainted after the mouse left)
            if (groupKey == suppressKey)
            {   // dismissed by a click: remember the hover (so a click can reopen it) but don't auto-open
                if (popup == null) { owner = w; key = groupKey; icon = iconScreen; tile = tileScreen; scale = s; provider = wins; pendingSince = 0; }
                return;
            }
            suppressKey = null;
            bool same = groupKey == key && w == owner;
            owner = w; icon = iconScreen; tile = tileScreen; scale = s; provider = wins;
            if (popup != null)
            {
                lastInside = clock.ElapsedMilliseconds;
                if (!same) { key = groupKey; var k = groupKey; w.Host.Post(() => { if (popup != null && key == k) Show(); }); }
                return;
            }
            if (!same || pendingSince == 0) { key = groupKey; pendingSince = clock.ElapsedMilliseconds; }
            EnsureTimer();
        }
        /// <summary>Called from Draw when the mouse is not over any group with windows.</summary>
        public static void NoHover(Widget w) { if (w == owner || owner == null) suppressKey = null; }
        /// <summary>A click on the icon (or a menu) dismisses the preview until the mouse moves to another icon.</summary>
        public static void Dismiss(Widget w) { if (owner != null && w != owner) return; suppressKey = key; Close(); }
        public static void DismissFromPopup() { suppressKey = key; Close(); }
        /// <summary>Click on a multi-window group: open its preview right away (or close it if it's already showing).</summary>
        public static bool ToggleNow(Widget w, string groupKey)
        {
            if (owner != w || key != groupKey) return false;          // no hover registered for this group
            if (popup != null) { DismissFromPopup(); return true; }
            suppressKey = null; pendingSince = 0; lastInside = clock.ElapsedMilliseconds;
            Show(); EnsureTimer();
            return true;
        }
        public static void Stop(Widget w) { if (w == owner) { Close(); owner = null; } }

        public static void Close()
        {
            pendingSince = 0; key = null;
            if (timer != null) timer.Stop();
            var p = popup; popup = null;
            if (p != null) { try { p.Close(); p.Dispose(); } catch { } }
        }

        static void EnsureTimer()
        {
            if (timer == null) { timer = new Timer { Interval = 100 }; timer.Tick += (o, e) => { try { Tick(); } catch { Close(); } }; }
            if (!timer.Enabled) timer.Start();
        }

        static bool AnyPanelOpen()
        {
            foreach (Form f in Application.OpenForms) if (f.Visible && f.GetType().Name == "PanelForm") return true;
            return false;
        }

        static void Show()
        {
            var wins = provider != null ? provider() : null;
            if (wins == null || wins.Count == 0) { Close(); return; }
            if (popup == null || popup.IsDisposed) { popup = new CtlThumbForm(Theme.Current, scale, owner); iconRetries = 0; }
            var scr = Screen.FromRectangle(tile).Bounds;
            bool top = tile.Y + tile.Height / 2 < scr.Y + scr.Height / 2;
            popup.SetWins(wins, Theme.Current, scale);
            popup.Place(icon, tile, top, BarSideAt(new Point(tile.X + tile.Width / 2, tile.Y + tile.Height / 2)));
            lastInside = clock.ElapsedMilliseconds;
            EnsureTimer();
        }

        static void Tick()
        {
            long now = clock.ElapsedMilliseconds;
            var p = Cursor.Position;
            if (AnyPanelOpen()) { Close(); return; }
            if (popup == null)
            {
                if (key == null || !icon.Contains(p)) { pendingSince = 0; key = null; timer.Stop(); return; }
                if (now - pendingSince >= OpenDelayMs && Control.MouseButtons == MouseButtons.None) Show();
                return;
            }
            var wins = provider != null ? provider() : null;
            if (wins == null || wins.Count == 0) { Close(); return; }
            if (!ReferenceEquals(wins, popup.Wins)) Show();
            if (popup == null) return;
            var pb = popup.Bounds;
            // bridge between the icon and the popup so crossing the gap doesn't count as leaving
            var bridge = popup.Side == null ? Rectangle.FromLTRB(icon.Left, Math.Min(pb.Bottom, icon.Bottom), icon.Right, Math.Max(pb.Top, icon.Top))
                : Rectangle.FromLTRB(Math.Min(pb.Right, icon.Right), icon.Top, Math.Max(pb.Left, icon.Left), icon.Bottom);   // vertical bar: sideways gap
            bool inside = icon.Contains(p) || pb.Contains(p) || bridge.Contains(p);
            if (inside) lastInside = now;
            else if (Control.MouseButtons != MouseButtons.None || now - lastInside > CloseDelayMs) { Close(); return; }
            if (popup.NeedsIcons && iconRetries++ < 20) popup.Render();
        }

        // ---------------- debug render hook (NOVABAR_THUMB_TEST=<exe>, render mode only) ----------------
        static readonly HashSet<string> tested = new HashSet<string>();
        static readonly List<KeyValuePair<string, Bitmap>> shots = new List<KeyValuePair<string, Bitmap>>();
        static string testExe; static bool testInit;
        public static string TestExe { get { if (!testInit) { testInit = true; testExe = ReadTestExe(); } return testExe; } }
        static string ReadTestExe()
        {
            var v = Environment.GetEnvironmentVariable("NOVABAR_THUMB_TEST");
            if (string.IsNullOrEmpty(v)) return null;
            return Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--render", StringComparison.OrdinalIgnoreCase)) ? v : null;
        }
        public static void RunTest(Widget w, Theme t, float s, List<CtlWin> wins)
        {
            if (!tested.Add(t.Id)) return;
            var args = Environment.GetCommandLineArgs();
            string png = null;
            for (int i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], "--render", StringComparison.OrdinalIgnoreCase)) png = args[i + 1];
            if (png == null) return;
            var f = new CtlThumbForm(t, s, w);
            try
            {
                f.SetWins(wins, t, s);
                f.TestHover = 0;
                var wa = Screen.PrimaryScreen.WorkingArea; int m = (int)(16 * s);
                f.PlaceAt(new Point(wa.Right - f.Width - m, wa.Bottom - f.Height - m));
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 2000) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                var r = f.Bounds; r.Inflate(m, m);
                var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                shots.Add(new KeyValuePair<string, Bitmap>(t.Name, bmp));
            }
            finally { f.Close(); f.Dispose(); }
            // stitch every theme captured so far into <png>-thumbs.png
            int lbl = 26, W = Math.Max(400, shots.Max(x => x.Value.Width)) + 20, H = shots.Sum(x => x.Value.Height + lbl + 10) + 10;
            using (var all = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(all))
            {
                Ui.Hq(g); g.Clear(Color.FromArgb(255, 30, 30, 34)); int y = 10;
                foreach (var kv in shots)
                {
                    Ui.Text(g, kv.Key + "  (" + wins.Count + (wins.Count == 1 ? " window)" : " windows)"), Ui.Font(14, true), Color.White, new RectangleF(10, y, W, lbl - 4));
                    g.DrawImageUnscaled(kv.Value, 10, y + lbl); y += kv.Value.Height + lbl + 10;
                }
                var full = Path.GetFullPath(png);
                var outp = Path.Combine(Path.GetDirectoryName(full), Path.GetFileNameWithoutExtension(full) + "-thumbs.png");
                all.Save(outp, ImageFormat.Png);
            }
        }
    }

    /// <summary>The preview popup: borderless, topmost, never activated. Our GDI+ paints cards/titles; DWM paints the live images.</summary>
    public class CtlThumbForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        struct THUMBPROPS
        {
            public int dwFlags; public Native.RECT rcDestination, rcSource; public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }
        [StructLayout(LayoutKind.Sequential)] struct PSIZE { public int cx, cy; }
        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMBPROPS p);
        [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        const int F_DEST = 1, F_SOURCE = 2, F_OPACITY = 4, F_VISIBLE = 8, F_CLIENTONLY = 0x10;

        class Card
        {
            public CtlWin W; public IntPtr Thumb; public Size Src; public Rectangle Src2; public bool Crop;
            public Rectangle R, Head, Img, Dest, X;
            public bool Visible;
        }

        Theme t; float s; readonly Widget owner;
        readonly List<Card> cards = new List<Card>();
        readonly BarBuffer buf = new BarBuffer();
        public List<CtlWin> Wins;
        int hover = -1, firstRow, cols = 1, rows = 1, visRows = 1;
        bool hoverX;
        public int TestHover = -1;
        Rectangle anchorIcon, anchorTile; bool anchorTop, anchored;
        string anchorSide;                  // "left"/"right" = beside a vertical bar, null = above/below
        public string Side { get { return anchorSide; } }

        public CtlThumbForm(Theme theme, float scale, Widget owner)
        {
            t = theme; s = scale; this.owner = owner;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; TopMost = true; MinimumSize = new Size(1, 1); Text = "NovaBar preview";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                if (t == null || !t.IsGlass) cp.ClassStyle |= 0x20000; // CS_DROPSHADOW (rectangular: not with the rounded glass region)
                return cp;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyLook();
        }
        // Glass themes use the same acrylic accent as the bar (it keeps blurring while the window is inactive, which this popup
        // always is). The accent ignores DWM corner rounding, so glass gets a rounded window region instead.
        bool Regioned { get { return t.IsGlass && BarShell.BackdropMode(true) == "accent"; } }
        void ApplyLook()
        {
            Native.SetDwm(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, t.Dark ? 1 : 0);
            Native.SetDwm(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
            if (t.IsGlass) { try { BarShell.ApplyBackdrop(Handle, t, true, true); } catch { } }
            else
            {
                // let DWM draw the 1px rounded border (COLORREF = 0x00BBGGRR, theme border flattened onto the card colour)
                var bc = Ui.Mix(t.PanelBg, Color.FromArgb(255, t.BarBorder), t.BarBorder.A / 255.0);
                Native.SetDwm(Handle, Native.DWMWA_BORDER_COLOR, bc.R | (bc.G << 8) | (bc.B << 16));
            }
            lastRgn = Size.Empty; UpdateRegion();
        }
        Size lastRgn;
        void UpdateRegion()
        {
            if (!IsHandleCreated) return;
            if (!Regioned) { if (!lastRgn.IsEmpty) { SetWindowRgn(Handle, IntPtr.Zero, true); lastRgn = Size.Empty; } return; }
            if (lastRgn == Size) return;
            lastRgn = Size; int d = S(16);
            SetWindowRgn(Handle, CreateRoundRectRgn(0, 0, Width + 1, Height + 1, d, d), true);   // the window owns the region afterwards
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateRegion(); }

        int S(float px) { return (int)Math.Round(px * s); }
        int CardW { get { return S(208); } }
        int HeadH { get { return S(32); } }
        int ImgH { get { return S(118); } }
        int CardH { get { return HeadH + ImgH + S(8); } }
        int Pad { get { return S(8); } }
        int Gap { get { return S(6); } }

        // ---------------- content ----------------
        public void SetWins(List<CtlWin> wins, Theme theme, float scale)
        {
            if (theme != t) { t = theme; if (IsHandleCreated) ApplyLook(); }
            s = scale; Wins = wins;
            if (!IsHandleCreated) CreateHandle();
            var old = cards.ToDictionary(c => c.W.Hwnd, c => c);
            var keep = new HashSet<IntPtr>(wins.Select(w => w.Hwnd));
            foreach (var c in cards) if (!keep.Contains(c.W.Hwnd) && c.Thumb != IntPtr.Zero) { DwmUnregisterThumbnail(c.Thumb); c.Thumb = IntPtr.Zero; }
            cards.Clear();
            foreach (var w in wins)
            {
                Card c;
                if (old.TryGetValue(w.Hwnd, out c) && c.Thumb != IntPtr.Zero) c.W = w;
                else
                {
                    c = new Card { W = w };
                    IntPtr th; if (DwmRegisterThumbnail(Handle, w.Hwnd, out th) == 0) c.Thumb = th;
                }
                QuerySource(c);
                cards.Add(c);
            }
            if (hover >= cards.Count) hover = -1;
            Relayout();
        }

        void QuerySource(Card c)
        {
            c.Src = Size.Empty; c.Crop = false;
            if (c.Thumb == IntPtr.Zero) return;
            PSIZE ps; if (DwmQueryThumbnailSourceSize(c.Thumb, out ps) == 0) c.Src = new Size(ps.cx, ps.cy);
            // crop the invisible resize borders (Win10/11 windows are 7px bigger than their visible frame)
            if (!Native.IsIconic(c.W.Hwnd))
            {
                Native.RECT wr, fr;
                if (Native.GetWindowRect(c.W.Hwnd, out wr) && CtlNative.DwmGetWindowAttribute(c.W.Hwnd, 9 /*EXTENDED_FRAME_BOUNDS*/, out fr, Marshal.SizeOf(typeof(Native.RECT))) == 0)
                {
                    var src = Rectangle.FromLTRB(fr.Left - wr.Left, fr.Top - wr.Top, fr.Right - wr.Left, fr.Bottom - wr.Top);
                    if (src.Width > 20 && src.Height > 20 && src.Width <= c.Src.Width + 2 && src.Height <= c.Src.Height + 2)
                    { c.Src2 = Rectangle.Intersect(src, new Rectangle(0, 0, c.Src.Width, c.Src.Height)); c.Crop = true; }
                }
            }
        }
        bool HasImage(Card c)
        {
            if (c.Thumb == IntPtr.Zero) return false;
            var sz = c.Crop ? c.Src2.Size : c.Src;
            return sz.Width >= 40 && sz.Height >= 24;   // minimized windows without a cached frame report a tiny caption-only size
        }

        void Relayout()
        {
            var scr = anchored ? Screen.FromRectangle(anchorTile) : Screen.PrimaryScreen;
            int margin = S(12);
            int maxW = scr.Bounds.Width - 2 * margin, maxH = (int)(scr.WorkingArea.Height * 0.7);
            int n = Math.Max(1, cards.Count);
            if (anchored && anchorSide != null)
            {   // beside a vertical bar: stack cards top→bottom, add columns only when the screen height runs out
                maxW = scr.Bounds.Width - anchorTile.Width - 3 * margin; maxH = scr.WorkingArea.Height - 2 * margin;
                int maxColsV = Math.Max(1, (maxW - 2 * Pad + Gap) / (CardW + Gap));
                int fitRows = Math.Max(1, (maxH - 2 * Pad + Gap) / (CardH + Gap));
                cols = Math.Min(maxColsV, (n + fitRows - 1) / fitRows); rows = (n + cols - 1) / cols;
            }
            else
            {
            int maxCols = Math.Max(1, (maxW - 2 * Pad + Gap) / (CardW + Gap));
            cols = Math.Min(n, maxCols); rows = (n + cols - 1) / cols;
            }
            visRows = Math.Max(1, Math.Min(rows, (maxH - 2 * Pad + Gap) / (CardH + Gap)));
            firstRow = Math.Max(0, Math.Min(firstRow, rows - visRows));
            int sb = rows > visRows ? S(8) : 0;
            int w = 2 * Pad + cols * CardW + (cols - 1) * Gap + sb, h = 2 * Pad + visRows * CardH + (visRows - 1) * Gap;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i]; int row = i / cols, col = i % cols;
                c.Visible = row >= firstRow && row < firstRow + visRows;
                int x = Pad + col * (CardW + Gap), y = Pad + (row - firstRow) * (CardH + Gap);
                c.R = new Rectangle(x, y, CardW, CardH);
                c.Head = new Rectangle(x, y, CardW, HeadH);
                c.Img = new Rectangle(x + S(8), y + HeadH, CardW - S(16), ImgH);
                int xs = S(24); c.X = new Rectangle(c.R.Right - xs - S(5), y + (HeadH - xs) / 2, xs, xs);
                c.Dest = Rectangle.Empty;
                if (HasImage(c))
                {
                    var sz = c.Crop ? c.Src2.Size : c.Src;
                    double k = Math.Min((double)c.Img.Width / sz.Width, (double)c.Img.Height / sz.Height);
                    int dw = Math.Max(1, (int)Math.Round(sz.Width * k)), dh = Math.Max(1, (int)Math.Round(sz.Height * k));
                    c.Dest = new Rectangle(c.Img.X + (c.Img.Width - dw) / 2, c.Img.Y + (c.Img.Height - dh) / 2, dw, dh);
                }
                UpdateThumb(c);
            }
            if (Width != w || Height != h) Size = new Size(w, h);
            if (anchored) Reposition();
            Render();
        }

        void UpdateThumb(Card c)
        {
            if (c.Thumb == IntPtr.Zero) return;
            var p = new THUMBPROPS { dwFlags = F_DEST | F_VISIBLE | F_OPACITY | F_CLIENTONLY, opacity = 255, fSourceClientAreaOnly = false };
            p.fVisible = c.Visible && !c.Dest.IsEmpty;
            var d = c.Dest.IsEmpty ? new Rectangle(0, 0, 1, 1) : c.Dest;
            p.rcDestination = new Native.RECT { Left = d.Left, Top = d.Top, Right = d.Right, Bottom = d.Bottom };
            if (c.Crop) { p.dwFlags |= F_SOURCE; p.rcSource = new Native.RECT { Left = c.Src2.Left, Top = c.Src2.Top, Right = c.Src2.Right, Bottom = c.Src2.Bottom }; }
            DwmUpdateThumbnailProperties(c.Thumb, ref p);
        }

        // ---------------- placement ----------------
        public void Place(Rectangle iconScr, Rectangle tileScr, bool barTop) { Place(iconScr, tileScr, barTop, null); }
        public void Place(Rectangle iconScr, Rectangle tileScr, bool barTop, string side)
        {
            bool sideChanged = side != anchorSide;
            anchorIcon = iconScr; anchorTile = tileScr; anchorTop = barTop; anchorSide = side;
            if (!anchored || sideChanged) { anchored = true; Relayout(); } else Reposition();
            if (!Visible) { Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Left, Top, Width, Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW); Visible = true; }
        }
        void Reposition()
        {
            var mon = Screen.FromRectangle(anchorTile).Bounds;
            int gap = S(10), edge = S(8);
            if (anchorSide != null)
            {
                var pt = CtlThumbs.BesideBar(anchorSide, anchorIcon, anchorTile, Size, gap + S(5), edge);
                if (Left != pt.X || Top != pt.Y) Location = pt;
                return;
            }
            int x = anchorIcon.X + anchorIcon.Width / 2 - Width / 2;
            x = Math.Max(mon.Left + edge, Math.Min(mon.Right - edge - Width, x));
            int y = anchorTop ? anchorTile.Bottom + gap : anchorTile.Top - gap - Height;
            y = Math.Max(mon.Top + edge, Math.Min(mon.Bottom - edge - Height, y));
            if (Left != x || Top != y) Location = new Point(x, y);
        }
        public void PlaceAt(Point p)
        {
            Location = p;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, p.X, p.Y, Width, Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            Visible = true; Render();
        }

        // ---------------- painting ----------------
        public bool NeedsIcons
        {
            get { return cards.Any(c => c.Visible && !HasImage(c) && BigIcon(c) == null && c.W.Exe != null); }
        }
        Bitmap BigIcon(Card c)
        {
            var exe = c.W.Exe; if (exe == null) return null;
            int px = S(40);
            return CtlIcons.Peek("f:" + exe, px, () => CtlIcons.FromFile(exe, px), owner);
        }

        public void Render()
        {
            if (IsDisposed || !IsHandleCreated) return;
            int cw = ClientSize.Width, ch = ClientSize.Height;
            var g = buf.Begin(cw, ch);
            g.FillRectangle(Ui.B(t.PanelBg), 0, 0, cw, ch);
            if (Regioned) Ui.StrokeRound(g, new RectangleF(0.5f, 0.5f, cw - 1.5f, ch - 1.5f), S(8), t.BarBorder, 1f);
            IntPtr fg = CtlTasks.Fg;
            int hv = hover >= 0 ? hover : TestHover;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i]; if (!c.Visible) continue;
                bool h = i == hv, active = c.W.Hwnd == fg;
                float rad = S(8);
                var bg = h ? t.TileHover : t.Tile;
                Ui.FillRound(g, c.R, rad, bg);
                if (active) Ui.StrokeRound(g, RectangleF.Inflate(c.R, -0.5f, -0.5f), rad, t.AccentA(h ? 200 : 150), Math.Max(1f, s * 1.2f));
                else if (t.IsGlass || !t.Dark) Ui.StrokeRound(g, RectangleF.Inflate(c.R, -0.5f, -0.5f), rad, t.TileBorder, 1f);
                // header: icon + title (+ close button on hover)
                int ip = S(16);
                var ir = new Rectangle(c.Head.X + S(10), c.Head.Y + (c.Head.Height - ip) / 2, ip, ip);
                if (c.W.Icon != null) Ctl.DrawImg(g, c.W.Icon, ir);
                else Ui.Icon(g, "", ir, t.TextDim, S(12));
                float tr = h ? c.X.Left - S(4) : c.Head.Right - S(10);
                Ui.Text(g, c.W.Title, Ui.FontSemi(S(12)), c.W.Min ? t.TextDim : t.Text, new RectangleF(ir.Right + S(8), c.Head.Y, tr - ir.Right - S(8), c.Head.Height));
                if (h)
                {
                    bool xh = hover == i && hoverX;
                    if (xh) Ui.FillRound(g, c.X, S(6), t.Bad);
                    Ui.Icon(g, "", c.X, xh ? Color.White : t.TextDim, S(10));
                }
                // image area
                if (c.Dest.IsEmpty)
                {
                    Ui.FillRound(g, c.Img, S(6), t.Dark ? Color.FromArgb(t.IsGlass ? 40 : 255, 12, 12, 16) : Color.FromArgb(t.IsGlass ? 90 : 255, 236, 236, 241));
                    var big = BigIcon(c) ?? c.W.Icon;
                    int bp = S(40);
                    var br = new Rectangle(c.Img.X + (c.Img.Width - bp) / 2, c.Img.Y + (c.Img.Height - bp) / 2 - S(8), bp, bp);
                    if (big != null) Ctl.DrawImg(g, big, br); else Ui.Icon(g, "", br, t.TextDim, S(28));
                    Ui.Text(g, c.W.Min ? "Minimized" : "No preview", Ui.Font(S(11)), t.TextDim, new RectangleF(c.Img.X, br.Bottom + S(4), c.Img.Width, S(18)), 0);
                }
                else
                {
                    // soft frame behind the live image (shows if the source has transparent edges)
                    Ui.FillRound(g, RectangleF.Inflate(c.Dest, 1, 1), S(3), t.Dark ? Color.FromArgb(t.IsGlass ? 50 : 255, 10, 10, 14) : Color.FromArgb(t.IsGlass ? 70 : 255, 225, 225, 230));
                }
            }
            if (rows > visRows)
            {
                int tw = S(3), x = ClientSize.Width - Pad / 2 - S(4);
                float trackH = ch - 2 * Pad, thumbH = Math.Max(S(16), trackH * visRows / rows), ty = Pad + (trackH - thumbH) * firstRow / Math.Max(1, rows - visRows);
                Ui.FillRound(g, new RectangleF(x, Pad, tw, trackH), tw / 2f, t.Track);
                Ui.FillRound(g, new RectangleF(x, ty, tw, thumbH), tw / 2f, t.TextFaint);
            }
            buf.BlitTo(Handle);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (buf.W != ClientSize.Width || buf.H != ClientSize.Height) { Render(); return; }
            var hdc = e.Graphics.GetHdc();
            try { buf.Blit(hdc, ClientSize.Width, ClientSize.Height); } finally { e.Graphics.ReleaseHdc(hdc); }
        }

        // ---------------- input ----------------
        int CardAt(Point p) { for (int i = 0; i < cards.Count; i++) if (cards[i].Visible && cards[i].R.Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = CardAt(e.Location); bool x = i >= 0 && cards[i].X.Contains(e.Location);
            if (i != hover || x != hoverX) { hover = i; hoverX = x; Render(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover != -1) { hover = -1; hoverX = false; Render(); } }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = CardAt(e.Location); if (i < 0) return;
            var c = cards[i]; var h = c.W.Hwnd;
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && c.X.Contains(e.Location))) { CtlTasks.Close(h); return; }
            if (e.Button == MouseButtons.Left) { CtlTasks.Activate(h); BeginInvoke((Action)CtlThumbs.DismissFromPopup); }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (rows <= visRows) return;
            int nr = Math.Max(0, Math.Min(rows - visRows, firstRow + (e.Delta > 0 ? -1 : 1)));
            if (nr != firstRow) { firstRow = nr; hover = -1; Relayout(); }
        }

        protected override void Dispose(bool disposing)
        {
            foreach (var c in cards) if (c.Thumb != IntPtr.Zero) { try { DwmUnregisterThumbnail(c.Thumb); } catch { } c.Thumb = IntPtr.Zero; }
            cards.Clear();
            if (disposing) buf.Dispose();
            base.Dispose(disposing);
        }
    }
}

