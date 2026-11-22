// Timer (countdown, one click) and Stopwatch (start/pause/lap, laps persisted).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    [Widget("timer", "Timer", "Productivity", "A countdown that starts in one click.", RefName = "Timer", Data = DataKind.Live)]
    public class ProdTimerWidget : Widget
    {
        bool loaded, running; double minutes = 5, remaining; DateTime endsAt, flashUntil; bool done;

        void Load()
        {
            if (loaded) return; loaded = true;
            minutes = J.Num(State, "min", 5); running = J.Bool(State, "running"); remaining = J.Num(State, "rem");
            endsAt = ProdUtil.FromUnix(J.Num(State, "ends"));
            if (running && DateTime.Now >= endsAt) { running = false; remaining = 0; }
        }
        void Persist() { State["min"] = minutes; State["running"] = running; State["rem"] = remaining; State["ends"] = ProdUtil.Unix(endsAt); SaveState(); }

        double Total { get { return minutes * 60; } }
        double Left { get { return running ? Math.Max(0, (endsAt - DateTime.Now).TotalSeconds) : (remaining > 0 ? remaining : Total); } }
        bool Active { get { return running || (remaining > 0 && remaining < Total); } }

        void StartPause()
        {
            Load(); done = false;
            if (running) { remaining = Left; running = false; }
            else { endsAt = DateTime.Now.AddSeconds(remaining > 0 ? remaining : Total); running = true; }
            Persist(); Invalidate();
        }
        void Start(double min) { Load(); minutes = min; remaining = 0; running = false; StartPause(); }
        void Reset() { Load(); running = false; remaining = 0; done = false; Persist(); Invalidate(); }
        void Adjust(int d)
        {
            Load(); if (running) return;
            double step = minutes >= 60 ? 5 : 1;
            minutes = Math.Max(1, Math.Min(240, minutes + d * step)); remaining = 0; done = false; Persist(); Invalidate();
        }

        public override int RefreshMs { get { return ProdUtil.Flashing(flashUntil) ? 250 : running ? 1000 : 0; } }
        public override void OnStart() { Load(); }
        public override void OnTick()
        {
            if (running && DateTime.Now >= endsAt)
            {
                running = false; remaining = 0; done = true; flashUntil = DateTime.Now.AddSeconds(6); ProdUtil.Chime(); Persist();
            }
        }

        public override int Measure(DrawCtx c) { return c.Si(106); }
        public override void Draw(DrawCtx c)
        {
            Load();
            var b = c.Bounds; var g = c.G;
            ProdUtil.Flash(c, flashUntil, c.T.Accent);
            var ring = new RectangleF(b.X + c.S(7), b.Y + (b.Height - c.S(26)) / 2, c.S(26), c.S(26));
            double frac = Active ? Left / Total : 0;
            Ui.Ring(g, ring, frac, c.T.Accent, c.T.Track, c.S(2.6f));
            if (c.IsHover(ring)) Ui.FillCircle(g, new PointF(ring.X + ring.Width / 2, ring.Y + ring.Height / 2), c.S(9), c.T.AccentA(50));
            Ui.Icon(g, running ? Icons.Pause : Icons.Play, ring, running ? c.T.Accent : c.T.Text, c.S(10));
            c.Hit(ring, StartPause);
            float x = ring.Right + c.S(8);
            var tr = new RectangleF(x, b.Y + c.S(3), b.Right - c.S(8) - x, c.S(17));
            Ui.Text(g, done ? "Done" : ProdUtil.MMSS(Left), c.FS(13.5f), done ? c.T.Accent : c.T.Text, tr);
            Ui.Text(g, running ? "remaining" : Active ? "paused" : "scroll to set", c.F(10), c.T.TextDim, new RectangleF(x, b.Y + c.S(19), tr.Width, c.S(13)));
            c.Wheel(b, Adjust);
        }

        public override Size PanelSize(float s) { return new Size((int)(320 * s), (int)(318 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            Load();
            var b = c.Bounds; var g = c.G;
            c.PanelHeader("Timer", running ? "ends " + Ui.Clock(endsAt) : null, Icons.Timer);
            float cx = b.X + b.Width / 2f;
            var ring = new RectangleF(cx - c.S(62), b.Y + c.S(48), c.S(124), c.S(124));
            Ui.Ring(g, ring, Active ? Left / Total : (done ? 0 : 1), Active ? c.T.Accent : c.T.AccentA(90), c.T.Track, c.S(7));
            Ui.Text(g, done ? "Done" : ProdUtil.MMSS(Left), c.F(28, true), c.T.Text, new RectangleF(ring.X, ring.Y + c.S(36), ring.Width, c.S(38)), 0);
            Ui.Text(g, running ? "running" : Active ? "paused" : (int)minutes + " min", c.F(11.5f), c.T.TextDim, new RectangleF(ring.X, ring.Y + c.S(74), ring.Width, c.S(16)), 0);
            // -/+ beside the ring
            var minus = new RectangleF(ring.X - c.S(44), ring.Y + ring.Height / 2 - c.S(15), c.S(30), c.S(30));
            var plus = new RectangleF(ring.Right + c.S(14), minus.Y, c.S(30), c.S(30));
            if (!running)
            {
                Ui.FillRound(g, minus, c.S(15), c.IsHover(minus) ? c.T.TileHover : c.T.Tile); c.IconButton(minus, Icons.Remove, () => Adjust(-1), false, 11);
                Ui.FillRound(g, plus, c.S(15), c.IsHover(plus) ? c.T.TileHover : c.T.Tile); c.IconButton(plus, Icons.Add, () => Adjust(1), false, 11);
            }
            c.Wheel(Rectangle.Round(ring), Adjust);
            // presets
            int[] presets = { 1, 5, 10, 25 };
            float py = ring.Bottom + c.S(16), pw = (b.Width - c.S(32) - c.S(8) * 3) / 4f, ph = c.S(30);
            for (int i = 0; i < presets.Length; i++)
            {
                var r = new RectangleF(b.X + c.S(16) + i * (pw + c.S(8)), py, pw, ph); int m = presets[i];
                bool sel = (int)minutes == m && !Active;
                Ui.FillRound(g, r, c.S(10), sel ? c.T.AccentA(60) : (c.IsHover(r) ? c.T.TileHover : c.T.Tile));
                Ui.Text(g, m + " min", c.FS(12), sel ? c.T.Accent : c.T.Text, r, 0);
                c.Hit(r, () => Start(m));
            }
            float by = py + ph + c.S(12), bh = c.S(32);
            var main = new RectangleF(b.X + c.S(16), by, b.Width - c.S(32) - bh - c.S(8), bh);
            c.Button(main, running ? "Pause" : Active ? "Resume" : "Start " + (int)minutes + " min", StartPause, true, running ? Icons.Pause : Icons.Play);
            var rs = new RectangleF(main.Right + c.S(8), by, bh, bh);
            Ui.FillRound(g, rs, bh / 2, c.IsHover(rs) ? c.T.TileHover : c.T.Tile); c.IconButton(rs, Icons.Refresh, Reset, false, 12);
            ProdUtil.Flash(c, flashUntil, c.T.Accent);
        }
    }

    [Widget("stopwatch", "Stopwatch", "Productivity", "Start, pause and lap from the bar; the card keeps every lap.", RefName = "Stopwatch", Data = DataKind.Live)]
    public class ProdStopwatchWidget : Widget
    {
        bool loaded, running, panelOpen; double accMs; DateTime startedAt; List<double> laps = new List<double>(); // laps = cumulative times (ms)
        int scroll;

        void Load()
        {
            if (loaded) return; loaded = true;
            running = J.Bool(State, "running"); accMs = J.Num(State, "acc"); startedAt = ProdUtil.FromUnix(J.Num(State, "start"));
            var a = J.Arr(State, "laps"); if (a != null) foreach (var x in a) laps.Add(J.AsNum(x));
        }
        void Persist() { State["running"] = running; State["acc"] = accMs; State["start"] = ProdUtil.Unix(startedAt) + (startedAt.Millisecond / 1000.0); State["laps"] = laps.Cast<object>().ToArray(); SaveState(); }
        double Elapsed { get { return accMs + (running ? (DateTime.Now - startedAt).TotalMilliseconds : 0); } }

        void StartPause() { Load(); if (running) { accMs = Elapsed; running = false; } else { startedAt = DateTime.Now; running = true; } Persist(); Invalidate(); }
        void Lap() { Load(); if (Elapsed <= 0) return; laps.Add(Elapsed); scroll = 0; Persist(); Invalidate(); }
        void Reset() { Load(); running = false; accMs = 0; laps.Clear(); Persist(); Invalidate(); }

        public override int RefreshMs { get { return running ? (panelOpen ? 100 : 1000) : 0; } }
        public override void OnStart() { Load(); }
        public override void OnPanelOpen() { panelOpen = true; }
        public override void OnPanelClose() { panelOpen = false; }

        static string Fmt(double ms, bool hundredths)
        {
            if (ms < 0) ms = 0;
            long t = (long)ms; long h = t / 3600000, m = (t / 60000) % 60, s = (t / 1000) % 60, cs = (t / 10) % 100;
            string core = h > 0 ? h + ":" + m.ToString("00") + ":" + s.ToString("00") : m.ToString("00") + ":" + s.ToString("00");
            return hundredths ? core + "." + cs.ToString("00") : core;
        }

        public override int Measure(DrawCtx c) { return c.Si(laps.Count > 0 || Elapsed > 0 ? 136 : 112); }
        public override void Draw(DrawCtx c)
        {
            Load();
            var b = c.Bounds; var g = c.G;
            var pb = new RectangleF(b.X + c.S(6), b.Y + (b.Height - c.S(24)) / 2, c.S(24), c.S(24));
            Ui.FillRound(g, pb, c.S(12), running ? c.T.AccentA(c.IsHover(pb) ? 90 : 55) : (c.IsHover(pb) ? c.T.TileHover : c.T.Track));
            Ui.Icon(g, running ? Icons.Pause : Icons.Play, pb, running ? c.T.Accent : c.T.Text, c.S(10));
            c.Hit(pb, StartPause);
            float right = b.Right - c.S(6);
            if (running || Elapsed > 0)
            {
                var lb = new RectangleF(right - c.S(24), pb.Y, c.S(24), c.S(24));
                if (running) c.IconButton(lb, Icons.Flag, Lap, false, 11); else c.IconButton(lb, Icons.Refresh, Reset, false, 11);
                right = lb.X;
            }
            float x = pb.Right + c.S(7);
            Ui.Text(g, Fmt(Elapsed, false), c.Mono(13.5f, true), Elapsed > 0 ? c.T.Text : c.T.TextDim, new RectangleF(x, b.Y + c.S(3), right - x, c.S(17)));
            Ui.Text(g, laps.Count > 0 ? "lap " + (laps.Count + 1) : (running ? "running" : Elapsed > 0 ? "paused" : "stopwatch"), c.F(10), c.T.TextDim, new RectangleF(x, b.Y + c.S(19), right - x, c.S(13)));
        }

        public override Size PanelSize(float s) { return new Size((int)(320 * s), (int)(380 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            Load();
            var b = c.Bounds; var g = c.G;
            c.PanelHeader("Stopwatch", laps.Count > 0 ? laps.Count + (laps.Count == 1 ? " lap" : " laps") : null, Icons.Stopwatch);
            float x = b.X + c.S(16), w = b.Width - c.S(32), y = b.Y + c.S(48);
            Ui.Text(g, Fmt(Elapsed, true), c.Mono(34, true), c.T.Text, new RectangleF(x, y, w, c.S(44)), 0);
            double cur = Elapsed - (laps.Count > 0 ? laps[laps.Count - 1] : 0);
            if (laps.Count > 0) Ui.Text(g, "Lap " + (laps.Count + 1) + "  " + Fmt(cur, true), c.Mono(12), c.T.TextDim, new RectangleF(x, y + c.S(44), w, c.S(16)), 0);
            y += c.S(70);
            float bh = c.S(32), bw = (w - c.S(16)) / 3;
            var b1 = new RectangleF(x, y, bw, bh); var b2 = new RectangleF(x + bw + c.S(8), y, bw, bh); var b3 = new RectangleF(x + 2 * (bw + c.S(8)), y, bw, bh);
            c.Button(b1, running ? "Pause" : Elapsed > 0 ? "Resume" : "Start", StartPause, true, running ? Icons.Pause : Icons.Play);
            c.Button(b2, "Lap", running ? (Action)Lap : () => { }, false, Icons.Flag);
            c.Button(b3, "Reset", Reset, false, Icons.Refresh);
            y += bh + c.S(14);
            using (var p = new Pen(c.T.Track, 1f)) g.DrawLine(p, x, y, x + w, y);
            y += c.S(6);
            if (laps.Count == 0) { Ui.Text(g, "Laps you take appear here", c.F(12), c.T.TextFaint, new RectangleF(x, y + c.S(8), w, c.S(20)), 0); return; }
            // splits
            var splits = new List<double>(); for (int i = 0; i < laps.Count; i++) splits.Add(laps[i] - (i > 0 ? laps[i - 1] : 0));
            double best = splits.Min(), worst = splits.Max();
            float rh = c.S(26); int rows = (int)((b.Bottom - c.S(10) - y) / rh);
            int maxScroll = Math.Max(0, laps.Count - rows); scroll = Math.Max(0, Math.Min(scroll, maxScroll));
            var listR = new Rectangle((int)x, (int)y, (int)w, (int)(b.Bottom - y));
            c.Wheel(listR, d => { scroll -= d; Invalidate(); });
            for (int k = 0; k < rows && k + scroll < laps.Count; k++)
            {
                int i = laps.Count - 1 - (k + scroll);
                var rr = new RectangleF(x, y + k * rh, w, rh);
                Color col = laps.Count > 1 && splits[i] == best ? c.T.Good : laps.Count > 1 && splits[i] == worst ? c.T.Bad : c.T.Text;
                Ui.Text(g, "Lap " + (i + 1), c.F(12), c.T.TextDim, rr);
                Ui.Text(g, Fmt(splits[i], true), c.Mono(12.5f, true), col, new RectangleF(rr.X + w * 0.32f, rr.Y, w * 0.34f, rh), 0);
                Ui.Text(g, Fmt(laps[i], true), c.Mono(12), c.T.TextFaint, rr, 1);
            }
        }
    }
}
