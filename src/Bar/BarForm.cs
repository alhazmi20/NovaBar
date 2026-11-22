// The bar window: frameless, topmost, never activates. Paints via a persistent PARGB DIB back buffer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NovaBar
{
    public class BarForm : Form, IWidgetHost
    {
        public static BarForm Current;

        readonly List<BarTile> tiles = new List<BarTile>();
        readonly Dictionary<BarTile, int> nextTick = new Dictionary<BarTile, int>();
        readonly BarBuffer buf = new BarBuffer();
        Bitmap measureBmp; Graphics measureG;
        internal BarMetrics M = BarMetrics.For("M", 1f);
        internal Theme T = Theme.Current;
        internal Screen Scr = Screen.PrimaryScreen;
        float dpi = 1f;
        Rectangle area;                 // edge area the bar lives in (appbar rect or working area)
        IntPtr hwnd;

        // repaint coalescing
        int pending; volatile bool barDirty = true, panelDirty;
        int lastPaint;
        readonly System.Windows.Forms.Timer paintTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer tickTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer fsTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer fsKick = new System.Windows.Forms.Timer();
        // WinEvent hook: react instantly when the foreground window changes or resizes (e.g. YouTube going fullscreen)
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        WinEventProc fsProc; IntPtr fsHookFg, fsHookLoc; uint fsLocPid;
        /// <summary>Foreground hook is global; the location hook is scoped to the foreground app's process only and
        /// re-targeted on every foreground change, so other apps' window traffic never reaches us.</summary>
        void FsHook(bool on)
        {
            const uint OUT = 0x0000 /*OUTOFCONTEXT*/ | 0x0002 /*SKIPOWNPROCESS*/;
            if (on && fsHookFg == IntPtr.Zero)
            {
                fsProc = (h, ev, w, obj, child, th, t) =>
                {
                    if (closing) return;
                    if (ev == 0x0003) { FsRetargetLoc(); if (!fsKick.Enabled) fsKick.Start(); return; }
                    if (obj != 0 || child != 0 || w != Native.GetForegroundWindow()) return;   // only the foreground top-level window
                    if (!fsKick.Enabled) fsKick.Start();
                };
                fsHookFg = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, fsProc, 0, 0, OUT);   // EVENT_SYSTEM_FOREGROUND
                FsRetargetLoc();
            }
            else if (!on && fsHookFg != IntPtr.Zero)
            {
                UnhookWinEvent(fsHookFg); fsHookFg = IntPtr.Zero;
                if (fsHookLoc != IntPtr.Zero) { UnhookWinEvent(fsHookLoc); fsHookLoc = IntPtr.Zero; }
                fsLocPid = 0; fsKick.Stop();
            }
        }
        void FsRetargetLoc()
        {
            uint pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            if (pid == fsLocPid && fsHookLoc != IntPtr.Zero) return;
            if (fsHookLoc != IntPtr.Zero) { UnhookWinEvent(fsHookLoc); fsHookLoc = IntPtr.Zero; }
            fsLocPid = pid;
            if (pid != 0 && pid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                fsHookLoc = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, fsProc, pid, 0, 0x0000);   // LOCATIONCHANGE, this process only
        }
        readonly System.Windows.Forms.Timer reloadTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer trimTimer = new System.Windows.Forms.Timer();

        // state
        BarTile hover; int hoverHit = -1; Point mouse = new Point(-1, -1);
        bool hiddenFs, locked, appbarReg, closing;
        uint abMsg;
        FileSystemWatcher watcher; static int selfSaveAt = -100000;
        readonly int screenIndex;       // -1 = AppConfig.Monitor; otherwise index into [primary, others...]
        internal readonly bool Owner;   // owner bar: taskbar, startup key, config watcher
        uint tbCreatedMsg;
        readonly System.Windows.Forms.Timer taskbarTimer = new System.Windows.Forms.Timer();
        PanelForm panel; volatile Widget panelWidget;

        // drag
        Point downPt; BarTile downTile; bool downEmpty, dragging, barDrag, widgetDrag; int grabDx;

        public BarForm() : this(-1, true) { }
        public BarForm(int screenIndex, bool owner)
        {
            BarRenderer.AnimHover = true;
            this.screenIndex = screenIndex; Owner = owner;
            if (owner) Current = this;
            taskbarTimer.Interval = 1500; taskbarTimer.Tick += (s, e) => BarShell.EnsureTaskbarHidden();
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; Text = "NovaBar"; MinimumSize = new Size(1, 1);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            Bounds = new Rectangle(-32000, -32000, 10, 10);
            paintTimer.Tick += (s, e) => { paintTimer.Stop(); DoPaint(); };
            tickTimer.Interval = 250; tickTimer.Tick += (s, e) => OnTickTimer();
            fsTimer.Interval = 3000; fsTimer.Tick += (s, e) => CheckFullscreen();          // slow safety net only
            fsKick.Interval = 40; fsKick.Tick += (s, e) => { fsKick.Stop(); CheckFullscreen(true); };  // event-driven, debounced
            reloadTimer.Interval = 300; reloadTimer.Tick += (s, e) => { reloadTimer.Stop(); ReloadConfig(); };
            trimTimer.Interval = 1500; trimTimer.Tick += (s, e) => { trimTimer.Stop(); Native.TrimMemory(); };
            measureBmp = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            measureG = Graphics.FromImage(measureBmp); Ui.Hq(measureG);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hwnd = Handle;
            abMsg = BarNative.RegisterWindowMessage("NovaBarAppBarMsg");
            tbCreatedMsg = BarNative.RegisterWindowMessage("TaskbarCreated");
            try { BarNative.WTSRegisterSessionNotification(hwnd, 0); } catch { }
            ApplyConfig(false, true);
            // the taskbar hide that runs at startup makes explorer recompute work areas a moment later: re-check our strip
            foreach (var ms in new[] { 1500, 5000 })
            {
                var t0 = new System.Windows.Forms.Timer { Interval = ms };
                t0.Tick += (o, e2) => { t0.Stop(); t0.Dispose(); if (!closing && appbarReg && ReservationLost()) { lastAbRect = Rectangle.Empty; Relayout(); } };
                t0.Start();
            }
            if (Owner) StartWatcher();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            trimTimer.Interval = 3000; trimTimer.Start();
        }

        // =============== config ===============
        void StartWatcher()
        {
            try
            {
                watcher = new FileSystemWatcher(Paths.Root, "config.json");
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime;
                FileSystemEventHandler h = (s, e) => OnConfigFileEvent();
                watcher.Changed += h; watcher.Created += h; watcher.Renamed += (s, e) => OnConfigFileEvent();
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { BarShell.Log("watcher", ex); }
        }
        void OnConfigFileEvent()
        {
            if (Environment.TickCount - selfSaveAt < 1200) return;
            try { BeginInvoke((Action)(() => { reloadTimer.Stop(); reloadTimer.Start(); })); } catch { }
        }
        internal static void SaveConfig() { selfSaveAt = Environment.TickCount; AppConfig.Current.Save(); }

        void ReloadConfig()
        {
            var nc = AppConfig.Load();
            bool fontChanged = nc.Font != AppConfig.Current.Font;
            AppConfig.Current = nc;
            BarApp.ApplyAll(fontChanged);
            AppConfig.RaiseChanged();
        }

        /// <summary>Apply AppConfig.Current: theme, fonts, widgets, shell options, layout.</summary>
        internal void ApplyConfig(bool fontChanged, bool first)
        {
            var cfg = AppConfig.Current;
            var nt = Theme.Make(cfg.Theme, cfg.Accent);
            bool themeChanged = T == null || nt.Id != T.Id || nt.Accent != T.Accent;
            T = Theme.Current = nt;
            if (fontChanged) Ui.ResetFonts();
            if (panel != null && (themeChanged || fontChanged)) ClosePanel();
            RebuildWidgets();
            if (Owner)
            {
                if (cfg.HideTaskbar) BarShell.HideTaskbar(); else if (BarShell.TaskbarHidden) BarShell.RestoreTaskbar();
                if (BarShell.TaskbarHidden) taskbarTimer.Start(); else taskbarTimer.Stop();
                BarShell.SetStartup(cfg.LaunchAtStartup);
            }
            if (cfg.ReserveSpace) AppBarRegister(); else AppBarRemove();
            if (cfg.HideOnFullscreen) { fsTimer.Start(); FsHook(true); } else { fsTimer.Stop(); FsHook(false); if (hiddenFs) ShowFromFullscreen(); }
            Relayout();
        }

        void RebuildWidgets()
        {
            var cfg = AppConfig.Current;
            var old = new Dictionary<string, BarTile>();
            foreach (var t in tiles) if (t.Inst != null && t.Inst.Inst != null) old[t.Inst.Inst] = t;
            var nl = new List<BarTile>();
            string myDev = PickScreen(cfg.Monitor).DeviceName;   // per-monitor widget assignment
            foreach (var zone in BarRenderer.Zones)
                foreach (var wi in cfg.Widgets)
                {
                    if (!wi.ShowsOn(myDev)) continue;
                    var probe = new BarTile { Zone = wi.Zone };
                    if (BarRenderer.ZoneOf(probe) != zone) continue;
                    BarTile t;
                    if (wi.Inst != null && old.TryGetValue(wi.Inst, out t) && t.W != null && t.W.Meta.Id == wi.Id)
                    {
                        old.Remove(wi.Inst);
                        bool changed = J.Stringify(t.Inst.Settings ?? new Dictionary<string, object>()) != J.Stringify(wi.Settings ?? new Dictionary<string, object>());
                        t.Inst = wi; t.W.Instance = wi; t.Zone = zone; t.Error = null;
                        if (changed)
                        {
                            if (panelWidget == t.W) ClosePanel();
                            try { t.W.OnStop(); } catch (Exception ex) { BarShell.Log(t.Id + " OnStop", ex); }
                            try { t.W.OnStart(); } catch (Exception ex) { t.Error = "OnStart: " + ex.Message; BarShell.Log(t.Id + " OnStart", ex); }
                        }
                    }
                    else
                    {
                        t = new BarTile { Inst = wi, Zone = zone };
                        try
                        {
                            t.W = WidgetRegistry.Create(wi.Id, wi);
                            if (t.W == null) continue; // unknown widget id (e.g. not in this build) — skip silently on the live bar
                            t.W.Host = this;
                            t.W.OnStart();
                        }
                        catch (Exception ex) { t.Error = "start: " + ex.Message; BarShell.Log(wi.Id + " create/OnStart", ex); }
                    }
                    nl.Add(t);
                }
            foreach (var t in old.Values)
            {
                if (t.W == null) continue;
                if (panelWidget == t.W) ClosePanel();
                try { t.W.OnStop(); } catch (Exception ex) { BarShell.Log(t.Id + " OnStop", ex); }
                Hub.UnsubscribeAll(t.W);
                nextTick.Remove(t);
            }
            tiles.Clear(); tiles.AddRange(nl);
            hover = null; hoverHit = -1;
        }

        // =============== layout ===============
        Screen PickScreen(int cfgIdx)
        {
            int idx = screenIndex >= 0 ? screenIndex : cfgIdx;
            var all = Screen.AllScreens;
            var list = new List<Screen> { Screen.PrimaryScreen };
            list.AddRange(all.Where(s => !s.Primary));
            if (idx < 0 || idx >= list.Count) idx = screenIndex >= 0 ? list.Count - 1 : 0;
            return list[idx];
        }
        static float DpiOf(Screen s)
        {
            try
            {
                var c = new Native.POINT { X = s.Bounds.X + s.Bounds.Width / 2, Y = s.Bounds.Y + s.Bounds.Height / 2 };
                var mon = MonitorFromPoint(c, 2);
                uint x, y; if (GetDpiForMonitor(mon, 0, out x, out y) == 0 && x > 0) return x / 96f;
            }
            catch { }
            try { return Native.GetDpiForSystem() / 96f; } catch { return 1f; }
        }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.POINT p, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);

        bool Pill { get { var s = AppConfig.Current.Style; return !string.Equals(s, "full", StringComparison.OrdinalIgnoreCase) && !string.Equals(s, "shell", StringComparison.OrdinalIgnoreCase); } }
        readonly FilletPair corners = new FilletPair();
        internal int FilletR { get { return (int)Math.Round(14 * dpi); } }
        /// <summary>Shell style: concave corners where the bar meets the screen's side edges.</summary>
        void UpdateCorners()
        {
            if (!AppConfig.Current.IsShell || Vertical || hiddenFs || closing || !Visible) { corners.Hide(); return; }
            var b = Bounds; int R = FilletR;
            if (Top) corners.Set(new Rectangle(b.X, b.Bottom, R, R), R, R, new Rectangle(b.Right - R, b.Bottom, R, R), 0, R, T.ShellBg);
            else corners.Set(new Rectangle(b.X, b.Y - R, R, R), R, 0, new Rectangle(b.Right - R, b.Y - R, R, R), 0, 0, T.ShellBg);
        }
        /// <summary>This bar's edge (per-display override, else the global position).</summary>
        internal string Pos { get { return (AppConfig.Current.PositionFor(Scr == null ? null : Scr.DeviceName) ?? "bottom").ToLowerInvariant(); } }
        bool Top { get { return Pos == "top"; } }
        /// <summary>Docked to the left or right edge: a vertical bar.</summary>
        internal bool Vertical { get { var p = Pos; return p == "left" || p == "right"; } }
        internal bool LeftEdge { get { return Pos == "left"; } }
        int lastVW;   // last vertical bar width (for the reserved-space appbar)
        BarMetrics Metrics(string size) { var m = BarMetrics.For(size, dpi); m.Vertical = Vertical; m.Edge = Pos; return m; }
        int Margin { get { return Pill ? (int)Math.Round(8 * dpi) : 0; } }

        /// <summary>Bar size for this bar's display (per-display override, else the global size).</summary>
        internal string MySize { get { return AppConfig.Current.SizeFor(Scr == null ? null : Scr.DeviceName); } }
        internal void Relayout()
        {
            if (!IsHandleCreated || closing) return;
            var cfg = AppConfig.Current;
            Scr = PickScreen(cfg.Monitor);
            dpi = DpiOf(Scr);
            M = Metrics(MySize);
            var mon = Scr.Bounds;
            int thick = (Vertical ? (lastVW > 0 ? lastVW : M.Si(200)) : M.Height) + 2 * Margin;
            if (appbarReg) area = AppBarSetPos(mon, Top, thick);
            else area = BarShell.TaskbarHidden ? mon : Scr.WorkingArea;
            BarShell.ApplyBackdrop(hwnd, T, !AppConfig.Current.IsShell, true);   // rounded corners for pill and full (same size/position)
            barDirty = true;
            Render();
        }

        void PlaceV(int width, int height)
        {
            int h = Pill ? Math.Min(height, area.Height - 2 * Margin) : area.Height;   // full style: the whole screen height
            int y = Pill ? area.Y + (area.Height - h) / 2 : area.Y;
            int x = LeftEdge ? area.X + Margin : area.Right - Margin - width;
            if (Bounds != new Rectangle(x, y, width, h))
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, width, h, Native.SWP_NOACTIVATE);
            corners.Hide();
            if (width != lastVW) { bool first = lastVW == 0; lastVW = width; if (appbarReg && !first) BeginInvoke((Action)Relayout); }
        }
        void Place(int width)
        {
            int w = Pill ? Math.Min(width, area.Width - 2 * Margin) : area.Width - 2 * Margin;
            int x = Pill ? area.X + (area.Width - w) / 2 : area.X + Margin;
            int y = Top ? area.Y + Margin : area.Bottom - Margin - M.Height;
            if (Bounds != new Rectangle(x, y, w, M.Height))
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, M.Height, Native.SWP_NOACTIVATE);
            UpdateCorners();
        }

        // =============== appbar ===============
        Native.APPBARDATA Abd() { var d = new Native.APPBARDATA(); d.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)); d.hWnd = hwnd; return d; }
        int lastReassert; Rectangle lastAbRect; System.Windows.Forms.Timer reassertTimer;
        /// <summary>Debounced: 400 ms after the last appbar/work-area notification, SETPOS again only if our strip is missing.
        /// (Re-laying out on every ABN_POSCHANGED made the three bars ping-pong ~30 SETPOS/s for seconds at startup.)</summary>
        void ScheduleReassert()
        {
            if (reassertTimer == null)
            {
                reassertTimer = new System.Windows.Forms.Timer { Interval = 400 };
                reassertTimer.Tick += (o, e) =>
                {
                    reassertTimer.Stop();
                    if (closing || !appbarReg) return;
                    if (!ReservationLost()) return;
                    if (Environment.TickCount - lastReassert < 800) return;
                    lastReassert = Environment.TickCount;
                    lastAbRect = Rectangle.Empty;   // force a real SETPOS
                    Relayout();
                };
            }
            reassertTimer.Stop(); reassertTimer.Start();
        }
        void AbLog(string s) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(Paths.Root, "appbar.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [" + (Scr != null ? Scr.DeviceName : "?") + "] " + s + Environment.NewLine); } catch { } }
        /// <summary>Reserve-space mode: true when this monitor's work area no longer excludes the bar's strip.</summary>
        bool ReservationLost()
        {
            try
            {
                var s = Screen.FromHandle(hwnd); var b = s.Bounds; var w = s.WorkingArea;
                if (Vertical) return LeftEdge ? w.Left <= b.Left : w.Right >= b.Right;
                return Top ? w.Top <= b.Top : w.Bottom >= b.Bottom;
            }
            catch { return false; }
        }
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(int action, int param, ref Native.RECT r, int flags);
        /// <summary>Reserve-space mode: make this monitor's work area exclude the bar's strip (SPI_SETWORKAREA). Explorer's appbar
        /// bookkeeping is unreliable while its own taskbar is hidden; this is applied only when the work area is wrong.</summary>
        void EnforceWorkArea()
        {
            if (!AppConfig.Current.ReserveSpace || hiddenFs || closing || hwnd == IntPtr.Zero) return;
            try
            {
                var s = Screen.FromHandle(hwnd); var b = s.Bounds; var bw = Bounds;
                Rectangle want;
                if (Vertical) want = LeftEdge ? Rectangle.FromLTRB(bw.Right + Margin, b.Top, b.Right, b.Bottom) : Rectangle.FromLTRB(b.Left, b.Top, bw.Left - Margin, b.Bottom);
                else want = Top ? Rectangle.FromLTRB(b.Left, bw.Bottom + Margin, b.Right, b.Bottom) : Rectangle.FromLTRB(b.Left, b.Top, b.Right, bw.Top - Margin);
                if (want.Width < 100 || want.Height < 100 || s.WorkingArea == want) return;
                var r = new Native.RECT { Left = want.Left, Top = want.Top, Right = want.Right, Bottom = want.Bottom };
                SystemParametersInfo(0x2F /*SPI_SETWORKAREA*/, 0, ref r, 2 /*SPIF_SENDCHANGE*/);
                AbLog("work area set " + want);
            }
            catch { }
        }
        void AppBarRegister()
        {
            if (appbarReg || hwnd == IntPtr.Zero) return;
            // explorer ties an appbar to the monitor its window is on at ABM_NEW: be on our own display first
            try
            {
                var s = PickScreen(AppConfig.Current.Monitor); Scr = s;
                if (Screen.FromHandle(hwnd).DeviceName != s.DeviceName)
                    Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, s.Bounds.X + s.Bounds.Width / 2, s.Bounds.Y + s.Bounds.Height / 2, Math.Max(1, Width), Math.Max(1, Height), Native.SWP_NOACTIVATE);
            }
            catch { }
            var d = Abd(); d.uCallbackMessage = abMsg; var rr = Native.SHAppBarMessage(Native.ABM_NEW, ref d); appbarReg = true;
        }
        internal void AppBarRemove()
        {
            if (!appbarReg) return;
            var d = Abd(); Native.SHAppBarMessage(Native.ABM_REMOVE, ref d); appbarReg = false; lastAbRect = Rectangle.Empty;
        }
        Rectangle AppBarSetPos(Rectangle mon, bool top, int thick)
        {
            var d = Abd();
            if (Vertical)
            {
                bool left = LeftEdge;
                d.uEdge = left ? 0u /*ABE_LEFT*/ : 2u /*ABE_RIGHT*/;
                d.rc.Top = mon.Top; d.rc.Bottom = mon.Bottom;
                d.rc.Left = left ? mon.Left : mon.Right - thick; d.rc.Right = left ? mon.Left + thick : mon.Right;
                Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref d);
                if (left) d.rc.Right = d.rc.Left + thick; else d.rc.Left = d.rc.Right - thick;
                var want = Rectangle.FromLTRB(d.rc.Left, d.rc.Top, d.rc.Right, d.rc.Bottom);
                Native.SHAppBarMessage(Native.ABM_SETPOS, ref d); lastAbRect = want;
                return Rectangle.FromLTRB(d.rc.Left, d.rc.Top, d.rc.Right, d.rc.Bottom);
            }
            d.uEdge = top ? Native.ABE_TOP : Native.ABE_BOTTOM;
            d.rc.Left = mon.Left; d.rc.Right = mon.Right;
            d.rc.Top = top ? mon.Top : mon.Bottom - thick; d.rc.Bottom = top ? mon.Top + thick : mon.Bottom;
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref d);
            if (top) d.rc.Bottom = d.rc.Top + thick; else d.rc.Top = d.rc.Bottom - thick;
            var wantH = Rectangle.FromLTRB(d.rc.Left, d.rc.Top, d.rc.Right, d.rc.Bottom);
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref d); lastAbRect = wantH;
            return Rectangle.FromLTRB(d.rc.Left, d.rc.Top, d.rc.Right, d.rc.Bottom);
        }

        // =============== painting ===============
        // ---- floating glass: clip the window (and therefore the acrylic blur) to the tiles ----
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        string rgnKey;
        readonly BarGlass glass = new BarGlass();
        void UpdateTileRegion()
        {
            bool want = false;   // DWM accent blur ignores window regions → it showed as a band behind the tiles; floating tiles stay fully transparent
            if (!want) { if (rgnKey != null) { SetWindowRgn(hwnd, IntPtr.Zero, true); rgnKey = null; } return; }
            var sb = new System.Text.StringBuilder();
            foreach (var t in tiles) if (t.R.Width > 0) sb.Append(t.R.X).Append(',').Append(t.R.Y).Append(',').Append(t.R.Width).Append(',').Append(t.R.Height).Append(';');
            var key = sb.ToString() + ClientSize.Width + "x" + ClientSize.Height;
            if (key == rgnKey) return;
            rgnKey = key;
            IntPtr rgn = CreateRoundRectRgn(0, 0, 0, 0, 0, 0);
            int d = M.TileRadius * 2;
            foreach (var t in tiles)
            {
                if (t.R.Width <= 0) continue;
                var r1 = CreateRoundRectRgn(t.R.Left, t.R.Top, t.R.Right + 1, t.R.Bottom + 1, d, d);
                CombineRgn(rgn, rgn, r1, 2 /*RGN_OR*/); Native.DeleteObject(r1);
            }
            SetWindowRgn(hwnd, rgn, true);   // the system owns rgn now
        }
        // overflow "»" tile (pill bars whose widgets don't fit the screen)
        readonly BarOverflowWidget ovW = new BarOverflowWidget();
        BarTile ovTileF;
        BarTile ovTile { get { if (ovTileF == null) { ovTileF = new BarTile { W = ovW, Zone = "center" }; } return ovTileF; } }
        static string OverflowMode { get { object v; var ex = AppConfig.Current.Extra; return ex != null && ex.TryGetValue("overflow", out v) && v != null ? J.AsStr(v) : "button"; } }
        // partial repaint: widgets whose tile alone needs redrawing (layout unchanged)
        readonly HashSet<Widget> dirtyW = new HashSet<Widget>(); volatile bool fullDirty = true;
        public void Invalidate(Widget w)
        {
            if (w != null && w == panelWidget) panelDirty = true;
            if (w != null && panelWidget == ovW && ovW.Hidden.Exists(t => t.W == w)) panelDirty = true;
            if (w == null) fullDirty = true; else lock (dirtyW) dirtyW.Add(w);
            barDirty = true;
            if (Interlocked.Exchange(ref pending, 1) == 0 && hwnd != IntPtr.Zero) Native.PostMessage(hwnd, BarNative.WM_APP_REPAINT, IntPtr.Zero, IntPtr.Zero);
        }
        void RequestPaint() { Invalidate(null); }

        // ---- hover motion: highlight fades in/out and the tile lifts (~130 ms), only while something is moving ----
        System.Windows.Forms.Timer hoverAnim; int hoverAnimLast;
        const float HoverMs = 130f;
        void KickHoverAnim()
        {
            if (hoverAnim == null)
            {
                hoverAnim = new System.Windows.Forms.Timer { Interval = 15 };
                hoverAnim.Tick += (o, e) => StepHoverAnim();
            }
            if (!hoverAnim.Enabled) { hoverAnimLast = Environment.TickCount; hoverAnim.Start(); }
        }
        void StepHoverAnim()
        {
            int now = Environment.TickCount; float step = Math.Max(1, now - hoverAnimLast) / HoverMs; hoverAnimLast = now;
            bool moving = false;
            foreach (var t in tiles)
            {
                float target = t == hover && !dragging ? 1f : 0f;
                if (t.HoverT == target) continue;
                t.HoverT = target > t.HoverT ? Math.Min(target, t.HoverT + step) : Math.Max(target, t.HoverT - step);
                moving = true;
                if (t.W != null) Invalidate(t.W); else RequestPaint();
            }
            if (!moving) hoverAnim.Stop();
        }

        void OnRepaintMessage()
        {
            int el = Environment.TickCount - lastPaint;
            if (el >= 16 || el < 0) DoPaint();
            else if (!paintTimer.Enabled) { paintTimer.Interval = Math.Max(1, 16 - el); paintTimer.Start(); }
        }

        void DoPaint()
        {
            Interlocked.Exchange(ref pending, 0);
            lastPaint = Environment.TickCount;
            if (barDirty && !hiddenFs) { barDirty = false; Render(); }
            if (panelDirty && panel != null) { panelDirty = false; panel.Render(); }
            EnsureTicking();
        }

        void Render()
        {
            if (hwnd == IntPtr.Zero || closing) return;
            // ---- fast path: only some tiles changed and their width is unchanged → repaint just those tiles ----
            Widget[] dw; lock (dirtyW) { dw = dirtyW.ToArray(); dirtyW.Clear(); }
            bool full = fullDirty; fullDirty = false;
            if (!full && dw.Length > 0 && !dragging && buf.W == ClientSize.Width && buf.H == ClientSize.Height && buf.G != null)
            {
                var todo = new List<BarTile>(); bool ok = true;
                foreach (var dwi in dw)
                {
                    var t = tiles.FirstOrDefault(x => x.W == dwi);
                    if (t == null) continue;                       // widget not on this bar
                    if (t.R.Width <= 0) continue;                  // hidden (didn't fit on this screen)
                    if (M.Vertical)
                    {
                        int pw = t.Width, nw2 = BarRenderer.MeasureTile(measureG, T, M, t);
                        if (nw2 != pw) { ok = false; break; }
                        int pvh = t.VH;
                        if (pvh > 0 && BarRenderer.MeasureTileV(measureG, T, M, t, t.R.Width) != t.R.Height) { ok = false; break; }
                        todo.Add(t); continue;
                    }
                    int nw = BarRenderer.MeasureTile(measureG, T, M, t);
                    if (nw != t.R.Width) { ok = false; break; }    // size changed → full layout
                    todo.Add(t);
                }
                if (ok)
                {
                    var g0 = buf.G;
                    foreach (var t in todo)
                    {
                        var rr = Rectangle.Inflate(t.R, 2, 3);       // include the floating-tile shadow
                        rr.Intersect(new Rectangle(0, 0, buf.W, buf.H));
                        var st = g0.Save();
                        g0.SetClip(rr);
                        g0.Clear(Color.Transparent);
                        BarRenderer.PaintBar(g0, T, new Rectangle(0, 0, buf.W, buf.H), 0);
                        g0.Restore(st);
                        BarRenderer.PaintTile(g0, T, M, t, t == hover, mouse);   // inflate (2px) < tile gap, so neighbours are untouched
                        buf.BlitRect(hwnd, rr);
                    }
                    return;
                }
            }
            bool pill = Pill;
            tiles.Remove(ovTile); ovW.Hidden.Clear();
            string ovMode = OverflowMode;
            M = Metrics(MySize);
            if (M.Vertical) { LayoutVertical(pill, ovMode); goto paint; }
            int w = BarRenderer.Layout(measureG, T, M, tiles, pill ? 0 : area.Width - 2 * Margin, pill);
            int avail = area.Width - 2 * Margin;
            if (pill && w > avail && ovMode == "shrink" && !string.Equals(MySize, "S", StringComparison.OrdinalIgnoreCase))
            {   // crowded screen: try the small size before hiding anything
                M = Metrics("S");
                w = BarRenderer.Layout(measureG, T, M, tiles, 0, true);
            }
            Place(w);
            int cw = ClientSize.Width, ch = ClientSize.Height;
            {
            if (pill && cw < w)
            {
                if (ovMode == "hide") BarRenderer.FitCompact(M, tiles, cw);   // too wide for this screen: show what fits, in order
                else
                {
                    ovW.M = M; ovW.Host = this;
                    int ow = BarRenderer.MeasureTile(measureG, T, M, ovTile);
                    BarRenderer.FitCompact(M, tiles, cw - ow - M.Gap);
                    int right = M.Pad - M.Gap;
                    foreach (var t in tiles) { if (t.R.Width > 0) right = Math.Max(right, t.R.Right); else if (t.W != null) ovW.Hidden.Add(t); }
                    ovTile.R = new Rectangle(right + M.Gap, (M.Height - M.TileH) / 2, ow, M.TileH);
                    tiles.Add(ovTile);
                }
            }
            }
            paint:
            cw = ClientSize.Width; ch = ClientSize.Height;
            if (ovW.Hidden.Count == 0 && panelWidget == ovW) ClosePanel();
            if (dragging && !barDrag && downTile != null)
            {
                if (M.Vertical) downTile.R = new Rectangle(downTile.R.X, Math.Max(0, Math.Min(ch - downTile.R.Height, mouse.Y - grabDx)), downTile.R.Width, downTile.R.Height);
                else downTile.R = new Rectangle(Math.Max(0, Math.Min(cw - downTile.R.Width, mouse.X - grabDx)), downTile.R.Y, downTile.R.Width, downTile.R.Height);
            }
            UpdateTileRegion();
            glass.Sync(this, tiles, T, !hiddenFs && !locked && !dragging && Visible);
            var g = buf.Begin(cw, ch);
            BarRenderer.PaintBar(g, T, new Rectangle(0, 0, cw, ch), 0);
            BarRenderer.PaintTiles(g, T, M, tiles, hover, mouse, dragging && !barDrag ? downTile : null);
            buf.BlitTo(hwnd);
        }

        /// <summary>Vertical bar: stack the tiles, size/place the window, overflow what doesn't fit.</summary>
        void LayoutVertical(bool pill, string ovMode)
        {
            int capW = M.Si(136);   // slim sidebar; wider widgets are scaled to fit
            var sz = BarRenderer.LayoutV(measureG, T, M, tiles, capW);
            int availH = area.Height - 2 * Margin;
            if (pill && sz.Height > availH && ovMode == "shrink" && !string.Equals(MySize, "S", StringComparison.OrdinalIgnoreCase))
            {
                M = Metrics("S");
                sz = BarRenderer.LayoutV(measureG, T, M, tiles, capW);
            }
            PlaceV(sz.Width, sz.Height);
            int ch = ClientSize.Height;
            if (sz.Height > ch)
            {
                if (ovMode == "hide") BarRenderer.FitCompactV(M, tiles, ch);
                else
                {
                    ovW.M = M; ovW.Host = this;
                    BarRenderer.MeasureTile(measureG, T, M, ovTile);
                    BarRenderer.FitCompactV(M, tiles, ch - M.TileH - M.Si(8));
                    int bottom = 0;
                    foreach (var t in tiles) { if (t.R.Width > 0) bottom = Math.Max(bottom, t.R.Bottom); else if (t.W != null) ovW.Hidden.Add(t); }
                    ovTile.R = new Rectangle(M.Si(8), bottom + M.Si(8), sz.Width - 2 * M.Si(8), M.TileH);
                    tiles.Add(ovTile);
                }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (buf.W != ClientSize.Width || buf.H != ClientSize.Height) { Render(); return; }
            var hdc = e.Graphics.GetHdc();
            try { buf.Blit(hdc, ClientSize.Width, ClientSize.Height); } finally { e.Graphics.ReleaseHdc(hdc); }
        }

        // =============== ticking ===============
        static int RefreshOf(BarTile t) { if (t.W == null || t.Error != null) return 0; try { return t.W.RefreshMs; } catch { return 0; } }

        void EnsureTicking()
        {
            if (hiddenFs || locked || closing) { if (tickTimer.Enabled) tickTimer.Stop(); return; }
            int min = int.MaxValue;
            for (int i = 0; i < tiles.Count; i++) { int r = RefreshOf(tiles[i]); if (r > 0 && r < min) min = r; }
            if (min == int.MaxValue) { if (tickTimer.Enabled) tickTimer.Stop(); return; }
            int iv = Math.Max(33, Math.Min(250, min));
            if (tickTimer.Interval != iv) tickTimer.Interval = iv;
            if (!tickTimer.Enabled) tickTimer.Start();
        }

        void OnTickTimer()
        {
            int now = Environment.TickCount; bool any = false;
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i]; int rm = RefreshOf(t); if (rm <= 0) continue;
                int due; if (nextTick.TryGetValue(t, out due) && now - due < 0) continue;
                nextTick[t] = now + rm - Math.Min(rm / 4, 40);
                try { t.W.OnTick(); } catch (Exception ex) { BarShell.Log(t.Id + " OnTick", ex); }
                any = true; ticked.Add(t);
                if (t.W == panelWidget) panelDirty = true;
            }
            if (any) { foreach (var t in ticked) Invalidate(t.W); ticked.Clear(); } else EnsureTicking();
        }
        readonly List<BarTile> ticked = new List<BarTile>();

        // =============== fullscreen / lock ===============
        void CheckFullscreen() { CheckFullscreen(false); }
        void CheckFullscreen(bool fromEvent)
        {
            if (closing) return;
            bool fs = AppConfig.Current.HideOnFullscreen && BarShell.FullscreenOn(Scr, hwnd, panel != null && panel.IsHandleCreated ? panel.Handle : IntPtr.Zero);
            if (fs && !hiddenFs)
            {
                hiddenFs = true; ClosePanel(); glass.Hide(); corners.Hide();
                Native.ShowWindow(hwnd, Native.SW_HIDE); EnsureTicking();
            }
            else if (!fs && hiddenFs) ShowFromFullscreen();
            else if (!hiddenFs && !fromEvent && !MenuOpen())
            {
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                if (AppConfig.Current.IsShell) corners.Raise();
            }
        }
        /// <summary>True while any popup menu (ours or another app's) or one of our own windows is in front:
        /// never re-raise the bar above it.</summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumThreadWindows(uint tid, Native.EnumWindowsProc cb, IntPtr l);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        /// <summary>True while any popup menu is showing — a system menu (#32768), or one of OUR windows other than the bars:
        /// ToolStrip context menus (they never take focus), panels, previews. Never re-raise the bar above those.</summary>
        static bool MenuOpen()
        {
            var m = Native.FindWindow("#32768", null);
            if (m != IntPtr.Zero && Native.IsWindowVisible(m)) return true;
            bool found = false;
            EnumThreadWindows(GetCurrentThreadId(), (h, l) =>
            {
                if (!Native.IsWindowVisible(h)) return true;
                var t = Native.WindowTitle(h);
                if (t == "NovaBar" || t == "NovaBarGlass") return true;          // the bars and their glass panes
                var cls = Native.WindowClass(h);
                if (cls.StartsWith("WindowsForms10.")) { found = true; return false; }   // menus, panels, previews, dialogs
                return true;
            }, IntPtr.Zero);
            if (found) return true;
            var fg = Native.GetForegroundWindow();
            return Native.WindowClass(fg) == "Chrome_WidgetWin_2";             // Chromium popup menus
        }
        void ShowFromFullscreen()
        {
            hiddenFs = false;
            Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            barDirty = true; RequestPaint(); UpdateCorners();
        }

        // =============== window messages ===============
        protected override void WndProc(ref Message msg)
        {
            switch (msg.Msg)
            {
                case Native.WM_MOUSEACTIVATE: msg.Result = new IntPtr(Native.MA_NOACTIVATE); return;
                case BarNative.WM_APP_REPAINT: OnRepaintMessage(); return;
                case BarNative.WM_WTSSESSION_CHANGE:
                    int code = msg.WParam.ToInt32();
                    if (code == BarNative.WTS_SESSION_LOCK) { locked = true; EnsureTicking(); }
                    else if (code == BarNative.WTS_SESSION_UNLOCK) { locked = false; barDirty = true; RequestPaint(); }
                    break;
                case Native.WM_DPICHANGED: base.WndProc(ref msg); BeginInvoke((Action)Relayout); return;
                case Native.WM_DISPLAYCHANGE: base.WndProc(ref msg); BeginInvoke((Action)(() => { if (Owner) BarApp.SyncMonitors(); Relayout(); })); return;
                case Native.WM_SETTINGCHANGE:
                    base.WndProc(ref msg);
                    if (msg.WParam.ToInt32() == BarNative.SPI_SETWORKAREA)
                    {
                        if (!appbarReg) BeginInvoke((Action)Relayout);
                        else ScheduleReassert();   // explorer recomputed work areas (e.g. after hiding the taskbar): re-claim if lost
                    }
                    return;
                case BarNative.WM_QUERYENDSESSION: ShutdownShell(); break;
                case BarNative.WM_ENDSESSION: if (msg.WParam != IntPtr.Zero) ShutdownShell(); break;
            }
            if (tbCreatedMsg != 0 && msg.Msg == (int)tbCreatedMsg)
            {
                // explorer restarted: our appbar registration is gone and the new taskbar is visible
                appbarReg = false;
                BeginInvoke((Action)(() =>
                {
                    if (closing) return;
                    if (Owner && AppConfig.Current.HideTaskbar) { BarShell.HideTaskbar(); taskbarTimer.Start(); }
                    if (AppConfig.Current.ReserveSpace) AppBarRegister();
                    Relayout();
                }));
                base.WndProc(ref msg); return;
            }
            if (abMsg != 0 && msg.Msg == (int)abMsg)
            {
                int n = msg.WParam.ToInt32();
                if (n == BarNative.ABN_POSCHANGED) BeginInvoke((Action)Relayout);   // re-claim (SETPOS is skipped when nothing changed)
                else if (n == BarNative.ABN_FULLSCREENAPP && AppConfig.Current.HideOnFullscreen)
                {
                    if (msg.LParam != IntPtr.Zero) { if (!hiddenFs) { hiddenFs = true; ClosePanel(); corners.Hide(); Native.ShowWindow(hwnd, Native.SW_HIDE); EnsureTicking(); } }
                    else CheckFullscreen();
                }
                return;
            }
            base.WndProc(ref msg);
        }

        /// <summary>Remove appbar + restore the taskbar. Safe from any thread, idempotent.</summary>
        internal void ShutdownShell()
        {
            try { AppBarRemove(); } catch { }
            if (Owner) BarShell.RestoreTaskbar();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closing = true;
            try { corners.Dispose(); } catch { }
            taskbarTimer.Stop(); paintTimer.Stop(); tickTimer.Stop(); fsTimer.Stop(); FsHook(false); glass.Clear(); reloadTimer.Stop(); trimTimer.Stop();
            if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); }
            ClosePanel();
            foreach (var t in tiles) if (t.W != null) { try { t.W.OnStop(); } catch { } }
            try { BarNative.WTSUnRegisterSessionNotification(hwnd); } catch { }
            ShutdownShell();
            buf.Dispose();
            base.OnFormClosed(e);
        }

        // =============== mouse ===============
        BarTile TileAt(Point p) { for (int i = tiles.Count - 1; i >= 0; i--) if (tiles[i].R.Contains(p)) return tiles[i]; return null; }
        static int HitIndex(DrawCtx c, Point p) { for (int i = c.Hits.Count - 1; i >= 0; i--) if (c.Hits[i].Key.Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
            if (e.Button == MouseButtons.Left && widgetDrag && downTile != null && downTile.W != null)
            {
                try { downTile.W.DragTo(e.Location); } catch { }
                RequestPaint(); return;
            }
            if (e.Button == MouseButtons.Left && (downTile != null || downEmpty))
            {
                if (!dragging && (Math.Abs(e.X - downPt.X) > M.Si(6) || Math.Abs(e.Y - downPt.Y) > M.Si(10)))
                {
                    if (downTile == ovTileF && downTile != null) return;   // the » tile can't be dragged
                    if (downTile != null && downTile.W != null && !widgetDrag)
                    {
                        bool takes = false; try { takes = downTile.W.BeginDrag(downPt); } catch { }
                        if (takes) { widgetDrag = true; Capture = true; }
                    }
                    if (widgetDrag) { try { downTile.W.DragTo(e.Location); } catch { } RequestPaint(); return; }
                    dragging = true; barDrag = downTile == null;
                    if (!barDrag) grabDx = M.Vertical ? downPt.Y - downTile.R.Y : downPt.X - downTile.R.X;
                    Capture = true;
                }
                if (dragging)
                {
                    if (barDrag)
                    {
                        var cur = Cursor.Position;
                        if (Vertical)
                        {
                            int nx = Math.Max(Scr.Bounds.Left, Math.Min(Scr.Bounds.Right - Width, cur.X - downPt.X));
                            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, nx, Top0, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                        }
                        else
                        {
                            int y = Math.Max(Scr.Bounds.Top, Math.Min(Scr.Bounds.Bottom - Height, cur.Y - downPt.Y));
                            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, Left, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                        }
                    }
                    else RequestPaint();
                    return;
                }
            }
            var t = TileAt(e.Location);
            int hh = t != null ? HitIndex(t.Ctx, e.Location) : -1;
            if (t != hover || hh != hoverHit) { bool tileChanged = t != hover; hover = t; hoverHit = hh; RequestPaint(); if (tileChanged) KickHoverAnim(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragging) return;
            mouse = new Point(-1, -1);
            if (hover != null) { hover = null; hoverHit = -1; RequestPaint(); KickHoverAnim(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            downPt = e.Location; downTile = TileAt(e.Location); downEmpty = downTile == null; dragging = false;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                bool wasDrag = dragging; var dt = downTile; bool wasBar = barDrag;
                dragging = false; downTile = null; downEmpty = false; barDrag = false; Capture = false;
                if (widgetDrag)
                {
                    widgetDrag = false;
                    if (dt != null && dt.W != null) { try { dt.W.EndDrag(e.Location, false); } catch (Exception ex) { BarShell.Log(dt.Id + " EndDrag", ex); } }
                    RequestPaint(); return;
                }
                if (wasDrag) { if (wasBar) FinishBarDrag(); else FinishTileDrag(dt); return; }
                var t = TileAt(e.Location);
                if (t != null && t.W != null && t.Error == null)
                {
                    var a = BarRenderer.HitAt(t.Ctx, e.Location);
                    try { if (a != null) a(); else t.W.OnClick(MouseButtons.Left); }
                    catch (Exception ex) { BarShell.Log(t.Id + " click", ex); }
                    RequestPaint();
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                // right-click on a tile goes to the widget; empty bar space (or Shift+right-click anywhere) opens the bar menu
                var t = TileAt(e.Location);
                bool own = false;
                if (t != null && t.W != null && t.Error == null && (ModifierKeys & Keys.Shift) == 0)
                    try { own = t.W.HandlesRightClick(); } catch { }
                if (own)
                {
                    try { t.W.OnClick(MouseButtons.Right); } catch (Exception ex) { BarShell.Log(t.Id + " right-click", ex); }
                    RequestPaint();
                }
                else if (t != null && t.Inst != null && (ModifierKeys & Keys.Shift) == 0) BarMenu.ShowWidget(this, t, PointToScreen(e.Location));
                else BarMenu.Show(this, PointToScreen(e.Location));
            }
            else if (e.Button == MouseButtons.Middle)
            {
                var t = TileAt(e.Location);
                if (t != null && t.W != null) { try { t.W.OnClick(MouseButtons.Middle); } catch (Exception ex) { BarShell.Log(t.Id + " click", ex); } RequestPaint(); }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var t = TileAt(e.Location);
            if (t == null || t.W == null) return;
            var a = BarRenderer.WheelAt(t.Ctx, e.Location);
            if (a == null) return;
            try { a(e.Delta > 0 ? 1 : -1); } catch (Exception ex) { BarShell.Log(t.Id + " wheel", ex); }
            RequestPaint();
        }

        static int ZoneRank(BarTile t) { var z = BarRenderer.ZoneOf(t); return z == "left" ? 0 : z == "center" ? 1 : 2; }

        void FinishTileDrag(BarTile dt)
        {
            if (dt == null) return;
            var others = tiles.Where(x => x != dt && x != ovTile).ToList();
            if (M.Vertical)
            {
                int cy = Math.Max(0, mouse.Y - grabDx) + dt.R.Height / 2, vi = others.Count;
                for (int i = 0; i < others.Count; i++) if (others[i].R.Y + others[i].R.Height / 2 > cy) { vi = i; break; }
                dt.Zone = "center"; others.Insert(vi, dt);
                tiles.Clear(); tiles.AddRange(others);
                PersistOrder(); RequestPaint();
                return;
            }
            int cx = Math.Max(0, mouse.X - grabDx) + dt.R.Width / 2;
            string zone;
            if (!Pill) zone = "center";   // single ordered bar
            else
            {
                BarTile best = null; int bd = int.MaxValue;
                foreach (var o in others) { int d = Math.Abs(o.R.X + o.R.Width / 2 - cx); if (d < bd) { bd = d; best = o; } }
                zone = best != null ? BarRenderer.ZoneOf(best) : BarRenderer.ZoneOf(dt);
            }
            dt.Zone = zone;
            int rank = ZoneRank(dt), idx = others.Count;
            for (int i = 0; i < others.Count; i++)
            {
                var o = others[i]; int r = ZoneRank(o);
                if (r > rank || (r == rank && o.R.X + o.R.Width / 2 > cx)) { idx = i; break; }
            }
            others.Insert(idx, dt);
            tiles.Clear(); tiles.AddRange(others);
            PersistOrder();
            RequestPaint();
        }

        void PersistOrder()
        {
            var cfg = AppConfig.Current;
            // This bar's new order, written back ONLY into the slots its widgets already occupy in the global list,
            // so widgets of other monitors (and unknown ids) keep their exact positions.
            var mine = new List<WidgetInstance>();
            foreach (var t in tiles) if (t.Inst != null) { t.Inst.Zone = "center"; mine.Add(t.Inst); }
            var list = new List<WidgetInstance>(cfg.Widgets); int k = 0;
            for (int i = 0; i < list.Count && k < mine.Count; i++) if (mine.Contains(list[i])) list[i] = mine[k++];
            for (; k < mine.Count; k++) if (!list.Contains(mine[k])) list.Add(mine[k]);
            cfg.Widgets = list;
            SaveConfig();
            BarApp.ApplyAll(false);
        }

        void FinishBarDrag()
        {
            var cur = Cursor.Position; var sb = Scr.Bounds;
            // dock to the nearest screen edge
            int dTop = cur.Y - sb.Top, dBot = sb.Bottom - cur.Y, dL = cur.X - sb.Left, dR = sb.Right - cur.X;
            int mn = Math.Min(Math.Min(dTop, dBot), Math.Min(dL, dR));
            string pos = mn == dTop ? "top" : mn == dBot ? "bottom" : mn == dL ? "left" : "right";
            AppConfig.Current.SetPositionFor(Scr.DeviceName, pos);   // dragging moves only this display's bar
            SaveConfig();
            ClosePanel();
            BarApp.ApplyAll(false);
        }

        // =============== IWidgetHost ===============
        public void Post(Action a)
        {
            if (a == null || closing || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => { try { a(); } catch (Exception ex) { BarShell.Log("Post", ex); } })); } catch { }
        }

        BarTile TileOf(Widget w)
        {
            foreach (var t in tiles) if (t.W == w) return (t.R.Width <= 0 && tiles.Contains(ovTile)) ? ovTile : t;   // hidden → anchor on »
            return null;
        }

        public void OpenPanel(Widget w)
        {
            if (w == null) return;
            ClosePanel();
            var t = TileOf(w); if (t == null) return;
            if (BarRenderer.SafePanelSize(w, M.Scale).IsEmpty) return;
            panelWidget = w;
            try { w.OnPanelOpen(); } catch (Exception ex) { BarShell.Log(t.Id + " OnPanelOpen", ex); }
            panel = new PanelForm(this, t);
            panel.ShowPanel();
            RequestPaint();
        }

        public void ClosePanel()
        {
            var p = panel;
            if (p != null && !p.IsDisposed) p.Close();
        }

        internal void OnPanelClosed(PanelForm p)
        {
            if (panel != p) return;
            panel = null;
            var w = panelWidget; panelWidget = null;
            if (w != null) { try { w.OnPanelClose(); } catch (Exception ex) { BarShell.Log("OnPanelClose", ex); } }
            trimTimer.Stop(); trimTimer.Interval = 1500; trimTimer.Start();
            RequestPaint();
        }

        public bool IsPanelOpen(Widget w) { return w != null && panel != null && panelWidget == w; }

        public void OpenSettings(string page)
        {
            try
            {
                var args = "--settings" + (string.IsNullOrEmpty(page) ? "" : " \"" + page + "\"");
                var home = Environment.GetEnvironmentVariable("NOVABAR_HOME");
                if (!string.IsNullOrEmpty(home)) args = "--home \"" + home + "\" " + args;
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = false });
            }
            catch (Exception ex) { BarShell.Log("OpenSettings", ex); }
        }

        public Rectangle ScreenBoundsOf(Widget w)
        {
            var t = TileOf(w);
            return t != null && IsHandleCreated ? RectangleToScreen(t.R) : Rectangle.Empty;
        }

        internal Rectangle BarScreenRect { get { return Bounds; } }
        internal bool IsTop { get { return Top; } }
        int Top0 { get { return base.Top; } }
    }
}
