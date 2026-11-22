// Weather data: one shared Hub key "tw.weather" (Open-Meteo, no key), current + hourly + 7-day in a single request
// every 15 minutes. Last good response is persisted to Paths.CacheDir so tiles show instantly on startup.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace NovaBar
{
    public class TwHour { public DateTime T; public double Temp, Pop; public int Code; public bool Day; }
    public class TwDay { public DateTime D; public int Code; public double Max, Min, Pop, Wind; public DateTime Rise, Set; }

    public class TwWx
    {
        public DateTime FetchedUtc; public bool Stale, Empty, Imperial; public int Offset; public string City = "";
        public double Temp, Feels, Hum, Wind, Precip; public int Code; public bool IsDay = true;
        public List<TwHour> Hours = new List<TwHour>(); public List<TwDay> Days = new List<TwDay>();
        public DateTime LocalNow { get { return DateTime.UtcNow.AddSeconds(Offset); } }
        public TwDay Today { get { var n = LocalNow.Date; return Days.FirstOrDefault(d => d.D == n) ?? Days.FirstOrDefault(); } }
        public List<TwDay> After(int n) { var d0 = LocalNow.Date; return Days.Where(d => d.D > d0).Take(n).ToList(); }
        public List<TwHour> Next(int n) { var h0 = LocalNow; h0 = new DateTime(h0.Year, h0.Month, h0.Day, h0.Hour, 0, 0); return Hours.Where(h => h.T >= h0).Take(n).ToList(); }
        public string WindUnit { get { return Imperial ? "mph" : "km/h"; } }
        public string RainUnit { get { return Imperial ? "in" : "mm"; } }
        public TwWx AsStale() { var c = (TwWx)MemberwiseClone(); c.Stale = true; return c; }
        public static string Deg(double v) { return double.IsNaN(v) ? "—°" : Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + "°"; }
        public string Ago
        {
            get
            {
                if (Empty) return "Offline";
                var m = (DateTime.UtcNow - FetchedUtc).TotalMinutes;
                string s = m < 1.5 ? "Updated just now" : m < 60 ? "Updated " + (int)m + " min ago" : m < 60 * 24 ? "Updated " + (int)(m / 60) + " h ago" : "Updated " + TwT.Fmt(FetchedUtc.ToLocalTime(), "MMM d");
                return Stale ? "Offline · " + s.Replace("Updated ", "updated ") : s;
            }
        }
    }

    public static class TwWeather
    {
        public const string Key = "tw.weather";
        static TwWx cached; static bool loaded; static string sig; static Timer retry;
        static TwWeather()
        {
            Hub.Define(Key, 15 * 60 * 1000, Sample);
            sig = Sig();
            AppConfig.Changed += () => { var s = Sig(); if (s != sig) { sig = s; cached = null; Hub.Refresh(Key); } };
        }
        static string Sig() { var c = AppConfig.Current; return c.Lat.ToString("0.###", TwT.Inv) + "," + c.Lon.ToString("0.###", TwT.Inv) + "," + c.Units; }
        public static void Sub(Widget w) { Hub.Subscribe(Key, w); }
        public static void Unsub(Widget w) { Hub.Unsubscribe(Key, w); }
        /// <summary>Latest weather (hub value, else the disk cache), or null while nothing is known yet.</summary>
        public static TwWx Now
        {
            get
            {
                var w = Hub.Get<TwWx>(Key); if (w != null) return w;
                if (!loaded) { loaded = true; cached = LoadCache(); }
                return cached;
            }
        }

        static object Sample()
        {
            var c = AppConfig.Current; bool imp = (c.Units ?? "").ToLowerInvariant() == "imperial";
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + c.Lat.ToString("0.####", TwT.Inv) + "&longitude=" + c.Lon.ToString("0.####", TwT.Inv) +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,precipitation,is_day" +
                "&hourly=temperature_2m,weather_code,precipitation_probability" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max,wind_speed_10m_max" +
                "&timezone=auto&forecast_days=7" + (imp ? "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch" : "");
            var txt = TwNet.Get(url);
            if (txt == null) { Thread.Sleep(2000); txt = TwNet.Get(url); }
            var wx = Parse(txt, imp, c.City);
            if (wx != null)
            {
                wx.FetchedUtc = DateTime.UtcNow;
                TwCache.Write("weather", new Dictionary<string, object> { { "sig", Sig() }, { "fetched", wx.FetchedUtc.Ticks.ToString(TwT.Inv) }, { "city", c.City ?? "" }, { "raw", txt } });
                return wx;
            }
            // offline: keep showing the last values, retry in a minute
            if (retry == null) retry = new Timer(_ => { retry = null; Hub.Refresh(Key); }, null, 60000, Timeout.Infinite);
            var prev = Hub.Get<TwWx>(Key) ?? Now;
            if (prev == null) return new TwWx { Empty = true, Stale = true, City = c.City ?? "", Temp = double.NaN };
            return prev.Stale ? prev : prev.AsStale();
        }

        static TwWx LoadCache()
        {
            var d = TwCache.Read("weather"); if (d == null || J.Str(d, "sig") != Sig()) return null;
            var w = Parse(J.Str(d, "raw"), (AppConfig.Current.Units ?? "") == "imperial", J.Str(d, "city"));
            if (w == null) return null;
            long t; if (long.TryParse(J.Str(d, "fetched"), out t)) w.FetchedUtc = new DateTime(t, DateTimeKind.Utc);
            if ((DateTime.UtcNow - w.FetchedUtc).TotalMinutes > 20) w.Stale = false; // just old; hub refresh replaces it shortly
            return w;
        }

        static DateTime Dt(object o)
        {
            DateTime d; var s = J.AsStr(o);
            if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd" }, TwT.Inv, DateTimeStyles.None, out d)) return d;
            return DateTime.MinValue;
        }

        public static TwWx Parse(string txt, bool imp, string city)
        {
            var o = J.ParseObj(txt); if (o == null || J.Obj(o, "current") == null) return null;
            try
            {
                var w = new TwWx { Imperial = imp, City = city ?? "", Offset = (int)J.Num(o, "utc_offset_seconds") };
                w.Temp = J.Num(o, "current.temperature_2m", double.NaN); w.Feels = J.Num(o, "current.apparent_temperature", w.Temp);
                w.Hum = J.Num(o, "current.relative_humidity_2m"); w.Code = (int)J.Num(o, "current.weather_code");
                w.Wind = J.Num(o, "current.wind_speed_10m"); w.Precip = J.Num(o, "current.precipitation"); w.IsDay = J.Num(o, "current.is_day", 1) != 0;
                var dt = J.Arr(o, "daily.time");
                if (dt != null) for (int i = 0; i < dt.Count; i++)
                    {
                        w.Days.Add(new TwDay
                        {
                            D = Dt(dt[i]).Date, Code = (int)TwCache.N(J.Arr(o, "daily.weather_code"), i, 0),
                            Max = TwCache.N(J.Arr(o, "daily.temperature_2m_max"), i), Min = TwCache.N(J.Arr(o, "daily.temperature_2m_min"), i),
                            Pop = TwCache.N(J.Arr(o, "daily.precipitation_probability_max"), i, 0), Wind = TwCache.N(J.Arr(o, "daily.wind_speed_10m_max"), i, 0),
                            Rise = Dt(J.Get(o, "daily.sunrise." + i)), Set = Dt(J.Get(o, "daily.sunset." + i))
                        });
                    }
                var ht = J.Arr(o, "hourly.time"); var hc = J.Arr(o, "hourly.weather_code"); var htp = J.Arr(o, "hourly.temperature_2m"); var hp = J.Arr(o, "hourly.precipitation_probability");
                if (ht != null) for (int i = 0; i < ht.Count; i++)
                    {
                        var h = new TwHour { T = Dt(ht[i]), Temp = TwCache.N(htp, i), Code = (int)TwCache.N(hc, i, 0), Pop = TwCache.N(hp, i, 0) };
                        var day = w.Days.FirstOrDefault(x => x.D == h.T.Date);
                        h.Day = day == null || day.Rise == DateTime.MinValue || (h.T >= day.Rise.AddMinutes(-30) && h.T < day.Set);
                        w.Hours.Add(h);
                    }
                return w;
            }
            catch { return null; }
        }
    }

    public enum TwSky { Clear, Partly, Cloudy, Fog, Drizzle, Rain, Snow, Storm }

    public static class TwWmo
    {
        public static string Text(int c)
        {
            switch (c)
            {
                case 0: return "Clear"; case 1: return "Mostly clear"; case 2: return "Partly cloudy"; case 3: return "Overcast";
                case 45: return "Fog"; case 48: return "Freezing fog";
                case 51: return "Light drizzle"; case 53: return "Drizzle"; case 55: return "Heavy drizzle";
                case 56: case 57: return "Freezing drizzle";
                case 61: return "Light rain"; case 63: return "Rain"; case 65: return "Heavy rain";
                case 66: case 67: return "Freezing rain";
                case 71: return "Light snow"; case 73: return "Snow"; case 75: return "Heavy snow"; case 77: return "Snow grains";
                case 80: return "Light showers"; case 81: return "Showers"; case 82: return "Heavy showers";
                case 85: return "Snow showers"; case 86: return "Heavy snow showers";
                case 95: return "Thunderstorm"; case 96: case 99: return "Thunderstorm, hail";
                default: return "—";
            }
        }
        public static TwSky Sky(int c)
        {
            if (c <= 1) return TwSky.Clear; if (c == 2) return TwSky.Partly; if (c == 3) return TwSky.Cloudy;
            if (c == 45 || c == 48) return TwSky.Fog; if (c >= 51 && c <= 57) return TwSky.Drizzle;
            if ((c >= 61 && c <= 67) || (c >= 80 && c <= 82)) return TwSky.Rain;
            if ((c >= 71 && c <= 77) || c == 85 || c == 86) return TwSky.Snow;
            if (c >= 95) return TwSky.Storm; return TwSky.Cloudy;
        }
        /// <summary>Segoe Fluent glyph that best matches (for places where a font glyph is preferred).</summary>
        public static string Glyph(int c, bool day)
        {
            switch (Sky(c)) { case TwSky.Clear: return day ? Icons.Sun : Icons.Moon; default: return Icons.Cloud; }
        }
    }

    /// <summary>Vector weather icons (24-unit grid), coloured from the theme. Day/night variants.</summary>
    public static class TwIcon
    {
        static GraphicsPath Cloud(float x, float y, float w, float h)
        {
            var p = new GraphicsPath(FillMode.Winding);
            float rr = h * 0.275f;
            using (var cap = Ui.Round(new RectangleF(x, y + h * 0.45f, w, h * 0.55f), rr)) p.AddPath(cap, false);
            p.AddEllipse(x + w * 0.32f - h * 0.3f, y + h * 0.55f - h * 0.3f, h * 0.6f, h * 0.6f);
            p.AddEllipse(x + w * 0.6f - h * 0.42f, y + h * 0.42f - h * 0.42f, h * 0.84f, h * 0.84f);
            return p;
        }
        static void Sun(Graphics g, PointF c, float r, float u, Color col, bool rays)
        {
            Ui.FillCircle(g, c, r, col);
            if (!rays) return;
            using (var p = Ui.P(col, 1.6f * u))
                for (int i = 0; i < 8; i++) g.DrawLine(p, TwDraw.Polar(c, r + 2.0f * u, i * 45), TwDraw.Polar(c, r + 3.9f * u, i * 45));
        }
        static void Moon(Graphics g, PointF c, float r, Color col)
        {
            // crescent = outer circle minus a shifted circle, built from two arcs (anti-aliased)
            var c2 = new PointF(c.X + r * 0.55f, c.Y - r * 0.4f); float r2 = r * 0.82f;
            double dx = c2.X - c.X, dy = c2.Y - c.Y, d = Math.Sqrt(dx * dx + dy * dy);
            double a = (r * r - r2 * r2 + d * d) / (2 * d), h = Math.Sqrt(Math.Max(0, r * r - a * a));
            double mx = c.X + a * dx / d, my = c.Y + a * dy / d;
            var p1 = new PointF((float)(mx - h * dy / d), (float)(my + h * dx / d)); var p2 = new PointF((float)(mx + h * dy / d), (float)(my - h * dx / d));
            Func<PointF, PointF, double> ang = (o, p) => Math.Atan2(p.Y - o.Y, p.X - o.X) * 180 / Math.PI;
            Func<double, double> nrm = v => { v %= 360; return v < 0 ? v + 360 : v; };
            double A1 = ang(c, p1), A2 = ang(c, p2), th = ang(c, c2), s = nrm(A2 - A1);
            double start; double sweep; PointF sp, ep;
            if (nrm(th - A1) < s) { start = A2; sweep = 360 - s; sp = p2; ep = p1; } else { start = A1; sweep = s; sp = p1; ep = p2; }
            double Be = ang(c2, ep), Bs = ang(c2, sp), phi = nrm(th + 180), tt = nrm(Bs - Be);
            using (var path = new GraphicsPath())
            {
                path.AddArc(c.X - r, c.Y - r, 2 * r, 2 * r, (float)start, (float)sweep);
                if (nrm(phi - Be) < tt) path.AddArc(c2.X - r2, c2.Y - r2, 2 * r2, 2 * r2, (float)Be, (float)tt);
                else path.AddArc(c2.X - r2, c2.Y - r2, 2 * r2, 2 * r2, (float)Be, (float)-(360 - tt));
                path.CloseFigure(); g.FillPath(Ui.B(col), path);
            }
        }

        public static Color SunC(Theme t) { return t.Warn; }
        public static Color CloudC(Theme t) { return t.Dark ? Ui.Mix(t.Text, t.TextDim, 0.3) : Ui.Mix(t.TextFaint, t.TextDim, 0.25); }
        public static Color RainC(Theme t) { return t.Dark ? Color.FromArgb(255, 96, 196, 255) : Color.FromArgb(255, 14, 140, 220); }

        public static void Draw(Graphics g, int code, bool day, RectangleF rect, Theme t)
        {
            var b = Ui.Square(rect); float u = b.Width / 24f;
            Func<float, float> X = v => b.X + v * u; Func<float, float> Y = v => b.Y + v * u;
            var sky = TwWmo.Sky(code);
            var cloud = CloudC(t); var sun = SunC(t); var moon = t.Dark ? Ui.Mix(t.Text, t.Warn, 0.3) : Ui.Mix(t.TextDim, t.Warn, 0.45);
            var st = g.Save();
            try
            {
                if (sky == TwSky.Clear)
                {
                    if (day) Sun(g, new PointF(X(12), Y(12)), 4.8f * u, u, sun, true);
                    else Moon(g, new PointF(X(12), Y(12.5f)), 8f * u, moon);
                    if (code == 1) using (var cp = Cloud(X(12), Y(13.5f), 11 * u, 7 * u)) { g.FillPath(Ui.B(Ui.WithA(cloud, 235)), cp); }
                    return;
                }
                if (sky == TwSky.Partly || sky == TwSky.Cloudy)
                {
                    using (var front = Cloud(X(4.5f), Y(9.5f), 18 * u, 11.2f * u))
                    using (var gap = Cloud(X(3.2f), Y(8.2f), 20.6f * u, 13.8f * u))
                    {
                        g.SetClip(gap, CombineMode.Exclude);
                        if (sky == TwSky.Partly) { if (day) Sun(g, new PointF(X(8.5f), Y(8.5f)), 3.7f * u, u * 0.8f, sun, true); else Moon(g, new PointF(X(8.5f), Y(8.5f)), 6f * u, moon); }
                        else using (var back = Cloud(X(1.5f), Y(4.5f), 15 * u, 9.4f * u)) g.FillPath(Ui.B(Ui.Mix(cloud, t.Dark ? Color.Black : Color.White, 0.35)), back);
                        g.Restore(st); st = g.Save();
                        g.FillPath(Ui.B(cloud), front);
                    }
                    return;
                }
                // precipitation / fog / storm: cloud on top, effect below
                using (var cp = Cloud(X(3), Y(2.5f), 18 * u, 11.5f * u)) g.FillPath(Ui.B(sky == TwSky.Storm ? Ui.Mix(cloud, t.TextDim, 0.4) : cloud), cp);
                var rain = RainC(t);
                switch (sky)
                {
                    case TwSky.Fog:
                        using (var p = Ui.P(Ui.WithA(cloud, 200), 1.7f * u)) { g.DrawLine(p, X(4), Y(17.5f), X(20), Y(17.5f)); g.DrawLine(p, X(7), Y(21), X(17), Y(21)); }
                        break;
                    case TwSky.Drizzle:
                        for (int i = 0; i < 3; i++) { Ui.FillCircle(g, new PointF(X(8 + i * 4) - 0.6f * u, Y(17.5f)), 1.1f * u, rain); Ui.FillCircle(g, new PointF(X(6 + i * 4 + 2) - 1.4f * u, Y(21.2f)), 1.1f * u, rain); }
                        break;
                    case TwSky.Rain:
                        using (var p = Ui.P(rain, 1.7f * u)) for (int i = 0; i < 3; i++) g.DrawLine(p, X(8.5f + i * 4), Y(16.2f), X(7 + i * 4), Y(21.5f));
                        break;
                    case TwSky.Snow:
                        var sn = t.Dark ? Ui.Mix(t.Text, rain, 0.35) : Ui.Mix(t.TextDim, rain, 0.5);
                        for (int i = 0; i < 3; i++) { Ui.FillCircle(g, new PointF(X(8 + i * 4), Y(17.3f)), 1.3f * u, sn); }
                        for (int i = 0; i < 2; i++) { Ui.FillCircle(g, new PointF(X(10 + i * 4), Y(21.2f)), 1.3f * u, sn); }
                        break;
                    case TwSky.Storm:
                        var bolt = new[] { new PointF(X(13.2f), Y(11.5f)), new PointF(X(8.6f), Y(18.2f)), new PointF(X(11.8f), Y(18.2f)), new PointF(X(10.2f), Y(23.5f)), new PointF(X(16.2f), Y(15.6f)), new PointF(X(12.9f), Y(15.6f)), new PointF(X(14.8f), Y(11.5f)) };
                        g.FillPolygon(Ui.B(sun), bolt);
                        break;
                }
            }
            finally { g.Restore(st); }
        }
    }
}
