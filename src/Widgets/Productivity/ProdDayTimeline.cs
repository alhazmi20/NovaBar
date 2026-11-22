// Day Timeline — the whole day on one line: app kinds (screen time slots), focus rounds, meetings, sunrise/sunset, now.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    [Widget("daytimeline", "Day Timeline", "Productivity", "Your whole day on one line: hours by kind of app, focus rounds, meetings, sunrise and sunset.", RefName = "Day Timeline", Data = DataKind.Live)]
    public class ProdDayTimelineWidget : Widget
    {
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Choice("range", "Hours shown", "Whole day", "Whole day", "6:00 – 24:00", "Sunrise – midnight") }; } }

        public override void OnStart()
        {
            ProdScreenTracker.Define(); ProdCal.Define();
            Hub.Subscribe(ProdScreenTracker.Key, this); Hub.Subscribe(ProdCal.Key, this);
        }
        public override void OnStop() { Hub.Unsubscribe(ProdScreenTracker.Key, this); Hub.Unsubscribe(ProdCal.Key, this); ProdScreenTracker.Flush(); }
        public override int RefreshMs { get { return 60000; } }

        class Data
        {
            public ProdDayRec Day; public bool SampleScreen, SampleCal;
            public List<ProdCalEvent> Meetings; public List<ProdPomoLog.Round> Focus;
            public DateTime? Rise, Set;
        }
        Data Collect()
        {
            var d = new Data();
            var today = DateTime.Today;
            var ss = Hub.Get<ProdScreenSnap>(ProdScreenTracker.Key);
            d.Day = ss != null ? ss.Today : ProdScreenTracker.GetDay(today);
            if (ProdScreenTracker.FewData) { d.Day = ProdScreenTracker.SampleDay(today); d.SampleScreen = true; }
            var cs = Hub.Get<ProdCalSnap>(ProdCal.Key) ?? (ProdCal.Connected ? null : ProdCal.SampleSnap());
            d.SampleCal = cs == null || cs.Sample;
            d.Meetings = cs == null ? new List<ProdCalEvent>() : cs.Events.Where(e => !e.AllDay && e.Start < today.AddDays(1) && e.End > today).ToList();
            d.Focus = ProdPomoLog.ForDay(today);
            if (ProdPomo.Phase == ProdPomo.Focus && ProdPomo.Running && ProdPomo.PhaseStart > today)
                d.Focus.Add(new ProdPomoLog.Round { Start = ProdPomo.PhaseStart, End = DateTime.Now });
            if (d.Focus.Count == 0 && d.SampleScreen)
            {   // demo rounds alongside the sample day
                foreach (var h in new[] { 9.75, 10.25, 14.75, 15.25 }) if (today.AddHours(h + 0.42) < DateTime.Now) d.Focus.Add(new ProdPomoLog.Round { Start = today.AddHours(h), End = today.AddHours(h + 0.42) });
            }
            DateTime? r, s; ProdUtil.SunTimes(today, out r, out s); d.Rise = r; d.Set = s;
            return d;
        }
        void Range(Data d, out double h0, out double h1)
        {
            var m = SStr("range");
            h0 = 0; h1 = 24;
            if (m.StartsWith("6")) h0 = 6;
            else if (m.StartsWith("Sunrise") && d.Rise.HasValue) h0 = Math.Floor(d.Rise.Value.TimeOfDay.TotalHours);
        }

        /// <summary>Draws the timeline. laneH = height of the event lane above the bar.</summary>
        void DrawLine(DrawCtx c, Data d, RectangleF bar, float laneH, bool big)
        {
            var g = c.G; double h0, h1; Range(d, out h0, out h1);
            var day = DateTime.Today;
            Func<DateTime, float> X = t => bar.X + (float)(((t - day).TotalHours - h0) / (h1 - h0)) * bar.Width;
            Func<DateTime, float> Xc = t => Math.Max(bar.X, Math.Min(bar.Right, X(t)));
            float rad = bar.Height / 2;
            // track
            Ui.FillRound(g, bar, rad, c.T.Track);
            using (var clip = Ui.Round(bar, rad))
            {
                var old = g.Clip; g.SetClip(clip, CombineMode.Intersect);
                // night shading
                var night = c.T.Dark ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(28, 0, 0, 40);
                if (d.Rise.HasValue) g.FillRectangle(Ui.B(night), bar.X, bar.Y, Xc(d.Rise.Value) - bar.X, bar.Height);
                if (d.Set.HasValue) g.FillRectangle(Ui.B(night), Xc(d.Set.Value), bar.Y, bar.Right - Xc(d.Set.Value), bar.Height);
                // kind segments (merge consecutive 5-min slots of the same kind)
                int i = 0;
                while (i < 288)
                {
                    char k = d.Day.Slots[i]; int j = i + 1;
                    while (j < 288 && d.Day.Slots[j] == k) j++;
                    if (k != '.')
                    {
                        float x1 = X(day.AddMinutes(i * 5)), x2 = X(day.AddMinutes(j * 5));
                        if (x2 > bar.X && x1 < bar.Right) g.FillRectangle(Ui.B(ProdUtil.KindColor(c.T, k)), x1, bar.Y, Math.Max(1f, x2 - x1 - (big ? 0.5f : 0)), bar.Height);
                    }
                    i = j;
                }
                g.Clip = old;
            }
            // lane: meetings (neutral pills) + focus rounds (tomato)
            float ly = bar.Y - laneH - c.S(big ? 3 : 2), lh = laneH;
            var meetCol = c.T.Dark ? Color.FromArgb(220, c.T.Text) : Color.FromArgb(200, c.T.Text);
            foreach (var e in d.Meetings)
            {
                float x1 = Xc(e.Start), x2 = Xc(e.End); if (x2 - x1 < c.S(3)) x2 = x1 + c.S(3);
                if (x2 <= bar.X || x1 >= bar.Right) continue;
                Ui.FillRound(g, new RectangleF(x1, ly, x2 - x1, lh), lh / 2, meetCol);
            }
            foreach (var f in d.Focus)
            {
                float x1 = Xc(f.Start), x2 = Xc(f.End); if (x2 - x1 < c.S(3)) x2 = x1 + c.S(3);
                Ui.FillRound(g, new RectangleF(x1, ly, x2 - x1, lh), lh / 2, c.T.Bad);
            }
            // sunrise / sunset markers
            foreach (var st in new[] { d.Rise, d.Set })
            {
                if (!st.HasValue) continue; float sx = X(st.Value); if (sx < bar.X || sx > bar.Right) continue;
                using (var p = Ui.P(c.T.Warn, c.S(big ? 1.6f : 1.3f))) g.DrawLine(p, sx, bar.Y - c.S(1), sx, bar.Bottom + c.S(1));
                float r = c.S(big ? 3.2f : 2.4f);
                if (big) Ui.FillCircle(g, new PointF(sx, bar.Bottom + c.S(1) + r), r, c.T.Warn);
            }
            // now marker
            float nx = X(DateTime.Now);
            if (nx >= bar.X && nx <= bar.Right)
            {
                var bg = c.T.Dark ? Color.FromArgb(160, 0, 0, 0) : Color.FromArgb(200, 255, 255, 255);
                using (var p = Ui.P(bg, c.S(big ? 4 : 3.4f))) g.DrawLine(p, nx, ly - c.S(1), nx, bar.Bottom + c.S(2));
                using (var p = Ui.P(c.T.Text, c.S(big ? 2 : 1.6f))) g.DrawLine(p, nx, ly - c.S(1), nx, bar.Bottom + c.S(2));
                Ui.FillCircle(g, new PointF(nx, ly - c.S(1)), c.S(big ? 3.5f : 2.6f), c.T.Text);
            }
        }

        public override int Measure(DrawCtx c) { return c.Si(250); }
        public override void Draw(DrawCtx c)
        {
            var d = Collect(); var b = c.Bounds;
            var bar = new RectangleF(b.X + c.S(10), b.Y + c.S(12), b.Width - c.S(20), c.S(8));
            DrawLine(c, d, bar, c.S(4.5f), false);
            double h0, h1; Range(d, out h0, out h1);
            var f = c.F(8.5f); float ty = bar.Bottom + c.S(2.5f);
            int step = (h1 - h0) > 12 ? 6 : 3;
            for (int h = (int)Math.Ceiling(h0 / step) * step; h <= h1; h += step)
            {
                float x = bar.X + (float)((h - h0) / (h1 - h0)) * bar.Width;
                string s = (h % 24).ToString();
                int al = h == h0 ? -1 : h == h1 ? 1 : 0;
                var r = al < 0 ? new RectangleF(x, ty, c.S(20), c.S(11)) : al > 0 ? new RectangleF(x - c.S(20), ty, c.S(20), c.S(11)) : new RectangleF(x - c.S(10), ty, c.S(20), c.S(11));
                Ui.Text(c.G, s, f, c.T.TextFaint, r, al);
            }
        }

        public override Size PanelSize(float s) { return new Size((int)(380 * s), (int)(330 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var d = Collect(); var b = c.Bounds; var g = c.G;
            c.PanelHeader("Day Timeline", DateTime.Now.ToString("dddd, MMM d", ProdUtil.Inv), Icons.Recent);
            float x = b.X + c.S(18), w = b.Width - c.S(36), y = b.Y + c.S(60);
            var bar = new RectangleF(x, y, w, c.S(18));
            DrawLine(c, d, bar, c.S(8), true);
            double h0, h1; Range(d, out h0, out h1);
            float ty = bar.Bottom + c.S(9);
            int step = (h1 - h0) > 12 ? 3 : 2;
            for (int h = (int)Math.Ceiling(h0 / step) * step; h <= h1; h += step)
            {
                float hx = bar.X + (float)((h - h0) / (h1 - h0)) * bar.Width;
                int al = h == h0 ? -1 : h == h1 ? 1 : 0;
                string s = (h % 24).ToString("00");
                var r = al < 0 ? new RectangleF(hx, ty, c.S(24), c.S(12)) : al > 0 ? new RectangleF(hx - c.S(24), ty, c.S(24), c.S(12)) : new RectangleF(hx - c.S(12), ty, c.S(24), c.S(12));
                Ui.Text(g, s, c.F(9.5f), c.T.TextFaint, r, al);
            }
            y = ty + c.S(22);
            // sun row
            float half = w / 2;
            Ui.Icon(g, Icons.Sun, new RectangleF(x, y, c.S(18), c.S(20)), c.T.Warn, c.S(14));
            Ui.Text(g, d.Rise.HasValue ? "Sunrise " + Ui.Clock(d.Rise.Value) : "No sunrise", c.F(11.5f), c.T.TextDim, new RectangleF(x + c.S(22), y, half - c.S(22), c.S(20)));
            Ui.Icon(g, Icons.Moon, new RectangleF(x + half, y, c.S(18), c.S(20)), c.T.Warn, c.S(14));
            Ui.Text(g, d.Set.HasValue ? "Sunset " + Ui.Clock(d.Set.Value) : "No sunset", c.F(11.5f), c.T.TextDim, new RectangleF(x + half + c.S(22), y, half - c.S(22), c.S(20)));
            y += c.S(30);
            using (var p = new Pen(c.T.Track, 1f)) g.DrawLine(p, x, y, x + w, y);
            y += c.S(12);
            // kinds legend with time
            var bk = d.Day.ByKind();
            float colW = w / 3, rowH = c.S(36);
            for (int i = 0; i < 6; i++)
            {
                float cx = x + (i % 3) * colW, cy = y + (i / 3) * rowH;
                var col = ProdUtil.KindColor(c.T, ProdUtil.KindChars[i]);
                Ui.FillRound(g, new RectangleF(cx, cy + c.S(3), c.S(10), c.S(10)), c.S(3), col);
                Ui.Text(g, ProdUtil.KindNames[i], c.F(10.5f), c.T.TextDim, new RectangleF(cx + c.S(15), cy, colW - c.S(15), c.S(16)));
                Ui.Text(g, ProdUtil.Dur(bk[i]), c.FS(12.5f), bk[i] > 0 ? c.T.Text : c.T.TextFaint, new RectangleF(cx + c.S(15), cy + c.S(15), colW - c.S(15), c.S(17)));
            }
            y += rowH * 2 + c.S(6);
            using (var p = new Pen(c.T.Track, 1f)) g.DrawLine(p, x, y, x + w, y);
            y += c.S(12);
            // focus + meetings summary
            double fm = d.Focus.Sum(f => (f.End - f.Start).TotalMinutes);
            double mm = d.Meetings.Sum(e => (Min(e.End, DateTime.Today.AddDays(1)) - Max(e.Start, DateTime.Today)).TotalMinutes);
            Ui.FillRound(g, new RectangleF(x, y + c.S(4), c.S(16), c.S(7)), c.S(3.5f), c.T.Bad);
            Ui.Text(g, "Focus", c.F(10.5f), c.T.TextDim, new RectangleF(x + c.S(22), y, half, c.S(16)));
            Ui.Text(g, d.Focus.Count + (d.Focus.Count == 1 ? " round · " : " rounds · ") + ProdUtil.Dur(fm * 60), c.FS(12.5f), c.T.Text, new RectangleF(x + c.S(22), y + c.S(15), half - c.S(22), c.S(17)));
            Ui.FillRound(g, new RectangleF(x + half, y + c.S(4), c.S(16), c.S(7)), c.S(3.5f), c.T.Dark ? Color.FromArgb(220, c.T.Text) : Color.FromArgb(200, c.T.Text));
            Ui.Text(g, "Meetings", c.F(10.5f), c.T.TextDim, new RectangleF(x + half + c.S(22), y, half, c.S(16)));
            Ui.Text(g, d.Meetings.Count + " · " + ProdUtil.Dur(mm * 60), c.FS(12.5f), c.T.Text, new RectangleF(x + half + c.S(22), y + c.S(15), half - c.S(22), c.S(17)));
            if (d.SampleScreen || d.SampleCal)
                Ui.Text(g, d.SampleScreen && d.SampleCal ? "Sample data — screen time starts tracking now; connect a calendar in Settings" : d.SampleScreen ? "Sample app data — screen time starts tracking now" : "Sample meetings — connect a calendar in Settings", c.F(9.5f), c.T.Warn, new RectangleF(x, b.Bottom - c.S(22), w, c.S(14)), 0);
        }
        static DateTime Min(DateTime a, DateTime b) { return a < b ? a : b; }
        static DateTime Max(DateTime a, DateTime b) { return a > b ? a : b; }
    }
}
