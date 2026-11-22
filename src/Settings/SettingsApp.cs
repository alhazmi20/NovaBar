// Settings program entry point: NovaBar.exe --settings [page]
// Screenshot mode (for visual testing, never shows a window): set env NOVABAR_SHOT=<out.png>.
//   optional: NOVABAR_SHOT_THEME=dark|white|glass|glasslight|midnight  NOVABAR_SHOT_SIZE=1140x780  NOVABAR_SHOT_SCALE=1  NOVABAR_SHOT_AFTER=ms (wait after each click)
//             NOVABAR_SHOT_WAIT=ms  NOVABAR_SHOT_SCROLL=px  NOVABAR_SHOT_CLICK="x,y;x,y" (design px)  NOVABAR_SHOT_HOVER="x,y"  NOVABAR_SHOT_DRAG="x,y>x,y" [NOVABAR_SHOT_DROP=1]
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NovaBar
{
    public static class SettingsApp
    {
        public const string Version = "1.0.0";

        public static void Run(string page)
        {
            if (page != null && page.StartsWith("widgets:")) { SetWidgetsPage.PendingSelect = page.Substring(8); page = "widgets"; }
            var shot = Environment.GetEnvironmentVariable("NOVABAR_SHOT");
            if (!string.IsNullOrEmpty(shot)) { RunShot(page, shot); return; }
            try { Native.SetProcessDPIAware(); } catch { }
            bool created;
            using (var mx = new Mutex(true, "NovaBar.Settings", out created))
            {
                if (!created)
                {
                    try { SetNative.AllowSetForegroundWindow(-1); } catch { }
                    Native.PostMessage(SetNative.HWND_BROADCAST, SetNative.ShowMsg, new IntPtr(string.IsNullOrEmpty(page) ? -1 : SetNative.PageIndex(page)), IntPtr.Zero);
                    return;
                }
                try { Application.EnableVisualStyles(); } catch { }
                try { Application.SetCompatibleTextRenderingDefault(false); } catch { }
                using (var w = new SetWindow(page, false)) Application.Run(w);
                GC.KeepAlive(mx);
            }
        }

        static string Env(string k, string def = null) { var v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? def : v; }

        static void RunShot(string page, string path)
        {
            try
            {
                try { Native.SetProcessDPIAware(); } catch { }
                using (var w = new SetWindow(page, true))
                {
                    var th = Env("NOVABAR_SHOT_THEME"); if (th != null) { w.Cfg.Theme = th; w.ApplyTheme(); }
                    float sc; if (float.TryParse(Env("NOVABAR_SHOT_SCALE", "1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sc)) w.Sc = sc;
                    var sz = Env("NOVABAR_SHOT_SIZE", "1140x780").Split('x');
                    int W = (int)(int.Parse(sz[0]) * w.Sc), H = (int)(int.Parse(sz[1]) * w.Sc);
                    var hnd = w.Handle; // create the (hidden) window so BeginInvoke works
                    Pump(int.Parse(Env("NOVABAR_SHOT_WAIT", "1200")));
                    w.Snapshot(W, H).Dispose();
                    var clicks = Env("NOVABAR_SHOT_CLICK");
                    if (clicks != null)
                        foreach (var c in clicks.Split(';'))
                        {
                            var p = ParsePt(c, w.Sc); w.ClickAt(p); Pump(int.Parse(Env("NOVABAR_SHOT_AFTER", "300")));
                            w.Snapshot(W, H).Dispose();
                        }
                    var type = Env("NOVABAR_SHOT_TYPE"); // text typed into the field focused by the last click
                    if (type != null) { w.TypeText(type); Pump(int.Parse(Env("NOVABAR_SHOT_AFTER", "300"))); w.Snapshot(W, H).Dispose(); }
                    var drag = Env("NOVABAR_SHOT_DRAG"); // "x,y>x,y": press on a hit, move; NOVABAR_SHOT_DROP=1 also releases
                    if (drag != null)
                    {
                        var ab = drag.Split('>'); var p1 = ParsePt(ab[0], w.Sc); var p2 = ParsePt(ab[1], w.Sc);
                        w.PressAt(p1); w.Page.DragMove(new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2)); w.Page.DragMove(p2); w.SetMouse(p2);
                        w.Snapshot(W, H).Dispose();
                        if (Env("NOVABAR_SHOT_DROP") == "1") { w.Page.DragEnd(p2); w.Snapshot(W, H).Dispose(); }
                    }
                    var scroll = Env("NOVABAR_SHOT_SCROLL"); if (scroll != null) w.Page.Scroll = float.Parse(scroll) * w.Sc;
                    var hover = Env("NOVABAR_SHOT_HOVER"); if (hover != null) w.SetMouse(ParsePt(hover, w.Sc));
                    using (var bmp = w.Snapshot(W, H)) bmp.Save(path, ImageFormat.Png);
                }
            }
            catch (Exception ex) { try { File.WriteAllText(path + ".log", ex.ToString()); } catch { } }
        }
        static Point ParsePt(string s, float sc) { var a = s.Split(','); return new Point((int)(float.Parse(a[0]) * sc), (int)(float.Parse(a[1]) * sc)); }
        static void Pump(int ms)
        {
            int end = Environment.TickCount + ms;
            while (Environment.TickCount < end) { Application.DoEvents(); Thread.Sleep(25); }
            Application.DoEvents();
        }
    }
}
