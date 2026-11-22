// "Shell" style helpers (quickshell / caelestia look): tiny click-through layered windows that draw the concave
// "inverted corner" where the edge-attached bar meets the screen edge, and where a popup panel grows out of the bar.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NovaBar
{
    sealed class FilletWin : Form
    {
        [StructLayout(LayoutKind.Sequential)] struct PT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte op, flags, alpha, format; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref PT pos, ref SZ size, IntPtr src, ref PT srcPos, int key, ref BLEND b, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

        string key;
        public FilletWin()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; Text = "NovaBarGlass"; MinimumSize = new Size(1, 1);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | 0x20 /*TRANSPARENT*/ | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        /// <summary>Square r (screen px) filled with c, minus a circle of radius r.Width centred on corner (cx, cy) ∈ {0, R}.</summary>
        public void Set(Rectangle r, int cx, int cy, Color c)
        {
            if (r.Width < 1 || r.Height < 1) { Off(); return; }
            var k = r + "|" + cx + "|" + cy + "|" + c.ToArgb();
            if (k == key && Visible) return;
            key = k;
            if (!IsHandleCreated) CreateHandle();
            int R = r.Width;
            using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var p = new GraphicsPath(FillMode.Alternate))
                using (var b = new SolidBrush(Color.FromArgb(255, c)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    p.AddRectangle(new Rectangle(0, 0, r.Width, r.Height));
                    p.AddEllipse(cx - R, cy - R, 2 * R, 2 * R);
                    g.SetClip(new Rectangle(0, 0, r.Width, r.Height));
                    g.FillPath(b, p);
                }
                var sdc = GetDC(IntPtr.Zero); var mdc = Native.CreateCompatibleDC(sdc);
                var hb = bmp.GetHbitmap(Color.FromArgb(0)); var old = Native.SelectObject(mdc, hb);
                try
                {
                    var pos = new PT { x = r.X, y = r.Y }; var size = new SZ { cx = r.Width, cy = r.Height }; var zero = new PT();
                    var bl = new BLEND { op = 0, flags = 0, alpha = 255, format = 1 };
                    UpdateLayeredWindow(Handle, sdc, ref pos, ref size, mdc, ref zero, 0, ref bl, 2 /*ULW_ALPHA*/);
                }
                finally { Native.SelectObject(mdc, old); Native.DeleteObject(hb); Native.DeleteDC(mdc); ReleaseDC(IntPtr.Zero, sdc); }
            }
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            Raise();
        }
        public void Raise() { if (IsHandleCreated) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); }
        public void Off() { if (IsHandleCreated && Visible) Native.ShowWindow(Handle, 0); key = null; }
    }

    /// <summary>A pair of fillets (left + right).</summary>
    sealed class FilletPair : IDisposable
    {
        FilletWin a, b;
        public void Set(Rectangle left, int lcx, int lcy, Rectangle right, int rcx, int rcy, Color c)
        {
            if (a == null) a = new FilletWin(); if (b == null) b = new FilletWin();
            if (left.IsEmpty) a.Off(); else a.Set(left, lcx, lcy, c);
            if (right.IsEmpty) b.Off(); else b.Set(right, rcx, rcy, c);
        }
        public void Raise() { if (a != null) a.Raise(); if (b != null) b.Raise(); }
        public void Hide() { if (a != null) a.Off(); if (b != null) b.Off(); }
        public void Dispose() { if (a != null) a.Dispose(); if (b != null) b.Dispose(); a = b = null; }
    }
}
