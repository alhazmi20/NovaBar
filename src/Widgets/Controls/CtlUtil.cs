// Shared helpers for the Controls widget group (prefix Ctl). C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NovaBar
{
    public static class CtlNative
    {
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetLastActivePopup(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeout")]
        public static extern IntPtr SendMessageTimeoutI(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")] static extern IntPtr GetClassLongPtr64(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "GetClassLong")] static extern IntPtr GetClassLong32(IntPtr h, int idx);
        public static IntPtr GetClassLongPtr(IntPtr h, int idx) { return IntPtr.Size == 8 ? GetClassLongPtr64(h, idx) : GetClassLong32(h, idx); }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint p, string pv, uint flags);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Native.RECT v, int size);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
        [DllImport("user32.dll")] public static extern bool ExitWindowsEx(uint flags, uint reason);

        public const uint GW_OWNER = 4, GA_ROOTOWNER = 3;
        public const int DWMWA_CLOAKED = 14, DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const uint WM_GETICON = 0x7F, SMTO_ABORTIFHUNG = 2;
        public const int GCLP_HICON = -14, GCLP_HICONSM = -34;

        public static string ProcessPath(uint pid)
        {
            var h = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (h == IntPtr.Zero) return null;
            try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null; }
            finally { CloseHandle(h); }
        }
        public static bool IsCloaked(IntPtr h)
        {
            int v; try { return DwmGetWindowAttribute(h, DWMWA_CLOAKED, out v, 4) == 0 && v != 0; } catch { return false; }
        }
        /// <summary>Bring a window to the front (restoring if minimized). Uses the Alt-key trick to bypass the foreground lock.</summary>
        [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool altTab);
        public static void Activate(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            if (!Native.SetForegroundWindow(h) || Native.GetForegroundWindow() != h) SwitchToThisWindow(h, true);
            BringWindowToTop(h);
        }
    }

    /// <summary>Cached, pre-scaled icon bitmaps (never re-extracted while cached).</summary>
    public static class CtlIcons
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> failedAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static Bitmap Scale(Image src, int px)
        {
            if (src == null) return null;
            var b = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawImage(src, new Rectangle(0, 0, px, px));
            }
            return b;
        }
        public static Bitmap FromHIcon(IntPtr h, int px)
        {
            if (h == IntPtr.Zero) return null;
            try { using (var ic = Icon.FromHandle(h)) using (var bmp = ic.ToBitmap()) return Scale(bmp, px); } catch { return null; }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint PrivateExtractIcons(string file, int idx, int cx, int cy, IntPtr[] icons, int[] ids, uint n, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        static Bitmap FromExe(string path, int px)
        {
            int req = px <= 16 ? 16 : px <= 24 ? 24 : px <= 32 ? 32 : 48;
            var h = new IntPtr[1]; var ids = new int[1];
            try
            {
                if (PrivateExtractIcons(path, 0, req, req, h, ids, 1, 0) == 0 || h[0] == IntPtr.Zero) return null;
                using (var ic = Icon.FromHandle(h[0])) using (var bmp = ic.ToBitmap()) return Scale(bmp, px);
            }
            catch { return null; }
            finally { if (h[0] != IntPtr.Zero) DestroyIcon(h[0]); }
        }
        public static Bitmap FromFile(string path, int px)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)) return CtlShellIcon.Get(path, px);
                if (CtlPkg.IsPkg(path))
                {   // Store app exe inside WindowsApps: no readable icon resource -> ask the shell for the app's tile icon
                    var aumid = CtlPkg.Aumid(path);
                    if (aumid != null) { var pb = CtlShellIcon.Get("shell:AppsFolder\\" + aumid, px); if (pb != null) return pb; }
                }
                if (Directory.Exists(path)) return null;
                if (!File.Exists(path)) return null;
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {   // read the exe's own icon resource: the shell image factory can hand back the generic placeholder while busy
                    var ex = FromExe(path, px); if (ex != null) return ex;
                }
                var sh = CtlShellIcon.Get(path, px);            // shell icon: follows .lnk IconLocation, .url, per-user installs
                if (sh != null) return sh;
                using (var ic = Icon.ExtractAssociatedIcon(path)) { if (ic == null) return null; using (var bmp = ic.ToBitmap()) return Scale(bmp, px); }
            }
            catch { return null; }
        }
        public static Bitmap FromPng(byte[] png, int px)
        {
            try { using (var ms = new MemoryStream(png)) using (var img = Image.FromStream(ms)) return Scale(img, px); } catch { return null; }
        }

        /// <summary>Synchronous (call from a background thread).</summary>
        public static Bitmap Get(string key, int px, Func<Bitmap> loader)
        {
            var k = key + "|" + px; Bitmap b;
            lock (cache) if (cache.TryGetValue(k, out b)) return b;
            b = null; try { b = loader(); } catch { }
            lock (cache) { if (cache.Count > 400) { cache.Clear(); } cache[k] = b; }
            return b;
        }
        /// <summary>Non-blocking: returns the cached bitmap or null and queues a background load, invalidating w when ready.</summary>
        public static Bitmap Peek(string key, int px, Func<Bitmap> loader, Widget w)
        {
            var k = key + "|" + px; Bitmap b;
            lock (cache)
            {
                if (cache.TryGetValue(k, out b))
                {
                    int t;
                    if (b != null || !failedAt.TryGetValue(k, out t) || Environment.TickCount - t < 30000) return b;
                    cache.Remove(k); failedAt.Remove(k);                                   // retry a failed load
                }
                if (pending.Contains(k)) return null;
                pending.Add(k);
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Bitmap r = null; try { r = loader(); } catch { }
                lock (cache)
                {
                    pending.Remove(k);
                    if (r != null) cache[k] = r;
                    else { cache[k] = null; failedAt[k] = Environment.TickCount; }   // remembered briefly, retried after 30 s
                }
                if (r != null && w != null) w.Invalidate();
            });
            return null;
        }
        public static Bitmap PeekFile(string path, int px, Widget w) { return Peek("f:" + path, px, () => FromFile(path, px), w); }
    }

    /// <summary>Packaged (Microsoft Store) apps: map C:\Program Files\WindowsApps\<PackageFullName>\x.exe to its AUMID.
    /// Survives app updates (the version in the folder name changes) by looking the package up in the AppModel repository.</summary>
    public static class CtlPkg
    {
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static bool IsPkg(string path) { return path != null && path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0; }
        public static string Aumid(string path)
        {
            string r;
            lock (cache) if (cache.TryGetValue(path, out r)) return r;
            r = null;
            try
            {
                int i = path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) + 13;
                int j = path.IndexOf('\\', i); if (j < 0) j = path.Length;
                string full = path.Substring(i, j - i), exe = Path.GetFileName(path);
                int u = full.IndexOf('_'), dd = full.LastIndexOf("__", StringComparison.Ordinal);
                if (u > 0 && dd > u)
                {
                    string name = full.Substring(0, u), pub = full.Substring(dd + 2), family = name + "_" + pub;
                    var roots = new List<string>();
                    if (Directory.Exists(Path.Combine(path.Substring(0, i), full))) roots.Add(Path.Combine(path.Substring(0, i), full));
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                        if (k != null)
                            foreach (var sub in k.GetSubKeyNames().Where(n => n.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase) && n.EndsWith("__" + pub, StringComparison.OrdinalIgnoreCase)).OrderByDescending(n => n))
                                using (var pk = k.OpenSubKey(sub)) { var root = pk == null ? null : pk.GetValue("PackageRootFolder") as string; if (root != null) roots.Add(root); }
                    string appId = null;
                    foreach (var root in roots)
                    {
                        var man = Path.Combine(root, "AppxManifest.xml");
                        if (!File.Exists(man)) continue;
                        var doc = new System.Xml.XmlDocument(); doc.Load(man);
                        string first = null;
                        foreach (System.Xml.XmlNode n in doc.GetElementsByTagName("Application"))
                        {
                            var id = n.Attributes == null || n.Attributes["Id"] == null ? null : n.Attributes["Id"].Value;
                            var ex = n.Attributes == null || n.Attributes["Executable"] == null ? null : n.Attributes["Executable"].Value;
                            if (id == null) continue;
                            if (first == null) first = id;
                            if (ex != null && string.Equals(Path.GetFileName(ex), exe, StringComparison.OrdinalIgnoreCase)) { appId = id; break; }
                        }
                        if (appId == null) appId = first;
                        if (appId != null) break;
                    }
                    if (appId != null) r = family + "!" + appId;
                }
            }
            catch { r = null; }
            lock (cache) cache[path] = r;
            return r;
        }
    }

    public static class Ctl
    {
        /// <summary>ShellExecute something on a pool thread (never blocks the UI).</summary>
        public static void Open(string target, string args = null)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var aumid = CtlPkg.IsPkg(target) ? CtlPkg.Aumid(target) : null;      // Store apps must be activated, not exec'd
                    if (aumid != null) { Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + aumid) { UseShellExecute = true }); return; }
                }
                catch { }
                try { var psi = new ProcessStartInfo(target) { UseShellExecute = true }; if (args != null) psi.Arguments = args; Process.Start(psi); } catch { }
            });
        }
        public static void Async(Action a) { ThreadPool.QueueUserWorkItem(_ => { try { a(); } catch { } }); }
        public static void Later(int ms, Action a) { ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(ms); try { a(); } catch { } }); }

        /// <summary>Run on a dedicated STA thread and wait (for apartment-sensitive COM like WScript.Shell / Shell.Application).</summary>
        public static T Sta<T>(Func<T> f, int timeoutMs = 15000)
        {
            T r = default(T);
            var t = new Thread(() => { try { r = f(); } catch { } }); t.IsBackground = true; t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join(timeoutMs);
            return r;
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + "m ago";
            if (d.TotalDays < 1) return (int)d.TotalHours + "h ago";
            if (d.TotalDays < 7) return (int)d.TotalDays + "d ago";
            return t.ToString("MMM d");
        }

        public static void DrawImg(Graphics g, Image img, RectangleF r)
        {
            if (img == null) return;
            try { g.DrawImage(img, r); } catch { }
        }
        /// <summary>Image clipped to a rounded rect (thumbnails).</summary>
        public static void DrawRoundImg(Graphics g, Image img, RectangleF r, float rad)
        {
            if (img == null) return;
            using (var p = Ui.Round(r, rad))
            {
                var st = g.Save(); g.SetClip(p, CombineMode.Intersect);
                // cover-fit
                float sw = img.Width, sh = img.Height, sc = Math.Max(r.Width / sw, r.Height / sh);
                float w = sw * sc, h = sh * sc;
                try { g.DrawImage(img, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h); } catch { }
                g.Restore(st);
            }
        }

        /// <summary>Small pill badge (e.g. counts).</summary>
        public static void Badge(DrawCtx c, string text, float cx, float cy, Color bg, Color fg)
        {
            var f = c.F(9f, true); float w = Math.Max(c.S(14), Ui.MeasureW(c.G, text, f) + c.S(8)), h = c.S(14);
            var r = new RectangleF(cx - w / 2, cy - h / 2, w, h);
            Ui.FillRound(c.G, r, h / 2, bg); Ui.Text(c.G, text, f, fg, r, 0);
        }

        /// <summary>Toggle switch (iOS style).</summary>
        public static void Switch(DrawCtx c, RectangleF r, bool on)
        {
            Ui.FillRound(c.G, r, r.Height / 2, on ? c.T.Accent : c.T.Track);
            float d = r.Height - c.S(4);
            float x = on ? r.Right - d - c.S(2) : r.X + c.S(2);
            Ui.FillCircle(c.G, new PointF(x + d / 2, r.Y + r.Height / 2), d / 2, Color.White);
        }

        /// <summary>Horizontal slider with click-to-set hit cells and wheel support.</summary>
        public static void Slider(DrawCtx c, RectangleF r, double frac, Action<double> set, int cells = 50)
        {
            float th = c.S(6);
            var tr = new RectangleF(r.X, r.Y + (r.Height - th) / 2, r.Width, th);
            Ui.Progress(c.G, tr, frac, c.T.Accent, c.T.Track);
            float kx = r.X + (float)(Math.Max(0, Math.Min(1, frac)) * r.Width);
            float kr = c.S(8);
            Ui.FillCircle(c.G, new PointF(kx, r.Y + r.Height / 2), kr + c.S(1), Ui.WithA(Color.Black, 40));
            Ui.FillCircle(c.G, new PointF(kx, r.Y + r.Height / 2), kr, Color.White);
            float cw = r.Width / cells;
            for (int i = 0; i <= cells; i++)
            {
                double v = (double)i / cells;
                var cr = new RectangleF(r.X + i * cw - cw / 2, r.Y - c.S(4), cw, r.Height + c.S(8));
                c.Hit(cr, () => set(v));
            }
            c.Wheel(Rectangle.Round(r), d => set(Math.Max(0, Math.Min(1, frac + d * 0.05))));
        }

        /// <summary>Row with hover highlight. Returns true if hovered.</summary>
        public static bool Row(DrawCtx c, RectangleF r, Action onClick)
        {
            bool h = c.IsHover(r);
            if (h) Ui.FillRound(c.G, r, c.S(8), c.T.TileHover);
            if (onClick != null) c.Hit(r, onClick);
            return h;
        }

        public static string Pictures { get { return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); } }
    }
}

