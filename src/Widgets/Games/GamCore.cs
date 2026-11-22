// Games group shared plumbing (prefix Gam): Valve KeyValues (VDF/ACF) parser, launch helper,
// and a small lazy image cache (local files or web URLs) that hands out pre-scaled, rounded bitmaps.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NovaBar
{
    /// <summary>Text KeyValues parser (libraryfolders.vdf, appmanifest_*.acf, localconfig.vdf).
    /// Objects are case-insensitive Dictionary&lt;string,object&gt;, leaves are strings.</summary>
    public static class GamVdf
    {
        public static Dictionary<string, object> ParseFile(string path)
        {
            try { return Parse(File.ReadAllText(path, Encoding.UTF8)); } catch { return null; }
        }

        public static Dictionary<string, object> Parse(string s)
        {
            if (s == null) return null;
            int i = 0;
            var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            ParseInto(s, ref i, root, 0);
            return root;
        }

        static void ParseInto(string s, ref int i, Dictionary<string, object> obj, int depth)
        {
            while (true)
            {
                string key; int kind = Next(s, ref i, out key);
                if (kind == 0 || kind == 2) return;          // EOF or '}'
                if (kind != 3) continue;                     // stray '{'
                string val; int vk = Next(s, ref i, out val);
                if (vk == 1)
                {
                    var child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    if (depth < 64) ParseInto(s, ref i, child, depth + 1);
                    obj[key] = child;
                }
                else if (vk == 3) obj[key] = val;
                else return;
            }
        }

        /// <summary>0 = EOF, 1 = '{', 2 = '}', 3 = string token.</summary>
        static int Next(string s, ref int i, out string tok)
        {
            tok = null;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '{') { i++; return 1; }
                if (c == '}') { i++; return 2; }
                if (c == '"')
                {
                    var sb = new StringBuilder(); i++;
                    while (i < s.Length && s[i] != '"')
                    {
                        if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            char e = s[i + 1]; i += 2;
                            sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                            continue;
                        }
                        sb.Append(s[i++]);
                    }
                    i++; tok = sb.ToString(); return 3;
                }
                if (c == '[') { while (i < s.Length && s[i] != ']') i++; i++; continue; } // conditionals like [$WIN32]
                int st = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"') i++;
                tok = s.Substring(st, i - st); return 3;
            }
            return 0;
        }

        public static object Get(object o, string path)
        {
            foreach (var p in path.Split('/'))
            {
                var d = o as Dictionary<string, object>; if (d == null) return null;
                object v; if (!d.TryGetValue(p, out v)) return null; o = v;
            }
            return o;
        }
        public static Dictionary<string, object> Obj(object o, string path) { return Get(o, path) as Dictionary<string, object>; }
        public static string Str(object o, string path) { return Get(o, path) as string ?? ""; }
        public static long Num(object o, string path) { long v; return long.TryParse(Str(o, path), out v) ? v : 0; }
    }

    public static class GamUtil
    {
        /// <summary>One-shot launch of a URI/exe (steam://..., spotify:, https://...). Never blocks the UI thread.</summary>
        public static void Launch(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            ThreadPool.QueueUserWorkItem(_ => { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } });
        }
        public static DateTime FromUnix(long s) { return s <= 0 ? DateTime.MinValue : new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s); }
        public static string Ago(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "never";
            var d = DateTime.UtcNow - utc;
            if (d.TotalMinutes < 2) return "just now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " min ago";
            if (d.TotalDays < 1) return (int)d.TotalHours + " h ago";
            if (d.TotalDays < 2) return "yesterday";
            if (d.TotalDays < 30) return (int)d.TotalDays + " days ago";
            return utc.ToLocalTime().ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }
        public static string Hours(double minutes)
        {
            if (minutes < 60) return Math.Round(minutes) + " min";
            double h = minutes / 60.0;
            return (h < 10 ? h.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : Math.Round(h).ToString("#,0")) + " h";
        }
        /// <summary>"1h 12m" style for a live session timer.</summary>
        public static string Session(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return "just started";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            return t.Minutes + "m";
        }
        /// <summary>Stable pleasant color for a name (placeholders / initials).</summary>
        public static Color NameColor(string s)
        {
            int h = 0; foreach (var ch in s ?? "") h = h * 31 + ch;
            Color[] pal = { Color.FromArgb(255, 99, 102, 241), Color.FromArgb(255, 14, 165, 233), Color.FromArgb(255, 16, 185, 129), Color.FromArgb(255, 245, 158, 11), Color.FromArgb(255, 236, 72, 153), Color.FromArgb(255, 139, 92, 246), Color.FromArgb(255, 239, 68, 68), Color.FromArgb(255, 20, 184, 166) };
            return pal[(h & 0x7fffffff) % pal.Length];
        }
        public static string Initials(string s)
        {
            var parts = (s ?? "").Split(new[] { ' ', ':', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Where(p => char.IsLetterOrDigit(p[0])).ToArray();
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
        }
    }

    /// <summary>
    /// Lazy image cache. Get() never blocks: the first call queues a pool load (file path or http URL, web images are
    /// cached on disk), later calls return a bitmap pre-scaled to the exact size with rounded corners (cover-crop).
    /// Originals are downsized to ≤ 480 px; at most ~48 originals and 96 scaled variants are kept.
    /// </summary>
    public static class GamImg
    {
        static readonly object L = new object();
        static readonly Dictionary<string, Bitmap> orig = new Dictionary<string, Bitmap>();
        static readonly HashSet<string> loading = new HashSet<string>();
        static readonly HashSet<string> failed = new HashSet<string>();
        static readonly Dictionary<string, Bitmap> scaled = new Dictionary<string, Bitmap>();
        static readonly Dictionary<string, List<Widget>> waiters = new Dictionary<string, List<Widget>>();

        /// <summary>source: absolute file path or http(s) URL. Returns null while loading / on failure.</summary>
        public static Bitmap Get(string source, int w, int h, float radius, Widget requester)
        {
            if (string.IsNullOrEmpty(source) || w < 2 || h < 2) return null;
            string sk = source + "|" + w + "x" + h + "|" + (int)Math.Round(radius * 4);
            Bitmap o;
            lock (L)
            {
                Bitmap s; if (scaled.TryGetValue(sk, out s)) return s;
                if (!orig.TryGetValue(source, out o))
                {
                    if (failed.Contains(source)) return null;
                    List<Widget> wl; if (!waiters.TryGetValue(source, out wl)) waiters[source] = wl = new List<Widget>();
                    if (requester != null && !wl.Contains(requester)) wl.Add(requester);
                    if (loading.Add(source)) { var src = source; ThreadPool.QueueUserWorkItem(_ => Load(src)); }
                    return null;
                }
                if (scaled.Count >= 96) { foreach (var x in scaled.Values) x.Dispose(); scaled.Clear(); }
                var bmp = Scale(o, w, h, radius);
                scaled[sk] = bmp; return bmp;
            }
        }

        static void Load(string source)
        {
            Bitmap bmp = null;
            try
            {
                byte[] bytes = null;
                if (source.StartsWith("http://") || source.StartsWith("https://"))
                {
                    var dir = Path.Combine(Paths.CacheDir, "gam-img"); Directory.CreateDirectory(dir);
                    string f;
                    using (var sha = SHA1.Create()) f = Path.Combine(dir, BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source))).Replace("-", "").Substring(0, 24) + ".img");
                    if (File.Exists(f)) bytes = File.ReadAllBytes(f);
                    else { bytes = Http.GetBytes(source); if (bytes != null && bytes.Length > 0) try { File.WriteAllBytes(f, bytes); } catch { } }
                }
                else if (File.Exists(source)) bytes = File.ReadAllBytes(source);
                if (bytes != null && bytes.Length > 0 && bytes.Length < 12 * 1024 * 1024)
                    using (var ms = new MemoryStream(bytes))
                    using (var img = Image.FromStream(ms))
                    {
                        double k = Math.Min(1.0, 480.0 / Math.Max(img.Width, img.Height));
                        int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
                        bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(bmp)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(img, 0, 0, w, h); }
                    }
            }
            catch { if (bmp != null) bmp.Dispose(); bmp = null; }
            Widget[] ws;
            lock (L)
            {
                loading.Remove(source);
                if (bmp == null) failed.Add(source);
                else
                {
                    if (orig.Count >= 48)
                    {
                        foreach (var x in orig.Values) x.Dispose(); orig.Clear();
                        foreach (var x in scaled.Values) x.Dispose(); scaled.Clear();
                    }
                    orig[source] = bmp;
                }
                List<Widget> wl; ws = waiters.TryGetValue(source, out wl) ? wl.ToArray() : new Widget[0]; waiters.Remove(source);
            }
            foreach (var w in ws) w.Invalidate();
        }

        static Bitmap Scale(Bitmap o, int w, int h, float radius)
        {
            // cover-crop source to target aspect
            double ta = (double)w / h, sa = (double)o.Width / o.Height;
            RectangleF src = sa > ta ? new RectangleF((float)((o.Width - o.Height * ta) / 2), 0, (float)(o.Height * ta), o.Height)
                                     : new RectangleF(0, (float)((o.Height - o.Width / ta) / 2), o.Width, (float)(o.Width / ta));
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var tmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(tmp))
                using (var ia = new ImageAttributes())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(o, new Rectangle(0, 0, w, h), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
                using (var g = Graphics.FromImage(bmp))
                using (var tb = new TextureBrush(tmp))
                using (var path = Ui.Round(new RectangleF(0, 0, w, h), radius))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.FillPath(tb, path);
                }
            }
            return bmp;
        }

        /// <summary>Draw cached image or a gradient placeholder with initials.</summary>
        public static void Draw(DrawCtx c, string source, RectangleF r, float radius, string name, Widget w)
        {
            var bmp = Get(source, (int)Math.Round(r.Width), (int)Math.Round(r.Height), radius, w);
            if (bmp != null) { c.G.DrawImage(bmp, (int)Math.Round(r.X), (int)Math.Round(r.Y), bmp.Width, bmp.Height); return; }
            var col = GamUtil.NameColor(name);
            using (var path = Ui.Round(r, radius))
            using (var br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), col, Ui.Mix(col, Color.Black, 0.45), 35f))
                c.G.FillPath(br, path);
            float px = Math.Min(r.Height * 0.42f, r.Width * 0.3f);
            Ui.Text(c.G, GamUtil.Initials(name), Ui.FontSemi(px), Color.FromArgb(235, 255, 255, 255), r, 0);
        }
    }
}
