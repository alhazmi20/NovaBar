// Clocks: clock, flipclock, worldclocks, sevendays, worldtime, today, date, daynumber, analog, segmentclock, clockweather.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace NovaBar
{
    [Widget("clock", "Clock", "Clocks", "Large, readable time with seconds and the date.", RefName = "Clock", Data = DataKind.Live)]
    public class TwClockWidget : TwBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { FormatDef(), SettingDef.Toggle("seconds", "Show seconds", true), SettingDef.Toggle("date", "Show date", true) }; } }
        public override int RefreshMs { get { return 1000; } }
        Font TF(DrawCtx c) { return TwF.Semi(c.S(19)); }
        Font SF(DrawCtx c) { return TwF.Semi(c.S(12)); }
        public override int Measure(DrawCtx c)
        {
            var g = c.G; var now = DateTime.Now; float w = c.S(11) * 2;
            w += TwT.W(g, H24 ? "00:00" : (now.Hour % 12 >= 10 || now.Hour % 12 == 0 ? "00:00" : "0:00"), TF(c));
            if (SBool("seconds")) w += c.S(2) + TwT.W(g, ":00", SF(c));
            if (!H24) w += c.S(4) + TwT.W(g, AP(now), c.FS(10.5f));
            if (SBool("date")) w += c.S(21) + Math.Max(TwT.W(g, TwT.Fmt(now, "dddd"), c.F(10)), TwT.W(g, TwT.Fmt(now, "d MMM"), c.FS(11.5f)));
            return (int)Math.Ceiling(w);
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var now = DateTime.Now;
            float x = b.X + c.S(11), cy = b.Y + b.Height / 2f;
            var tf = TF(c); float bl = TwT.Mid(b, tf);
            x += TwT.Str(g, HM(now), tf, t.Text, x, bl);
            if (SBool("seconds")) x += c.S(2) + TwT.Str(g, ":" + TwT.Fmt(now, "ss"), SF(c), t.TextDim, x + c.S(2), bl);
            if (!H24) x += c.S(4) + TwT.Str(g, AP(now), c.FS(10.5f), t.TextDim, x + c.S(4), bl);
            if (SBool("date"))
            {
                x += c.S(10);
                using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, x, cy - c.S(9), x, cy + c.S(9));
                x += c.S(10);
                TwT.Str(g, TwT.Fmt(now, "dddd"), c.F(10), t.TextDim, x, cy - c.S(2.5f));
                TwT.Str(g, TwT.Fmt(now, "d MMM"), c.FS(11.5f), t.Text, x, cy + c.S(9.5f));
            }
        }
        public override Size PanelSize(float s) { return CalSize(s); }
        public override void DrawPanel(DrawCtx c) { DrawCalendar(c); }
    }

    [Widget("flipclock", "Flip Clock", "Clocks", "Split-flap digits that turn over every minute.", RefName = "Flip Clock", Data = DataKind.Live)]
    public class TwFlipClockWidget : TwBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { FormatDef() }; } }
        string shown, prev; DateTime flipAt = DateTime.MinValue;
        const double FlipMs = 400;
        bool Animating { get { return (DateTime.Now - flipAt).TotalMilliseconds < FlipMs + 30; } }
        // the host schedules the next tick from the current RefreshMs, so switch to 33 ms just before the minute turns
        bool NearTurn { get { var n = DateTime.Now; return 60000 - (n.Second * 1000 + n.Millisecond) < 1050; } }
        public override int RefreshMs { get { return Animating || NearTurn ? 33 : 1000; } }
        void Sync()
        {
            var now = DateTime.Now; var s = H24 ? TwT.Fmt(now, "HHmm") : TwT.Fmt(now, "hhmm");
            if (!H24 && s[0] == '0') s = " " + s.Substring(1);
            if (shown == null) { shown = s; prev = s; return; }
            if (s != shown) { prev = shown; shown = s; flipAt = now; }
        }
        double Prog { get { var e = (DateTime.Now - flipAt).TotalMilliseconds; if (e >= FlipMs) return 1; var p = Math.Max(0, e / FlipMs); return p * p * (3 - 2 * p); } }
        public override void OnTick() { Sync(); }

        void Cards(DrawCtx c, float x, float y, float cw, float ch, float gap, float colon, Font f, bool draw)
        {
            var t = c.T; var g = c.G;
            var card = t.Dark ? Ui.Mix(Color.FromArgb(255, 38, 38, 44), t.Accent, 0.04) : Color.FromArgb(255, 30, 30, 36);
            if (t.IsGlass && t.Dark) card = Color.FromArgb(200, 10, 10, 16);
            var fg = Color.FromArgb(255, 245, 245, 248); var seam = Color.FromArgb(150, 0, 0, 0);
            double p = Prog;
            for (int i = 0; i < 4; i++)
            {
                float cx = x + i * (cw + gap) + (i >= 2 ? colon - gap : 0);
                var r = new RectangleF(cx, y, cw, ch);
                string cur = shown.Substring(i, 1), old = prev != null ? prev.Substring(i, 1) : cur;
                if (cur == " " && (old == " " || p >= 1)) { Ui.FillRound(g, r, Math.Max(2f, cw * 0.14f), Ui.WithA(card, card.A / 3)); continue; }
                TwDraw.Flip(g, r, cur, old, cur == old ? 1 : p, f, card, fg, seam);
            }
            float mx = x + 2 * (cw + gap) - gap + colon / 2, dr = Math.Max(1.3f, ch * 0.055f);
            Ui.FillCircle(g, new PointF(mx, y + ch * 0.36f), dr, t.TextDim);
            Ui.FillCircle(g, new PointF(mx, y + ch * 0.64f), dr, t.TextDim);
        }
        public override int Measure(DrawCtx c)
        {
            float w = c.S(9) * 2 + 4 * c.S(17) + 2 * c.S(2) + c.S(10);
            if (!H24) w += c.S(18);
            return (int)Math.Ceiling(w);
        }
        public override void Draw(DrawCtx c)
        {
            Sync();
            var b = c.Bounds; float ch = c.S(26), cw = c.S(17);
            float x = b.X + c.S(9), y = b.Y + (b.Height - ch) / 2;
            Cards(c, x, y, cw, ch, c.S(2), c.S(10), TwF.Semi(c.S(19)), true);
            if (!H24)
            {
                float ax = x + 4 * cw + 2 * c.S(2) + c.S(10) + c.S(4);
                var f = c.FS(9.5f); var now = DateTime.Now;
                TwT.Str(c.G, TwT.Fmt(now, "tt"), f, c.T.TextDim, ax, b.Y + b.Height / 2 + TwT.Cap(f) / 2);
            }
        }
        public override Size PanelSize(float s) { return new Size((int)(320 * s), (int)(178 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            Sync();
            var b = c.Bounds; var now = DateTime.Now;
            float cw = c.S(56), ch = c.S(84), gap = c.S(6), colon = c.S(24);
            float total = 4 * cw + 2 * gap + colon;
            float x = b.X + (b.Width - total) / 2, y = b.Y + c.S(22);
            Cards(c, x, y, cw, ch, gap, colon, TwF.Semi(c.S(62)), true);
            float ty = y + ch + c.S(30);
            string l = TwT.Fmt(now, "dddd, d MMMM") + (H24 ? "" : "  ·  " + TwT.Fmt(now, "tt"));
            TwT.Str(c.G, l, c.FS(13), c.T.Text, b.X + b.Width / 2, ty, 0);
            TwT.Str(c.G, "Week " + TwTime.IsoWeek(now) + "  ·  :" + TwT.Fmt(now, "ss"), c.F(11), c.T.TextDim, b.X + b.Width / 2, ty + c.S(18), 0);
        }
    }

    [Widget("worldclocks", "World Clocks", "Clocks", "Three small analog faces for the cities you work with.", RefName = "World Clocks", Data = DataKind.Live)]
    public class TwWorldClocksWidget : TwBase
    {
        public override SettingDef[] SettingDefs
        {
            get
            {
                return new[] {
                    SettingDef.Text("city1", "City 1", "New York"), SettingDef.Text("tz1", "Time zone 1 (Windows id, e.g. Eastern Standard Time)", "Eastern Standard Time"),
                    SettingDef.Text("city2", "City 2", "London"), SettingDef.Text("tz2", "Time zone 2", "GMT Standard Time"),
                    SettingDef.Text("city3", "City 3", "Tokyo"), SettingDef.Text("tz3", "Time zone 3", "Tokyo Standard Time"), FormatDef() };
            }
        }
        public override int RefreshMs { get { return 1000; } }
        struct City { public string Name; public TimeZoneInfo Z; public DateTime T; }
        List<City> Cities()
        {
            var l = new List<City>();
            for (int i = 1; i <= 3; i++)
            {
                var n = SStr("city" + i); var z = TwTime.Zone(SStr("tz" + i));
                if (string.IsNullOrWhiteSpace(n)) n = z.Id;
                l.Add(new City { Name = n, Z = z, T = TwTime.In(z) });
            }
            return l;
        }
        public static void Face(Graphics g, PointF c, float r, DateTime t, Theme th, bool seconds)
        {
            bool day = t.Hour >= 6 && t.Hour < 18;
            var face = day ? Color.FromArgb(255, 246, 246, 248) : Color.FromArgb(255, 30, 30, 36);
            var hand = day ? Color.FromArgb(255, 24, 24, 28) : Color.FromArgb(255, 242, 242, 246);
            var rim = day ? (th.Dark ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(th.IsGlass ? 70 : 52, 0, 0, 0)) : (th.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(0, 0, 0, 0));
            TwDraw.Analog(g, c, r, t, face, rim, Ui.WithA(hand, 150), hand, th.Accent, r > 16 ? 12 : 4, seconds);
        }
        float BlockW(DrawCtx c, City ct)
        {
            return c.S(24) + c.S(6) + Math.Max(TwT.W(c.G, TwTime.Abbrev(ct.Name), c.FS(10.5f)), TwT.W(c.G, HM(ct.T) + (H24 ? "" : AP(ct.T).ToLowerInvariant()), c.F(10)));
        }
        public override int Measure(DrawCtx c)
        {
            float w = c.S(8) * 2; var l = Cities();
            for (int i = 0; i < l.Count; i++) w += BlockW(c, l[i]) + (i < l.Count - 1 ? c.S(12) : 0);
            return (int)Math.Ceiling(w);
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; float x = b.X + c.S(8), cy = b.Y + b.Height / 2f;
            foreach (var ct in Cities())
            {
                Face(g, new PointF(x + c.S(12), cy), c.S(12), ct.T, t, false);
                float tx = x + c.S(30);
                TwT.Str(g, TwTime.Abbrev(ct.Name), c.FS(10.5f), t.Text, tx, cy - c.S(2));
                TwT.Str(g, HM(ct.T) + (H24 ? "" : AP(ct.T).ToLowerInvariant()), c.F(10), t.TextDim, tx, cy + c.S(10));
                x += BlockW(c, ct) + c.S(12);
            }
        }
        public override Size PanelSize(float s) { return new Size((int)(320 * s), (int)(264 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds;
            float y = c.PanelHeader("World Clocks", "You: " + TwT.Fmt(DateTime.Now, H24 ? "HH:mm" : "h:mm tt"));
            float x0 = b.X + c.S(16), x1 = b.Right - c.S(16), rh = c.S(66);
            var l = Cities();
            for (int i = 0; i < l.Count; i++)
            {
                var ct = l[i]; var r = new RectangleF(x0, y + i * rh, x1 - x0, rh - c.S(8));
                Ui.FillRound(g, r, c.S(12), TwDraw.Card(t));
                float cy = r.Y + r.Height / 2;
                Face(g, new PointF(r.X + c.S(30), cy), c.S(21), ct.T, t, true);
                float tx = r.X + c.S(62);
                TwT.Str(g, ct.Name, c.FS(13.5f), t.Text, tx, cy - c.S(3));
                TwT.Str(g, TwTime.DayRel(ct.T) + "  ·  " + TwTime.Offset(ct.Z), c.F(11), t.TextDim, tx, cy + c.S(13));
                var tf = TwF.Light(c.S(26)); float rx = r.Right - c.S(12);
                if (!H24) rx -= c.S(4) + TwT.Str(g, AP(ct.T), c.FS(11), t.TextDim, rx, cy + TwT.Cap(tf) / 2, 1);
                TwT.Str(g, HM(ct.T), tf, t.Text, rx, cy + TwT.Cap(tf) / 2, 1);
            }
        }
    }

    [Widget("sevendays", "7 Days", "Clocks", "This week as a strip of days, with today's temperature.", RefName = "7 Days", Data = DataKind.Live)]
    public class TwSevenDaysWidget : TwBase
    {
        protected override bool NeedsWeather { get { return true; } }
        public override int RefreshMs { get { return 30000; } }
        public override int Measure(DrawCtx c)
        {
            var w = Wx; float tw = HasWx(w) ? TwT.W(c.G, TwWx.Deg(w.Temp), c.FS(13)) : TwT.W(c.G, "—°", c.FS(13));
            return (int)Math.Ceiling(c.S(7) + 7 * c.S(19) + c.S(19) + c.S(20) + c.S(3) + tw + c.S(10));
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var now = DateTime.Now; float cy = b.Y + b.Height / 2f;
            var mon = now.Date.AddDays(-TwTime.Mon0(now));
            float x = b.X + c.S(7), cw = c.S(19);
            string[] ini = { "M", "T", "W", "T", "F", "S", "S" };
            for (int i = 0; i < 7; i++)
            {
                var d = mon.AddDays(i); bool today = d == now.Date; float cx = x + i * cw + cw / 2;
                if (today) Ui.FillRound(g, new RectangleF(cx - c.S(9), cy - c.S(14), c.S(18), c.S(28)), c.S(8), t.Accent);
                var fg = today ? Color.White : d < now.Date ? t.TextFaint : t.Text;
                TwT.Str(g, ini[i], c.FS(8.5f), today ? Color.FromArgb(220, 255, 255, 255) : t.TextFaint, cx, cy - c.S(3), 0);
                TwT.Str(g, d.Day.ToString(), today ? c.FS(11) : c.F(11), fg, cx, cy + c.S(10), 0);
            }
            x += 7 * cw + c.S(9);
            using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, x, cy - c.S(9), x, cy + c.S(9));
            x += c.S(8);
            var w = Wx;
            if (HasWx(w)) TwIcon.Draw(g, w.Code, w.IsDay, new RectangleF(x, cy - c.S(10), c.S(20), c.S(20)), t);
            else TwIcon.Draw(g, 3, true, new RectangleF(x, cy - c.S(10), c.S(20), c.S(20)), t);
            var f = c.FS(13);
            TwT.Str(g, HasWx(w) ? TwWx.Deg(w.Temp) : "—°", f, HasWx(w) ? t.Text : t.TextFaint, x + c.S(23), TwT.Mid(b, f));
        }
        public override Size PanelSize(float s) { return TwPanels.WeatherSize(s); }
        public override void DrawPanel(DrawCtx c) { TwPanels.Weather(c); }
    }

    [Widget("worldtime", "World Time", "Clocks", "One more city in big digits, with its offset.", RefName = "World Time", Data = DataKind.Live)]
    public class TwWorldTimeWidget : TwBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Text("city", "City", "Tokyo"), SettingDef.Text("tz", "Time zone (Windows id, e.g. Tokyo Standard Time)", "Tokyo Standard Time"), FormatDef() }; } }
        public override int RefreshMs { get { return 1000; } }
        TimeZoneInfo Z { get { return TwTime.Zone(SStr("tz")); } }
        string City { get { var s = SStr("city"); return string.IsNullOrWhiteSpace(s) ? Z.Id : s; } }
        public override int Measure(DrawCtx c)
        {
            var z = Z; var tt = TwTime.In(z);
            float lw = Math.Max(TwT.W(c.G, City, c.FS(11)), TwT.W(c.G, TwTime.Offset(z) + " · " + TwT.Fmt(tt, "ddd"), c.F(9.5f)));
            float tw = TwT.W(c.G, HM(tt), TwF.Semi(c.S(19))) + (H24 ? 0 : c.S(3) + TwT.W(c.G, AP(tt), c.FS(10)));
            return (int)Math.Ceiling(c.S(11) + lw + c.S(12) + tw + c.S(11));
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var z = Z; var tt = TwTime.In(z); float cy = b.Y + b.Height / 2f;
            float x = b.X + c.S(11);
            float lw = Math.Max(TwT.Str(g, City, c.FS(11), t.Text, x, cy - c.S(2)), TwT.Str(g, TwTime.Offset(z) + " · " + TwT.Fmt(tt, "ddd"), c.F(9.5f), t.Accent, x, cy + c.S(10)));
            x += lw + c.S(12);
            var tf = TwF.Semi(c.S(19)); float bl = TwT.Mid(b, tf);
            x += TwT.Str(g, HM(tt), tf, t.Text, x, bl);
            if (!H24) TwT.Str(g, AP(tt), c.FS(10), t.TextDim, x + c.S(3), bl);
        }
        public override Size PanelSize(float s) { return new Size((int)(320 * s), (int)(236 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var z = Z; var tt = TwTime.In(z); var now = DateTime.Now;
            float x0 = b.X + c.S(16), x1 = b.Right - c.S(16);
            float y = c.PanelHeader(City, TwTime.Utc(z));
            var tf = TwF.Light(c.S(48)); float bl = y + c.S(4) + TwT.Cap(tf);
            float w = TwT.Str(g, HM(tt), tf, t.Text, x0, bl);
            float w2 = TwT.Str(g, ":" + TwT.Fmt(tt, "ss"), TwF.Reg(c.S(18)), t.TextDim, x0 + w + c.S(3), bl);
            if (!H24) TwT.Str(g, AP(tt), c.FS(14), t.TextDim, x0 + w + w2 + c.S(8), bl);
            var d = TwTime.Diff(z);
            string rel = Math.Abs(d.TotalMinutes) < 1 ? "Same time as you" : TwTime.Offset(z).TrimStart('+', '−') + (d.Ticks > 0 ? " ahead of you" : " behind you");
            TwT.Str(g, TwT.Fmt(tt, "dddd, d MMMM") + "  ·  " + TwTime.DayRel(tt), c.FS(12.5f), t.Text, x0, bl + c.S(24));
            TwT.Str(g, rel, c.F(11.5f), t.TextDim, x0, bl + c.S(42));
            // hour ruler: you vs them
            float ry = bl + c.S(60); int n = 12; float cw = (x1 - x0 - c.S(34)) / n, lx = x0 + c.S(34);
            TwT.Str(g, "You", c.F(10), t.TextDim, x0, ry + c.S(14));
            TwT.Str(g, TwTime.Abbrev(City), c.F(10), t.TextDim, x0, ry + c.S(38));
            for (int i = 0; i < n; i++)
            {
                var lh = now.AddHours(i - 2); var th = tt.AddHours(i - 2);
                var cr = new RectangleF(lx + i * cw + c.S(1), ry + c.S(24), cw - c.S(2), c.S(22));
                bool work = th.Hour >= 9 && th.Hour < 18, night = th.Hour < 7 || th.Hour >= 22;
                Ui.FillRound(g, cr, c.S(5), work ? Ui.WithA(t.Good, t.Dark ? 60 : 50) : night ? Ui.WithA(t.Text, 8) : TwDraw.Card(t));
                if (i == 2) Ui.StrokeRound(g, Ui.Inset(cr, -c.S(1), -c.S(1)), c.S(6), t.Accent, c.S(1.5f));
                TwT.Str(g, H24 ? lh.Hour.ToString() : (lh.Hour % 12 == 0 ? 12 : lh.Hour % 12).ToString(), c.F(10), i == 2 ? t.Text : t.TextDim, cr.X + cr.Width / 2, ry + c.S(14), 0);
                TwT.Str(g, H24 ? th.Hour.ToString() : (th.Hour % 12 == 0 ? 12 : th.Hour % 12).ToString(), i == 2 ? c.FS(10.5f) : c.F(10.5f), night ? t.TextFaint : t.Text, cr.X + cr.Width / 2, TwT.Mid(cr, c.F(10.5f)), 0);
            }
        }
    }

    [Widget("today", "Today", "Clocks", "Weekday, date, time and weather in a single tile.", RefName = "Today", Data = DataKind.Live)]
    public class TwTodayWidget : TwBase
    {
        protected override bool NeedsWeather { get { return true; } }
        public override SettingDef[] SettingDefs { get { return new[] { FormatDef() }; } }
        public override int RefreshMs { get { return 1000; } }
        public override int Measure(DrawCtx c)
        {
            var g = c.G; var now = DateTime.Now; var w = Wx;
            float a = Math.Max(TwT.W(g, TwT.Fmt(now, "dddd"), c.FS(11)), TwT.W(g, TwT.Fmt(now, "d MMMM"), c.F(10)));
            float tm = TwT.W(g, HM(now), TwF.Semi(c.S(18))) + (H24 ? 0 : c.S(3) + TwT.W(g, AP(now), c.FS(9.5f)));
            float wt = c.S(20) + c.S(3) + TwT.W(g, HasWx(w) ? TwWx.Deg(w.Temp) : "—°", c.FS(13));
            return (int)Math.Ceiling(c.S(11) + a + c.S(21) + tm + c.S(21) + wt + c.S(10));
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var now = DateTime.Now; var w = Wx; float cy = b.Y + b.Height / 2f;
            float x = b.X + c.S(11);
            float a = Math.Max(TwT.Str(g, TwT.Fmt(now, "dddd"), c.FS(11), t.Accent, x, cy - c.S(2)), TwT.Str(g, TwT.Fmt(now, "d MMMM"), c.F(10), t.TextDim, x, cy + c.S(10)));
            x += a + c.S(10);
            using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, x, cy - c.S(9), x, cy + c.S(9));
            x += c.S(11);
            var tf = TwF.Semi(c.S(18)); float bl = TwT.Mid(b, tf);
            x += TwT.Str(g, HM(now), tf, t.Text, x, bl);
            if (!H24) x += c.S(3) + TwT.Str(g, AP(now), c.FS(9.5f), t.TextDim, x + c.S(3), bl);
            x += c.S(10);
            using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, x, cy - c.S(9), x, cy + c.S(9));
            x += c.S(9);
            TwIcon.Draw(g, HasWx(w) ? w.Code : 3, !HasWx(w) || w.IsDay, new RectangleF(x, cy - c.S(10), c.S(20), c.S(20)), t);
            var f = c.FS(13);
            TwT.Str(g, HasWx(w) ? TwWx.Deg(w.Temp) : "—°", f, HasWx(w) ? t.Text : t.TextFaint, x + c.S(23), TwT.Mid(b, f));
        }
        public override Size PanelSize(float s) { return CalSize(s); }
        public override void DrawPanel(DrawCtx c) { DrawCalendar(c); }
    }

    [Widget("date", "Date", "Clocks", "The full date, set large.", RefName = "Date", Data = DataKind.Live)]
    public class TwDateWidget : TwBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Toggle("year", "Show year", true) }; } }
        public override int RefreshMs { get { return 30000; } }
        string A(DateTime d) { return TwT.Fmt(d, "dddd"); }
        string B(DateTime d) { return TwT.Fmt(d, "d MMMM"); }
        public override int Measure(DrawCtx c)
        {
            var g = c.G; var n = DateTime.Now; var f = TwF.Semi(c.S(15)); var f2 = TwF.Reg(c.S(15));
            float w = c.S(12) * 2 + TwT.W(g, A(n), f) + c.S(7) + TwT.W(g, B(n), f2);
            if (SBool("year")) w += c.S(6) + TwT.W(g, n.Year.ToString(), f2);
            return (int)Math.Ceiling(w);
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var n = DateTime.Now; var f = TwF.Semi(c.S(15)); var f2 = TwF.Reg(c.S(15));
            float bl = TwT.Mid(b, f), x = b.X + c.S(12);
            x += TwT.Str(g, A(n), f, t.Accent, x, bl) + c.S(7);
            x += TwT.Str(g, B(n), f2, t.Text, x, bl) + c.S(6);
            if (SBool("year")) TwT.Str(g, n.Year.ToString(), f2, t.TextFaint, x, bl);
        }
        public override Size PanelSize(float s) { return CalSize(s); }
        public override void DrawPanel(DrawCtx c) { DrawCalendar(c); }
    }

    [Widget("daynumber", "Day Number", "Clocks", "Today's date as a calendar page.", RefName = "Day Number", Data = DataKind.Live)]
    public class TwDayNumberWidget : TwBase
    {
        public override int RefreshMs { get { return 30000; } }
        public override int Measure(DrawCtx c) { return c.Si(40); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; float h = Math.Min(b.Height - c.S(6), c.S(28)), w = h * 0.93f;
            TwDraw.CalPage(c.G, new RectangleF(b.X + (b.Width - w) / 2, b.Y + (b.Height - h) / 2, w, h), DateTime.Now, c.T, c.Scale);
        }
        public override Size PanelSize(float s) { return CalSize(s); }
        public override void DrawPanel(DrawCtx c) { DrawCalendar(c); }
    }

    [Widget("analog", "Analog Minimal", "Clocks", "Hands and hour ticks on a dark face, nothing else.", RefName = "Analog Minimal", Data = DataKind.Live)]
    public class TwAnalogWidget : TwBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Toggle("seconds", "Second hand", true) }; } }
        public override int RefreshMs { get { return 1000; } }
        public override int Measure(DrawCtx c) { return c.Bounds.Height > 0 ? Math.Max(c.Si(38), c.Bounds.Height + c.Si(4)) : c.Si(38); }
        static void Face(DrawCtx c, PointF ctr, float r, bool sec, bool big)
        {
            var t = c.T;
            var face = t.Dark ? (t.IsGlass ? Color.FromArgb(215, 8, 8, 12) : Color.FromArgb(255, 10, 10, 13)) : Color.FromArgb(255, 22, 22, 27);
            var rim = t.Dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(0, 0, 0, 0);
            var hand = Color.FromArgb(255, 244, 244, 247);
            TwDraw.Analog(c.G, ctr, r, DateTime.Now, face, rim, Color.FromArgb(big ? 210 : 190, 230, 230, 236), hand, t.Accent, big ? 60 : 12, sec);
        }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; float r = b.Height / 2f - c.S(3);
            Face(c, new PointF(b.X + b.Width / 2f, b.Y + b.Height / 2f), r, SBool("seconds"), false);
        }
        public override Size PanelSize(float s) { return new Size((int)(260 * s), (int)(292 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; float r = c.S(108);
            Face(c, new PointF(b.X + b.Width / 2f, b.Y + c.S(20) + r), r, true, true);
            TwT.Str(c.G, TwT.Fmt(DateTime.Now, "dddd, d MMMM"), c.FS(12.5f), c.T.TextDim, b.X + b.Width / 2f, b.Bottom - c.S(20), 0);
        }
    }

    [Widget("segmentclock", "Segment Clock", "Clocks", "The time as a seven-segment display, under the weekday and date.", RefName = "Segment Clock", Data = DataKind.Live)]
    public class TwSegmentClockWidget : TwBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { FormatDef(), SettingDef.Toggle("seconds", "Show seconds", true) }; } }
        public override int RefreshMs { get { return 1000; } }
        string Time(DateTime n) { var s = TwT.Fmt(n, H24 ? "HH:mm" : "hh:mm"); if (!H24 && s[0] == '0') s = " " + s.Substring(1); return s; }
        string Label(DateTime n) { return TwT.Up(TwT.Fmt(n, "ddd  d MMM")); }
        public override int Measure(DrawCtx c)
        {
            var n = DateTime.Now;
            float dw = TwDraw.SegStr(c.G, "88:88", 0, 0, c.S(14), Color.Empty, Color.Empty, true);
            if (SBool("seconds")) dw += c.S(4) + TwDraw.SegStr(c.G, "88", 0, 0, c.S(8), Color.Empty, Color.Empty, true);
            float lw = TwT.W(c.G, Label(n) + (H24 ? "" : "  PM"), c.FS(8.5f));
            return (int)Math.Ceiling(Math.Max(dw, lw) + c.S(24));
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var n = DateTime.Now;
            float dh = c.S(14), top = b.Y + (b.Height - (c.S(6) + c.S(4) + dh)) / 2;
            float x = b.X + c.S(12), right = b.Right - c.S(12);
            var lf = c.FS(8.5f);
            TwT.Str(g, Label(n), lf, t.TextDim, x, top + c.S(6));
            if (!H24) TwT.Str(g, TwT.Fmt(n, "tt"), lf, t.Accent, right, top + c.S(6), 1);
            var on = t.Accent; var off = Ui.WithA(t.Text, t.Dark ? 9 : 10);
            float y = top + c.S(10);
            float w = TwDraw.SegStr(g, Time(n), x, y, dh, on, off);
            if (SBool("seconds")) TwDraw.SegStr(g, TwT.Fmt(n, "ss"), x + w + c.S(4), y + dh - c.S(8), c.S(8), t.TextDim, off);
        }
        public override Size PanelSize(float s) { return new Size((int)(320 * s), (int)(150 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var n = DateTime.Now;
            float dh = c.S(54), sh = c.S(26);
            float w = TwDraw.SegStr(g, "88:88", 0, 0, dh, Color.Empty, Color.Empty, true) + c.S(10) + TwDraw.SegStr(g, "88", 0, 0, sh, Color.Empty, Color.Empty, true);
            float x = b.X + (b.Width - w) / 2, y = b.Y + c.S(46);
            var lf = c.FS(11.5f);
            TwT.Str(g, TwT.Up(TwT.Fmt(n, "dddd  ·  d MMMM yyyy")), lf, t.TextDim, x, b.Y + c.S(30));
            if (!H24) TwT.Str(g, TwT.Fmt(n, "tt"), lf, t.Accent, x + w, b.Y + c.S(30), 1);
            var on = t.Accent; var off = Ui.WithA(t.Text, t.Dark ? 8 : 9);
            float dw = TwDraw.SegStr(g, Time(n), x, y, dh, on, off);
            TwDraw.SegStr(g, TwT.Fmt(n, "ss"), x + dw + c.S(10), y + dh - sh, sh, t.TextDim, off);
            TwT.Str(g, "WEEK " + TwTime.IsoWeek(n) + "   ·   DAY " + n.DayOfYear, c.F(10), t.TextFaint, b.X + b.Width / 2, y + dh + c.S(24), 0);
        }
    }

    [Widget("clockweather", "Clock & Weather", "Clocks", "Time, date and temperature as three colour blocks in one tile.", RefName = "Clock & Weather", Data = DataKind.Live)]
    public class TwClockWeatherWidget : TwBase
    {
        protected override bool NeedsWeather { get { return true; } }
        public override SettingDef[] SettingDefs { get { return new[] { FormatDef() }; } }
        public override int RefreshMs { get { return 1000; } }
        float[] Widths(DrawCtx c)
        {
            var g = c.G; var n = DateTime.Now; var w = Wx;
            float a = TwT.W(g, HM(n), TwF.Semi(c.S(14))) + (H24 ? 0 : c.S(2) + TwT.W(g, AP(n), c.FS(8))) + c.S(16);
            float b = Math.Max(TwT.W(g, TwT.Up(TwT.Fmt(n, "ddd")), c.FS(8)), TwT.W(g, TwT.Up(TwT.Fmt(n, "d MMM")), c.FS(11))) + c.S(14);
            float d = TwT.W(g, HasWx(w) ? TwWx.Deg(w.Temp) : "—°", TwF.Semi(c.S(14))) + c.S(16);
            return new[] { a, b, d };
        }
        public override int Measure(DrawCtx c) { var w = Widths(c); return (int)Math.Ceiling(c.S(4) * 2 + w[0] + w[1] + w[2] + c.S(3) * 2); }
        static Color Temp(Theme t, TwWx w)
        {
            if (!HasWx(w)) return Ui.Mix(t.TextDim, Color.Black, 0.2);
            double cel = w.Imperial ? (w.Temp - 32) / 1.8 : w.Temp;
            var hot = Color.FromArgb(255, 240, 108, 78); var mild = Color.FromArgb(255, 236, 160, 48); var cold = Color.FromArgb(255, 58, 160, 230);
            return cel >= 30 ? hot : cel >= 18 ? mild : cel >= 8 ? Color.FromArgb(255, 46, 178, 140) : cold;
        }
        public override void Draw(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var n = DateTime.Now; var w = Wx;
            var ws = Widths(c); float x = b.X + c.S(4), h = b.Height - c.S(8), y = b.Y + c.S(4), rad = c.S(8), gap = c.S(3);
            Color[] cols = { Ui.Mix(t.Accent, Color.Black, 0.12), Ui.Mix(Color.FromArgb(255, 139, 92, 246), Color.Black, 0.08), Ui.Mix(Temp(t, w), Color.Black, 0.12) };
            var white = Color.White;
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(x, y, ws[i], h);
                using (var br = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), Ui.Mix(cols[i], Color.White, 0.12), cols[i], 90f))
                using (var p = Ui.Round(r, rad)) g.FillPath(br, p);
                if (i == 0)
                {
                    var f = TwF.Semi(c.S(14)); float bl = TwT.Mid(r, f);
                    float tw = TwT.W(g, HM(n), f) + (H24 ? 0 : c.S(2) + TwT.W(g, AP(n), c.FS(8)));
                    float tx = r.X + (r.Width - tw) / 2;
                    tx += TwT.Str(g, HM(n), f, white, tx, bl);
                    if (!H24) TwT.Str(g, AP(n), c.FS(8), Color.FromArgb(210, 255, 255, 255), tx + c.S(2), bl);
                }
                else if (i == 1)
                {
                    float cy = r.Y + r.Height / 2;
                    TwT.Str(g, TwT.Up(TwT.Fmt(n, "ddd")), c.FS(8), Color.FromArgb(200, 255, 255, 255), r.X + r.Width / 2, cy - c.S(3), 0);
                    TwT.Str(g, TwT.Up(TwT.Fmt(n, "d MMM")), c.FS(11), white, r.X + r.Width / 2, cy + c.S(8.5f), 0);
                }
                else
                {
                    var f = TwF.Semi(c.S(14));
                    TwT.Str(g, HasWx(w) ? TwWx.Deg(w.Temp) : "—°", f, white, r.X + r.Width / 2 + c.S(2), TwT.Mid(r, f), 0);
                }
                x += ws[i] + gap;
            }
        }
        public override Size PanelSize(float s) { return TwPanels.WeatherSize(s); }
        public override void DrawPanel(DrawCtx c) { TwPanels.Weather(c); }
    }
}