namespace NovaBar
{
    /// <summary>
    /// Synchronous wait on WinRT async ops WITHOUT System.WindowsRuntimeSystemExtensions.AsTask (its generic overloads
    /// need the union Windows.winmd which this csc setup lacks). Call from background threads only.
    /// </summary>
    public static class CtlWinRT
    {
        public static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op, int timeoutMs = 8000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (sw.ElapsedMilliseconds > timeoutMs) { try { op.Cancel(); } catch { } throw new TimeoutException(); }
                System.Threading.Thread.Sleep(10);
            }
            if (op.Status != Windows.Foundation.AsyncStatus.Completed) throw new InvalidOperationException("WinRT op " + op.Status);
            return op.GetResults();
        }
    }
}

namespace NovaBar
{
    /// <summary>Small themed context menu (opaque, rounded) built on demand and disposed on close. UI thread only.</summary>
    public class CtlMenu
    {
        readonly System.Windows.Forms.ContextMenuStrip cms;
        public CtlMenu()
        {
            var t = Theme.Current;
            cms = new System.Windows.Forms.ContextMenuStrip { Renderer = new CtlMenuRenderer(t), ShowImageMargin = false, ShowCheckMargin = false };
            float dpi = 1f; try { dpi = Native.GetDpiForSystem() / 96f; } catch { }
            cms.Font = Ui.Font(13f * dpi); cms.Padding = new System.Windows.Forms.Padding(2, 4, 2, 4);
        }
        public CtlMenu Header(string text)
        {
            var i = new System.Windows.Forms.ToolStripMenuItem(text.Length > 48 ? text.Substring(0, 47) + "…" : text) { Enabled = false };
            cms.Items.Add(i); return this;
        }
        public CtlMenu Item(string text, Action a, bool enabled = true)
        {
            var i = new System.Windows.Forms.ToolStripMenuItem(text.Length > 56 ? text.Substring(0, 55) + "…" : text) { Enabled = enabled };
            i.Click += (o, e) => { try { a(); } catch { } };
            cms.Items.Add(i); return this;
        }
        public CtlMenu Sep() { if (cms.Items.Count > 0) cms.Items.Add(new System.Windows.Forms.ToolStripSeparator()); return this; }
        public void Show()
        {
            var p = System.Windows.Forms.Cursor.Position;
            var scr = System.Windows.Forms.Screen.FromPoint(p).Bounds;
            cms.Opened += (o, e) => Native.SetDwm(cms.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
            cms.Closed += (o, e) => { var m = cms; m.BeginInvoke((Action)(() => m.Dispose())); };
            bool up = p.Y > scr.Y + scr.Height / 2;
            if (CtlThumbs.BarSide == "right")   // vertical bar on the right edge: open the menu towards the screen, away from the bar
                cms.Show(p, up ? System.Windows.Forms.ToolStripDropDownDirection.AboveLeft : System.Windows.Forms.ToolStripDropDownDirection.BelowLeft);
            else
                cms.Show(p, up ? System.Windows.Forms.ToolStripDropDownDirection.AboveRight : System.Windows.Forms.ToolStripDropDownDirection.BelowRight);
        }
    }

