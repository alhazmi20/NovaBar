// Tray = real notification-area icons. NovaBar replaces the taskbar, so we host a tray service the way
// ManagedShell / Cairo / RetroBar do: a hidden top-most window of class "Shell_TrayWnd" in our process.
// Shell_NotifyIcon() in every app does FindWindow("Shell_TrayWnd") (first in z-order = ours) and sends
// WM_COPYDATA(dwData=1, TRAYNOTIFYDATA). We keep the icon list and forward clicks to the owning app
// (callback message, NOTIFYICON_VERSION semantics). Coexistence with explorer:
//   * every tray message is ALSO forwarded to explorer's own Shell_TrayWnd, so explorer's tray stays in sync
//     (if NovaBar exits/crashes nothing is lost; balloons still become Windows toasts via explorer);
//   * every other WM_COPYDATA (AppBar = 0, etc.) and WM_USER+/WM_COMMAND message is forwarded to explorer
//     and its result returned, so SHAppBarMessage keeps working for every app (including NovaBar's own AppBar);
//   * on start (and, rate-limited, if explorer ever regains the top z-order) we broadcast "TaskbarCreated".
// Cost model (the tray sees 20-150 messages/s on a busy PC: G HUB re-sends the same icon ~20x/s, Task Manager
// re-sends 8 icons every second, every app re-adds everything after a TaskbarCreated broadcast):
//   * the message is parsed in place from the COPYDATA buffer (no struct marshalling, no strings unless the tip changed);
//   * an icon is converted to a bitmap only when its pixels changed (64-bit hash of the HICON's bitmap bits);
//   * nothing is repainted unless something visible changed, and tray repaints are coalesced to <= 4/s;
//   * forwarding goes only to *explorer's* tray (never to another NovaBar instance: two hosts used to ping-pong).
// If the window can't be created we fall back to the registry list (HKCU\Control Panel\NotifyIconSettings).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NovaBar
{
    public class CtlTrayIcon
    {
        public IntPtr HWnd; public uint UID, CallbackMsg, Version, Pid; public Guid Guid; public bool HasGuid, Hidden;
        public string Tip = "", Exe = "", Path; public Bitmap Raw;
        public Bitmap Scaled; public int ScaledPx;
        public Rectangle ScreenRect;    // where the widget last drew it (for Shell_NotifyIconGetRect / menu anchor)
        public bool Promoted;           // fallback mode only (Explorer's IsPromoted)
        public bool Fallback;           // item comes from the registry list, not from a live NIM_ADD
        public string PinKey;           // CtlTrayPins identity (cached)
        public ulong IconHash; public IntPtr IconHandle;
        int mono = -1;                  // 1 = white monochrome glyph (made for a dark taskbar)
        Bitmap inv; int invPx;

        /// <summary>Replace the icon bitmap (UI thread). Drops every derived bitmap.</summary>
        public void SetRaw(Bitmap b)
        {
            var old = Raw; Raw = b;
            if (Scaled != null) Scaled.Dispose(); Scaled = null;
            if (inv != null) inv.Dispose(); inv = null;
            mono = -1;
            if (old != null) old.Dispose();
        }
        public Bitmap Get(int px) { return Get(px, true); }
        /// <summary>Scaled icon; on light themes white monochrome glyphs are drawn dark so they stay visible.</summary>
        public Bitmap Get(int px, bool darkTheme)
        {
            if (Raw == null) return null;
            if (Scaled == null || ScaledPx != px) { if (Scaled != null) Scaled.Dispose(); Scaled = CtlIcons.Scale(Raw, px); ScaledPx = px; if (inv != null) inv.Dispose(); inv = null; }
            if (darkTheme) return Scaled;
            if (mono < 0) mono = IsWhiteMono(Raw) ? 1 : 0;
            if (mono == 0) return Scaled;
            if (inv == null || invPx != px)
            {
                var b = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(b)) using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    var cm = new System.Drawing.Imaging.ColorMatrix(new[] { new float[] { -1, 0, 0, 0, 0 }, new float[] { 0, -1, 0, 0, 0 }, new float[] { 0, 0, -1, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0.95f, 0.95f, 0.95f, 0, 1 } });
                    ia.SetColorMatrix(cm);
                    g.DrawImage(Scaled, new Rectangle(0, 0, px, px), 0, 0, px, px, GraphicsUnit.Pixel, ia);
                }
                inv = b; invPx = px;
            }
            return inv;
        }
        static bool IsWhiteMono(Bitmap b)
        {
            try
            {
                var rc = new Rectangle(0, 0, b.Width, b.Height);
                var d = b.LockBits(rc, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                int[] px = new int[b.Width * b.Height];
                try { for (int y = 0; y < b.Height; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * b.Width, b.Width); }
                finally { b.UnlockBits(d); }
                int n = 0, light = 0;
                foreach (var v in px)
                {
                    int a = (v >> 24) & 255; if (a < 80) continue; n++;
                    int r = (v >> 16) & 255, g = (v >> 8) & 255, bl = v & 255;
                    int mx = Math.Max(r, Math.Max(g, bl)), mn = Math.Min(r, Math.Min(g, bl));
                    if (mn > 170 && mx - mn < 40) light++;
                }
                return n > 4 && light > n * 0.8;
            }
            catch { return false; }
        }
        public string Name { get { var t = (Tip ?? "").Split('\n')[0].Trim(); return t.Length > 0 ? t : Exe; } }
    }

    public static class CtlTrayService
    {
        // ---------------- interop ----------------
        delegate IntPtr WndProcD(IntPtr h, uint m, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX { public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm; }
        [StructLayout(LayoutKind.Sequential)] struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [StructLayout(LayoutKind.Sequential)] struct ICONIDENT { public int dwMagic, dwMessage, cbSize, dwPadding; public uint hWnd, uID; public Guid guidItem; }
        [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
        [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWindowEx(int ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
        [DllImport("user32.dll")] static extern bool SendNotifyMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr h);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int cb, ref BITMAP bm);
        [DllImport("gdi32.dll")] static extern int GetBitmapBits(IntPtr h, int cb, byte[] bits);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string n);
        [DllImport("user32.dll")] static extern uint GetDoubleClickTime();

        const uint WM_COPYDATA = 0x4A, WM_USER = 0x400, WM_COMMAND = 0x111;
        const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETFOCUS = 3, NIM_SETVERSION = 4;
        const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_STATE = 8, NIF_GUID = 0x20;
        const uint NIS_HIDDEN = 1;
        // mouse / notification codes sent to the icon's callback
        public const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203,
            WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_CONTEXTMENU = 0x7B, NIN_SELECT = 0x400;
        // TRAYNOTIFYDATAW layout (32-bit handles even in 64-bit processes): signature, message, then NOTIFYICONDATAW
        const int O_MSG = 4, O_HWND = 12, O_UID = 16, O_FLAGS = 20, O_CBMSG = 24, O_HICON = 28, O_TIP = 32, O_STATE = 288, O_STATEMASK = 292, O_VERSION = 808, O_GUID = 944;

        // ---------------- state (UI thread) ----------------
        static WndProcD proc, childProc;
        static IntPtr hwnd, child;
        static uint taskbarCreated;
        static readonly uint myPid = (uint)Process.GetCurrentProcess().Id;
        static readonly List<CtlTrayIcon> icons = new List<CtlTrayIcon>();
        static readonly HashSet<Widget> listeners = new HashSet<Widget>();
        static Timer guard, flush;
        static Rectangle lastRect;
        static int lastBroadcast = Environment.TickCount - 600000, rebroadcastAt;
        static bool dirtyVisible, dirtyTip;
        static List<CtlTrayIcon> visible;
        public static bool Running { get { return hwnd != IntPtr.Zero; } }
        public static IntPtr Hwnd { get { return hwnd; } }
        public static int Changes;
        public static string LastError;
        /// <summary>Message counters (cheap ints, for diagnostics/measurements).</summary>
        public static int StatMsgs, StatNotify, StatSkipped, StatIconConv, StatRepaints;

        /// <summary>Visible icons, cached until something visible changes (no per-frame allocation).</summary>
        public static List<CtlTrayIcon> Icons
        {
            get
            {
                if (visible == null) visible = icons.Where(i => !i.Hidden && i.Raw != null).ToList();
                return visible;
            }
        }

        public static void Attach(Widget w) { listeners.Add(w); if (listeners.Count == 1) Start(); }
        public static void Detach(Widget w) { if (listeners.Remove(w) && listeners.Count == 0) Stop(); }

        /// <summary>Schedule a coalesced repaint. visibleChange=false means only the tooltip text changed (panel only).</summary>
        static void MarkDirty(bool visibleChange)
        {
            if (visibleChange) { dirtyVisible = true; visible = null; Changes++; } else dirtyTip = true;
            if (flush != null && !flush.Enabled) flush.Start();
        }
        static void Flush()
        {
            flush.Stop();
            bool v = dirtyVisible, t = dirtyTip; dirtyVisible = dirtyTip = false;
            foreach (var w in listeners.ToArray())
            {
                bool panel = false; try { panel = w.Host != null && w.Host.IsPanelOpen(w); } catch { }
                if (v || (t && panel)) { StatRepaints++; w.Invalidate(); }
            }
        }
        /// <summary>Repaint every tray tile now (pin state changed).</summary>
        public static void Repaint() { foreach (var w in listeners.ToArray()) w.Invalidate(); }

        static bool Start()
        {
            if (hwnd != IntPtr.Zero) return true;
            try
            {
                var inst = GetModuleHandle(null);
                proc = WndProc; childProc = (h, m, w, l) => DefWindowProc(h, m, w, l);
                var wc = new WNDCLASSEX { cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc), hInstance = inst, lpszClassName = "Shell_TrayWnd" };
                if (RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410 /*already exists*/) { LastError = "RegisterClass " + Marshal.GetLastWin32Error(); return false; }
                var wc2 = new WNDCLASSEX { cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(childProc), hInstance = inst, lpszClassName = "TrayNotifyWnd" };
                RegisterClassEx(ref wc2);
                hwnd = CreateWindowEx(Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW, "Shell_TrayWnd", "", 0x80000000 /*WS_POPUP*/ | 0x04000000 | 0x02000000, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) { LastError = "CreateWindow " + Marshal.GetLastWin32Error(); return false; }
                child = CreateWindowEx(0, "TrayNotifyWnd", "", 0x40000000 /*WS_CHILD*/, 0, 0, 0, 0, hwnd, IntPtr.Zero, inst, IntPtr.Zero);
                taskbarCreated = RegisterWindowMessage("TaskbarCreated");
                flush = new Timer { Interval = 250 }; flush.Tick += (o, e) => Flush();
                MakeTopmost();
                Broadcast();
                // Explorer can retake the top z-order. One FindWindow every 5 s is the cheapest check; taking the spot back
                // is a SetWindowPos, but asking every app to re-add its icons (a few hundred messages) is rate-limited.
                guard = new Timer { Interval = 5000 };
                guard.Tick += (o, e) => GuardTick();
                guard.Start();
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; hwnd = IntPtr.Zero; return false; }
        }
        static void GuardTick()
        {
            if (hwnd == IntPtr.Zero) return;
            if (icons.RemoveAll(i => i.HWnd != IntPtr.Zero && !CtlNative.IsWindow(i.HWnd)) > 0) MarkDirty(true);   // owner died without NIM_DELETE
            int now = Environment.TickCount;
            if (Native.FindWindow("Shell_TrayWnd", null) != hwnd)
            {
                MakeTopmost();
                if (now - lastBroadcast > 60000) Broadcast();
            }
            else if (rebroadcastAt != 0 && now - rebroadcastAt >= 0) { rebroadcastAt = 0; Broadcast(); }
        }
        static void Stop()
        {
            if (guard != null) { guard.Stop(); guard.Dispose(); guard = null; }
            if (flush != null) { flush.Stop(); flush.Dispose(); flush = null; }
            if (hwnd == IntPtr.Zero) return;
            var statsFile = Environment.GetEnvironmentVariable("NOVABAR_TRAYSTATS");   // measurement aid, off by default
            if (!string.IsNullOrEmpty(statsFile)) try { System.IO.File.WriteAllText(statsFile, "msgs=" + StatMsgs + " notify=" + StatNotify + " skipped=" + StatSkipped + " iconConversions=" + StatIconConv + " repaints=" + StatRepaints + " icons=" + icons.Count); } catch { }
            try { if (child != IntPtr.Zero) DestroyWindow(child); DestroyWindow(hwnd); } catch { }
            hwnd = IntPtr.Zero; child = IntPtr.Zero;
            foreach (var i in icons) i.SetRaw(null);
            icons.Clear(); visible = null;
            Broadcast();   // let apps re-register with explorer (it already has them via forwarding; harmless)
        }
        static void MakeTopmost() { Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); }
        public static void Refresh() { Broadcast(); }
        static void Broadcast() { lastBroadcast = Environment.TickCount; if (taskbarCreated != 0) SendNotifyMessage(new IntPtr(0xFFFF), taskbarCreated, IntPtr.Zero, IntPtr.Zero); }

        /// <summary>Keep our (hidden) tray window's rect equal to the bar so apps that GetWindowRect(Shell_TrayWnd) position popups sensibly.</summary>
        public static void SetBarRect(Rectangle r)
        {
            if (hwnd == IntPtr.Zero || r == lastRect || r.Width <= 0) return;
            lastRect = r;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, Native.SWP_NOACTIVATE);
        }

        // ---------------- forwarding to explorer (only explorer: never another tray host) ----------------
        static IntPtr exTray; static int exTrayMiss;
        static IntPtr ExplorerTray()
        {
            if (exTray != IntPtr.Zero && CtlNative.IsWindow(exTray)) return exTray;
            exTray = IntPtr.Zero;
            int now = Environment.TickCount;
            if (exTrayMiss != 0 && now - exTrayMiss < 2000) return IntPtr.Zero;   // don't search on every message while explorer is gone
            uint shellPid = 0; var sh = CtlNative.GetShellWindow(); if (sh != IntPtr.Zero) Native.GetWindowThreadProcessId(sh, out shellPid);
            IntPtr h = IntPtr.Zero, any = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
            {
                if (h == hwnd) continue;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == myPid) continue;
                if (shellPid != 0 && pid == shellPid) { exTray = h; break; }
                if (any == IntPtr.Zero) { var p = CtlNative.ProcessPath(pid); if (p != null && p.EndsWith("\\explorer.exe", StringComparison.OrdinalIgnoreCase)) any = h; }
            }
            if (exTray == IntPtr.Zero) exTray = any;
            exTrayMiss = exTray == IntPtr.Zero ? (now | 1) : 0;
            return exTray;
        }
        static IntPtr Forward(uint m, IntPtr w, IntPtr l, uint timeout)
        {
            var ex = ExplorerTray(); if (ex == IntPtr.Zero) return IntPtr.Zero;
            IntPtr r; CtlNative.SendMessageTimeoutI(ex, m, w, l, CtlNative.SMTO_ABORTIFHUNG, timeout, out r);
            return r;
        }

        static IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            try
            {
                if (m == WM_COPYDATA && l != IntPtr.Zero)
                {
                    var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(l, typeof(COPYDATASTRUCT));
                    long kind = (long)cds.dwData;
                    if (kind == 1)
                    {
                        StatMsgs++;
                        bool ok = HandleNotify(cds.lpData, cds.cbData);
                        Forward(m, w, l, 500);                        // keep explorer's tray in sync
                        return new IntPtr(ok ? 1 : 0);
                    }
                    if (kind == 3) { var r = IconRect(cds); if (r != IntPtr.Zero) return r; }
                    return Forward(m, w, l, 3000);                    // AppBar (0) and anything else -> explorer
                }
                if (m == taskbarCreated)
                {
                    // explorer restarted (not our own broadcast): take the top spot back and re-ask apps once it settled
                    if (Environment.TickCount - lastBroadcast > 3000) { exTray = IntPtr.Zero; exTrayMiss = 0; MakeTopmost(); rebroadcastAt = (Environment.TickCount + 4000) | 1; }
                    return IntPtr.Zero;
                }
                if (m >= WM_USER || m == WM_COMMAND) return Forward(m, w, l, 1000);
            }
            catch { }
            return DefWindowProc(h, m, w, l);
        }

        static CtlTrayIcon Find(uint hWnd, uint uid, Guid g, bool hasGuid)
        {
            var hw = new IntPtr((int)hWnd);
            for (int k = 0; k < icons.Count && hasGuid; k++) if (icons[k].HasGuid && icons[k].Guid == g) return icons[k];
            for (int k = 0; k < icons.Count; k++) { var i = icons[k]; if (i.HWnd == hw && i.UID == uid && (!hasGuid || !i.HasGuid || i.Guid == g)) return i; }
            return null;
        }

        // ---- in-place readers (bounds-checked against cbData; missing fields read as 0) ----
        static uint U32(IntPtr p, int cb, int off) { return off + 4 <= cb ? unchecked((uint)Marshal.ReadInt32(p, off)) : 0; }
        static Guid ReadGuid(IntPtr p, int cb, int off)
        {
            if (off + 16 > cb) return Guid.Empty;
            return new Guid(Marshal.ReadInt32(p, off), Marshal.ReadInt16(p, off + 4), Marshal.ReadInt16(p, off + 6),
                Marshal.ReadByte(p, off + 8), Marshal.ReadByte(p, off + 9), Marshal.ReadByte(p, off + 10), Marshal.ReadByte(p, off + 11),
                Marshal.ReadByte(p, off + 12), Marshal.ReadByte(p, off + 13), Marshal.ReadByte(p, off + 14), Marshal.ReadByte(p, off + 15));
        }
        /// <summary>Returns the tip only when it differs from <paramref name="cur"/> (else null): no allocation for repeats.</summary>
        static string TipIfChanged(IntPtr p, int cb, string cur)
        {
            int max = Math.Min(128, (cb - O_TIP) / 2); if (max <= 0) return cur.Length == 0 ? null : "";
            int n = 0; bool same = true;
            for (; n < max; n++)
            {
                char ch = (char)Marshal.ReadInt16(p, O_TIP + n * 2);
                if (ch == '\0') break;
                if (same && (n >= cur.Length || cur[n] != ch)) same = false;
            }
            if (same && n == cur.Length) return null;
            return Marshal.PtrToStringUni(p + O_TIP, n);
        }

        static byte[] hashBuf = new byte[4096];
        /// <summary>64-bit FNV-1a of the icon's colour (or mask) bits + size. ~1 µs for a 32x32 icon; 0 on failure.</summary>
        static ulong IconHashOf(IntPtr hIcon)
        {
            ICONINFO ii;
            if (hIcon == IntPtr.Zero || !GetIconInfo(hIcon, out ii)) return 0;
            try
            {
                ulong hsh = 14695981039346656037UL;
                foreach (var hb in new[] { ii.hbmColor, ii.hbmMask })
                {
                    if (hb == IntPtr.Zero) continue;
                    var bm = new BITMAP();
                    if (GetObject(hb, Marshal.SizeOf(typeof(BITMAP)), ref bm) == 0) continue;
                    int n = bm.bmWidthBytes * bm.bmHeight; if (n <= 0 || n > 4 << 20) continue;
                    if (hashBuf.Length < n) hashBuf = new byte[n];
                    n = GetBitmapBits(hb, n, hashBuf);
                    hsh = (hsh ^ (ulong)(uint)bm.bmWidth) * 1099511628211UL; hsh = (hsh ^ (ulong)(uint)bm.bmHeight) * 1099511628211UL;
                    for (int i = 0; i < n; i++) hsh = (hsh ^ hashBuf[i]) * 1099511628211UL;
                }
                return hsh == 0 ? 1 : hsh;
            }
            finally { if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor); if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask); }
        }

        static bool HandleNotify(IntPtr p, int cb)
        {
            if (p == IntPtr.Zero || cb < O_HICON + 4) return false;
            uint msg = U32(p, cb, O_MSG), hWnd = U32(p, cb, O_HWND), uid = U32(p, cb, O_UID), flags = U32(p, cb, O_FLAGS);
            Guid guid = (flags & NIF_GUID) != 0 ? ReadGuid(p, cb, O_GUID) : Guid.Empty;
            bool hasGuid = guid != Guid.Empty;
            var ic = Find(hWnd, uid, guid, hasGuid);
            switch (msg)
            {
                case NIM_ADD:
                case NIM_MODIFY:
                    {
                        bool isNew = false;
                        if (ic == null)
                        {
                            if (msg == NIM_MODIFY && (flags & NIF_ICON) == 0) return false;
                            ic = new CtlTrayIcon { HWnd = new IntPtr((int)hWnd), UID = uid, Guid = guid, HasGuid = hasGuid };
                            uint pid; Native.GetWindowThreadProcessId(ic.HWnd, out pid); ic.Pid = pid;
                            ic.Path = CtlNative.ProcessPath(pid);
                            ic.Exe = ic.Path != null ? System.IO.Path.GetFileNameWithoutExtension(ic.Path) : "";
                            icons.Add(ic); isNew = true;
                        }
                        bool wasShown = !ic.Hidden && ic.Raw != null, tipChanged = false, iconChanged = false;
                        if ((flags & NIF_MESSAGE) != 0) ic.CallbackMsg = U32(p, cb, O_CBMSG);
                        if ((flags & NIF_TIP) != 0) { var t = TipIfChanged(p, cb, ic.Tip ?? ""); if (t != null) { ic.Tip = t; tipChanged = true; } }
                        if ((flags & NIF_STATE) != 0 && (U32(p, cb, O_STATEMASK) & NIS_HIDDEN) != 0) ic.Hidden = (U32(p, cb, O_STATE) & NIS_HIDDEN) != 0;
                        uint hi = U32(p, cb, O_HICON);
                        if ((flags & NIF_ICON) != 0 && hi != 0)
                        {
                            var hIcon = new IntPtr((int)hi);
                            ulong hsh = IconHashOf(hIcon);
                            if (hsh == 0 || hsh != ic.IconHash || ic.Raw == null)
                            {
                                var copy = CopyIcon(hIcon);
                                if (copy != IntPtr.Zero)
                                {
                                    try { using (var i = Icon.FromHandle(copy)) { ic.SetRaw(i.ToBitmap()); ic.IconHash = hsh; ic.IconHandle = hIcon; iconChanged = true; StatIconConv++; } }
                                    catch { }
                                    finally { DestroyIcon(copy); }
                                }
                            }
                        }
                        bool shown = !ic.Hidden && ic.Raw != null;
                        if (isNew || shown != wasShown || (shown && iconChanged)) { StatNotify++; MarkDirty(true); }
                        else if (shown && tipChanged) { StatNotify++; MarkDirty(false); }
                        else StatSkipped++;                       // identical re-send (or a hidden icon): no work, no repaint
                        return true;
                    }
                case NIM_DELETE:
                    if (ic == null) return false;
                    icons.Remove(ic); ic.SetRaw(null); MarkDirty(true); return true;
                case NIM_SETVERSION:
                    {
                        uint v = U32(p, cb, O_VERSION);
                        if (ic != null) ic.Version = v;
                        return v <= 4;
                    }
                case NIM_SETFOCUS:
                    return true;
            }
            return false;
        }

        static IntPtr IconRect(COPYDATASTRUCT cds)
        {
            if (cds.cbData < Marshal.SizeOf(typeof(ICONIDENT))) return IntPtr.Zero;
            var id = (ICONIDENT)Marshal.PtrToStructure(cds.lpData, typeof(ICONIDENT));
            var ic = Find(id.hWnd, id.uID, id.guidItem, id.guidItem != Guid.Empty);
            if (ic == null) return IntPtr.Zero;
            var r = ic.ScreenRect;
            if (r.Width <= 0) { var p = Cursor.Position; r = new Rectangle(p.X - 12, p.Y - 12, 24, 24); }
            int x = id.dwMessage == 1 ? r.Left : r.Right, y = id.dwMessage == 1 ? r.Top : r.Bottom;
            return new IntPtr((y << 16) | (x & 0xFFFF));
        }

        // ---------------- clicks (UI thread, right after the user's click so we may grant foreground) ----------------
        /// <summary>MAKELONG(lo, hi) as an unsigned 32-bit value (zero-extended, like MAKEWPARAM/MAKELPARAM in C).</summary>
        public static IntPtr MakeLong(int lo, int hi) { return U(((uint)(hi & 0xFFFF) << 16) | (uint)(lo & 0xFFFF)); }
        static IntPtr U(uint v) { return IntPtr.Size == 8 ? new IntPtr((long)v) : new IntPtr(unchecked((int)v)); }
        /// <summary>The (wParam, lParam) pair Explorer sends to an icon's callback for <paramref name="code"/>.</summary>
        public static void Pack(uint version, uint uid, uint code, Point anchor, out IntPtr wp, out IntPtr lp)
        {
            if (version >= 4) { wp = MakeLong(anchor.X, anchor.Y); lp = MakeLong((int)code, (int)uid); }   // NOTIFYICON_VERSION_4
            else { wp = U(uid); lp = U(code); }                                                             // legacy: wParam = uID, lParam = message
        }
        static void Send(CtlTrayIcon ic, uint code, Point anchor)
        {
            IntPtr wp, lp; Pack(ic.Version, ic.UID, code, anchor, out wp, out lp);
            SendNotifyMessage(ic.HWnd, ic.CallbackMsg, wp, lp);
        }
        /// <summary>Anchor for v4 messages: the cursor when it is on the icon, else the icon's centre (screen coordinates).</summary>
        static Point Anchor(CtlTrayIcon ic)
        {
            var c = Cursor.Position; var r = ic.ScreenRect;
            if (r.Width <= 0 || r.Contains(c)) return c;
            return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }
        static CtlTrayIcon lastClick; static int lastClickTick;
        /// <summary>Exactly what Explorer sends: left = DOWN, UP (+ NIN_SELECT for v4); a second left click on the same icon
        /// within the double-click time = DBLCLK, UP; right = RBUTTONDOWN, RBUTTONUP (+ WM_CONTEXTMENU for v4) after making the
        /// owner able to take the foreground (so its TrackPopupMenu works and the menu closes when clicking elsewhere).</summary>
        public static void Click(CtlTrayIcon ic, MouseButtons b)
        {
            if (ic == null || ic.HWnd == IntPtr.Zero || ic.CallbackMsg == 0) return;
            if (!CtlNative.IsWindow(ic.HWnd)) { icons.Remove(ic); ic.SetRaw(null); MarkDirty(true); return; }
            CtlNative.AllowSetForegroundWindow((int)ic.Pid);
            var a = Anchor(ic);
            if (b == MouseButtons.Right)
            {
                lastClick = null;
                Native.SetForegroundWindow(ic.HWnd);                  // what the app itself does before TrackPopupMenu; we hold the input right now
                Send(ic, WM_RBUTTONDOWN, a); Send(ic, WM_RBUTTONUP, a);
                if (ic.Version >= 4) Send(ic, WM_CONTEXTMENU, a);
                return;
            }
            if (b == MouseButtons.Middle) { lastClick = null; Send(ic, WM_MBUTTONDOWN, a); Send(ic, WM_MBUTTONUP, a); return; }
            int now = Environment.TickCount;
            bool dbl = lastClick == ic && (uint)(now - lastClickTick) <= GetDoubleClickTime();
            lastClick = dbl ? null : ic; lastClickTick = now;
            if (dbl) { Send(ic, WM_LBUTTONDBLCLK, a); Send(ic, WM_LBUTTONUP, a); }
            else
            {
                Send(ic, WM_LBUTTONDOWN, a); Send(ic, WM_LBUTTONUP, a);
                if (ic.Version >= 4) Send(ic, NIN_SELECT, a);
            }
        }
        /// <summary>One WM_MOUSEMOVE when the pointer enters an icon (apps use it for rich tooltips / NIN_POPUPOPEN).</summary>
        public static void Hover(CtlTrayIcon ic) { if (ic != null && ic.HWnd != IntPtr.Zero && ic.CallbackMsg != 0) Send(ic, WM_MOUSEMOVE, Anchor(ic)); }

        /// <summary>Self-check of the message packing (run by the render harness). Returns null when OK.</summary>
        public static string SelfTest()
        {
            IntPtr w, l;
            Pack(4, 7, WM_CONTEXTMENU, new Point(1920 + 37, 1040), out w, out l);
            if ((long)w != ((1040L << 16) | 1957) || (long)l != ((7L << 16) | 0x7B)) return "v4 context pack " + w + "/" + l;
            Pack(4, 0xFFFF, NIN_SELECT, new Point(-5, 3000), out w, out l);       // negative x on a left monitor
            if ((long)w != ((3000L << 16) | 0xFFFB) || (long)l != 0xFFFF0400L) return "v4 sign " + ((long)w).ToString("X") + "/" + ((long)l).ToString("X");
            Pack(0, 0xFFFFFFFF, WM_RBUTTONUP, Point.Empty, out w, out l);
            if ((ulong)(long)w != (IntPtr.Size == 8 ? 0xFFFFFFFFUL : 0xFFFFFFFFFFFFFFFFUL) || (long)l != 0x205) return "legacy pack " + w + "/" + l;
            Pack(3, 2, WM_LBUTTONUP, new Point(5, 5), out w, out l);
            if ((long)w != 2 || (long)l != 0x202) return "v3 pack " + w + "/" + l;
            return null;
        }
    }

    // ------------------------------------------------------------------ fallback (registry) list
    public static class CtlTrayFallback
    {
        public const string Key = "ctl.trayfb";
        public class Snap
        {
            public List<CtlTrayIcon> Items = new List<CtlTrayIcon>();
            public override bool Equals(object o) { var x = o as Snap; return x != null && x.Items.Select(i => i.Exe + i.Pid).SequenceEqual(Items.Select(i => i.Exe + i.Pid)); }
            public override int GetHashCode() { return Items.Count; }
        }
        static CtlTrayFallback() { Hub.Define(Key, 15000, Sample); }
        public static void Init() { }
        static object Sample()
        {
            var running = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) { try { if (!running.ContainsKey(p.ProcessName)) running[p.ProcessName] = (uint)p.Id; } catch { } finally { p.Dispose(); } }
            var snap = new Snap(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
            {
                if (root == null) return snap;
                foreach (var sub in root.GetSubKeyNames())
                {
                    try
                    {
                        using (var k = root.OpenSubKey(sub))
                        {
                            var ep = k.GetValue("ExecutablePath") as string; if (string.IsNullOrEmpty(ep)) continue;
                            var exe = System.IO.Path.GetFileNameWithoutExtension(ep);
                            uint pid; if (!running.TryGetValue(exe, out pid) || exe.Equals("explorer", StringComparison.OrdinalIgnoreCase) || !seen.Add(exe)) continue;
                            var path = CtlNative.ProcessPath(pid);
                            var png = k.GetValue("IconSnapshot") as byte[];
                            var it = new CtlTrayIcon { Exe = exe, Pid = pid, Path = path, Fallback = true, Promoted = Convert.ToInt32(k.GetValue("IsPromoted", 0)) != 0 };
                            it.Tip = (k.GetValue("InitialTooltip") as string ?? "").Trim();
                            it.Raw = CtlIcons.Get("trayfb:" + exe, 32, () => CtlIcons.FromFile(path, 32) ?? (png != null ? CtlIcons.FromPng(png, 32) : null));
                            snap.Items.Add(it);
                        }
                    }
                    catch { }
                }
            }
            snap.Items = snap.Items.OrderByDescending(i => i.Promoted).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return snap;
        }
        public static void OpenOverflow() { Ctl.Async(() => { Native.KeyChord(Native.VK_LWIN, 0x42 /*B*/); System.Threading.Thread.Sleep(180); Native.KeyChord(0x0D); }); }
        public static void Click(CtlTrayIcon it)
        {
            IntPtr found = IntPtr.Zero;
            CtlNative.EnumWindows((h, l) =>
            {
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == it.Pid && CtlTasks.IsTaskWindow(h)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) CtlNative.Activate(found); else OpenOverflow();
        }
    }

    // ------------------------------------------------------------------ Tray widget
    [Widget("tray", "Tray", "System", "Your tray icons: pinned ones on the bar, the rest one click away; right-click opens the app's own menu.", RefName = "Tray", Data = DataKind.Live)]
    public class CtlTrayWidget : Widget
    {
        static CtlTrayWidget() { CtlTrayFallback.Init(); }
        const string PinGlyph = "", UnpinGlyph = "", GripGlyph = "";
        bool hosting; CtlTrayIcon hover, lastHoverSent;
        bool customize, openCustomize; int scroll;
        CtlTrayPanelHook hook;
        readonly List<KeyValuePair<RectangleF, CtlTrayIcon>> cells = new List<KeyValuePair<RectangleF, CtlTrayIcon>>();   // panel client coords
        readonly List<KeyValuePair<RectangleF, CtlTrayIcon>> pinRows = new List<KeyValuePair<RectangleF, CtlTrayIcon>>(); // customize: pinned rows
        CtlTrayIcon dragIc; float dragY = -1;

        public override SettingDef[] SettingDefs
        {
            get { return new[] { SettingDef.Toggle("host", "Host tray icons (needed when the Windows taskbar is hidden)", true), SettingDef.Number("max", "Max inline icons (pinned icons shown on the bar)", 8, 0, 30) }; }
        }
        public override void OnStart()
        {
            bool harness = Host != null && Host.GetType().DeclaringType != null && Host.GetType().DeclaringType.Name == "RenderTest";
            if (harness)
            {
                var err = CtlTrayService.SelfTest(); if (err != null) throw new Exception("tray message packing self-test failed: " + err);
                customize = SStr("panelMode") == "customize";   // harness-only knob to render the Customize view
            }
            // Only the real bar (or the render harness) may host the tray: a second host in another process (e.g. the
            // Settings window's live preview) would fight the bar for the Shell_TrayWnd spot and double all traffic.
            hosting = SBool("host") && (harness || (Host != null && Host.GetType().Name == "BarForm"));
            if (hosting) { CtlTrayService.Attach(this); hosting = CtlTrayService.Running; }
            if (!hosting) Hub.Subscribe(CtlTrayFallback.Key, this);
        }
        public override void OnStop() { CtlTrayService.Detach(this); Hub.Unsubscribe(CtlTrayFallback.Key, this); DropHook(); }

        List<CtlTrayIcon> Items
        {
            get
            {
                if (hosting) return CtlTrayService.Icons;
                var s = Hub.Get<CtlTrayFallback.Snap>(CtlTrayFallback.Key); return s != null ? s.Items : new List<CtlTrayIcon>();
            }
        }
        int Max { get { return Math.Max(0, (int)SNum("max")); } }

        // pinned (inline) / overflow split, cached until icons, pins or the max change
        List<CtlTrayIcon> cInline = new List<CtlTrayIcon>(), cOver = new List<CtlTrayIcon>(), cPinned = new List<CtlTrayIcon>();
        object cSrc; int cChanges = -1, cPins = -1, cMax = -1;
        void Split()
        {
            var l = Items;
            if (l == cSrc && cChanges == CtlTrayService.Changes && cPins == CtlTrayPins.Version && cMax == Max) return;
            var pinned = l.Where(CtlTrayPins.IsPinned).OrderBy(CtlTrayPins.OrderOf).ToList();
            cPinned = pinned;
            cInline = pinned.Take(Max).ToList();
            cOver = pinned.Skip(Max).Concat(l.Where(i => !CtlTrayPins.IsPinned(i))).ToList();
            cSrc = l; cChanges = CtlTrayService.Changes; cPins = CtlTrayPins.Version; cMax = Max;
        }

        void Act(CtlTrayIcon ic, MouseButtons b)
        {
            if (hosting) CtlTrayService.Click(ic, b);
            else if (b == MouseButtons.Left) CtlTrayFallback.Click(ic);
            else CtlTrayFallback.OpenOverflow();
        }
        void SetPinned(CtlTrayIcon ic, bool on) { CtlTrayPins.SetPinned(ic, on); CtlTrayService.Repaint(); Invalidate(); }

        public override bool HandlesRightClick() { return hover != null; }
        public override void OnClick(MouseButtons b)
        {
            if (b == MouseButtons.Right && hover != null)
            {
                if ((Control.ModifierKeys & Keys.Control) != 0) ShowPinMenu(hover, Cursor.Position);   // Ctrl+right-click: our own pin menu
                else Act(hover, b);
                return;
            }
            if (b == MouseButtons.Middle && hover != null) { Act(hover, b); return; }
            base.OnClick(b);
        }
        public override void AddMenuItems(List<KeyValuePair<string, Action>> items)
        {
            items.Add(new KeyValuePair<string, Action>("Customize tray icons…", OpenCustomize));
            items.Add(new KeyValuePair<string, Action>("Tray settings…", () => Host.OpenSettings("widgets:" + InstId)));
            if (hosting) items.Add(new KeyValuePair<string, Action>("Refresh tray icons", () => CtlTrayService.Refresh()));
        }
        void OpenCustomize() { openCustomize = true; Host.OpenPanel(this); }

        void ShowPinMenu(CtlTrayIcon ic, Point screen)
        {
            var cms = new ContextMenuStrip { Renderer = new BarMenuRenderer(Theme.Current), ShowImageMargin = false, ShowCheckMargin = true };
            float s = 1f; try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) s = Math.Max(1f, g.DpiX / 96f); } catch { }
            cms.Font = Ui.Font(13f * s); cms.Padding = new Padding(2, 4, 2, 4);
            var head = new ToolStripMenuItem(ic.Name) { Enabled = false }; cms.Items.Add(head);
            bool pinned = CtlTrayPins.IsPinned(ic);
            var tg = new ToolStripMenuItem(pinned ? "Move to overflow" : "Pin to bar"); tg.Click += (o, e) => SetPinned(ic, !pinned); cms.Items.Add(tg);
            var menu = new ToolStripMenuItem("Open app menu"); menu.Click += (o, e) => Act(ic, MouseButtons.Right); cms.Items.Add(menu);
            cms.Items.Add(new ToolStripSeparator());
            var cu = new ToolStripMenuItem("Customize tray icons…"); cu.Click += (o, e) => OpenCustomize(); cms.Items.Add(cu);
            var st = new ToolStripMenuItem("Tray settings…"); st.Click += (o, e) => Host.OpenSettings("widgets:" + InstId); cms.Items.Add(st);
            cms.Opened += (o, e) => Native.SetDwm(cms.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
            cms.Closed += (o, e) => { try { Host.Post(() => cms.Dispose()); } catch { } };
            if (CtlThumbs.BarSide == "right") cms.Show(screen, ToolStripDropDownDirection.BelowLeft);   // vertical bar on the right: open away from it
            else cms.Show(screen);
        }

        // ---------------- tile ----------------
        public override int Measure(DrawCtx c) { Split(); int n = cInline.Count; return c.Si(30) + n * c.Si(26) + (n > 0 ? c.Si(4) : 0); }
        // vertical bar (docked left/right): pinned icons + the chevron wrap into a grid by the sidebar width
        void VGeom(DrawCtx c, out float pad, out float gap, out int cols, out float cw, out float ch)
        {
            pad = c.S(4); gap = c.S(2); ch = c.S(28);
            float avail = Math.Max(c.S(28), c.Bounds.Width - 2 * pad);
            cols = Math.Max(1, (int)((avail + gap) / (c.S(28) + gap)));
            cw = (avail - (cols - 1) * gap) / cols;
        }
        public override int MeasureHeight(DrawCtx c)
        {
            if (!c.Vertical) return 0;
            Split();
            float pad, gap, cw, ch; int cols; VGeom(c, out pad, out gap, out cols, out cw, out ch);
            int n = cInline.Count + 1, rows = (n + cols - 1) / cols;   // +1 = the chevron cell
            return Math.Max(c.Si(34), (int)Math.Ceiling(2 * pad + rows * ch + (rows - 1) * gap));
        }
        void DrawVertical(DrawCtx c, Rectangle scr)
        {
            var b = c.Bounds; var l = cInline; int n = l.Count; int px = c.Si(16);
            float pad, gap, cw, ch; int cols; VGeom(c, out pad, out gap, out cols, out cw, out ch);
            CtlTrayIcon hov = null;
            for (int i = 0; i <= n; i++)
            {
                var r = new RectangleF(b.X + pad + (i % cols) * (cw + gap), b.Y + pad + (i / cols) * (ch + gap), cw, ch);
                if (i == n)
                {   // chevron: no Hit() → a click falls through to OnClick = toggle the overflow panel
                    bool open = false; try { open = Host != null && Host.IsPanelOpen(this); } catch { }
                    bool right = c.Edge == "right";
                    if (c.IsHover(r)) Ui.FillRound(c.G, r, c.S(6), c.T.TileHover);
                    Ui.Icon(c.G, (open ^ right) ? Icons.ChevronLeft : Icons.ChevronRight, r, c.T.TextDim, c.S(11));
                    if (cOver.Count > 0 && n > 0) Ui.FillCircle(c.G, new PointF(r.X + r.Width / 2 + c.S(7), r.Y + c.S(7)), c.S(2.5f), c.T.Accent);
                    break;
                }
                var it = l[i];
                if (c.IsHover(r)) { Ui.FillRound(c.G, r, c.S(6), c.T.TileHover); hov = it; }
                var ir = new RectangleF(r.X + (r.Width - px) / 2, r.Y + (r.Height - px) / 2, px, px);
                var bmp = it.Get(px, c.T.Dark);
                if (bmp != null) Ctl.DrawImg(c.G, bmp, ir); else Ui.FillRound(c.G, ir, c.S(4), c.T.Track);
                if (scr.Width > 0) it.ScreenRect = new Rectangle(scr.X + (int)(r.X - b.X), scr.Y + (int)(r.Y - b.Y), (int)r.Width, (int)r.Height);
                var ii = it; c.Hit(r, () => Act(ii, MouseButtons.Left));
            }
            hover = hov;
            if (hov != lastHoverSent) { lastHoverSent = hov; if (hosting && hov != null) CtlTrayService.Hover(hov); }
        }
        public override void Draw(DrawCtx c)
        {
            Split();
            if (c.Vertical)
            {
                Rectangle vs = Rectangle.Empty; try { if (Host != null) vs = Host.ScreenBoundsOf(this); } catch { }
                if (hosting && vs.Width > 0)
                {   // our hidden Shell_TrayWnd mirrors the sidebar: full monitor height, the bar's width
                    var mon = Screen.FromRectangle(vs).Bounds; int bp = c.Si(5);
                    CtlTrayService.SetBarRect(new Rectangle(vs.X - bp, mon.Y, vs.Width + 2 * bp, mon.Height));
                }
                DrawVertical(c, vs);
                return;
            }
            var b = c.Bounds; var l = cInline; int n = l.Count; float x = b.X + c.S(6); int px = c.Si(16);
            Rectangle scr = Rectangle.Empty; try { if (Host != null) scr = Host.ScreenBoundsOf(this); } catch { }
            if (hosting && scr.Width > 0)
            {
                var mon = Screen.FromRectangle(scr).Bounds; int bh = Math.Max(c.BarHeight, scr.Height);
                CtlTrayService.SetBarRect(new Rectangle(mon.X, scr.Y - (bh - scr.Height) / 2, mon.Width, bh));
            }
            CtlTrayIcon hov = null;
            for (int i = 0; i < n; i++)
            {
                var it = l[i];
                var r = new RectangleF(x, b.Y + c.S(5), c.S(26), b.Height - c.S(10));
                if (c.IsHover(r)) { Ui.FillRound(c.G, r, c.S(6), c.T.TileHover); hov = it; }
                var ir = new RectangleF(r.X + (r.Width - px) / 2, r.Y + (r.Height - px) / 2, px, px);
                var bmp = it.Get(px, c.T.Dark);
                if (bmp != null) Ctl.DrawImg(c.G, bmp, ir); else Ui.FillRound(c.G, ir, c.S(4), c.T.Track);
                if (scr.Width > 0) it.ScreenRect = new Rectangle(scr.X + (int)(r.X - b.X), scr.Y + (int)(r.Y - b.Y), (int)r.Width, (int)r.Height);
                var ii = it; c.Hit(r, () => Act(ii, MouseButtons.Left));
                x += c.S(26);
            }
            hover = hov;
            if (hov != lastHoverSent) { lastHoverSent = hov; if (hosting && hov != null) CtlTrayService.Hover(hov); }
            var cr = new RectangleF(b.Right - c.S(28), b.Y, c.S(22), b.Height);
            bool open = false; try { open = Host != null && Host.IsPanelOpen(this); } catch { }
            Ui.Icon(c.G, open ? Icons.ChevronDown : Icons.ChevronUp, cr, c.T.TextDim, c.S(11));
            if (cOver.Count > 0 && n > 0) Ui.FillCircle(c.G, new PointF(cr.X + cr.Width / 2 + c.S(6), b.Y + c.S(11)), c.S(2.5f), c.T.Accent);
        }

        // ---------------- panel ----------------
        const int Cols = 5, MaxRows = 9;
        public override void OnPanelOpen() { scroll = 0; if (openCustomize) { customize = true; openCustomize = false; } }
        public override void OnPanelClose() { DropHook(); customize = false; dragIc = null; dragY = -1; }

        public override Size PanelSize(float sc)
        {
            Split();
            if (customize)
            {
                int rows = Math.Min(MaxRows, Math.Max(1, cInline.Count + cOver.Count));
                return new Size((int)(340 * sc), (int)((48 + 2 * 24 + rows * 36 + 58) * sc));
            }
            int gPin = Math.Max(1, (int)Math.Ceiling(cInline.Count / (double)Cols)), gHid = Math.Max(1, (int)Math.Ceiling(cOver.Count / (double)Cols));
            return new Size((int)(330 * sc), (int)((48 + 2 * 26 + Math.Min(3, gPin) * 70 + Math.Min(4, gHid) * 70 + 8 + 22 + 54) * sc));
        }

        void EnsureHook()
        {
            if (hook != null && hook.Handle != IntPtr.Zero && CtlNative.IsWindow(hook.Handle)) return;
            DropHook();
            Form mine = null;
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f.IsDisposed || !f.IsHandleCreated || f.GetType().Name != "PanelForm") continue;
                    var fi = f.GetType().GetField("w", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (fi != null ? fi.GetValue(f) == this : f.Text == Meta.Name) { mine = f; break; }
                }
            }
            catch { }
            if (mine == null) return;
            hook = new CtlTrayPanelHook();
            hook.AssignHandle(mine.Handle);
            hook.RightClick = PanelRightClick;
            hook.DragStart = p => { if (!customize) return false; var it = PinRowAt(p); if (it == null) return false; dragIc = it; return true; };
            hook.DragMove = p => { dragY = p.Y; Invalidate(); };
            hook.DragEnd = (p, moved) =>
            {
                var it = dragIc; dragIc = null; dragY = -1;
                if (moved && it != null) { CtlTrayPins.MoveBefore(it, DropTarget(p.Y, it)); CtlTrayService.Repaint(); }
                Invalidate();
            };
            hook.CloseRequest = () => { try { Host.ClosePanel(); } catch { } };
        }
        void DropHook() { if (hook != null) { hook.Detach(); hook = null; } }

        CtlTrayIcon CellAt(Point p) { for (int i = cells.Count - 1; i >= 0; i--) if (cells[i].Key.Contains(p)) return cells[i].Value; return null; }
        CtlTrayIcon PinRowAt(Point p)
        {
            foreach (var kv in pinRows) if (kv.Key.Contains(p) && p.X < kv.Key.Right - 60) return kv.Value;   // not on the switch
            return null;
        }
        CtlTrayIcon DropTarget(float y, CtlTrayIcon moving)
        {
            foreach (var kv in pinRows) if (kv.Value != moving && y < kv.Key.Y + kv.Key.Height / 2) return kv.Value;
            return null;
        }
        void SetCellScreenRect(CtlTrayIcon ic, RectangleF clientRect)
        {
            try
            {
                var f = hook != null ? hook.Form : null; if (f == null) return;
                var o = f.PointToScreen(Point.Empty);
                ic.ScreenRect = new Rectangle(o.X + (int)clientRect.X, o.Y + (int)clientRect.Y, (int)clientRect.Width, (int)clientRect.Height);
            }
            catch { }
        }
        bool PanelRightClick(Point p)
        {
            var ic = CellAt(p); if (ic == null) return false;
            foreach (var kv in cells) if (kv.Value == ic) { SetCellScreenRect(ic, kv.Key); break; }
            if ((Control.ModifierKeys & (Keys.Shift | Keys.Control)) != 0) { ShowPinMenu(ic, Cursor.Position); return true; }
            if (hosting && hook != null) hook.HoldFor(ic.Pid);       // keep the panel up under the app's menu, close it after
            Act(ic, MouseButtons.Right);
            if (!hosting) { try { Host.ClosePanel(); } catch { } }
            return true;
        }

        public override void DrawPanel(DrawCtx c)
        {
            Split();
            EnsureHook();
            cells.Clear(); pinRows.Clear();
            if (customize) { DrawCustomize(c); return; }
            var b = c.Bounds; int total = cInline.Count + cOver.Count;
            float y = c.PanelHeader("Tray", total == 0 ? null : cInline.Count + " pinned · " + cOver.Count + " hidden", Icons.Tray);
            if (total == 0)
            {
                Ui.Text(c.G, hosting ? "Waiting for apps to add their icons…" : "No tray apps running", c.F(12), c.T.TextDim, new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(40)));
            }
            else
            {
                y = DrawSection(c, "Pinned to bar", cInline, true, y, 3);
                y = DrawSection(c, "Hidden", cOver, false, y + c.S(8), 4);
            }
            Ui.Text(c.G, "Right-click: app menu  ·  hover an icon to pin / unpin it", c.F(10), c.T.TextFaint,
                new RectangleF(b.X + c.S(16), b.Bottom - c.S(66), b.Width - c.S(32), c.S(16)), 0);
            float gap = c.S(8), bw = (b.Width - c.S(32) - gap) / 2;
            c.Button(new RectangleF(b.X + c.S(16), b.Bottom - c.S(42), bw, c.S(30)), "Customize", () => { customize = true; scroll = 0; Invalidate(); }, false, PinGlyph);
            if (hosting) c.Button(new RectangleF(b.X + c.S(16) + bw + gap, b.Bottom - c.S(42), bw, c.S(30)), "Refresh icons", () => CtlTrayService.Refresh(), false, Icons.Refresh);
            else c.Button(new RectangleF(b.X + c.S(16) + bw + gap, b.Bottom - c.S(42), bw, c.S(30)), "Hidden icons", () => { Host.ClosePanel(); CtlTrayFallback.OpenOverflow(); }, false, Icons.ChevronUp);
        }

        /// <summary>One titled grid of tray icons. pinned=true → hover button unpins (moves to Hidden), else pins.</summary>
        float DrawSection(DrawCtx c, string title, List<CtlTrayIcon> l, bool pinned, float y, int maxRows)
        {
            var b = c.Bounds;
            Ui.Text(c.G, title.ToUpperInvariant(), c.FS(10.5f), c.T.TextFaint, new RectangleF(b.X + c.S(16), y, b.Width * 0.6f, c.S(18)));
            Ui.Text(c.G, l.Count.ToString(), c.F(10.5f), c.T.TextFaint, new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(18)), 1);
            y += c.S(24);
            float cw = (b.Width - c.S(24)) / Cols, ch = c.S(66); int px = c.Si(24);
            if (l.Count == 0)
            {
                Ui.Text(c.G, pinned ? "Nothing pinned — hover an icon below and press the pin" : "Every icon is pinned to the bar", c.F(11.5f), c.T.TextDim,
                    new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), ch), 0);
                return y + ch + c.S(4);
            }
            int n = Math.Min(l.Count, Cols * maxRows);
            for (int i = 0; i < n; i++)
            {
                var it = l[i];
                var r = new RectangleF(b.X + c.S(12) + (i % Cols) * cw, y + (i / Cols) * (ch + c.S(4)), cw, ch);
                var cell = Ui.Inset(r, c.S(2), 0); bool h = c.IsHover(cell);
                if (h) Ui.FillRound(c.G, cell, c.S(10), c.T.TileHover);
                var ir = new RectangleF(r.X + (r.Width - px) / 2, r.Y + c.S(10), px, px);
                var bmp = it.Get(px, c.T.Dark);
                if (bmp != null) Ctl.DrawImg(c.G, bmp, ir); else Ui.FillRound(c.G, ir, c.S(6), c.T.Track);
                Ui.Text(c.G, it.Name, c.F(10), c.T.TextDim, new RectangleF(r.X + c.S(4), r.Y + c.S(42), r.Width - c.S(8), c.S(16)), 0);
                var ii = it;
                cells.Add(new KeyValuePair<RectangleF, CtlTrayIcon>(cell, it));
                c.Hit(cell, () => { foreach (var kv in cells) if (kv.Value == ii) { SetCellScreenRect(ii, kv.Key); break; } Act(ii, MouseButtons.Left); Host.ClosePanel(); });
                if (h)
                {
                    var mr = new RectangleF(cell.Right - c.S(21), cell.Y + c.S(3), c.S(18), c.S(18));
                    bool mh = c.IsHover(mr);
                    Ui.FillCircle(c.G, new PointF(mr.X + mr.Width / 2, mr.Y + mr.Height / 2), c.S(9), mh ? (pinned ? c.T.Bad : c.T.Accent) : c.T.Track);
                    Ui.Icon(c.G, pinned ? Icons.Remove : PinGlyph, mr, mh ? Color.White : c.T.Text, c.S(9));
                    c.Hit(mr, () => SetPinned(ii, !pinned));
                }
            }
            int rows = (int)Math.Ceiling(n / (double)Cols);
            return y + rows * (ch + c.S(4));
        }

        void DrawCustomize(DrawCtx c)
        {
            var b = c.Bounds;
            var pinned = cPinned; var unpinned = cOver.Where(i => !pinned.Contains(i)).ToList();
            float y = c.PanelHeader("Customize tray", pinned.Count + " pinned · max " + Max + " on bar", PinGlyph);
            float top = y, bottom = b.Bottom - c.S(58), rh = c.S(36);
            // rows: [label] pinned..., [label] unpinned...
            var rows = new List<KeyValuePair<string, CtlTrayIcon>>();
            rows.Add(new KeyValuePair<string, CtlTrayIcon>("ON THE BAR  ·  drag to reorder", null));
            foreach (var i in pinned) rows.Add(new KeyValuePair<string, CtlTrayIcon>(null, i));
            if (pinned.Count == 0) rows.Add(new KeyValuePair<string, CtlTrayIcon>("~Nothing pinned yet: switch an icon on below", null));
            rows.Add(new KeyValuePair<string, CtlTrayIcon>("IN THE OVERFLOW", null));
            foreach (var i in unpinned) rows.Add(new KeyValuePair<string, CtlTrayIcon>(null, i));
            float contentH = rows.Sum(r => r.Value == null ? c.S(24) : rh);
            float maxScroll = Math.Max(0, contentH - (bottom - top));
            scroll = (int)Math.Max(0, Math.Min(maxScroll, scroll));
            c.Wheel(new Rectangle((int)b.X, (int)top, b.Width, (int)(bottom - top)), d => { scroll -= (int)(d * rh); Invalidate(); });
            var st = c.G.Save();
            c.G.SetClip(new RectangleF(b.X, top, b.Width, bottom - top));
            float yy = top - scroll; int px = c.Si(20); int pinIdx = 0;
            foreach (var row in rows)
            {
                if (row.Value == null)
                {
                    bool note = row.Key.StartsWith("~");
                    Ui.Text(c.G, note ? row.Key.Substring(1) : row.Key, note ? c.F(11) : c.FS(10), note ? c.T.TextDim : c.T.TextFaint, new RectangleF(b.X + c.S(18), yy, b.Width - c.S(36), c.S(24)));
                    yy += c.S(24); continue;
                }
                var it = row.Value; bool isPinned = pinned.Contains(it);
                var r = new RectangleF(b.X + c.S(10), yy, b.Width - c.S(20), rh - c.S(2));
                bool dragging = dragIc == it && dragY >= 0;
                if (isPinned) { pinRows.Add(new KeyValuePair<RectangleF, CtlTrayIcon>(r, it)); pinIdx++; }
                if (r.Bottom >= top && r.Y <= bottom && !dragging) DrawRow(c, r, it, isPinned, px, pinIdx > Max && isPinned);
                yy += rh;
            }
            if (dragIc != null && dragY >= 0)
            {
                var tgt = DropTarget(dragY, dragIc); float ly = -1;
                foreach (var kv in pinRows) if (kv.Value == tgt) { ly = kv.Key.Y; break; }
                if (ly < 0 && pinRows.Count > 0) ly = pinRows[pinRows.Count - 1].Key.Bottom;
                if (ly >= 0) Ui.FillRound(c.G, new RectangleF(b.X + c.S(14), ly - c.S(1.5f), b.Width - c.S(28), c.S(3)), c.S(1.5f), c.T.Accent);
                var dr = new RectangleF(b.X + c.S(10), dragY - rh / 2, b.Width - c.S(20), rh - c.S(2));
                Ui.FillRound(c.G, dr, c.S(8), c.T.Tile);
                Ui.StrokeRound(c.G, dr, c.S(8), c.T.Accent, c.S(1.5f));
                DrawRow(c, dr, dragIc, true, px, false);
            }
            c.G.Restore(st);
            if (maxScroll > 0)
            {
                float trackH = bottom - top, th = Math.Max(c.S(24), trackH * (bottom - top) / contentH), ty = top + (trackH - th) * (scroll / maxScroll);
                Ui.FillRound(c.G, new RectangleF(b.Right - c.S(6), ty, c.S(3), th), c.S(1.5f), c.T.Track);
            }
            float gap = c.S(8), bw = (b.Width - c.S(32) - gap) / 2;
            c.Button(new RectangleF(b.X + c.S(16), b.Bottom - c.S(44), bw, c.S(30)), "Settings", () => Host.OpenSettings("widgets:" + InstId), false, Icons.Settings);
            c.Button(new RectangleF(b.X + c.S(16) + bw + gap, b.Bottom - c.S(44), bw, c.S(30)), "Done", () => { customize = false; Invalidate(); }, true, Icons.Check);
        }
        void DrawRow(DrawCtx c, RectangleF r, CtlTrayIcon it, bool isPinned, int px, bool beyondMax)
        {
            bool h = c.IsHover(r);
            if (h) Ui.FillRound(c.G, r, c.S(8), c.T.TileHover);
            float x = r.X + c.S(6);
            if (isPinned) Ui.Icon(c.G, GripGlyph, new RectangleF(x, r.Y, c.S(16), r.Height), h ? c.T.TextDim : c.T.TextFaint, c.S(11));
            x += c.S(20);
            var ir = new RectangleF(x, r.Y + (r.Height - px) / 2, px, px);
            var bmp = it.Get(px, c.T.Dark);
            if (bmp != null) Ctl.DrawImg(c.G, bmp, ir); else Ui.FillRound(c.G, ir, c.S(5), c.T.Track);
            x += px + c.S(10);
            var sw = new RectangleF(r.Right - c.S(46), r.Y + (r.Height - c.S(20)) / 2, c.S(36), c.S(20));
            string sub = beyondMax ? "over the max: shown in overflow" : (it.Exe ?? "");
            Ui.Text(c.G, it.Name, c.F(12), c.T.Text, new RectangleF(x, r.Y + c.S(2), sw.X - x - c.S(8), r.Height / 2));
            Ui.Text(c.G, sub, c.F(9.5f), beyondMax ? c.T.Warn : c.T.TextFaint, new RectangleF(x, r.Y + r.Height / 2 - c.S(1), sw.X - x - c.S(8), r.Height / 2));
            Ctl.Switch(c, sw, isPinned);
            var ii = it;
            c.Hit(Ui.Inset(sw, -c.S(6), -c.S(6)), () => SetPinned(ii, !isPinned));
            cells.Add(new KeyValuePair<RectangleF, CtlTrayIcon>(r, it));   // right-click on a row = the app's menu
        }
    }
}
