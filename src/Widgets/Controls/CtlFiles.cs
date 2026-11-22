// Recently opened, recycle bin, search, screenshot capture.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NovaBar
{
    // ------------------------------------------------------------------ Recycle bin core
    public class CtlBinSnap
    {
        public long Items, Bytes;
        public override bool Equals(object o) { var x = o as CtlBinSnap; return x != null && x.Items == Items && x.Bytes == Bytes; }
        public override int GetHashCode() { return (int)Items; }
    }
    public static class CtlBin
    {
        public const string Key = "ctl.bin";
        [StructLayout(LayoutKind.Sequential, Pack = 8)] struct Q64 { public int cbSize; public long size, items; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Q32 { public int cbSize; public long size, items; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")] static extern int Query64(string root, ref Q64 q);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")] static extern int Query32(string root, ref Q32 q);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        static CtlBin() { Hub.Define(Key, 30000, Sample); }
        public static void Init() { }
        static object Sample()
        {
            if (IntPtr.Size == 8) { var q = new Q64 { cbSize = Marshal.SizeOf(typeof(Q64)) }; if (Query64(null, ref q) != 0) return null; return new CtlBinSnap { Items = q.items, Bytes = q.size }; }
            else { var q = new Q32 { cbSize = Marshal.SizeOf(typeof(Q32)) }; if (Query32(null, ref q) != 0) return null; return new CtlBinSnap { Items = q.items, Bytes = q.size }; }
        }
        public static void Empty()
        {
            Ctl.Async(() => { SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2 | 4 /*no confirm, no progress, no sound*/); Hub.Refresh(Key); });
        }
    }

    [Widget("recyclebin", "Recycle Bin", "Apps & Files", "What the bin holds, and a click to empty it.", RefName = "Recycle Bin", Data = DataKind.Live)]
    public class CtlRecycleBinWidget : Widget
    {
        static CtlRecycleBinWidget() { CtlBin.Init(); }
        bool confirming;
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Toggle("label", "Show size on the tile", true) }; } }
        public override void OnStart() { Hub.Subscribe(CtlBin.Key, this); }
        public override void OnStop() { Hub.Unsubscribe(CtlBin.Key, this); }
        public override void OnPanelOpen() { confirming = false; Hub.Refresh(CtlBin.Key); }
        CtlBinSnap Snap { get { return Hub.Get<CtlBinSnap>(CtlBin.Key); } }

        public override int Measure(DrawCtx c)
        {
            if (!SBool("label")) return c.Si(38);
            var s = Snap; return c.Si(36) + Ui.MeasureW(c.G, s == null ? "…" : s.Items == 0 ? "Empty" : Ui.Bytes(s.Bytes), c.FS(12)) + c.Si(6);
        }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var s = Snap; bool full = s != null && s.Items > 0;
            var ir = SBool("label") ? new RectangleF(b.X + c.S(9), b.Y, c.S(20), b.Height) : (RectangleF)b;
            Ui.Icon(c.G, Icons.RecycleBin, ir, full ? c.T.Text : c.T.TextDim, c.S(15));
            if (full)
            {
                // fill dot
                Ui.FillCircle(c.G, new PointF(ir.X + ir.Width / 2 + c.S(6), b.Y + b.Height / 2 - c.S(6)), c.S(3), c.T.Accent);
            }
            if (SBool("label"))
                Ui.Text(c.G, s == null ? "…" : s.Items == 0 ? "Empty" : Ui.Bytes(s.Bytes), c.FS(12), full ? c.T.Text : c.T.TextDim, new RectangleF(ir.Right + c.S(5), b.Y, b.Right - ir.Right - c.S(8), b.Height));
        }

        public override Size PanelSize(float sc) { return new Size((int)(300 * sc), (int)(196 * sc)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var s = Snap;
            float y = c.PanelHeader("Recycle Bin", null, Icons.RecycleBin);
            var big = new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(38));
            string v = s == null ? "…" : s.Items == 0 ? "Empty" : Ui.Bytes(s.Bytes);
            Ui.Text(c.G, v, c.F(26, true), c.T.Text, big);
            y += c.S(40);
            Ui.Text(c.G, s == null ? "Reading…" : s.Items == 0 ? "Nothing to clean up" : s.Items.ToString("#,0") + (s.Items == 1 ? " item" : " items") + " in the bin",
                c.F(12), c.T.TextDim, new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(18)));
            y += c.S(30);
            float gap = c.S(8), bw = (b.Width - c.S(32) - gap) / 2;
            if (confirming)
            {
                Ui.Text(c.G, "Permanently delete everything?", c.FS(12), c.T.Bad, new RectangleF(b.X + c.S(16), y - c.S(8), b.Width, c.S(16)));
                c.Button(new RectangleF(b.X + c.S(16), y + c.S(12), bw, c.S(30)), "Cancel", () => { confirming = false; Invalidate(); });
                var yes = new RectangleF(b.X + c.S(16) + bw + gap, y + c.S(12), bw, c.S(30));
                Ui.FillRound(c.G, yes, c.S(10), c.IsHover(yes) ? Ui.Mix(c.T.Bad, Color.White, 0.15) : c.T.Bad);
                Ui.Text(c.G, "Empty bin", c.FS(12.5f), Color.White, yes, 0);
                c.Hit(yes, () => { confirming = false; CtlBin.Empty(); Invalidate(); });
                return;
            }
            c.Button(new RectangleF(b.X + c.S(16), y + c.S(12), bw, c.S(30)), "Open", () => Ctl.Open("shell:RecycleBinFolder"), false, Icons.Folder);
            bool any = s != null && s.Items > 0;
            var er = new RectangleF(b.X + c.S(16) + bw + gap, y + c.S(12), bw, c.S(30));
            if (any) c.Button(er, "Empty", () => { confirming = true; Invalidate(); }, true, Icons.Delete);
            else { Ui.FillRound(c.G, er, c.S(10), c.T.Tile); Ui.Text(c.G, "Empty", c.FS(12.5f), c.T.TextFaint, er, 0); }
        }
    }

    // ------------------------------------------------------------------ Recently opened
    public class CtlRecentItem { public string Lnk, Name, Target; public DateTime When; public bool Folder; }
    public class CtlRecentSnap
    {
        public List<CtlRecentItem> Items = new List<CtlRecentItem>();
        public override bool Equals(object o)
        {
            var x = o as CtlRecentSnap; if (x == null || x.Items.Count != Items.Count) return false;
            for (int i = 0; i < Items.Count; i++) if (x.Items[i].Lnk != Items[i].Lnk || x.Items[i].When != Items[i].When) return false;
            return true;
        }
        public override int GetHashCode() { return Items.Count; }
    }

    [Widget("recent", "Recently Opened", "Apps & Files", "The apps and files you used last.", RefName = "Recently Opened", Data = DataKind.Live)]
    public class CtlRecentWidget : Widget
    {
        const string Key = "ctl.recent";
        static readonly Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // lnk|mtime -> target
        static CtlRecentWidget() { Hub.Define(Key, 60000, Sample); }
        static object Sample()
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (!Directory.Exists(dir)) return new CtlRecentSnap();
            var files = new DirectoryInfo(dir).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(24).ToList();
            var need = files.Where(f => { lock (targets) return !targets.ContainsKey(f.FullName + "|" + f.LastWriteTime.Ticks); }).ToList();
            if (need.Count > 0)
            {
                Ctl.Sta(() =>
                {
                    var t = Type.GetTypeFromProgID("WScript.Shell"); if (t == null) return false;
                    dynamic sh = Activator.CreateInstance(t);
                    try
                    {
                        foreach (var f in need)
                        {
                            string tp = "";
                            try { dynamic lk = sh.CreateShortcut(f.FullName); tp = (string)lk.TargetPath ?? ""; Marshal.FinalReleaseComObject(lk); } catch { }
                            lock (targets) targets[f.FullName + "|" + f.LastWriteTime.Ticks] = tp;
                        }
                    }
                    finally { Marshal.FinalReleaseComObject(sh); }
                    return true;
                });
            }
            var snap = new CtlRecentSnap();
            foreach (var f in files)
            {
                string tp; lock (targets) targets.TryGetValue(f.FullName + "|" + f.LastWriteTime.Ticks, out tp);
                if (string.IsNullOrEmpty(tp)) continue;               // shell items w/o file path (e.g. control panel)
                bool folder = Directory.Exists(tp);
                if (!folder && !File.Exists(tp)) continue;             // deleted / offline
                snap.Items.Add(new CtlRecentItem { Lnk = f.FullName, Target = tp, Name = Path.GetFileName(tp.TrimEnd('\\')), When = f.LastWriteTime, Folder = folder });
                if (snap.Items.Count >= 12) break;
            }
            return snap;
        }
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Toggle("files", "Hide folders", false) }; } }
        public override void OnStart() { }
        public override void OnPanelOpen() { Hub.Subscribe(Key, this); Hub.Refresh(Key); }
        public override void OnPanelClose() { Hub.Unsubscribe(Key, this); }
        public override void OnStop() { Hub.Unsubscribe(Key, this); }

        public override int Measure(DrawCtx c) { return c.Si(38); }
        public override void Draw(DrawCtx c) { Ui.Icon(c.G, Icons.Recent, c.Bounds, c.T.Text, c.S(16)); }

        public override Size PanelSize(float sc) { return new Size((int)(340 * sc), (int)((48 + 8 * 40 + 50) * sc)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var s = Hub.Get<CtlRecentSnap>(Key);
            float y = c.PanelHeader("Recently opened", null, Icons.Recent);
            if (s == null) { Ui.Text(c.G, "Loading…", c.F(12), c.T.TextDim, new RectangleF(b.X + c.S(16), y, b.Width, c.S(30))); }
            else
            {
                var items = s.Items.Where(i => !(SBool("files") && i.Folder)).Take(8).ToList();
                if (items.Count == 0) Ui.Text(c.G, "Nothing opened recently", c.F(12), c.T.TextDim, new RectangleF(b.X + c.S(16), y, b.Width, c.S(30)));
                int ipx = c.Si(20);
                foreach (var it in items)
                {
                    var r = new RectangleF(b.X + c.S(8), y, b.Width - c.S(16), c.S(38));
                    var lnk = it.Lnk; var tgt = it.Target;
                    Ctl.Row(c, r, () => { Ctl.Open(File.Exists(tgt) || Directory.Exists(tgt) ? tgt : lnk); Host.ClosePanel(); });
                    var icr = new RectangleF(r.X + c.S(10), r.Y + (r.Height - ipx) / 2, ipx, ipx);
                    if (it.Folder) Ui.Icon(c.G, Icons.Folder, icr, c.T.Warn, c.S(16));
                    else { var bmp = CtlIcons.PeekFile(tgt, ipx, this); if (bmp != null) Ctl.DrawImg(c.G, bmp, icr); else Ui.Icon(c.G, Icons.File, icr, c.T.TextDim, c.S(15)); }
                    float tx = icr.Right + c.S(12);
                    Ui.Text(c.G, it.Name, c.FS(12.5f), c.T.Text, new RectangleF(tx, r.Y + c.S(3), r.Right - tx - c.S(64), c.S(18)));
                    Ui.Text(c.G, Path.GetDirectoryName(tgt.TrimEnd('\\')) ?? "", c.F(10.5f), c.T.TextDim, new RectangleF(tx, r.Y + c.S(20), r.Right - tx - c.S(64), c.S(14)));
                    Ui.Text(c.G, Ctl.Ago(it.When), c.F(10.5f), c.T.TextFaint, new RectangleF(r.Right - c.S(66), r.Y, c.S(58), r.Height), 1);
                    y += c.S(40);
                }
            }
            c.Button(new RectangleF(b.X + c.S(16), b.Bottom - c.S(42), b.Width - c.S(32), c.S(30)), "Open Recent folder", () => Ctl.Open("shell:recent"), false, Icons.Folder);
        }
    }

    // ------------------------------------------------------------------ Search
    [Widget("search", "Search", "Apps & Files", "Search the web, or type a folder path to open it.", RefName = "Search", Data = DataKind.Live)]
    public class CtlSearchWidget : Widget
    {
        int gen; string query = "";
        protected override string StateKey { get { return Meta.Id; } }
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Choice("engine", "Search engine", "Google", "Google", "Bing", "DuckDuckGo") }; } }
        string Engine { get { object o; return State.TryGetValue("engine", out o) && o != null ? J.AsStr(o) : SStr("engine"); } }
        static string Url(string engine, string q)
        {
            var e = Http.Esc(q);
            switch (engine) { case "Bing": return "https://www.bing.com/search?q=" + e; case "DuckDuckGo": return "https://duckduckgo.com/?q=" + e; default: return "https://www.google.com/search?q=" + e; }
        }
        static readonly Regex urlRx = new Regex(@"^(https?://\S+|www\.\S+|[\w-]+(\.[\w-]+)*\.[a-z]{2,24}(:\d+)?(/\S*)?)$", RegexOptions.IgnoreCase);
        /// <summary>What Enter would do for this text: kind + target.</summary>
        static KeyValuePair<string, string> Resolve(string text, string engine)
        {
            var t = (text ?? "").Trim().Trim('"');
            if (t.Length == 0) return new KeyValuePair<string, string>("", "");
            var exp = Environment.ExpandEnvironmentVariables(t);
            try
            {
                if (exp.Length >= 2 && (exp[1] == ':' || exp.StartsWith("\\\\") || exp.StartsWith("~")))
                {
                    if (exp.StartsWith("~")) exp = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + exp.Substring(1);
                    if (Directory.Exists(exp)) return new KeyValuePair<string, string>("folder", exp);
                    if (File.Exists(exp)) return new KeyValuePair<string, string>("file", exp);
                }
                if (exp.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || exp.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)) return new KeyValuePair<string, string>("folder", exp);
            }
            catch { }
            if (!t.Contains(" ") && urlRx.IsMatch(t)) return new KeyValuePair<string, string>("url", t.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? t : "https://" + t);
            return new KeyValuePair<string, string>("web", Url(engine, t));
        }
        void Go(string text)
        {
            var r = Resolve(text, Engine); if (r.Key == "") return;
            if (r.Key == "folder" && Directory.Exists(r.Value)) Ctl.Open("explorer.exe", "\"" + r.Value + "\"");
            else Ctl.Open(r.Value);
            object o; var hist = State.TryGetValue("history", out o) && o is System.Collections.IList ? ((System.Collections.IList)o).Cast<object>().Select(x => J.AsStr(x)).ToList() : new List<string>();
            var t = text.Trim(); hist.Remove(t); hist.Insert(0, t); State["history"] = hist.Take(5).Cast<object>().ToArray(); SaveState();
            query = ""; gen++; Host.ClosePanel();
        }
        public override void OnPanelOpen() { query = ""; gen++; }

        public override int Measure(DrawCtx c) { return c.Si(150); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds;
            var ir = new RectangleF(b.X + c.S(10), b.Y, c.S(16), b.Height);
            Ui.Icon(c.G, Icons.Search, ir, c.T.TextDim, c.S(13));
            Ui.Text(c.G, "Search or type a path", c.F(12), c.T.TextFaint, new RectangleF(ir.Right + c.S(7), b.Y, b.Right - ir.Right - c.S(14), b.Height));
        }

        public override Size PanelSize(float sc) { return new Size((int)(380 * sc), (int)(286 * sc)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds;
            var ir = new RectangleF(b.X + c.S(12), b.Y + c.S(12), b.Width - c.S(24), c.S(40));
            Ui.FillRound(c.G, ir, c.S(12), c.T.Input);
            Ui.Icon(c.G, Icons.Search, new RectangleF(ir.X + c.S(8), ir.Y, c.S(22), ir.Height), c.T.TextDim, c.S(14));
            c.Input("s" + gen, Rectangle.Round(new RectangleF(ir.X + c.S(36), ir.Y + c.S(9), ir.Width - c.S(46), ir.Height - c.S(18))), "Search the web, or paste a path / URL", Go, s => { query = s ?? ""; Invalidate(); });
            float y = ir.Bottom + c.S(10);
            // what Enter does
            var res = Resolve(query, Engine);
            string glyph = res.Key == "folder" ? Icons.Folder : res.Key == "file" ? Icons.File : res.Key == "url" ? Icons.Link : Icons.Globe;
            string label = res.Key == "" ? "Type to search with " + Engine : res.Key == "folder" ? "Open folder" : res.Key == "file" ? "Open file" : res.Key == "url" ? "Open website" : "Search " + Engine;
            var ar = new RectangleF(b.X + c.S(12), y, b.Width - c.S(24), c.S(36));
            if (res.Key != "") { bool h = Ctl.Row(c, ar, () => Go(query)); if (!h) Ui.FillRound(c.G, ar, c.S(8), c.T.AccentA(30)); }
            Ui.Icon(c.G, glyph, new RectangleF(ar.X + c.S(8), ar.Y, c.S(22), ar.Height), res.Key == "" ? c.T.TextFaint : c.T.Accent, c.S(14));
            Ui.Text(c.G, label + (res.Key == "web" || res.Key == "" ? "" : " · " + res.Value), c.F(12.5f), res.Key == "" ? c.T.TextDim : c.T.Text, new RectangleF(ar.X + c.S(38), ar.Y, ar.Width - c.S(100), ar.Height));
            if (res.Key != "") Ui.Text(c.G, "Enter ↵", c.F(10.5f), c.T.TextFaint, new RectangleF(ar.Right - c.S(70), ar.Y, c.S(60), ar.Height), 1);
            y += c.S(44);
            // engine chooser
            Ui.Text(c.G, "ENGINE", c.F(10.5f, true), c.T.TextFaint, new RectangleF(b.X + c.S(18), y, c.S(80), c.S(16)));
            y += c.S(20);
            string[] engines = { "Google", "Bing", "DuckDuckGo" };
            float gap = c.S(6), ew = (b.Width - c.S(32) - gap * 2) / 3;
            for (int i = 0; i < engines.Length; i++)
            {
                var e = engines[i]; var r = new RectangleF(b.X + c.S(16) + i * (ew + gap), y, ew, c.S(28));
                bool on = Engine == e, h = c.IsHover(r);
                Ui.FillRound(c.G, r, c.S(9), on ? c.T.Accent : h ? c.T.TileHover : c.T.Tile);
                Ui.Text(c.G, e, c.FS(12), on ? Color.White : c.T.Text, r, 0);
                c.Hit(r, () => { State["engine"] = e; SaveState(); Invalidate(); });
            }
            y += c.S(40);
            Ui.Text(c.G, "RECENT", c.F(10.5f, true), c.T.TextFaint, new RectangleF(b.X + c.S(18), y, c.S(80), c.S(16)));
            y += c.S(20);
            object o; var hist = State.TryGetValue("history", out o) && o is System.Collections.IList ? ((System.Collections.IList)o).Cast<object>().Select(x => J.AsStr(x)).ToList() : new List<string>();
            if (hist.Count == 0) Ui.Text(c.G, "Try  C:\\Users  ·  github.com  ·  weather tomorrow", c.F(11.5f), c.T.TextDim, new RectangleF(b.X + c.S(18), y, b.Width - c.S(36), c.S(24)));
            float hx = b.X + c.S(16);
            foreach (var h in hist)
            {
                var f = c.F(11.5f); float w = Math.Min(c.S(150), Ui.MeasureW(c.G, h, f) + c.S(20));
                if (hx + w > b.Right - c.S(16)) break;
                var r = new RectangleF(hx, y, w, c.S(26)); var hh = h;
                Ui.FillRound(c.G, r, c.S(13), c.IsHover(r) ? c.T.TileHover : c.T.Tile);
                Ui.Text(c.G, h, f, c.T.Text, Ui.Inset(r, c.S(10), 0), 0);
                c.Hit(r, () => Go(hh));
                hx += w + c.S(6);
            }
        }
    }

    // ------------------------------------------------------------------ Screenshot
    [Widget("screenshot", "Screenshot Capture", "Apps & Files", "Capture the screen, a window or a region.", RefName = "Screenshot Capture", Data = DataKind.Live)]
    public class CtlScreenshotWidget : Widget
    {
        Bitmap thumb; string lastPath, status; DateTime statusAt;
        IntPtr lastFg;
        static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Toggle("clip", "Copy captures to clipboard", true), SettingDef.Number("delay", "Delay (seconds)", 0, 0, 10) }; } }

        static string Folder { get { var d = Path.Combine(Ctl.Pictures, "Screenshots"); Directory.CreateDirectory(d); return d; } }
        public override void OnPanelOpen()
        {
            var fg = Native.GetForegroundWindow(); uint pid; Native.GetWindowThreadProcessId(fg, out pid);
            if (pid != ownPid) lastFg = fg;
        }
        public override void OnClick(MouseButtons b)
        {
            if (b == MouseButtons.Middle) { Capture(false); return; }
            base.OnClick(b);
        }
        void SetStatus(string s) { status = s; statusAt = DateTime.Now; Invalidate(); Ctl.Later(3200, Invalidate); }

        void Capture(bool window)
        {
            var target = lastFg; bool clip = SBool("clip"); int delay = (int)SNum("delay") * 1000;
            Host.ClosePanel();
            Ctl.Async(() =>
            {
                System.Threading.Thread.Sleep(300 + delay);   // let the panel fade out
                Rectangle r = SystemInformation.VirtualScreen;
                if (window && target != IntPtr.Zero && CtlNative.IsWindow(target))
                {
                    Native.RECT wr;
                    if (CtlNative.DwmGetWindowAttribute(target, CtlNative.DWMWA_EXTENDED_FRAME_BOUNDS, out wr, Marshal.SizeOf(typeof(Native.RECT))) != 0) Native.GetWindowRect(target, out wr);
                    r = Rectangle.Intersect(Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom), SystemInformation.VirtualScreen);
                    if (r.Width < 4 || r.Height < 4) r = SystemInformation.VirtualScreen;
                }
                var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy);
                var path = Path.Combine(Folder, "Screenshot " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png");
                bmp.Save(path, ImageFormat.Png);
                float sc = Math.Min(1f, 296f / bmp.Width); var t = new Bitmap(bmp, Math.Max(1, (int)(bmp.Width * sc)), Math.Max(1, (int)(bmp.Height * sc)));
                var old = thumb; thumb = t; lastPath = path; if (old != null) old.Dispose();
                if (clip && Host != null) Host.Post(() => { try { Clipboard.SetImage(bmp); } catch { } bmp.Dispose(); });
                else bmp.Dispose();
                SetStatus(clip ? "Saved & copied" : "Saved");
            });
        }

        public override int Measure(DrawCtx c) { return StatusOn ? c.Si(110) : c.Si(38); }
        bool StatusOn { get { return status != null && (DateTime.Now - statusAt).TotalSeconds < 3; } }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds;
            if (StatusOn)
            {
                var ir = new RectangleF(b.X + c.S(9), b.Y, c.S(18), b.Height);
                Ui.Icon(c.G, Icons.Check, ir, c.T.Good, c.S(14));
                Ui.Text(c.G, status, c.FS(12), c.T.Text, new RectangleF(ir.Right + c.S(5), b.Y, b.Right - ir.Right - c.S(8), b.Height));
            }
            else Ui.Icon(c.G, Icons.Screenshot, b, c.T.Text, c.S(16));
        }

        public override Size PanelSize(float sc) { return new Size((int)(320 * sc), (int)((thumb != null ? 330 : 186) * sc)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds;
            float y = c.PanelHeader("Screenshot", SNum("delay") > 0 ? SNum("delay") + "s delay" : null, Icons.Screenshot);
            float gap = c.S(8), w = (b.Width - c.S(32) - gap * 2) / 3, h = c.S(70);
            Big(c, new RectangleF(b.X + c.S(16), y, w, h), Icons.Desktop, "Screen", () => Capture(false));
            Big(c, new RectangleF(b.X + c.S(16) + w + gap, y, w, h), Icons.Window, "Window", () => Capture(true));
            Big(c, new RectangleF(b.X + c.S(16) + 2 * (w + gap), y, w, h), "\uE8B3", "Region", () => { Host.ClosePanel(); Ctl.Later(250, () => Ctl.Open("ms-screenclip:")); });
            y += h + c.S(12);
            if (thumb != null)
            {
                var tr = new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(134));
                Ui.FillRound(c.G, tr, c.S(10), c.T.Track);
                Ctl.DrawRoundImg(c.G, thumb, tr, c.S(10));
                var lp = lastPath; c.Hit(tr, () => Ctl.Open(lp));
                y += c.S(142);
                Ui.Text(c.G, Path.GetFileName(lastPath), c.F(10.5f), c.T.TextDim, new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(16)));
                y += c.S(20);
            }
            float bw = (b.Width - c.S(32) - gap) / 2;
            c.Button(new RectangleF(b.X + c.S(16), y, bw, c.S(30)), "Folder", () => Ctl.Open(Folder), false, Icons.Folder);
            c.Button(new RectangleF(b.X + c.S(16) + bw + gap, y, bw, c.S(30)), "Snipping Tool", () => Ctl.Open("ms-screensketch:"), false, Icons.Edit);
        }
        void Big(DrawCtx c, RectangleF r, string glyph, string label, Action a)
        {
            bool h = c.IsHover(r);
            Ui.FillRound(c.G, r, c.S(12), h ? c.T.TileHover : c.T.Tile);
            Ui.StrokeRound(c.G, r, c.S(12), c.T.TileBorder);
            Ui.Icon(c.G, glyph, new RectangleF(r.X, r.Y + c.S(12), r.Width, c.S(24)), h ? c.T.Accent : c.T.Text, c.S(18));
            Ui.Text(c.G, label, c.FS(12), c.T.Text, new RectangleF(r.X, r.Y + c.S(42), r.Width, c.S(18)), 0);
            c.Hit(r, a);
        }
    }
}
