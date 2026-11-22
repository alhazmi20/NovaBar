// Weather tiles: weather, weatherdetails, forecast, weatherstatus, hourly, sun. All share Hub key "tw.weather".
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace NovaBar
{
    public abstract class TwWxBase : TwBase
    {
        protected override bool NeedsWeather { get { return true; } }
        public override int RefreshMs { get { return 60000; } } // keeps "now"-relative bits (hour columns, sun position) current
        public override Size PanelSize(float s) { return TwPanels.WeatherSize(s); }
        public override void DrawPanel(DrawCtx c) { TwPanels.Weather(c); }
        protected const string Dash = "—°";
        protected static Color Dim(DrawCtx c, TwWx w) { return w != null && w.Stale ? c.T.TextDim : c.T.Text; }
    }

    [Widget("weather", "Weather", "Weather", "Temperature now, with today's high and low.", RefName = "Weather", Data = DataKind.Live)]
    public class TwWeatherWidget : TwWxBase
    {
        public override int Measure(DrawCtx c)
        {
            var w = Wx; var d = HasWx(w) ? w.Today : null;
            float tw = TwT.W(c.G, HasWx(w) ? TwWx.Deg(w.Temp) : Dash, TwF.Semi(c.S(18)));
            float hl = d != null ? Math.Max(TwT.W(c.G, "H " + TwWx.Deg(d.Max), c.F(10)), TwT.W(c.G, "L " + TwWx.Deg(d.Min), c.F(10))) : 0;
            return (int)Math.Ceiling(c.S(8) + c.S(22) + c.S(5) + tw + (hl > 0 ? c.S(9) + hl : 0) + c.S(11));
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var w = Wx; float cy = b.Y + b.Height / 2f, x = b.X + c.S(8);
            TwIcon.Draw(g, HasWx(w) ? w.Code : 3, !HasWx(w) || w.IsDay, new RectangleF(x, cy - c.S(11), c.S(22), c.S(22)), t);
            x += c.S(27);
            var tf = TwF.Semi(c.S(18));
            x += TwT.Str(g, HasWx(w) ? TwWx.Deg(w.Temp) : Dash, tf, HasWx(w) ? Dim(c, w) : t.TextFaint, x, TwT.Mid(b, tf));
            var d = HasWx(w) ? w.Today : null;
            if (d != null)
            {
                x += c.S(9);
                TwT.Str(g, "H " + TwWx.Deg(d.Max), c.F(10), t.TextDim, x, cy - c.S(2));
                TwT.Str(g, "L " + TwWx.Deg(d.Min), c.F(10), t.TextFaint, x, cy + c.S(10));
            }
        }
    }

    [Widget("weatherdetails", "Weather Details", "Weather", "Wind, rain and the day's range.", RefName = "Weather Details", Data = DataKind.Live)]
    public class TwWeatherDetailsWidget : TwWxBase
    {
        string[] Vals(TwWx w)
        {
            if (!HasWx(w)) return new[] { "—", "—", "—" };
            var d = w.Today;
            string rain = d != null ? Math.Round(d.Pop) + "%" : "—";
            if (w.Precip > 0) rain += " · " + w.Precip.ToString(w.Imperial ? "0.00" : "0.#", TwT.Inv) + w.RainUnit;
            return new[] { Math.Round(w.Wind) + " " + w.WindUnit, rain, d != null ? Math.Round(d.Min) + "–" + TwWx.Deg(d.Max) : "—" };
        }
        static readonly string[] Labels = { "WIND", "RAIN", "RANGE" };
        public override int Measure(DrawCtx c)
        {
            var v = Vals(Wx); float w = c.S(11) * 2;
            for (int i = 0; i < 3; i++) w += Math.Max(TwT.W(c.G, v[i], c.FS(11.5f)), TwT.W(c.G, Labels[i], c.FS(8))) + (i < 2 ? c.S(21) : 0);
            return (int)Math.Ceiling(w);
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var w = Wx; var v = Vals(w); float cy = b.Y + b.Height / 2f, x = b.X + c.S(11);
            Color[] dots = { t.Accent, TwIcon.RainC(t), t.Warn };
            for (int i = 0; i < 3; i++)
            {
                Ui.FillCircle(g, new PointF(x + c.S(2.5f), cy - c.S(5)), c.S(2.2f), dots[i]);
                TwT.Str(g, Labels[i], c.FS(8), t.TextFaint, x + c.S(7.5f), cy - c.S(2));
                float vw = TwT.Str(g, v[i], c.FS(11.5f), HasWx(w) ? Dim(c, w) : t.TextFaint, x, cy + c.S(10.5f));
                float cw = Math.Max(vw, TwT.W(g, Labels[i], c.FS(8)) + c.S(7.5f));
                x += cw + c.S(10);
                if (i < 2) using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, x, cy - c.S(9), x, cy + c.S(9));
                x += c.S(11);
            }
        }
    }

    [Widget("forecast", "Today & Forecast", "Weather", "Today next to the next three days.", RefName = "Today & Forecast", Data = DataKind.Live)]
    public class TwForecastWidget : TwWxBase
    {
        public override int Measure(DrawCtx c)
        {
            var w = Wx; float tw = TwT.W(c.G, HasWx(w) ? TwWx.Deg(w.Temp) : Dash, TwF.Semi(c.S(16)));
            return (int)Math.Ceiling(c.S(8) + c.S(22) + c.S(4) + tw + c.S(19) + 3 * c.S(40) + c.S(6));
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var w = Wx; float cy = b.Y + b.Height / 2f, x = b.X + c.S(8);
            bool ok = HasWx(w);
            TwIcon.Draw(g, ok ? w.Code : 3, !ok || w.IsDay, new RectangleF(x, cy - c.S(11), c.S(22), c.S(22)), t);
            x += c.S(26);
            var tf = TwF.Semi(c.S(16));
            x += TwT.Str(g, ok ? TwWx.Deg(w.Temp) : Dash, tf, ok ? Dim(c, w) : t.TextFaint, x, TwT.Mid(b, tf)) + c.S(9);
            using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, x, cy - c.S(9), x, cy + c.S(9));
            x += c.S(10);
            var days = ok ? w.After(3) : new List<TwDay>();
            DateTime today = DateTime.Now.Date;
            for (int i = 0; i < 3; i++)
            {
                float cx = x + i * c.S(40) + c.S(20);
                var d = i < days.Count ? days[i] : null;
                string name = d != null ? TwT.Up(TwT.Fmt(d.D, "ddd")) : TwT.Up(TwT.Fmt(today.AddDays(i + 1), "ddd"));
                TwT.Str(g, name, c.FS(8), t.TextFaint, cx, cy - c.S(4), 0);
                if (d == null) { TwT.Str(g, "—", c.FS(10.5f), t.TextFaint, cx, cy + c.S(10), 0); continue; }
                string tt = TwWx.Deg(d.Max); var f = c.FS(10.5f); float ww = c.S(12) + c.S(2) + TwT.W(g, tt, f);
                float lx = cx - ww / 2;
                TwIcon.Draw(g, d.Code, true, new RectangleF(lx, cy + c.S(0.5f), c.S(12), c.S(12)), t);
                TwT.Str(g, tt, f, Dim(c, w), lx + c.S(14), cy + c.S(10.5f));
            }
        }
    }

    [Widget("weatherstatus", "Weather Status", "Weather", "The conditions in words, with your city.", RefName = "Weather Status", Data = DataKind.Live)]
    public class TwWeatherStatusWidget : TwWxBase
    {
        string Cond(TwWx w) { return HasWx(w) ? TwWmo.Text(w.Code) + "  ·  " + TwWx.Deg(w.Temp) : (w == null ? "Loading weather…" : "Weather offline"); }
        string City { get { return string.IsNullOrEmpty(AppConfig.Current.City) ? "Your location" : AppConfig.Current.City; } }
        public override int Measure(DrawCtx c)
        {
            var w = Wx;
            return (int)Math.Ceiling(c.S(8) + c.S(22) + c.S(7) + Math.Max(TwT.W(c.G, Cond(w), c.FS(11.5f)), TwT.W(c.G, City, c.F(10))) + c.S(12));
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var w = Wx; float cy = b.Y + b.Height / 2f, x = b.X + c.S(8);
            bool ok = HasWx(w);
            TwIcon.Draw(g, ok ? w.Code : 3, !ok || w.IsDay, new RectangleF(x, cy - c.S(11), c.S(22), c.S(22)), t);
            x += c.S(29);
            TwT.Str(g, Cond(w), c.FS(11.5f), ok ? Dim(c, w) : t.TextDim, x, cy - c.S(1.5f));
            float cw = TwT.Str(g, City, c.F(10), t.TextDim, x, cy + c.S(10.5f));
            if (w != null && w.Stale) Ui.FillCircle(g, new PointF(x + cw + c.S(5), cy + c.S(7)), c.S(2), t.Warn);
        }
    }

    [Widget("hourly", "Hourly", "Weather", "The next six hours.", RefName = "Hourly", Data = DataKind.Live)]
    public class TwHourlyWidget : TwWxBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { FormatDef() }; } }
        public override int Measure(DrawCtx c) { return (int)Math.Ceiling(c.S(6) * 2 + 6 * c.S(36)); }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var w = Wx; float cy = b.Y + b.Height / 2f, x = b.X + c.S(6);
            var hrs = HasWx(w) ? w.Next(6) : new List<TwHour>();
            var now = DateTime.Now;
            for (int i = 0; i < 6; i++)
            {
                float cx = x + i * c.S(36) + c.S(18);
                var h = i < hrs.Count ? hrs[i] : null;
                string lbl = i == 0 ? "NOW" : HourLbl(h != null ? h.T : now.AddHours(i)).ToUpperInvariant();
                TwT.Str(g, lbl, c.FS(8), i == 0 ? t.Accent : t.TextFaint, cx, cy - c.S(4), 0);
                if (h == null) { TwT.Str(g, "—", c.FS(10.5f), t.TextFaint, cx, cy + c.S(10), 0); continue; }
                string tt = TwWx.Deg(i == 0 ? w.Temp : h.Temp); var f = c.FS(10.5f); float ww = c.S(12) + c.S(2) + TwT.W(g, tt, f);
                float lx = cx - ww / 2;
                TwIcon.Draw(g, i == 0 ? w.Code : h.Code, i == 0 ? w.IsDay : h.Day, new RectangleF(lx, cy + c.S(0.5f), c.S(12), c.S(12)), t);
                TwT.Str(g, tt, f, Dim(c, w), lx + c.S(14), cy + c.S(10.5f));
            }
        }
    }

    [Widget("sun", "Sunrise & Sunset", "Weather", "Where the sun is between sunrise and sunset.", RefName = "Sunrise & Sunset", Data = DataKind.Live)]
    public class TwSunWidget : TwWxBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { FormatDef() }; } }
        string T(DateTime d) { return d == DateTime.MinValue ? "—" : TwT.Fmt(d, H24 ? "HH:mm" : "h:mm") + (H24 ? "" : TwT.Fmt(d, "tt").ToLowerInvariant()); }
        void Times(TwWx w, out DateTime rise, out DateTime set, out double frac)
        {
            rise = set = DateTime.MinValue; frac = -1;
            if (!HasWx(w) || w.Today == null) return;
            var d = w.Today; rise = d.Rise; set = d.Set; var now = w.LocalNow;
            if (rise == DateTime.MinValue || set <= rise) return;
            if (now >= rise && now <= set) frac = (now - rise).TotalMinutes / (set - rise).TotalMinutes;
            else if (now > set) { var nx = w.After(1); if (nx.Count > 0) { rise = nx[0].Rise; set = nx[0].Set; } }
        }
        public override int Measure(DrawCtx c)
        {
            DateTime r, s; double f; Times(Wx, out r, out s, out f);
            float a = Math.Max(TwT.W(c.G, T(r), c.FS(11)), TwT.W(c.G, "Sunrise", c.F(9))), z = Math.Max(TwT.W(c.G, T(s), c.FS(11)), TwT.W(c.G, "Sunset", c.F(9)));
            return (int)Math.Ceiling(c.S(11) + a + c.S(8) + c.S(52) + c.S(8) + z + c.S(11));
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var w = Wx; float cy = b.Y + b.Height / 2f, x = b.X + c.S(11);
            DateTime rise, set; double frac; Times(w, out rise, out set, out frac);
            float a = Math.Max(TwT.Str(g, T(rise), c.FS(11), frac < 0 ? t.Text : t.TextDim, x, cy - c.S(1)), TwT.Str(g, "Sunrise", c.F(9), t.TextFaint, x, cy + c.S(10)));
            x += a + c.S(8);
            var arc = new RectangleF(x, cy - c.S(9), c.S(52), c.S(30)); // ellipse; top half shown above the horizon
            float hy = arc.Y + arc.Height / 2;
            using (var p = new Pen(Ui.WithA(t.Text, t.Dark ? 60 : 70), Math.Max(1f, c.S(1.2f))) { DashStyle = DashStyle.Dash })
                g.DrawArc(p, arc, 180, 180);
            using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, arc.X - c.S(3), hy, arc.Right + c.S(3), hy);
            if (frac >= 0)
            {
                using (var p = Ui.P(t.Warn, c.S(2))) g.DrawArc(p, arc, 180, (float)(180 * frac));
                double ang = Math.PI * (1 - frac);
                var sp = new PointF(arc.X + arc.Width / 2 + (float)Math.Cos(ang) * arc.Width / 2, hy - (float)Math.Sin(ang) * arc.Height / 2);
                Ui.FillCircle(g, sp, c.S(5.5f), Ui.WithA(t.Warn, 60));
                Ui.FillCircle(g, sp, c.S(3.4f), t.Warn);
            }
            else
            {
                Ui.FillCircle(g, new PointF(arc.X, hy), c.S(2.6f), t.TextFaint);
            }
            x += arc.Width + c.S(8);
            TwT.Str(g, T(set), c.FS(11), frac >= 0 ? t.Text : t.TextDim, x, cy - c.S(1));
            TwT.Str(g, "Sunset", c.F(9), t.TextFaint, x, cy + c.S(10));
        }
    }
}
