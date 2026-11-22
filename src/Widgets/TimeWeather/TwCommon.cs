// TimeWeather shared helpers: fonts, baseline text, time zones, analog faces, seven-segment digits, flip cards,
// vector weather icons (Segoe Fluent Icons has no real weather glyph set, so icons are drawn as paths).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NovaBar
{
    public static class TwF
    {
        static bool init; static string reg, semi, light;
        static void Init()
        {
            if (init) return; init = true;
            reg = Ui.HasFamily("Segoe UI Variable Display") ? "Segoe UI Variable Display" : Ui.HasFamily("Segoe UI") ? "Segoe UI" : Ui.TextFamily;
            semi = Ui.HasFamily("Segoe UI Variable Display Semibold") ? "Segoe UI Variable Display Semibold" : Ui.HasFamily("Segoe UI Semibold") ? "Segoe UI Semibold" : null;
            light = Ui.HasFamily("Segoe UI Variable Display Light") ? "Segoe UI Variable Display Light" : Ui.HasFamily("Segoe UI Light") ? "Segoe UI Light" : null;
        }
        public static Font Reg(float px) { Init(); return Ui.GetFont(reg, px, FontStyle.Regular); }
        public static Font Semi(float px) { Init(); return semi != null ? Ui.GetFont(semi, px, FontStyle.Regular) : Ui.GetFont(reg, px, FontStyle.Bold); }
        public static Font Bold(float px) { Init(); return Ui.GetFont(reg, px, FontStyle.Bold); }
        public static Font Light(float px) { Init(); return light != null ? Ui.GetFont(light, px, FontStyle.Regular) : Ui.GetFont(reg, px, FontStyle.Regular); }
    }

    /// <summary>Baseline-accurate text placement (lets mixed font sizes share a baseline).</summary>
    public static class TwT
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static float Asc(Font f) { var ff = f.FontFamily; return f.Size * ff.GetCellAscent(f.Style) / ff.GetEmHeight(f.Style); }
        public static float Cap(Font f) { return f.Size * 0.70f; }
        public static float W(Graphics g, string s, Font f) { return Ui.Measure(g, s, f).Width; }
        /// <summary>Draw s with its baseline at y. align -1 left (x = left), 0 center, 1 right (x = right). Returns width.</summary>
        public static float Str(Graphics g, string s, Font f, Color c, float x, float baseline, int align = -1)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            float w = W(g, s, f);
            float left = align < 0 ? x : align == 0 ? x - w / 2 : x - w;
            g.DrawString(s, f, Ui.B(c), left, baseline - Asc(f), StringFormat.GenericTypographic);
            return w;
        }
        /// <summary>Baseline that vertically centers cap-height text in r.</summary>
        public static float Mid(RectangleF r, Font f) { return r.Y + r.Height / 2f + Cap(f) / 2f; }
        public static string Fmt(DateTime t, string f) { return t.ToString(f, Inv); }
        public static string Up(string s) { return s.ToUpperInvariant(); }
    }

    public static class TwTime
    {
        static readonly Dictionary<string, TimeZoneInfo> zones = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> iana = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"America/Los_Angeles","Pacific Standard Time"},{"America/Denver","Mountain Standard Time"},{"America/Chicago","Central Standard Time"},
            {"America/New_York","Eastern Standard Time"},{"America/Sao_Paulo","E. South America Standard Time"},{"America/Toronto","Eastern Standard Time"},
            {"Europe/London","GMT Standard Time"},{"Europe/Paris","Romance Standard Time"},{"Europe/Berlin","W. Europe Standard Time"},
            {"Europe/Moscow","Russian Standard Time"},{"Europe/Istanbul","Turkey Standard Time"},{"Asia/Riyadh","Arab Standard Time"},
            {"Asia/Dubai","Arabian Standard Time"},{"Asia/Kolkata","India Standard Time"},{"Asia/Singapore","Singapore Standard Time"},
            {"Asia/Shanghai","China Standard Time"},{"Asia/Hong_Kong","China Standard Time"},{"Asia/Tokyo","Tokyo Standard Time"},
            {"Asia/Seoul","Korea Standard Time"},{"Australia/Sydney","AUS Eastern Standard Time"},{"Pacific/Auckland","New Zealand Standard Time"},
            {"UTC","UTC"},{"Etc/UTC","UTC"},{"Africa/Cairo","Egypt Standard Time"},{"Africa/Lagos","W. Central Africa Standard Time"}
        };
        public static TimeZoneInfo Zone(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Equals("Local", StringComparison.OrdinalIgnoreCase)) return TimeZoneInfo.Local;
            id = id.Trim();
            lock (zones)
            {
                TimeZoneInfo z; if (zones.TryGetValue(id, out z)) return z;
                string win; var look = iana.TryGetValue(id, out win) ? win : id;
                try { z = TimeZoneInfo.FindSystemTimeZoneById(look); } catch { z = null; }
                if (z == null) z = TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(x => x.DisplayName.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0);
                if (z == null) z = TimeZoneInfo.Local;
                zones[id] = z; return z;
            }
        }
        public static DateTime In(TimeZoneInfo z) { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, z); }
        public static TimeSpan Diff(TimeZoneInfo z) { var u = DateTime.UtcNow; return z.GetUtcOffset(u) - TimeZoneInfo.Local.GetUtcOffset(u); }
        /// <summary>"+3h", "−5h 30m", "Same time".</summary>
        public static string Offset(TimeZoneInfo z)
        {
            var d = Diff(z); if (Math.Abs(d.TotalMinutes) < 1) return "Same time";
            var a = d.Duration(); string s = (d.Ticks > 0 ? "+" : "−") + (int)a.TotalHours + "h";
            if (a.Minutes != 0) s += " " + a.Minutes + "m"; return s;
        }
        public static string Utc(TimeZoneInfo z)
        {
            var o = z.GetUtcOffset(DateTime.UtcNow); var a = o.Duration();
            return "UTC" + (o.Ticks < 0 ? "−" : "+") + (int)a.TotalHours + (a.Minutes != 0 ? ":" + a.Minutes.ToString("00") : "");
        }
        public static string DayRel(DateTime there)
        {
            var d = (there.Date - DateTime.Now.Date).Days;
            return d == 0 ? "Today" : d == 1 ? "Tomorrow" : d == -1 ? "Yesterday" : TwT.Fmt(there, "ddd");
        }
        /// <summary>"San Francisco" -> "SF", "London" -> "LON".</summary>
        public static string Abbrev(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            var parts = label.Trim().Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (label.Trim().Length <= 4) return label.Trim().ToUpperInvariant();
            if (parts.Length > 1) return new string(parts.Take(3).Select(p => char.ToUpperInvariant(p[0])).ToArray());
            return label.Trim().Substring(0, 3).ToUpperInvariant();
        }
        public static int IsoWeek(DateTime d)
        {
            var day = CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(d);
            if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday) d = d.AddDays(3);
            return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }
        public static int Mon0(DateTime d) { return ((int)d.DayOfWeek + 6) % 7; } // Monday = 0
    }

    public static class TwDraw
    {
        public static PointF Polar(PointF c, float r, double deg) { var a = (deg - 90) * Math.PI / 180; return new PointF(c.X + (float)(Math.Cos(a) * r), c.Y + (float)(Math.Sin(a) * r)); }

        /// <summary>Analog clock face. ticks: 0 none, 4 quarters, 12 hours, 60 with minute marks.</summary>
        public static void Analog(Graphics g, PointF c, float r, DateTime t, Color face, Color rim, Color tick, Color hand, Color sec, int ticks, bool seconds)
        {
            if (face.A > 0) Ui.FillCircle(g, c, r, face);
            if (rim.A > 0) using (var p = new Pen(rim, Math.Max(1f, r * 0.04f))) g.DrawEllipse(p, c.X - r, c.Y - r, 2 * r, 2 * r);
            if (ticks > 0)
            {
                for (int i = 0; i < ticks; i++)
                {
                    bool major = ticks == 60 ? i % 5 == 0 : true;
                    bool quarter = ticks == 60 ? i % 15 == 0 : (ticks == 12 ? i % 3 == 0 : true);
                    float w = r * (quarter ? 0.075f : major ? 0.05f : 0.022f);
                    float len = r * (quarter ? 0.2f : major ? 0.13f : 0.06f);
                    double deg = i * 360.0 / ticks;
                    using (var p = Ui.P(major ? tick : Ui.WithA(tick, tick.A / 2), Math.Max(0.8f, w)))
                        g.DrawLine(p, Polar(c, r * 0.9f, deg), Polar(c, r * 0.9f - len, deg));
                }
            }
            double sDeg = (t.Second + t.Millisecond / 1000.0) * 6;
            double mDeg = (t.Minute + t.Second / 60.0) * 6;
            double hDeg = ((t.Hour % 12) + t.Minute / 60.0) * 30;
            using (var p = Ui.P(hand, Math.Max(1.4f, r * 0.11f))) g.DrawLine(p, Polar(c, -r * 0.08f, hDeg), Polar(c, r * 0.5f, hDeg));
            using (var p = Ui.P(hand, Math.Max(1.1f, r * 0.075f))) g.DrawLine(p, Polar(c, -r * 0.08f, mDeg), Polar(c, r * 0.78f, mDeg));
            if (seconds)
            {
                using (var p = Ui.P(sec, Math.Max(0.8f, r * 0.03f))) g.DrawLine(p, Polar(c, -r * 0.18f, sDeg), Polar(c, r * 0.84f, sDeg));
                Ui.FillCircle(g, c, Math.Max(1.4f, r * 0.08f), sec);
            }
            else Ui.FillCircle(g, c, Math.Max(1.2f, r * 0.08f), hand);
        }

        // ---- seven segment ----
        static readonly string[] segMap = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };
        /// <summary>Seven-segment digit in r (slight italic). digit -1 = all off.</summary>
        public static void Seg(Graphics g, int digit, RectangleF r, Color on, Color off, float thick, float skew = 0.08f)
        {
            float t = thick, gap = Math.Max(0.6f, t * 0.18f);
            float x0 = r.X + t / 2, x1 = r.Right - t / 2, y0 = r.Y + t / 2, y1 = r.Bottom - t / 2, ym = (y0 + y1) / 2, cy = (r.Y + r.Bottom) / 2;
            string lit = digit >= 0 && digit <= 9 ? segMap[digit] : "";
            Action<char, PointF[]> seg = (ch, pts) =>
            {
                for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(pts[i].X + (cy - pts[i].Y) * skew, pts[i].Y);
                var col = lit.IndexOf(ch) >= 0 ? on : off;
                if (col.A > 0) g.FillPolygon(Ui.B(col), pts);
            };
            Func<float, float, float, PointF[]> hz = (cx, yy, len) => new[] {
                new PointF(cx - len / 2, yy), new PointF(cx - len / 2 + t / 2, yy - t / 2), new PointF(cx + len / 2 - t / 2, yy - t / 2),
                new PointF(cx + len / 2, yy), new PointF(cx + len / 2 - t / 2, yy + t / 2), new PointF(cx - len / 2 + t / 2, yy + t / 2) };
            Func<float, float, float, PointF[]> vt = (xx, cyy, len) => new[] {
                new PointF(xx, cyy - len / 2), new PointF(xx + t / 2, cyy - len / 2 + t / 2), new PointF(xx + t / 2, cyy + len / 2 - t / 2),
                new PointF(xx, cyy + len / 2), new PointF(xx - t / 2, cyy + len / 2 - t / 2), new PointF(xx - t / 2, cyy - len / 2 + t / 2) };
            float hl = (x1 - x0) - gap * 2, vl1 = (ym - y0) - gap * 2;
            seg('a', hz((x0 + x1) / 2, y0, hl)); seg('g', hz((x0 + x1) / 2, ym, hl)); seg('d', hz((x0 + x1) / 2, y1, hl));
            seg('f', vt(x0, (y0 + ym) / 2, vl1)); seg('b', vt(x1, (y0 + ym) / 2, vl1));
            seg('e', vt(x0, (ym + y1) / 2, vl1)); seg('c', vt(x1, (ym + y1) / 2, vl1));
        }
        /// <summary>Draw a seven-segment string ("12:34"); returns the width used. h = digit height.</summary>
        public static float SegStr(Graphics g, string s, float x, float y, float h, Color on, Color off, bool measureOnly = false)
        {
            float dw = h * 0.52f, thick = Math.Max(1.6f, h * 0.15f), sp = h * 0.16f, cw = h * 0.3f, cx = x;
            foreach (var ch in s)
            {
                if (ch == ':')
                {
                    if (!measureOnly) { float rr = thick * 0.55f; Ui.FillCircle(g, new PointF(cx + cw / 2 + h * 0.05f, y + h * 0.3f), rr, on); Ui.FillCircle(g, new PointF(cx + cw / 2 - h * 0.03f, y + h * 0.72f), rr, on); }
                    cx += cw; continue;
                }
                if (ch == ' ') { cx += dw + sp; continue; }
                if (!measureOnly) Seg(g, char.IsDigit(ch) ? ch - '0' : -1, new RectangleF(cx, y, dw, h), on, off, thick);
                cx += dw + sp;
            }
            return cx - x - (s.Length > 0 && s[s.Length - 1] != ':' ? sp : 0);
        }

        // ---- flip card ----
        /// <summary>Split-flap card. prog in 0..1 animates the flap from old to new (1 = settled).</summary>
        public static void Flip(Graphics g, RectangleF r, string cur, string old, double prog, Font f, Color card, Color fg, Color seam)
        {
            float rad = Math.Max(2f, r.Width * 0.14f), mid = r.Y + r.Height / 2;
            var top = new RectangleF(r.X, r.Y, r.Width, r.Height / 2); var bot = new RectangleF(r.X, mid, r.Width, r.Height / 2);
            Ui.FillRound(g, r, rad, card);
            // subtle top sheen
            Ui.FillRound(g, new RectangleF(r.X, r.Y, r.Width, r.Height / 2), rad, Color.FromArgb(14, 255, 255, 255));
            bool anim = prog < 1 && old != null && old != cur;
            float base0 = r.Y + r.Height / 2 + TwT.Cap(f) / 2;
            Action<string, RectangleF, float, Color> half = (s, clip, sy, col) =>
            {
                var st = g.Save();
                g.SetClip(clip, CombineMode.Intersect);
                if (sy != 1f) { g.TranslateTransform(0, mid); g.ScaleTransform(1, Math.Max(0.001f, sy)); g.TranslateTransform(0, -mid); }
                TwT.Str(g, s, f, col, r.X + r.Width / 2, base0, 0);
                g.Restore(st);
            };
            if (!anim) { half(cur, top, 1f, fg); half(cur, bot, 1f, fg); }
            else
            {
                half(cur, top, 1f, fg);                  // new top revealed behind the falling flap
                half(old, bot, 1f, fg);                  // old bottom until the flap lands
                if (prog < 0.5)
                {
                    float sy = (float)(1 - prog * 2);    // old top folds down
                    var st = g.Save(); g.TranslateTransform(0, mid); g.ScaleTransform(1, Math.Max(0.001f, sy)); g.TranslateTransform(0, -mid);
                    Ui.FillRound(g, top, rad, card); g.Restore(st);
                    half(old, top, sy, Ui.Mix(fg, card, 0.25 * prog * 2));
                }
                else
                {
                    float sy = (float)((prog - 0.5) * 2); // new bottom unfolds
                    var st = g.Save(); g.TranslateTransform(0, mid); g.ScaleTransform(1, Math.Max(0.001f, sy)); g.TranslateTransform(0, -mid);
                    Ui.FillRound(g, bot, rad, card); g.Restore(st);
                    half(cur, bot, sy, Ui.Mix(fg, card, 0.3 * (1 - sy)));
                    using (var p = new Pen(Color.FromArgb((int)(60 * (1 - sy)), 0, 0, 0), 1f)) g.DrawLine(p, r.X + rad, mid + bot.Height * sy, r.Right - rad, mid + bot.Height * sy);
                }
            }
            using (var p = new Pen(seam, Math.Max(1f, r.Height * 0.035f))) g.DrawLine(p, r.X, mid, r.Right, mid);
        }

        /// <summary>Calendar page: coloured band with month, big day number.</summary>
        public static void CalPage(Graphics g, RectangleF r, DateTime d, Theme t, float sc)
        {
            float rad = r.Width * 0.18f, band = r.Height * 0.34f;
            var body = t.Dark ? Ui.WithA(t.Text, 30) : (t.IsGlass ? Color.FromArgb(200, 255, 255, 255) : Ui.Mix(t.Tile, t.Text, 0.04));
            Ui.FillRound(g, r, rad, body);
            if (!t.Dark) Ui.StrokeRound(g, r, rad, Ui.WithA(t.Text, 26), 1f);
            var st = g.Save(); g.SetClip(new RectangleF(r.X, r.Y, r.Width, band), CombineMode.Intersect);
            Ui.FillRound(g, r, rad, t.Bad); g.Restore(st);
            var mf = TwF.Semi(band * 0.62f);
            TwT.Str(g, TwT.Up(TwT.Fmt(d, "MMM")), mf, Color.White, r.X + r.Width / 2, TwT.Mid(new RectangleF(r.X, r.Y, r.Width, band), mf), 0);
            var nf = TwF.Semi((r.Height - band) * 0.66f);
            TwT.Str(g, d.Day.ToString(), nf, t.Text, r.X + r.Width / 2, TwT.Mid(new RectangleF(r.X, r.Y + band, r.Width, r.Height - band), nf) + sc * 0.3f, 0);
        }

        public static Color Card(Theme t) { return t.Dark ? Ui.WithA(t.Text, t.IsGlass ? 22 : 12) : Ui.WithA(t.Text, 12); }
        public static Color Divider(Theme t) { return Ui.WithA(t.Text, t.Dark ? 34 : 30); }
    }

    /// <summary>Very small JSON-text cache in Paths.CacheDir (survives restarts so data shows instantly).</summary>
    public static class TwCache
    {
        static string P(string name) { return Path.Combine(Paths.CacheDir, "tw-" + name + ".json"); }
        public static void Write(string name, Dictionary<string, object> d) { try { File.WriteAllText(P(name), J.Stringify(d), Encoding.UTF8); } catch { } }
        public static Dictionary<string, object> Read(string name) { try { var f = P(name); return File.Exists(f) ? J.ParseObj(File.ReadAllText(f, Encoding.UTF8)) : null; } catch { return null; } }
        public static double N(System.Collections.IList a, int i, double def = double.NaN) { if (a == null || i < 0 || i >= a.Count || a[i] == null) return def; return J.Num(a[i], null, def); }
    }
}

namespace NovaBar
{
    /// <summary>
    /// GET with gzip only. Core Http enables Deflate decompression, and some servers (Open-Meteo) answer with zlib-wrapped
    /// deflate that .NET's DeflateStream rejects ("Block length does not match with its complement") -> null.
    /// </summary>
    public static class TwNet
    {
        public static string Get(string url, int timeoutMs = 12000)
        {
            Http.Esc(""); // make sure Core Http's static ctor has set TLS 1.2/1.3
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "NovaBar/1.0 (Windows desktop widget)";
                req.Accept = "application/json";
                req.AutomaticDecompression = System.Net.DecompressionMethods.GZip;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (var r = new System.IO.StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8)) return r.ReadToEnd();
            }
            catch { return null; }
        }
    }
}
