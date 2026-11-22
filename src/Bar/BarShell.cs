// Host-only interop + shell helpers: back buffer, backdrop, taskbar hide/restore, startup key, fullscreen detection.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NovaBar
{
    static class BarNative
    {
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(IntPtr h, int flags);
        [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);

        [StructLayout(LayoutKind.Sequential)] public struct ACCENT_POLICY { public int AccentState, AccentFlags, GradientColor, AnimationId; }
        [StructLayout(LayoutKind.Sequential)] public struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr h, ref WINCOMPATTRDATA d);

        public const int WM_APP_REPAINT = 0x8000 + 11;
        public const int WM_WTSSESSION_CHANGE = 0x2B1, WTS_SESSION_LOCK = 7, WTS_SESSION_UNLOCK = 8;
        public const int WM_QUERYENDSESSION = 0x11, WM_ENDSESSION = 0x16;
        public const int SPI_SETWORKAREA = 0x2F;
        public const int ABN_STATECHANGE = 0, ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;
        public const int LWA_ALPHA = 2;
        public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
        public const int SW_SHOWNA = 8;
    }

    /// <summary>Persistent 32bpp premultiplied DIB section + GDI+ Graphics over its bits. Recreated only on resize.</summary>
    sealed class BarBuffer : IDisposable
    {
        IntPtr mdc, hbmp, oldBmp, bits;
        Bitmap bmp; Graphics g;
        public int W, H;

        public Graphics Begin(int w, int h)
        {
            if (w < 1) w = 1; if (h < 1) h = 1;
            if (g == null || w != W || h != H) { Free(); Create(w, h); }
            g.Clear(Color.Transparent);
            return g;
        }
        public Graphics G { get { return g; } }

        void Create(int w, int h)
        {
            W = w; H = h;
            var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var sdc = BarNative.GetDC(IntPtr.Zero);
            mdc = Native.CreateCompatibleDC(sdc);
            hbmp = Native.CreateDIBSection(sdc, ref bi, 0, out bits, IntPtr.Zero, 0);
            BarNative.ReleaseDC(IntPtr.Zero, sdc);
            oldBmp = Native.SelectObject(mdc, hbmp);
            bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            g = Graphics.FromImage(bmp);
            Ui.Hq(g);
        }

        public void Blit(IntPtr hdc, int w, int h)
        {
            if (g == null) return;
            g.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            Native.BitBlt(hdc, 0, 0, Math.Min(w, W), Math.Min(h, H), mdc, 0, 0, Native.SRCCOPY);
        }
        /// <summary>Copy a w×h window-sized region starting at (sx, sy) of the back buffer.</summary>
        public void BlitFrom(IntPtr hdc, int w, int h, int sx, int sy)
        {
            if (g == null) return;
            g.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            sx = Math.Max(0, sx); sy = Math.Max(0, sy);
            Native.BitBlt(hdc, 0, 0, Math.Min(w, W - sx), Math.Min(h, H - sy), mdc, sx, sy, Native.SRCCOPY);
        }
        /// <summary>Copy only rectangle r of the back buffer to the window.</summary>
        public void BlitRect(IntPtr hwnd, Rectangle r)
        {
            if (g == null) return;
            r.Intersect(new Rectangle(0, 0, W, H)); if (r.Width <= 0 || r.Height <= 0) return;
            g.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            var dc = BarNative.GetDC(hwnd);
            try { Native.BitBlt(dc, r.X, r.Y, r.Width, r.Height, mdc, r.X, r.Y, Native.SRCCOPY); } finally { BarNative.ReleaseDC(hwnd, dc); }
        }
        public void BlitTo(IntPtr hwnd)
        {
            var dc = BarNative.GetDC(hwnd);
            try { Blit(dc, W, H); } finally { BarNative.ReleaseDC(hwnd, dc); }
        }

        void Free()
        {
            if (g != null) { g.Dispose(); g = null; }
            if (bmp != null) { bmp.Dispose(); bmp = null; }
            if (mdc != IntPtr.Zero) { Native.SelectObject(mdc, oldBmp); Native.DeleteDC(mdc); mdc = IntPtr.Zero; }
            if (hbmp != IntPtr.Zero) { Native.DeleteObject(hbmp); hbmp = IntPtr.Zero; }
            W = H = 0;
        }
        public void Dispose() { Free(); }
    }

    static class BarShell
    {
        // ---------------- backdrop ----------------
        /// <summary>"dwm" (DWMWA_SYSTEMBACKDROP_TYPE) or "accent" (SetWindowCompositionAttribute acrylic, stays on while inactive).</summary>
        public static string BackdropMode(bool forBar)
        {
            object v; var ex = AppConfig.Current.Extra;
            if (ex != null && ex.TryGetValue("backdropMode", out v) && v != null) return J.AsStr(v);
            return forBar ? "accent" : "dwm";
        }

        public static void ApplyBackdrop(IntPtr h, Theme t, bool rounded, bool forBar)
        {
            Native.SetDwm(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, t.Dark ? 1 : 0);
            if (AppConfig.Current.IsShell)
            {   // shell: opaque, square window; shapes come from fillet windows / regions
                Native.SetDwm(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_DONOTROUND);
                Native.SetDwm(h, Native.DWMWA_BORDER_COLOR, BarNative.DWMWA_COLOR_NONE);
                SetAccent(h, 0, 0);
                Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, Native.DWMSBT_NONE);
                Native.ExtendFrame(h, false);
                return;
            }
            if (forBar && !AppConfig.Current.BarBackground)
            {
                // Floating tiles: fully transparent window (alpha-0 pixels show the desktop), no backdrop, no DWM corner/border.
                Native.SetDwm(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_DONOTROUND);
                Native.SetDwm(h, Native.DWMWA_BORDER_COLOR, BarNative.DWMWA_COLOR_NONE);
                SetAccent(h, 0, 0);
                Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, Native.DWMSBT_NONE);
                Native.ExtendFrame(h, true);
                return;
            }
            if (forBar && !AppConfig.Current.BarBackground) rounded = false;   // floating glass: the tile-shaped window region rounds instead
            Native.SetDwm(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, rounded ? Native.DWMWCP_ROUND : Native.DWMWCP_DONOTROUND);
            Native.SetDwm(h, Native.DWMWA_BORDER_COLOR, BarNative.DWMWA_COLOR_NONE);
            bool accent = BackdropMode(forBar) == "accent";
            if (t.IsGlass)
            {
                Native.ExtendFrame(h, true);
                if (accent)
                {
                    Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, Native.DWMSBT_NONE);
                    // tint: AABBGGRR. Very light tint; the theme's BarBg provides the real colour.
                    SetAccent(h, 4, t.Dark ? 0x30141418 : 0x30F4F4F6);
                }
                else { SetAccent(h, 0, 0); Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, t.Backdrop); }
            }
            else
            {
                SetAccent(h, 0, 0);
                Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, Native.DWMSBT_NONE);
                Native.ExtendFrame(h, false);
            }
        }

        internal static void SetAccent(IntPtr h, int state, int color)
        {
            try
            {
                var ap = new BarNative.ACCENT_POLICY { AccentState = state, AccentFlags = state == 0 ? 0 : 2, GradientColor = color };
                int size = Marshal.SizeOf(ap); IntPtr p = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(ap, p, false);
                    var d = new BarNative.WINCOMPATTRDATA { Attribute = 19, Data = p, SizeOfData = size };
                    BarNative.SetWindowCompositionAttribute(h, ref d);
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }
        }

        // ---------------- taskbar ----------------
        static string FlagFile { get { return Path.Combine(Paths.Root, "taskbar-hidden.flag"); } }
        static int savedState = -1;
        public static bool TaskbarHidden;

        static readonly uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        /// <summary>Explorer's taskbars only (the tray widget may host its own "Shell_TrayWnd" inside our process).</summary>
        static List<IntPtr> Trays()
        {
            var l = new List<IntPtr>();
            foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                IntPtr h = IntPtr.Zero;
                while ((h = Native.FindWindowEx(IntPtr.Zero, h, cls, null)) != IntPtr.Zero)
                {
                    uint pid; Native.GetWindowThreadProcessId(h, out pid);
                    if (pid != myPid) l.Add(h);
                }
            }
            return l;
        }
        static IntPtr ExplorerTray()
        {
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
            {
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid != myPid) return h;
            }
            return IntPtr.Zero;
        }
        static Native.APPBARDATA TrayAbd()
        {
            var d = new Native.APPBARDATA(); d.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)); d.hWnd = ExplorerTray(); return d;
        }

        /// <summary>Hide every taskbar (primary + secondary) and set it to auto-hide so the work area is freed.
        /// Safe to call repeatedly (e.g. after explorer restarts): the user's original state is captured only once.</summary>
        public static void HideTaskbar()
        {
            try
            {
                if (!TaskbarHidden || savedState < 0)
                {
                    var d = TrayAbd();
                    if (savedState < 0)
                    {
                        int st = (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref d);
                        // a flag file from a previous run means the current state is ours, not the user's
                        int prev; if (File.Exists(FlagFile) && int.TryParse(File.ReadAllText(FlagFile).Trim(), out prev)) st = prev;
                        savedState = st;
                        try { File.WriteAllText(FlagFile, savedState.ToString()); } catch { }
                    }
                }
                var d2 = TrayAbd();
                d2.lParam = new IntPtr(Native.ABS_AUTOHIDE);
                Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d2);
                foreach (var h in Trays()) if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
                TaskbarHidden = true;
                HookTaskbarShow();
            }
            catch { }
        }

        // ---- instant re-hide: WinEvent SHOW/LOCATIONCHANGE hook scoped to explorer's process ----
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        static WinEventProc tbProc; static IntPtr tbHook; static uint tbPid; static HashSet<IntPtr> tbSet = new HashSet<IntPtr>();
        static void HookTaskbarShow()
        {
            var trays = Trays(); if (trays.Count == 0) return;
            uint pid; Native.GetWindowThreadProcessId(trays[0], out pid);
            tbSet = new HashSet<IntPtr>(trays);
            if (tbHook != IntPtr.Zero && pid == tbPid) return;
            UnhookTaskbarShow();
            tbPid = pid;
            if (tbProc == null) tbProc = (h, ev, w, obj, child, th, t) =>
            {
                if (!TaskbarHidden || obj != 0 || child != 0 || !tbSet.Contains(w)) return;
                if (Native.IsWindowVisible(w)) Native.ShowWindow(w, Native.SW_HIDE);   // explorer just re-showed it → hide at once
            };
            tbHook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, tbProc, pid, 0, 0 /*OUTOFCONTEXT*/);   // EVENT_OBJECT_SHOW, explorer only
        }
        static void UnhookTaskbarShow() { if (tbHook != IntPtr.Zero) { UnhookWinEvent(tbHook); tbHook = IntPtr.Zero; tbPid = 0; } }

        /// <summary>Cheap watchdog (IsWindowVisible only): Windows re-shows the taskbar on some actions (Win key, explorer restart).</summary>
        public static void EnsureTaskbarHidden()
        {
            if (!TaskbarHidden) return;
            try { foreach (var h in Trays()) if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE); } catch { }
        }

        /// <summary>Always safe to call (idempotent). Also recovers from a previous crash via the flag file.</summary>
        public static void RestoreTaskbar()
        {
            try
            {
                int st = savedState;
                if (st < 0 && File.Exists(FlagFile)) { int x; if (int.TryParse(File.ReadAllText(FlagFile).Trim(), out x)) st = x; }
                if (TaskbarHidden || st >= 0)
                {
                    var d = TrayAbd(); d.lParam = new IntPtr(st >= 0 ? st : Native.ABS_ALWAYSONTOP);
                    Native.SHAppBarMessage(Native.ABM_SETSTATE, ref d);
                }
                bool wasOurs = TaskbarHidden || st >= 0;
                UnhookTaskbarShow();
                TaskbarHidden = false; savedState = -1;
                // explorer applies the state change asynchronously and may re-hide the tray right after we show it:
                // keep re-showing for a short while (only when we were the ones hiding it)
                for (int i = 0; i < (wasOurs ? 12 : 1); i++)
                {
                    bool allVisible = true;
                    foreach (var h in Trays()) if (!Native.IsWindowVisible(h)) { allVisible = false; Native.ShowWindow(h, BarNative.SW_SHOWNA); }
                    if (!wasOurs || (allVisible && i >= 3)) break;
                    System.Threading.Thread.Sleep(100);
                }
                try { if (File.Exists(FlagFile)) File.Delete(FlagFile); } catch { }
            }
            catch { }
        }

        /// <summary>Last-resort recovery: show all taskbars and turn auto-hide off.</summary>
        public static void ForceShowTaskbar()
        {
            try
            {
                foreach (var h in Trays()) if (!Native.IsWindowVisible(h)) Native.ShowWindow(h, BarNative.SW_SHOWNA);
            }
            catch { }
        }

        // ---------------- startup ----------------
        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (on) { var v = "\"" + Application.ExecutablePath + "\""; if (!v.Equals(k.GetValue("NovaBar") as string)) k.SetValue("NovaBar", v); }
                    else if (k.GetValue("NovaBar") != null) k.DeleteValue("NovaBar", false);
                }
            }
            catch { }
        }

        // ---------------- fullscreen ----------------
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsZoomed")] static extern bool FsIsZoomed(IntPtr h);
        public static bool FullscreenOn(Screen scr, IntPtr self, IntPtr panel)
        {
            try
            {
                var fg = Native.GetForegroundWindow();
                // A normal maximized window (has a caption or resizable frame) is NOT fullscreen, even though with the
                // taskbar hidden it covers the whole monitor (and Windows may then report QUNS_BUSY). Real fullscreen
                // (games, F11 browsers, video players) drops the caption/frame.
                if (fg != IntPtr.Zero && FsIsZoomed(fg) && (Native.GetWindowLong(fg, -16 /*GWL_STYLE*/) & (0x00C00000 /*WS_CAPTION*/ | 0x00040000 /*WS_THICKFRAME*/)) != 0) return false;
                int st; if (Native.SHQueryUserNotificationState(out st) == 0 && (st == 3 || st == 4)) return true;   // D3D fullscreen / presentation mode
                if (fg == IntPtr.Zero || fg == self || fg == panel || fg == BarNative.GetDesktopWindow() || fg == BarNative.GetShellWindow()) return false;
                var cls = Native.WindowClass(fg);
                if (cls == "WorkerW" || cls == "Progman" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
                uint fpid; Native.GetWindowThreadProcessId(fg, out fpid);
                if (fpid == myPid) return false; // our own windows (panels, tray host, menus)
                Native.RECT r; if (!Native.GetWindowRect(fg, out r)) return false;
                var b = scr.Bounds;
                if (!(r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom)) return false;
                // must also be borderless: a captioned/resizable window that merely overhangs the screen isn't fullscreen
                return (Native.GetWindowLong(fg, -16) & (0x00C00000 | 0x00040000)) == 0;
            }
            catch { return false; }
        }

        // ---------------- error log (bounded) ----------------
        public static void Log(string where, Exception ex)
        {
            try
            {
                var f = Path.Combine(Paths.Root, "error.log");
                if (File.Exists(f) && new FileInfo(f).Length > 256 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("s") + " " + where + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