    public class CtlMenuRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        readonly Theme t; readonly Color bg, hover, border;
        public CtlMenuRenderer(Theme t)
        {
            this.t = t; RoundedEdges = false;
            switch (t.Id)
            {
                case "white": case "glasslight": bg = Color.FromArgb(252, 252, 253); hover = Color.FromArgb(236, 236, 241); break;
                case "midnight": bg = Color.FromArgb(14, 18, 40); hover = Color.FromArgb(32, 40, 78); break;
                default: bg = Color.FromArgb(28, 28, 33); hover = Color.FromArgb(46, 46, 54); break;
            }
            border = t.Dark ? Color.FromArgb(60, 60, 70) : Color.FromArgb(220, 220, 226);
        }
        protected override void OnRenderToolStripBackground(System.Windows.Forms.ToolStripRenderEventArgs e) { e.Graphics.Clear(bg); }
        protected override void OnRenderImageMargin(System.Windows.Forms.ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(System.Windows.Forms.ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(System.Windows.Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Ui.FillRound(e.Graphics, new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 5, hover);
        }
        protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? t.Text : t.TextDim; base.OnRenderItemText(e); }
        protected override void OnRenderSeparator(System.Windows.Forms.ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2; using (var p = new Pen(border)) e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
        }
    }
}


namespace NovaBar
{
    /// <summary>Icons through IShellItemImageFactory — the same source Explorer/Start use (alpha-correct, any size).</summary>
    public static class CtlShellIcon
    {
        [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
         System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [System.Runtime.InteropServices.PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Interface)] out IShellItemImageFactory ppv);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

