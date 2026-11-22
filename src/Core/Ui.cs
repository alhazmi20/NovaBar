// Shared drawing helpers. Everything is GDI+ on a premultiplied ARGB back buffer.
// Fonts/brushes/pens are cached; widgets should use these instead of allocating per frame.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace NovaBar
{
    public static class Ui
    {
        // ------- fonts -------
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static string textFamily, iconFamily, monoFamily;
        public static string TextFamily
        {
            get
            {
                if (textFamily == null)
                {
                    var want = AppConfig.Current.Font;
                    textFamily = HasFamily(want) ? want : HasFamily("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
                }
                return textFamily;
            }
        }
        public static string IconFamily { get { if (iconFamily == null) iconFamily = HasFamily("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets"; return iconFamily; } }
        public static string MonoFamily { get { if (monoFamily == null) monoFamily = HasFamily("Cascadia Mono") ? "Cascadia Mono" : "Consolas"; return monoFamily; } }
        public static void ResetFonts() { lock (fonts) { foreach (var f in fonts.Values) f.Dispose(); fonts.Clear(); textFamily = null; } }
        static HashSet<string> families;
        public static bool HasFamily(string name)
        {
            if (families == null) { families = new HashSet<string>(StringComparer.OrdinalIgnoreCase); using (var ifc = new InstalledFontCollection()) foreach (var f in ifc.Families) families.Add(f.Name); }
            return name != null && families.Contains(name);
        }

        /// <summary>Text font. size in pixels (already scaled).</summary>
        public static Font Font(float px, bool bold = false) { return GetFont(TextFamily, px, bold ? FontStyle.Bold : FontStyle.Regular); }
        public static Font FontSemi(float px) { return GetFont(HasFamily("Segoe UI Semibold") ? "Segoe UI Semibold" : TextFamily, px, FontStyle.Regular); }
        public static Font Mono(float px, bool bold = false) { return GetFont(MonoFamily, px, bold ? FontStyle.Bold : FontStyle.Regular); }
        public static Font IconFont(float px) { return GetFont(IconFamily, px, FontStyle.Regular); }
        public static Font GetFont(string family, float px, FontStyle st)
        {
            px = (float)Math.Round(px * 2) / 2f; if (px < 4) px = 4;
            var key = family + "|" + px + "|" + (int)st;
            lock (fonts)
            {
                Font f;
                if (!fonts.TryGetValue(key, out f))
                {
                    try { f = new Font(family, px, st, GraphicsUnit.Pixel); } catch { f = new Font("Segoe UI", px, st, GraphicsUnit.Pixel); }
                    fonts[key] = f;
                }
                return f;
            }
        }

        // ------- brushes / pens -------
        static readonly Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();
        public static SolidBrush B(Color c)
        {
            int k = c.ToArgb(); SolidBrush b;
            if (!brushes.TryGetValue(k, out b)) { if (brushes.Count > 512) { foreach (var x in brushes.Values) x.Dispose(); brushes.Clear(); } b = new SolidBrush(c); brushes[k] = b; }
            return b;
        }
        /// <summary>Returns a NEW pen (dispose it, or use `using`).</summary>
        public static Pen P(Color c, float w, bool round = true)
        {
            var p = new Pen(c, w); if (round) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round; }
            return p;
        }

        static readonly StringFormat sfLeft = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
        static readonly StringFormat sfCenter = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        static readonly StringFormat sfRight = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        static readonly StringFormat sfWrap = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };

        // ------- text -------
        /// <summary>Draw single-line text, vertically centered in r. align: -1 left, 0 center, 1 right.</summary>
        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, int align = -1)
        {
            if (string.IsNullOrEmpty(s)) return;
            g.DrawString(s, f, B(c), r, align < 0 ? sfLeft : align == 0 ? sfCenter : sfRight);
        }
        /// <summary>Draw text at a point (top-left).</summary>
        public static void TextAt(Graphics g, string s, Font f, Color c, float x, float y)
        {
            if (string.IsNullOrEmpty(s)) return;
            g.DrawString(s, f, B(c), x, y, StringFormat.GenericTypographic);
        }
        /// <summary>Multi-line wrapped text in r (top aligned).</summary>
        public static void TextWrap(Graphics g, string s, Font f, Color c, RectangleF r)
        {
            if (string.IsNullOrEmpty(s)) return;
            g.DrawString(s, f, B(c), r, sfWrap);
        }
        public static SizeF Measure(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return SizeF.Empty;
            return g.MeasureString(s, f, PointF.Empty, sfLeft);
        }
        public static int MeasureW(Graphics g, string s, Font f) { return (int)Math.Ceiling(Measure(g, s, f).Width); }

        /// <summary>Draw a Segoe Fluent Icons glyph centered in r. e.g. Ui.Icon(g, Icons.Wifi, r, color, 16).</summary>
        public static void Icon(Graphics g, string glyph, RectangleF r, Color c, float px)
        {
            var f = IconFont(px);
            g.DrawString(glyph, f, B(c), r, sfCenter);
        }

        // ------- shapes -------
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            if (c.A == 0) return;
            using (var p = Round(r, rad)) g.FillPath(B(c), p);
        }
        public static void StrokeRound(Graphics g, RectangleF r, float rad, Color c, float w = 1f)
        {
            if (c.A == 0) return;
            using (var p = Round(new RectangleF(r.X + w / 2, r.Y + w / 2, r.Width - w, r.Height - w), rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }
        public static void FillCircle(Graphics g, PointF center, float radius, Color c) { g.FillEllipse(B(c), center.X - radius, center.Y - radius, radius * 2, radius * 2); }
        public static RectangleF Inset(RectangleF r, float dx, float dy) { return new RectangleF(r.X + dx, r.Y + dy, r.Width - 2 * dx, r.Height - 2 * dy); }
        public static RectangleF Square(RectangleF r) { float s = Math.Min(r.Width, r.Height); return new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s); }

        // ------- data viz -------
        /// <summary>Ring gauge. frac 0..1, starts at 12 o'clock, clockwise.</summary>
        public static void Ring(Graphics g, RectangleF r, double frac, Color fg, Color track, float thick)
        {
            var rr = Inset(Square(r), thick / 2, thick / 2);
            if (rr.Width <= 1) return;
            using (var p = new Pen(track, thick)) g.DrawEllipse(p, rr);
            frac = Math.Max(0, Math.Min(1, frac));
            if (frac > 0.001) using (var p = P(fg, thick)) g.DrawArc(p, rr, -90, (float)(360 * frac));
        }
        /// <summary>Horizontal progress bar with rounded ends.</summary>
        public static void Progress(Graphics g, RectangleF r, double frac, Color fg, Color track)
        {
            FillRound(g, r, r.Height / 2, track);
            frac = Math.Max(0, Math.Min(1, frac));
            if (frac > 0) { var f = new RectangleF(r.X, r.Y, Math.Max(r.Height, (float)(r.Width * frac)), r.Height); FillRound(g, f, r.Height / 2, fg); }
        }
        /// <summary>Line chart of values scaled to r. fill = draw a soft gradient under the line.</summary>
        public static void Sparkline(Graphics g, RectangleF r, IList<double> vals, Color c, bool fill = true, float width = 1.6f, double min = double.NaN, double max = double.NaN)
        {
            if (vals == null || vals.Count < 2) return;
            double lo = double.IsNaN(min) ? vals.Min() : min, hi = double.IsNaN(max) ? vals.Max() : max;
            if (hi - lo < 1e-9) { hi = lo + 1; lo -= 0; }
            var pts = new PointF[vals.Count];
            for (int i = 0; i < vals.Count; i++)
                pts[i] = new PointF(r.X + r.Width * i / (vals.Count - 1), r.Bottom - (float)((vals[i] - lo) / (hi - lo)) * r.Height);
            if (fill)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(pts); path.AddLine(pts[pts.Length - 1], new PointF(r.Right, r.Bottom)); path.AddLine(new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom)); path.CloseFigure();
                    using (var br = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Color.FromArgb(Math.Min(110, (int)c.A), c), Color.FromArgb(0, c), LinearGradientMode.Vertical))
                        g.FillPath(br, path);
                }
            }
            using (var p = P(c, width)) g.DrawLines(p, pts);
        }
        /// <summary>Vertical bars chart.</summary>
        public static void Bars(Graphics g, RectangleF r, IList<double> vals, Color c, float gap = 2f, double max = double.NaN, int highlight = -1, Color? hi = null)
        {
            if (vals == null || vals.Count == 0) return;
            double mx = double.IsNaN(max) ? Math.Max(1e-9, vals.Max()) : max;
            float w = (r.Width - gap * (vals.Count - 1)) / vals.Count;
            for (int i = 0; i < vals.Count; i++)
            {
                float h = Math.Max(1.5f, (float)(vals[i] / mx * r.Height));
                var br = new RectangleF(r.X + i * (w + gap), r.Bottom - h, w, h);
                FillRound(g, br, Math.Min(w / 2, 2.5f), i == highlight && hi.HasValue ? hi.Value : c);
            }
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        public static Color WithA(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }

        public static void Hq(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // grayscale AA keeps alpha correct on glass
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        // ------- formatting -------
        public static string Bytes(double b, int dec = 1)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return b.ToString(i == 0 ? "0" : "0." + new string('#', dec)) + " " + u[i];
        }
        public static string Rate(double bps) { return Bytes(bps) + "/s"; }
        public static string Compact(double n)
        {
            double a = Math.Abs(n);
            if (a >= 1e9) return (n / 1e9).ToString("0.#") + "B";
            if (a >= 1e6) return (n / 1e6).ToString("0.#") + "M";
            if (a >= 1e4) return (n / 1e3).ToString("0.#") + "K";
            return n.ToString("#,0");
        }
        public static string Money(double n, string cur = "$") { return cur + (Math.Abs(n) >= 1e5 ? Compact(n) : n.ToString("#,0")); }
        public static string Pct(double frac) { return Math.Round(frac * 100) + "%"; }
        public static string Duration(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            if (t.TotalMinutes >= 1) return t.Minutes + "m";
            return t.Seconds + "s";
        }
        public static string Clock(DateTime t, bool seconds = false)
        {
            bool h24 = AppConfig.Current.Clock24;
            return t.ToString((h24 ? "HH:mm" : "h:mm") + (seconds ? ":ss" : "") + (h24 ? "" : " tt"), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Segoe Fluent Icons code points (subset). Any glyph string works with Ui.Icon.
    /// Note: Segoe Fluent Icons has no real weather glyphs; the weather constants map to the closest existing glyph
    /// (cloud / drop / bolt / sun / moon). Draw vector icons if you need accurate weather art.</summary>
    public static class Icons
    {
        public const string Wifi = "", WifiOff = "", Bluetooth = "", Airplane = "", Hotspot = "", Volume = "", Volume0 = "", Volume1 = "", Volume2 = "", Volume3 = "", Mute = "",
            Mic = "", MicOff = "", Play = "", Pause = "", Next = "", Prev = "", Stop = "", Music = "",
            Battery = "", BatteryCharging = "", Plug = "", Brightness = "", Moon = "", Sun = "", Settings = "", Search = "",
            Calendar = "", Clock = "", Timer = "", Stopwatch = "", Alarm = "", Check = "", CheckBox = "", CheckBoxOn = "", Add = "", Remove = "", Delete = "", Edit = "",
            Note = "", Mail = "", Chat = "", People = "", Person = "", Heart = "", Star = "", Flag = "",
            Cpu = "", Memory = "", Network = "", Download = "", Upload = "", Storage = "", Temperature = "", Speed = "", Chart = "", Money = "", Globe = "",
            Desktop = "", Keyboard = "", Notification = "", Bell = "", Quiet = "", Screenshot = "", Camera = "", Picture = "", Folder = "", File = "", Recent = "", RecycleBin = "",
            Apps = "", Start = "", Window = "", Language = "", Tray = "", ChevronUp = "", ChevronDown = "", ChevronLeft = "", ChevronRight = "", More = "", Refresh = "", Link = "", Code = "",
            Cloud = "", Sunny = "", PartlyCloudy = "", Rain = "", Snow = "", Thunder = "", Fog = "", Wind = "", Drop = "", Sunrise = "", Sunset = "", MoonClear = "",
            Focus = "", Coffee = "", Lock = "", Key = "", Power = "", Info = "", Warning = "", Sparkle = "", Robot = "", Trophy = "", Eye = "", Video = "", Game = "", Phone = "";
    }
}
