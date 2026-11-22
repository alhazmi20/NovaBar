// NovaBar — native interop. C# 5 (built-in .NET Framework csc). Keep allocation-free where possible.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NovaBar
{
    public static class Native
    {
        // ---------- window ----------
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WS_EX_LAYERED = 0x80000;
        public const int SW_HIDE = 0, SW_SHOW = 5, SW_RESTORE = 9, SW_SHOWNOACTIVATE = 4;
        public const int WM_NCHITTEST = 0x84, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3, WM_DISPLAYCHANGE = 0x7E, WM_SETTINGCHANGE = 0x1A, WM_DPICHANGED = 0x02E0;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int v);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr v);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        public const uint KEYEVENTF_KEYUP = 2, KEYEVENTF_EXTENDEDKEY = 1;

        /// <summary>Press a key chord, e.g. KeyChord(0x5B,0x44) = Win+D.</summary>
        public static void KeyChord(params byte[] vks)
        {
            foreach (var k in vks) keybd_event(k, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            for (int i = vks.Length - 1; i >= 0; i--) keybd_event(vks[i], 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        public const byte VK_LWIN = 0x5B, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SPACE = 0x20,
            VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1, VK_MEDIA_STOP = 0xB2, VK_MEDIA_PLAY_PAUSE = 0xB3,
            VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;

        public static string WindowTitle(IntPtr h)
        {
            int n = GetWindowTextLength(h); if (n <= 0) return "";
            var sb = new StringBuilder(n + 1); GetWindowText(h, sb, sb.Capacity); return sb.ToString();
        }
        public static string WindowClass(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }

        // ---------- DWM ----------
        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int l, r, t, b; }
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;
        public const int DWMSBT_NONE = 1, DWMSBT_MAINWINDOW = 2 /*mica*/, DWMSBT_TRANSIENTWINDOW = 3 /*acrylic*/, DWMSBT_TABBEDWINDOW = 4;

        public static void SetDwm(IntPtr h, int attr, int v) { try { DwmSetWindowAttribute(h, attr, ref v, 4); } catch { } }
        public static void ExtendFrame(IntPtr h, bool on)
        {
            try { var m = on ? new MARGINS { l = -1, r = -1, t = -1, b = -1 } : new MARGINS(); DwmExtendFrameIntoClientArea(h, ref m); } catch { }
        }

        // ---------- GDI (blit back-buffer with alpha) ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int op);
        public const int SRCCOPY = 0x00CC0020;

        // ---------- AppBar ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
        [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
        public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
        public const uint ABE_TOP = 1, ABE_BOTTOM = 3;
        public const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

        // ---------- system info ----------
        [StructLayout(LayoutKind.Sequential)] public struct FILETIME { public uint Low, High; public ulong Value { get { return ((ulong)High << 32) | Low; } } }
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX { public uint dwLength = 64, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
        [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr proc);

        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        // 2 = busy (fullscreen app), 3 = D3D fullscreen, 4 = presentation mode

        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
        public static uint IdleMs()
        {
            var i = new LASTINPUTINFO { cbSize = 8 }; if (!GetLastInputInfo(ref i)) return 0;
            return (uint)Environment.TickCount - i.dwTime;
        }

        /// <summary>Trim this process's working set (call after closing heavy UI).</summary>
        public static void TrimMemory()
        {
            try { GC.Collect(); GC.WaitForPendingFinalizers(); EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); } catch { }
        }
    }
}
