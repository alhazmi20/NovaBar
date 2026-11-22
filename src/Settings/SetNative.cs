// Settings app: native helpers (single instance messaging, alpha back-buffer for DWM backdrops).
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NovaBar
{
    static class SetNative
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, int id);
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        public const int WM_CLOSE = 0x10;
        static uint showMsg;
        public static uint ShowMsg { get { if (showMsg == 0) showMsg = RegisterWindowMessage("NovaBar.Settings.Show"); return showMsg; } }
        static readonly string[] pages = { "general", "appearance", "dock", "widgets", "notifications", "accounts", "checklist", "about" };
        public static string[] Pages { get { return pages; } }
        public static int PageIndex(string p) { int i = Array.IndexOf(pages, (p ?? "").ToLowerInvariant()); return i < 0 ? 0 : i; }
    }

    /// <summary>32bpp premultiplied top-down DIB with a GDI+ Bitmap over the same bits. Alpha survives BitBlt
    /// onto a window whose frame is extended into the client area, so DWM backdrops show through.</summary>
    sealed class SetDib : IDisposable
    {
        public IntPtr Dc, HBmp, Old, Bits; public Bitmap Bmp; public int W, H;
        public SetDib(int w, int h)
        {
            W = Math.Max(1, w); H = Math.Max(1, h);
            var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
            Dc = Native.CreateCompatibleDC(IntPtr.Zero);
            HBmp = Native.CreateDIBSection(Dc, ref bi, 0, out Bits, IntPtr.Zero, 0);
            Old = Native.SelectObject(Dc, HBmp);
            Bmp = new Bitmap(W, H, W * 4, PixelFormat.Format32bppPArgb, Bits);
        }
        public void Blit(IntPtr dst, int x, int y) { Native.BitBlt(dst, x, y, W, H, Dc, 0, 0, Native.SRCCOPY); }
        public void Dispose()
        {
            if (Bmp != null) { Bmp.Dispose(); Bmp = null; }
            if (Dc != IntPtr.Zero) { Native.SelectObject(Dc, Old); Native.DeleteObject(HBmp); Native.DeleteDC(Dc); Dc = IntPtr.Zero; }
        }
    }
}
