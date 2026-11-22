using System;
using System.Drawing;
using System.Globalization;

namespace NovaBar
{
    public abstract class SysMonProgressBase : Widget
    {
        protected static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // tile only changes every few minutes: tick once a minute, repaint only when the shown % changes
        public override int RefreshMs { get { return 60000; } }
        protected static string P1(double f) { return (f * 100).ToString("0.0", Inv) + "%"; }
    }

    [Widget("dayprogress", "Day Progress", "System", "How much of today is behind you.", RefName = "Day Progress", Data = DataKind.Live)]
    public class SysMonDayWidget : SysMonProgressBase
    {
        static double Frac(DateTime n) { return n.TimeOfDay.TotalSeconds / 86400.0; }
        public override int Measure(DrawCtx c) { return (int)(c.S(10) + SysMonDraw.LabelValueW(c, "Today", "100%") + c.S(10) + c.S(64) + c.S(10)); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var now = DateTime.Now; double f = Frac(now);
            float x = b.X + c.S(10), lw = SysMonDraw.LabelValueW(c, "Today", "100%");
            SysMonDraw.LabelValue(c, new RectangleF(x, b.Y, lw + c.S(2), b.Height), "Today", Ui.Pct(f), t.Text);
            float bx = x + lw + c.S(10), bw = b.Right - c.S(10) - bx;
            // 24 hour segments
            DrawSegments(c, new RectangleF(bx, b.Y + b.Height / 2f - c.S(3), bw, c.S(6)), 24, f * 24, t.Accent, t.Track, c.S(1.5f));
        }
        public override Size PanelSize(float scale) { return new Size((int)(320 * scale), (int)(196 * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var now = DateTime.Now; double f = Frac(now);
            float y = c.PanelHeader("Day Progress", now.ToString("dddd, d MMMM", Inv), Icons.Sun);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            Ui.Text(c.G, P1(f), c.F(30, true), t.Text, new RectangleF(x, y, w, c.S(38)));
            Ui.Text(c.G, "of today is behind you", c.F(11.5f), t.TextDim, new RectangleF(x + c.S(118), y + c.S(10), w - c.S(118), c.S(22)));
            y += c.S(46);
            DrawSegments(c, new RectangleF(x, y, w, c.S(14)), 24, f * 24, t.Accent, t.Track, c.S(2.5f));
            y += c.S(18);
            foreach (var h in new[] { 0, 6, 12, 18, 24 })
                Ui.Text(c.G, h.ToString("00") + ":00", c.F(9.5f), t.TextFaint, new RectangleF(h == 0 ? x : h == 24 ? x + w - c.S(40) : x + w * h / 24f - c.S(20), y, c.S(40), c.S(13)), h == 0 ? -1 : h == 24 ? 1 : 0);
            y += c.S(24);
            var left = DateTime.Today.AddDays(1) - now;
            float cw = w / 2;
            SysMonDraw.Stat(c, new RectangleF(x, y, cw, c.S(34)), "Elapsed", Ui.Duration(now.TimeOfDay));
            SysMonDraw.Stat(c, new RectangleF(x + cw, y, cw, c.S(34)), "Remaining", Ui.Duration(left), t.Accent, 1);
        }
        /// <summary>Segmented bar: n cells, `filled` cells (fractional last cell).</summary>
        public static void DrawSegments(DrawCtx c, RectangleF r, int n, double filled, Color on, Color off, float gap)
        {
            float w = (r.Width - gap * (n - 1)) / n, rad = Math.Min(w, r.Height) / 2.2f;
            for (int i = 0; i < n; i++)
            {
                var cell = new RectangleF(r.X + i * (w + gap), r.Y, w, r.Height);
                Ui.FillRound(c.G, cell, rad, off);
                double part = Math.Max(0, Math.Min(1, filled - i));
                if (part >= 0.999) Ui.FillRound(c.G, cell, rad, on);
                else if (part > 0) Ui.FillRound(c.G, cell, rad, Ui.WithA(on, (int)(60 + 140 * part)));
            }
        }
    }

    [Widget("monthprogress", "Month Progress", "System", "How much of the month is behind you.", RefName = "Month Progress", Data = DataKind.Live)]
    public class SysMonMonthWidget : SysMonProgressBase
    {
        static double Frac(DateTime n, out int days) { days = DateTime.DaysInMonth(n.Year, n.Month); return (n.Day - 1 + n.TimeOfDay.TotalDays) / days; }
        public override int Measure(DrawCtx c) { return (int)(c.S(10) + SysMonDraw.LabelValueW(c, "September", "100%") + c.S(10) + c.S(90) + c.S(10)); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var now = DateTime.Now; int days; double f = Frac(now, out days);
            float x = b.X + c.S(10), lw = SysMonDraw.LabelValueW(c, "September", "100%");
            SysMonDraw.LabelValue(c, new RectangleF(x, b.Y, lw + c.S(2), b.Height), now.ToString("MMMM", Inv), Ui.Pct(f), t.Text);
            float bx = x + lw + c.S(10), bw = b.Right - c.S(10) - bx;
            SysMonDayWidget.DrawSegments(c, new RectangleF(bx, b.Y + b.Height / 2f - c.S(4), bw, c.S(8)), days, f * days, t.Accent, t.Track, c.S(1.2f));
        }
        static int Rows(DateTime n) { int lead = ((int)new DateTime(n.Year, n.Month, 1).DayOfWeek + 6) % 7; return (lead + DateTime.DaysInMonth(n.Year, n.Month) + 6) / 7; }
        public override Size PanelSize(float scale) { return new Size((int)(320 * scale), (int)((150 + Rows(DateTime.Now) * 24) * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var now = DateTime.Now; int days; double f = Frac(now, out days);
            float y = c.PanelHeader("Month Progress", now.ToString("MMMM yyyy", Inv), Icons.Calendar);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            Ui.Text(c.G, P1(f), c.F(30, true), t.Text, new RectangleF(x, y, w, c.S(38)));
            Ui.Text(c.G, "Day " + now.Day + " of " + days, c.FS(12), t.TextDim, new RectangleF(x, y + c.S(8), w, c.S(22)), 1);
            y += c.S(46);
            // calendar grid, Monday first
            int lead = ((int)new DateTime(now.Year, now.Month, 1).DayOfWeek + 6) % 7;
            float cell = w / 7, ch = c.S(21);
            string[] dn = { "M", "T", "W", "T", "F", "S", "S" };
            for (int i = 0; i < 7; i++) Ui.Text(c.G, dn[i], c.F(9.5f), t.TextFaint, new RectangleF(x + i * cell, y, cell, c.S(13)), 0);
            y += c.S(16);
            for (int d = 1; d <= days; d++)
            {
                int k = lead + d - 1, col = k % 7, row = k / 7;
                var r = new RectangleF(x + col * cell + c.S(2), y + row * c.S(24), cell - c.S(4), ch);
                bool past = d < now.Day, today = d == now.Day;
                Ui.FillRound(c.G, r, c.S(6), today ? t.Accent : past ? Ui.WithA(t.Accent, t.Dark ? 70 : 55) : t.Track);
                Ui.Text(c.G, d.ToString(), today ? c.FS(11) : c.F(10.5f), today ? Color.White : past ? t.Text : t.TextDim, r, 0);
            }
            int rows = (lead + days + 6) / 7;
            y += rows * c.S(24) + c.S(4);
            int leftDays = days - now.Day;
            Ui.Text(c.G, leftDays == 0 ? "Last day of the month" : leftDays + (leftDays == 1 ? " day left" : " days left"), c.F(11), t.TextDim, new RectangleF(x, y, w, c.S(16)));
        }
    }

    [Widget("yearprogress", "Year Progress", "System", "The year as a grid of dots, filling up.", RefName = "Year Progress", Data = DataKind.Live)]
    public class SysMonYearWidget : SysMonProgressBase
    {
        static double Frac(DateTime n, out int days) { days = DateTime.IsLeapYear(n.Year) ? 366 : 365; return (n.DayOfYear - 1 + n.TimeOfDay.TotalDays) / days; }
        static int Weeks(DateTime n, int days) { int lead = ((int)new DateTime(n.Year, 1, 1).DayOfWeek + 6) % 7; return (lead + days + 6) / 7; }

        /// <summary>GitHub-style grid: columns = weeks, rows = Mon..Sun.</summary>
        static void Grid(DrawCtx c, float x, float y, float pitch, float dot, DateTime now, int days)
        {
            var t = c.T;
            int lead = ((int)new DateTime(now.Year, 1, 1).DayOfWeek + 6) % 7, today = now.DayOfYear - 1;
            var past = Ui.WithA(t.Accent, t.Dark ? 200 : 215); var future = t.Track;
            for (int d = 0; d < days; d++)
            {
                int k = lead + d, col = k / 7, row = k % 7;
                var r = new RectangleF(x + col * pitch + (pitch - dot) / 2, y + row * pitch + (pitch - dot) / 2, dot, dot);
                var cc = d < today ? past : d == today ? t.Text : future;
                if (dot >= 4) Ui.FillRound(c.G, r, dot * 0.3f, cc); else c.G.FillRectangle(Ui.B(cc), r);
            }
        }
        const float TilePitch = 3.3f;
        public override int Measure(DrawCtx c)
        {
            var now = DateTime.Now; int days = DateTime.IsLeapYear(now.Year) ? 366 : 365;
            return (int)(c.S(10) + SysMonDraw.LabelValueW(c, "2026", "100%") + c.S(10) + Weeks(now, days) * c.S(TilePitch) + c.S(10));
        }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var now = DateTime.Now; int days; double f = Frac(now, out days);
            float x = b.X + c.S(10), lw = SysMonDraw.LabelValueW(c, "2026", "100%");
            SysMonDraw.LabelValue(c, new RectangleF(x, b.Y, lw + c.S(2), b.Height), now.Year.ToString(), Ui.Pct(f), c.T.Text);
            float pitch = c.S(TilePitch);
            Grid(c, x + lw + c.S(10), b.Y + (b.Height - pitch * 7) / 2f, pitch, pitch * 0.72f, now, days);
        }
        public override Size PanelSize(float scale) { return new Size((int)(344 * scale), (int)(222 * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var now = DateTime.Now; int days; double f = Frac(now, out days);
            float y = c.PanelHeader("Year Progress", now.Year.ToString(), Icons.Calendar);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            Ui.Text(c.G, P1(f), c.F(30, true), t.Text, new RectangleF(x, y, w, c.S(38)));
            Ui.Text(c.G, "Day " + now.DayOfYear + " of " + days, c.FS(12), t.TextDim, new RectangleF(x, y + c.S(8), w, c.S(22)), 1);
            y += c.S(48);
            int weeks = Weeks(now, days);
            float pitch = w / weeks;
            Grid(c, x, y, pitch, pitch * 0.74f, now, days);
            // month labels
            y += pitch * 7 + c.S(4);
            int lead = ((int)new DateTime(now.Year, 1, 1).DayOfWeek + 6) % 7;
            for (int m = 1; m <= 12; m += 1)
            {
                int k = lead + new DateTime(now.Year, m, 1).DayOfYear - 1;
                if (m % 2 == 0) continue;
                Ui.Text(c.G, new DateTime(now.Year, m, 1).ToString("MMM", Inv), c.F(9.5f), t.TextFaint, new RectangleF(x + (k / 7) * pitch, y, c.S(30), c.S(13)));
            }
            y += c.S(22);
            int left = days - now.DayOfYear;
            float cw = w / 3;
            SysMonDraw.Stat(c, new RectangleF(x, y, cw, c.S(34)), "Days left", left.ToString(), t.Accent);
            SysMonDraw.Stat(c, new RectangleF(x + cw, y, cw, c.S(34)), "Week", CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday).ToString(), null, 0);
            SysMonDraw.Stat(c, new RectangleF(x + 2 * cw, y, cw, c.S(34)), "Quarter", "Q" + ((now.Month - 1) / 3 + 1), null, 1);
        }
    }
}