        public static System.Drawing.Bitmap Get(string path, int px)
        {
            IShellItemImageFactory f = null; IntPtr hbm = IntPtr.Zero;
            try
            {
                var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out f) != 0 || f == null) return null;
                int req = Math.Max(16, px <= 16 ? 16 : px <= 24 ? 24 : px <= 32 ? 32 : 48);
                if (f.GetImage(new SIZE { cx = req, cy = req }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                using (var raw = System.Drawing.Image.FromHbitmap(hbm))
                {
                    var rect = new System.Drawing.Rectangle(0, 0, raw.Width, raw.Height);
                    var data = raw.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, raw.PixelFormat);
                    try
                    {
                        // FromHbitmap drops alpha (32bppRgb) but the bytes still carry it: reinterpret as premultiplied ARGB
                        bool hasAlpha = false;
                        if (raw.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppRgb || raw.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                        {
                            int rowBytes = data.Width * 4;                       // stride is negative for bottom-up DIBs
                            var row = new byte[rowBytes];
                            for (int y = 0; y < data.Height && !hasAlpha; y++)
                            {
                                System.Runtime.InteropServices.Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, rowBytes);
                                for (int x = 3; x < data.Width * 4; x += 4) if (row[x] != 0) { hasAlpha = true; break; }
                            }
                        }
                        System.Drawing.Bitmap src;
                        if (hasAlpha) using (var view = new System.Drawing.Bitmap(data.Width, data.Height, data.Stride, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, data.Scan0)) src = new System.Drawing.Bitmap(view);
                        else src = new System.Drawing.Bitmap(raw);
                        using (src) return CtlIcons.Scale(src, px);
                    }
                    finally { raw.UnlockBits(data); }
                }
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (f != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(f);
            }
        }
    }
}
