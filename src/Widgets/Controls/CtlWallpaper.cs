// Wallpaper Gallery (Bing image archive).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NovaBar
{
    // ------------------------------------------------------------------ Wallpaper Gallery
    public class CtlWallItem { public string Title, Copyright, UrlBase, Thumb, Date; }
    public class CtlWallSnap
    {
        public List<CtlWallItem> Items = new List<CtlWallItem>();
        public override bool Equals(object o) { var x = o as CtlWallSnap; return x != null && x.Items.Count == Items.Count && x.Items.Select(i => i.UrlBase + i.Thumb).SequenceEqual(Items.Select(i => i.UrlBase + i.Thumb)); }
        public override int GetHashCode() { return Items.Count; }
    }

    [Widget("wallpaper", "Wallpaper Gallery", "Online & Mail", "Browse and set new wallpapers.", RefName = "Wallpaper Gallery", Data = DataKind.Live)]
    public class CtlWallpaperWidget : Widget
    {
        const string Key = "ctl.wall";
        static string Dir { get { var d = Path.Combine(Paths.CacheDir, "wall"); Directory.CreateDirectory(d); return d; } }
        static string mkt = "en-US";
        int page; string status; int hover = -1; string applying;
        static CtlWallpaperWidget() { Hub.Define(Key, 3 * 3600 * 1000, Sample); }

        static string Safe(string s) { foreach (var ch in Path.GetInvalidFileNameChars().Concat(new[] { '=', '&', '?' })) s = s.Replace(ch, '_'); return s; }
        static object Sample()
        {
            var listFile = Path.Combine(Dir, "list.json");
            var items = new List<CtlWallItem>(); var seen = new HashSet<string>();
            foreach (var idx in new[] { 0, 7 })
            {
                var j = Http.Get("https://www.bing.com/HPImageArchive.aspx?format=js&idx=" + idx + "&n=8&mkt=" + mkt);
                var arr = J.Arr(J.Parse(j), "images"); if (arr == null) continue;
                foreach (var o in arr)
                {
                    var ub = J.Str(o, "urlbase"); if (ub.Length == 0 || !seen.Add(ub)) continue;
                    var cr = J.Str(o, "copyright"); var t = J.Str(o, "title");
                    if (t.Length == 0) { int p = cr.IndexOf(" ("); t = p > 0 ? cr.Substring(0, p) : cr; }
                    items.Add(new CtlWallItem { UrlBase = ub, Title = t, Copyright = cr, Date = J.Str(o, "startdate") });
                }
            }
            if (items.Count > 0) { try { File.WriteAllText(listFile, J.Stringify(items.Select(i => (object)new Dictionary<string, object> { { "u", i.UrlBase }, { "t", i.Title }, { "c", i.Copyright }, { "d", i.Date } }).ToArray())); } catch { } }
            else
            {
                try
                {
                    var arr = J.Parse(File.ReadAllText(listFile)) as System.Collections.IList;
                    if (arr != null) foreach (var o in arr) items.Add(new CtlWallItem { UrlBase = J.Str(o, "u"), Title = J.Str(o, "t"), Copyright = J.Str(o, "c"), Date = J.Str(o, "d") });
                }
                catch { }
            }
            if (items.Count == 0) return null;
            foreach (var it in items)
            {
                var f = Path.Combine(Dir, Safe(it.UrlBase.Replace("/th?id=", "")) + "_t.jpg");
                if (!File.Exists(f))
                {
                    var bytes = Http.GetBytes("https://www.bing.com" + it.UrlBase + "_400x240.jpg");
                    if (bytes != null && bytes.Length > 1000) try { File.WriteAllBytes(f, bytes); } catch { }
                }
                if (File.Exists(f)) it.Thumb = f;
            }
            return new CtlWallSnap { Items = items.Where(i => i.Thumb != null).ToList() };
        }
        static Bitmap LoadThumb(string path, int w, int h)
        {
            try
            {
                using (var fs = File.OpenRead(path)) using (var img = Image.FromStream(fs))
                {
                    var b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        float sc = Math.Max((float)w / img.Width, (float)h / img.Height);
                        g.DrawImage(img, (w - img.Width * sc) / 2, (h - img.Height * sc) / 2, img.Width * sc, img.Height * sc);
                    }
                    return b;
                }
            }
            catch { return null; }
        }
        Bitmap Thumb(CtlWallItem it, int w, int h) { var p = it.Thumb; return CtlIcons.Peek("wall:" + p + ":" + w + "x" + h, w, () => LoadThumb(p, w, h), this); }

        public override SettingDef[] SettingDefs
        {
            get { return new[] { SettingDef.Choice("market", "Region", "en-US", "en-US", "en-GB", "de-DE", "fr-FR", "ja-JP", "zh-CN", "en-IN", "en-CA", "en-AU"), SettingDef.Choice("fit", "Fit", "Fill", "Fill", "Fit", "Stretch", "Center", "Span") }; }
        }
        public override void OnStart() { mkt = SStr("market"); Hub.Subscribe(Key, this); }
        public override void OnStop() { Hub.Unsubscribe(Key, this); }
        public override void OnPanelOpen() { status = null; if (Hub.Get(Key) == null) Hub.Refresh(Key); }
        CtlWallSnap Snap { get { return Hub.Get<CtlWallSnap>(Key); } }

        void Apply(CtlWallItem it)
        {
            if (applying != null) return;
            applying = it.UrlBase; status = "Downloading…"; Invalidate();
            var fit = SStr("fit");
            Ctl.Async(() =>
            {
                try
                {
                    var f = Path.Combine(Dir, Safe(it.UrlBase.Replace("/th?id=", "")) + "_full.jpg");
                    if (!File.Exists(f))
                    {
                        var bytes = Http.GetBytes("https://www.bing.com" + it.UrlBase + "_UHD.jpg");
                        if (bytes == null || bytes.Length < 10000) bytes = Http.GetBytes("https://www.bing.com" + it.UrlBase + "_1920x1080.jpg");
                        if (bytes == null) { status = "Download failed"; return; }
                        File.WriteAllBytes(f, bytes);
                    }
                    string style = fit == "Fit" ? "6" : fit == "Stretch" ? "2" : fit == "Center" ? "0" : fit == "Span" ? "22" : "10";
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop")) { k.SetValue("WallpaperStyle", style); k.SetValue("TileWallpaper", "0"); }
                    bool ok = CtlNative.SystemParametersInfo(20 /*SPI_SETDESKWALLPAPER*/, 0, f, 3 /*UPDATEINIFILE|SENDCHANGE*/);
                    status = ok ? "Wallpaper set" : "Couldn't set wallpaper";
                    if (ok) { State["current"] = it.UrlBase; SaveState(); }
                }
                catch { status = "Couldn't set wallpaper"; }
                finally { applying = null; Invalidate(); }
            });
        }

        public override int Measure(DrawCtx c) { return c.Si(52); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var s = Snap;
            var r = new RectangleF(b.X + c.S(8), b.Y + c.S(7), b.Width - c.S(16), b.Height - c.S(14));
            Bitmap t = s != null && s.Items.Count > 0 ? Thumb(s.Items[0], (int)r.Width, (int)r.Height) : null;
            if (t != null) { Ctl.DrawImg(c.G, t, r); Ui.StrokeRound(c.G, r, c.S(5), c.T.TileBorder); }
            else { Ui.FillRound(c.G, r, c.S(5), c.T.Track); Ui.Icon(c.G, Icons.Picture, r, c.T.TextDim, c.S(13)); }
        }

        public override Size PanelSize(float sc) { return new Size((int)(380 * sc), (int)(336 * sc)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var s = Snap;
            int per = 9, pages = s == null ? 1 : Math.Max(1, (int)Math.Ceiling(s.Items.Count / (double)per));
            if (page >= pages) page = pages - 1;
            float y = c.PanelHeader("Wallpapers", null, Icons.Picture);
            // pager in header
            c.IconButton(new RectangleF(b.Right - c.S(72), b.Y + c.S(10), c.S(26), c.S(26)), Icons.ChevronLeft, () => { if (page > 0) { page--; Invalidate(); } }, false, 11);
            Ui.Text(c.G, (page + 1) + "/" + pages, c.F(10.5f), c.T.TextDim, new RectangleF(b.Right - c.S(48), b.Y + c.S(10), c.S(20), c.S(26)), 0);
            c.IconButton(new RectangleF(b.Right - c.S(30), b.Y + c.S(10), c.S(26), c.S(26)), Icons.ChevronRight, () => { if (page < pages - 1) { page++; Invalidate(); } }, false, 11);
            if (s == null) { Ui.Text(c.G, "Loading today's images…", c.F(12), c.T.TextDim, new RectangleF(b.X + c.S(16), y, b.Width, c.S(40))); return; }
            float gap = c.S(8), cw = (b.Width - c.S(32) - gap * 2) / 3, ch = (float)Math.Round(cw * 9 / 16);
            object cur; State.TryGetValue("current", out cur);
            int hv = -1;
            for (int i = 0; i < per; i++)
            {
                int idx = page * per + i; if (idx >= s.Items.Count) break;
                var it = s.Items[idx];
                var r = new RectangleF(b.X + c.S(16) + (i % 3) * (cw + gap), y + (i / 3) * (ch + gap), cw, ch);
                var t = Thumb(it, (int)cw, (int)ch);
                Ui.FillRound(c.G, r, c.S(8), c.T.Track);
                if (t != null) Ctl.DrawRoundImg(c.G, t, r, c.S(8));
                bool h = c.IsHover(r); if (h) hv = idx;
                if (h) Ui.StrokeRound(c.G, r, c.S(8), c.T.Accent, c.S(2));
                if (J.AsStr(cur) == it.UrlBase || applying == it.UrlBase)
                {
                    var br = new RectangleF(r.Right - c.S(22), r.Y + c.S(4), c.S(18), c.S(18));
                    Ui.FillCircle(c.G, new PointF(br.X + br.Width / 2, br.Y + br.Height / 2), c.S(9), c.T.Accent);
                    Ui.Icon(c.G, applying == it.UrlBase ? "" : Icons.Check, br, Color.White, c.S(9));
                }
                if (idx == 0) { var tr = new RectangleF(r.X + c.S(4), r.Bottom - c.S(18), c.S(42), c.S(14)); Ui.FillRound(c.G, tr, c.S(7), Color.FromArgb(150, 0, 0, 0)); Ui.Text(c.G, "Today", c.F(9, true), Color.White, tr, 0); }
                var ii = it; c.Hit(r, () => Apply(ii));
            }
            hover = hv;
            float iy = y + 3 * (ch + gap) + c.S(4);
            var show = hv >= 0 ? s.Items[hv] : (page * per < s.Items.Count ? s.Items[page * per] : null);
            if (show != null)
            {
                Ui.Text(c.G, show.Title, c.FS(12.5f), c.T.Text, new RectangleF(b.X + c.S(16), iy, b.Width - c.S(32), c.S(18)));
                var credit = show.Copyright; int p = credit.IndexOf('('); if (p >= 0) credit = credit.Substring(p).Trim('(', ')', ' ');
                Ui.Text(c.G, status != null && hv < 0 ? status : credit, c.F(10.5f), status != null && hv < 0 ? c.T.Accent : c.T.TextDim, new RectangleF(b.X + c.S(16), iy + c.S(19), b.Width - c.S(32), c.S(15)));
            }
            Ui.Text(c.G, "Click an image to set it as your wallpaper · Bing daily images", c.F(10), c.T.TextFaint, new RectangleF(b.X + c.S(16), b.Bottom - c.S(24), b.Width - c.S(32), c.S(14)));
        }
    }
}
