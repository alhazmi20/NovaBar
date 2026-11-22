// Base class for TimeWeather widgets + the shared panels (month calendar, weather detail card).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace NovaBar
{
    public abstract class TwBase : Widget
    {
        protected virtual bool NeedsWeather { get { return false; } }
        public override void OnStart() { if (NeedsWeather) TwWeather.Sub(this); }
        public override void OnStop() { if (NeedsWeather) TwWeather.Unsub(this); }

        public static SettingDef FormatDef() { return SettingDef.Choice("format", "Time format", "Bar default", "Bar default", "24-hour", "12-hour"); }
        protected bool H24
        {
            get
            {
                var f = SStr("format");
                if (f == "24-hour") return true; if (f == "12-hour") return false;
                return AppConfig.Current.Clock24;
            }
        }
        protected string HM(DateTime t) { return TwT.Fmt(t, H24 ? "HH:mm" : "h:mm"); }
        protected string AP(DateTime t) { return H24 ? "" : TwT.Fmt(t, "tt"); }
        protected string HourLbl(DateTime t) { return H24 ? TwT.Fmt(t, "HH") : TwT.Fmt(t, "htt").ToLowerInvariant(); }
        protected TwWx Wx { get { return TwWeather.Now; } }
        protected static bool HasWx(TwWx w) { return w != null && !w.Empty; }

        // ---------- calendar panel ----------
        int calOff;
        protected Size CalSize(float s) { return new Size((int)(304 * s), (int)(338 * s)); }
        public override void OnPanelOpen() { calOff = 0; }
        protected void DrawCalendar(DrawCtx c, bool showTime = true)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var now = DateTime.Now;
            float x0 = b.X + c.S(16), x1 = b.Right - c.S(16), y = b.Y + c.S(16);
            if (showTime)
            {
                var tf = TwF.Semi(c.S(34)); float bl = y + TwT.Cap(tf);
                float w = TwT.Str(g, HM(now), tf, t.Text, x0, bl);
                var sf = TwF.Semi(c.S(16));
                float w2 = TwT.Str(g, ":" + TwT.Fmt(now, "ss"), sf, t.TextDim, x0 + w + c.S(2), bl);
                if (!H24) TwT.Str(g, AP(now), sf, t.TextDim, x0 + w + w2 + c.S(6), bl);
                y = bl + c.S(18);
                TwT.Str(g, TwT.Fmt(now, "dddd, d MMMM yyyy"), c.FS(12.5f), t.Text, x0, y);
                TwT.Str(g, "Week " + TwTime.IsoWeek(now) + " · Day " + now.DayOfYear, c.F(11.5f), t.TextDim, x1, y, 1);
                y += c.S(14);
                using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, x0, y, x1, y);
                y += c.S(10);
            }
            var m = new DateTime(now.Year, now.Month, 1).AddMonths(calOff);
            var hdr = new RectangleF(x0, y, x1 - x0, c.S(26));
            Ui.Text(g, TwT.Fmt(m, "MMMM yyyy"), c.FS(13.5f), t.Text, hdr);
            var nb = new RectangleF(x1 - c.S(26), y, c.S(26), c.S(26)); var pb = new RectangleF(nb.X - c.S(28), y, c.S(26), c.S(26));
            c.IconButton(pb, Icons.ChevronLeft, () => { calOff--; Invalidate(); }, false, 11);
            c.IconButton(nb, Icons.ChevronRight, () => { calOff++; Invalidate(); }, false, 11);
            if (calOff != 0)
            {
                var tb = new RectangleF(pb.X - c.S(56), y + c.S(3), c.S(52), c.S(20));
                bool h = c.IsHover(tb);
                Ui.FillRound(g, tb, tb.Height / 2, h ? t.TileHover : t.Tile);
                Ui.Text(g, "Today", c.FS(11), t.Accent, tb, 0);
                c.Hit(tb, () => { calOff = 0; Invalidate(); });
            }
            c.Wheel(new Rectangle((int)x0, (int)y, (int)(x1 - x0), (int)(b.Bottom - y)), d => { calOff -= d; Invalidate(); });
            y += c.S(32);
            float wkW = c.S(26), cw = (x1 - x0 - wkW) / 7f, ch = c.S(30);
            string[] dn = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };
            var lf = c.FS(10.5f);
            Ui.Text(g, "Wk", lf, t.TextFaint, new RectangleF(x0, y, wkW, c.S(16)), 0);
            for (int i = 0; i < 7; i++) Ui.Text(g, dn[i], lf, i >= 5 ? t.TextFaint : t.TextDim, new RectangleF(x0 + wkW + i * cw, y, cw, c.S(16)), 0);
            y += c.S(20);
            var first = m.AddDays(-TwTime.Mon0(m));
            var df = c.F(12.5f); var dfb = c.FS(12.5f);
            for (int row = 0; row < 6; row++)
            {
                var rowStart = first.AddDays(row * 7);
                var wr = new RectangleF(x0, y + row * ch, wkW, ch);
                Ui.Text(g, TwTime.IsoWeek(rowStart).ToString(), c.F(10.5f), Ui.WithA(t.Accent, 200), wr, 0);
                for (int i = 0; i < 7; i++)
                {
                    var d = rowStart.AddDays(i);
                    var cr = new RectangleF(x0 + wkW + i * cw, y + row * ch, cw, ch);
                    bool inM = d.Month == m.Month, today = d.Date == now.Date;
                    if (today)
                    {
                        float s = Math.Min(cw, ch) - c.S(4);
                        Ui.FillCircle(g, new PointF(cr.X + cr.Width / 2, cr.Y + cr.Height / 2), s / 2, t.Accent);
                        Ui.Text(g, d.Day.ToString(), dfb, Color.White, cr, 0);
                    }
                    else Ui.Text(g, d.Day.ToString(), df, !inM ? Ui.WithA(t.TextFaint, 150) : i >= 5 ? t.TextDim : t.Text, cr, 0);
                }
            }
        }
    }

    public static class TwPanels
    {
        public static Size WeatherSize(float s) { return new Size((int)(344 * s), (int)(452 * s)); }

        public static void Weather(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var w = TwWeather.Now;
            float x0 = b.X + c.S(16), x1 = b.Right - c.S(16), y = b.Y + c.S(14);
            string city = string.IsNullOrEmpty(AppConfig.Current.City) ? "Weather" : AppConfig.Current.City;
            Ui.Text(g, city, c.FS(14), t.Text, new RectangleF(x0, y, x1 - x0, c.S(20)));
            if (w == null || w.Empty)
            {
                Ui.Text(g, w == null ? "Loading…" : "Offline", c.F(11.5f), t.TextDim, new RectangleF(x0, y, x1 - x0, c.S(20)), 1);
                var mr = new RectangleF(b.X, b.Y + b.Height * 0.3f, b.Width, c.S(60));
                TwIcon.Draw(g, 3, true, new RectangleF(mr.X + mr.Width / 2 - c.S(28), mr.Y, c.S(56), c.S(56)), t);
                Ui.Text(g, w == null ? "Fetching the forecast…" : "Weather is offline", c.FS(13), t.Text, new RectangleF(b.X, mr.Bottom + c.S(10), b.Width, c.S(20)), 0);
                Ui.Text(g, w == null ? "Open-Meteo · " + city : "Check your connection — we'll retry shortly.", c.F(11.5f), t.TextDim, new RectangleF(b.X, mr.Bottom + c.S(32), b.Width, c.S(18)), 0);
                if (w != null) c.Button(new RectangleF(b.X + b.Width / 2 - c.S(50), mr.Bottom + c.S(62), c.S(100), c.S(30)), "Retry", () => Hub.Refresh(TwWeather.Key), false, Icons.Refresh);
                return;
            }
            var ago = new RectangleF(x0, y, x1 - x0, c.S(20));
            Ui.Text(g, w.Ago, c.F(11), w.Stale ? t.Warn : t.TextFaint, ago, 1);
            c.Hit(new RectangleF(x1 - c.S(120), y, c.S(120), c.S(20)), () => Hub.Refresh(TwWeather.Key));
            y += c.S(30);
            // hero
            var today = w.Today;
            TwIcon.Draw(g, w.Code, w.IsDay, new RectangleF(x0 - c.S(2), y, c.S(62), c.S(62)), t);
            var tf = TwF.Light(c.S(50)); float bl = y + c.S(31) + TwT.Cap(tf) / 2;
            float tw = TwT.Str(g, TwWx.Deg(w.Temp), tf, t.Text, x0 + c.S(70), bl);
            float rx = x0 + c.S(70) + tw + c.S(14);
            TwT.Str(g, TwWmo.Text(w.Code), c.FS(14), t.Text, rx, y + c.S(18));
            TwT.Str(g, "Feels like " + TwWx.Deg(w.Feels), c.F(11.5f), t.TextDim, rx, y + c.S(36));
            if (today != null) TwT.Str(g, "H " + TwWx.Deg(today.Max) + "   L " + TwWx.Deg(today.Min), c.F(11.5f), t.TextDim, rx, y + c.S(53));
            y += c.S(74);
            // stat chips
            string[] lab = { "Humidity", "Wind", "Rain", "Sun" };
            string sunTxt = "—";
            if (today != null && today.Rise != DateTime.MinValue)
            {
                var ln = w.LocalNow;
                sunTxt = ln < today.Rise ? "↑ " + TwT.Fmt(today.Rise, "HH:mm") : ln < today.Set ? "↓ " + TwT.Fmt(today.Set, "HH:mm") : "↑ " + (w.Days.Count > 1 && w.After(1).Count > 0 ? TwT.Fmt(w.After(1)[0].Rise, "HH:mm") : "—");
            }
            string[] val = { Math.Round(w.Hum) + "%", Math.Round(w.Wind) + " " + w.WindUnit, (today != null ? Math.Round(today.Pop) : 0) + "%", sunTxt };
            float gap = c.S(6), chW = (x1 - x0 - gap * 3) / 4f, chH = c.S(44);
            for (int i = 0; i < 4; i++)
            {
                var r = new RectangleF(x0 + i * (chW + gap), y, chW, chH);
                Ui.FillRound(g, r, c.S(10), TwDraw.Card(t));
                TwT.Str(g, lab[i], c.F(10), t.TextDim, r.X + c.S(9), r.Y + c.S(16));
                TwT.Str(g, val[i], c.FS(12.5f), t.Text, r.X + c.S(9), r.Y + c.S(34));
            }
            y += chH + c.S(14);
            // hourly strip
            var hrs = w.Next(8);
            if (hrs.Count > 0)
            {
                float cw = (x1 - x0) / hrs.Count;
                for (int i = 0; i < hrs.Count; i++)
                {
                    var h = hrs[i]; float cx = x0 + cw * i + cw / 2;
                    string lbl = i == 0 ? "Now" : (AppConfig.Current.Clock24 ? TwT.Fmt(h.T, "HH") : TwT.Fmt(h.T, "htt").ToLowerInvariant());
                    TwT.Str(g, lbl, c.F(10.5f), i == 0 ? t.Text : t.TextDim, cx, y + c.S(10), 0);
                    TwIcon.Draw(g, h.Code, h.Day, new RectangleF(cx - c.S(11), y + c.S(15), c.S(22), c.S(22)), t);
                    TwT.Str(g, TwWx.Deg(i == 0 ? w.Temp : h.Temp), c.FS(12), t.Text, cx + c.S(2), y + c.S(51), 0);
                    if (h.Pop >= 20) TwT.Str(g, Math.Round(h.Pop) + "%", c.F(9), TwIcon.RainC(t), cx, y + c.S(64), 0);
                }
                y += c.S(72);
            }
            using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, x0, y, x1, y);
            y += c.S(6);
            // 7-day list
            var days = w.Days.Take(7).ToList();
            if (days.Count == 0) return;
            double lo = days.Min(d => d.Min), hi = days.Max(d => d.Max); if (hi - lo < 1) hi = lo + 1;
            float rh = c.S(27.5f);
            var dn = w.LocalNow.Date;
            for (int i = 0; i < days.Count; i++)
            {
                var d = days[i]; var r = new RectangleF(x0, y + i * rh, x1 - x0, rh);
                float mid = TwT.Mid(r, c.F(12.5f));
                TwT.Str(g, d.D == dn ? "Today" : TwT.Fmt(d.D, "ddd"), d.D == dn ? c.FS(12.5f) : c.F(12.5f), t.Text, r.X, mid);
                TwIcon.Draw(g, d.Code, true, new RectangleF(r.X + c.S(50), r.Y + c.S(3.5f), c.S(20), c.S(20)), t);
                if (d.Pop >= 20) TwT.Str(g, Math.Round(d.Pop) + "%", c.F(10.5f), TwIcon.RainC(t), r.X + c.S(76), mid);
                float barX = r.X + c.S(150), barW = r.Right - c.S(36) - barX;
                TwT.Str(g, TwWx.Deg(d.Min), c.F(12.5f), t.TextDim, barX - c.S(8), mid, 1);
                TwT.Str(g, TwWx.Deg(d.Max), c.FS(12.5f), t.Text, r.Right, mid, 1);
                var track = new RectangleF(barX, r.Y + r.Height / 2 - c.S(2.5f), barW, c.S(5));
                Ui.FillRound(g, track, track.Height / 2, t.Track);
                float a = (float)((d.Min - lo) / (hi - lo)), z = (float)((d.Max - lo) / (hi - lo));
                var seg = new RectangleF(track.X + track.Width * a, track.Y, Math.Max(track.Height, track.Width * (z - a)), track.Height);
                using (var br = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y, track.Width + 2, track.Height), TwIcon.RainC(t), t.Warn, 0f))
                using (var path = Ui.Round(seg, seg.Height / 2)) g.FillPath(br, path);
                if (d.D == dn && !double.IsNaN(w.Temp))
                {
                    float px = track.X + track.Width * (float)Math.Max(0, Math.Min(1, (w.Temp - lo) / (hi - lo)));
                    Ui.FillCircle(g, new PointF(px, track.Y + track.Height / 2), c.S(4.5f), t.Dark ? t.PanelBg.A > 200 ? t.PanelBg : Color.FromArgb(255, 20, 20, 26) : Color.White);
                    Ui.FillCircle(g, new PointF(px, track.Y + track.Height / 2), c.S(3f), t.Text);
                }
            }
        }
    }
}
