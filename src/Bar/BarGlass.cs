// Real glass behind floating tiles: one tiny borderless window per tile, exactly behind it, carrying the acrylic blur
// (DWM blur applies to whole windows and ignores window regions, so per-tile windows are the only way to get blur
// under each widget with nothing in between). They paint nothing themselves and never take input or focus.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NovaBar
{
    sealed class BarGlassPane : Form
    {
        public BarGlassPane()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; MinimumSize = new Size(1, 1); Text = "NovaBarGlass";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | 0x20 /*TRANSPARENT*/; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            if (m.Msg == Native.WM_NCHITTEST) { m.Result = new IntPtr(-1); return; }   // HTTRANSPARENT
            base.WndProc(ref m);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Color.Black); }   // alpha 0 → only the blur shows
        protected override void OnPaint(PaintEventArgs e) { }
        public void Apply(Theme t)
        {
            Native.SetDwm(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, t.Dark ? 1 : 0);
            Native.SetDwm(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
            Native.SetDwm(Handle, Native.DWMWA_BORDER_COLOR, BarNative.DWMWA_COLOR_NONE);
            Native.ExtendFrame(Handle, true);
            // ACCENT_ENABLE_ACRYLICBLURBEHIND keeps blurring while inactive (the system backdrop would turn flat grey).
            BarShell.SetAccent(Handle, 4, t.Dark ? 0x18101016 : 0x22FFFFFF);
        }
    }

    sealed class BarGlass
    {
        readonly List<BarGlassPane> panes = new List<BarGlassPane>();
        string key; string themeId; bool visible = true;

        public static bool Wanted(Theme t) { return t.IsGlass && !AppConfig.Current.SolidBar; }

        /// <summary>Place one blur pane behind each visible tile (screen coords), just under the bar in z-order.</summary>
        public void Sync(BarForm bar, List<BarTile> tiles, Theme t, bool show)
        {
            if (!Wanted(t) || !show) { Hide(); if (!Wanted(t)) Clear(); return; }
            var rects = new List<Rectangle>();
            foreach (var tl in tiles) if (tl.R.Width > 0) rects.Add(bar.RectangleToScreen(tl.R));
            var sb = new System.Text.StringBuilder(t.Id);
            foreach (var r in rects) sb.Append('|').Append(r.X).Append(',').Append(r.Y).Append(',').Append(r.Width).Append(',').Append(r.Height);
            var k = sb.ToString();
            if (k == key && visible) return;
            key = k;
            if (themeId != t.Id) { Clear(); themeId = t.Id; }
            while (panes.Count < rects.Count) { var p = new BarGlassPane(); p.CreateControl(); var h = p.Handle; p.Apply(t); panes.Add(p); }
            while (panes.Count > rects.Count) { var p = panes[panes.Count - 1]; panes.RemoveAt(panes.Count - 1); p.Dispose(); }
            IntPtr after = bar.Handle;
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i]; var p = panes[i];
                Native.SetWindowPos(p.Handle, after, r.X, r.Y, r.Width, r.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                after = p.Handle;
            }
            visible = true;
        }
        public void Hide()
        {
            if (!visible) return;
            foreach (var p in panes) Native.ShowWindow(p.Handle, Native.SW_HIDE);
            visible = false; key = null;
        }
        public void Clear() { foreach (var p in panes) p.Dispose(); panes.Clear(); key = null; themeId = null; }
    }
}
