// Pomodoro — focus/break rounds. One shared engine (static) so the Day Timeline can show the running round.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    public static class ProdPomo
    {
        public const int Idle = 0, Focus = 1, Short = 2, Long = 3;
        public static int Phase, Cycle;            // Cycle = focus rounds completed in the current set
        public static bool Running;
        public static DateTime EndsAt, PhaseStart; // PhaseStart = first start of the current focus phase
        public static double Remaining;            // seconds left while paused / idle
        public static DateTime FlashUntil;
        public static int FocusMin = 25, ShortMin = 5, LongMin = 15, Rounds = 4;
        public static bool AutoStart, Sound = true;
        const string Key = "prod.pomodoro";
        static bool loaded;

        public static void Load()
        {
            if (loaded) return; loaded = true;
            var d = StateStore.Load(Key);
            Phase = (int)J.Num(d, "phase"); Cycle = (int)J.Num(d, "cycle"); Running = J.Bool(d, "running");
            EndsAt = ProdUtil.FromUnix(J.Num(d, "ends")); PhaseStart = ProdUtil.FromUnix(J.Num(d, "pstart")); Remaining = J.Num(d, "rem");
            Check(false);
        }
        static void Save()
        {
            var d = new Dictionary<string, object> { { "phase", Phase }, { "cycle", Cycle }, { "running", Running }, { "ends", ProdUtil.Unix(EndsAt) }, { "pstart", ProdUtil.Unix(PhaseStart) }, { "rem", Remaining } };
            System.Threading.ThreadPool.QueueUserWorkItem(_ => StateStore.Save(Key, d));
        }
        public static int Minutes(int phase) { return phase == Short ? ShortMin : phase == Long ? LongMin : FocusMin; }
        public static double Left { get { return Running ? Math.Max(0, (EndsAt - DateTime.Now).TotalSeconds) : (Phase == Idle ? FocusMin * 60 : Remaining); } }
        public static double Frac { get { double tot = Minutes(Phase == Idle ? Focus : Phase) * 60.0; return Phase == Idle ? 0 : 1 - Left / tot; } }
        public static string PhaseName(int p) { return p == Short ? "Short break" : p == Long ? "Long break" : p == Focus ? "Focus" : "Ready"; }
        public static bool IsBreak { get { return Phase == Short || Phase == Long; } }

        public static void StartPause()
        {
            Load();
            if (Phase == Idle) { Phase = Focus; Remaining = FocusMin * 60; PhaseStart = DateTime.Now; }
            if (Running) { Remaining = Left; Running = false; }
            else { EndsAt = DateTime.Now.AddSeconds(Remaining > 0 ? Remaining : Minutes(Phase) * 60); Running = true; if (Phase == Focus && PhaseStart == DateTime.MinValue) PhaseStart = DateTime.Now; }
            Save();
        }
        public static void Skip() { Load(); if (Phase == Idle) return; Advance(false); Save(); }
        public static void Reset() { Load(); Phase = Idle; Running = false; Cycle = 0; Remaining = 0; Save(); }

        /// <summary>Call periodically. Returns true when a phase just ended.</summary>
        public static bool Check(bool notify = true)
        {
            if (!Running || DateTime.Now < EndsAt) return false;
            bool fresh = (DateTime.Now - EndsAt).TotalMinutes < 2;
            Advance(true);
            if (notify && fresh) { FlashUntil = DateTime.Now.AddSeconds(6); if (Sound) ProdUtil.Chime(); }
            Save();
            return true;
        }
        static void Advance(bool completed)
        {
            int next;
            if (Phase == Focus)
            {
                if (completed) { var end = EndsAt; var st = PhaseStart > end.AddHours(-12) && PhaseStart < end ? PhaseStart : end.AddMinutes(-FocusMin); ProdPomoLog.Add(st, end); }
                Cycle++;
                next = Cycle % Math.Max(1, Rounds) == 0 ? Long : Short;
            }
            else { next = Focus; if (Phase == Long) Cycle = 0; }
            bool keepRunning = completed ? AutoStart : Running;
            Phase = next; Remaining = Minutes(next) * 60;
            if (next == Focus) PhaseStart = keepRunning ? DateTime.Now : DateTime.MinValue;
            Running = keepRunning; if (Running) EndsAt = DateTime.Now.AddSeconds(Remaining);
        }
    }

    [Widget("pomodoro", "Pomodoro", "Productivity", "Focus rounds and breaks you start and skip from the bar.", RefName = "Pomodoro", Data = DataKind.Live)]
    public class ProdPomodoroWidget : Widget
    {
        public override SettingDef[] SettingDefs
        {
            get
            {
                return new[] { SettingDef.Number("focus", "Focus minutes", 25, 1, 180), SettingDef.Number("short", "Short break minutes", 5, 1, 60), SettingDef.Number("long", "Long break minutes", 15, 1, 90),
                    SettingDef.Number("rounds", "Rounds before long break", 4, 1, 12), SettingDef.Toggle("auto", "Start next phase automatically", false), SettingDef.Toggle("sound", "Play a sound at phase end", true) };
            }
        }
        void Apply()
        {
            ProdPomo.FocusMin = (int)SNum("focus"); ProdPomo.ShortMin = (int)SNum("short"); ProdPomo.LongMin = (int)SNum("long");
            ProdPomo.Rounds = (int)SNum("rounds"); ProdPomo.AutoStart = SBool("auto"); ProdPomo.Sound = SBool("sound");
        }
        public override void OnStart() { Apply(); ProdPomo.Load(); }
        public override int RefreshMs { get { return ProdUtil.Flashing(ProdPomo.FlashUntil) ? 250 : ProdPomo.Running ? 1000 : 0; } }
        public override void OnTick() { Apply(); ProdPomo.Check(); }

        Color PhaseColor(Theme t) { return ProdPomo.IsBreak ? t.Good : t.Bad; }

        public override int Measure(DrawCtx c) { return c.Si(ProdPomo.Phase == ProdPomo.Idle ? 108 : 128); }
        public override void Draw(DrawCtx c)
        {
            ProdPomo.Load();
            var b = c.Bounds; var g = c.G; var col = PhaseColor(c.T);
            ProdUtil.Flash(c, ProdPomo.FlashUntil, col);
            var ring = new RectangleF(b.X + c.S(7), b.Y + (b.Height - c.S(26)) / 2, c.S(26), c.S(26));
            Ui.Ring(g, ring, ProdPomo.Frac, col, c.T.Track, c.S(2.6f));
            bool hov = c.IsHover(ring);
            if (hov) Ui.FillCircle(g, new PointF(ring.X + ring.Width / 2, ring.Y + ring.Height / 2), c.S(9), Ui.WithA(col, 50));
            Ui.Icon(g, ProdPomo.Running ? Icons.Pause : Icons.Play, ring, ProdPomo.Running ? col : c.T.Text, c.S(10));
            c.Hit(ring, () => { Apply(); ProdPomo.StartPause(); Invalidate(); });
            float x = ring.Right + c.S(8);
            float right = b.Right - c.S(8);
            if (ProdPomo.Phase != ProdPomo.Idle)
            {
                var sk = new RectangleF(right - c.S(22), b.Y + (b.Height - c.S(22)) / 2, c.S(22), c.S(22));
                c.IconButton(sk, Icons.Next, () => { Apply(); ProdPomo.Skip(); Invalidate(); }, false, 10);
                right = sk.X;
            }
            Ui.Text(g, ProdUtil.MMSS(ProdPomo.Left), c.FS(13.5f), c.T.Text, new RectangleF(x, b.Y + c.S(3), right - x, c.S(17)));
            string lbl = ProdPomo.Phase == ProdPomo.Focus ? "Focus " + (ProdPomo.Cycle % Math.Max(1, ProdPomo.Rounds) + 1) + "/" + ProdPomo.Rounds : ProdPomo.Phase == ProdPomo.Idle ? "Focus" : ProdPomo.IsBreak ? "Break" : "";
            if (!ProdPomo.Running && ProdPomo.Phase != ProdPomo.Idle) lbl = "Paused";
            Ui.Text(g, lbl, c.F(10), ProdPomo.Phase == ProdPomo.Idle ? c.T.TextDim : col, new RectangleF(x, b.Y + c.S(19), right - x, c.S(13)));
        }

        public override Size PanelSize(float s) { return new Size((int)(320 * s), (int)(330 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            ProdPomo.Load(); Apply();
            var b = c.Bounds; var g = c.G; var col = PhaseColor(c.T);
            c.PanelHeader("Pomodoro", ProdPomo.PhaseName(ProdPomo.Phase) + (ProdPomo.Running || ProdPomo.Phase == ProdPomo.Idle ? "" : " · paused"), Icons.Timer);
            float cx = b.X + b.Width / 2f, top = b.Y + c.S(50);
            var ring = new RectangleF(cx - c.S(66), top, c.S(132), c.S(132));
            Ui.Ring(g, ring, ProdPomo.Frac, col, c.T.Track, c.S(7));
            Ui.Text(g, ProdUtil.MMSS(ProdPomo.Left), c.F(30, true), c.T.Text, new RectangleF(ring.X, ring.Y + c.S(36), ring.Width, c.S(40)), 0);
            Ui.Text(g, ProdPomo.Phase == ProdPomo.Idle ? ProdPomo.FocusMin + " min focus" : ProdPomo.PhaseName(ProdPomo.Phase), c.F(11.5f), ProdPomo.Phase == ProdPomo.Idle ? c.T.TextDim : col, new RectangleF(ring.X, ring.Y + c.S(78), ring.Width, c.S(16)), 0);
            // round dots
            int n = Math.Max(1, ProdPomo.Rounds); float dy = ring.Bottom + c.S(14), dsp = c.S(14);
            float dx = cx - (n - 1) * dsp / 2;
            int done = ProdPomo.Cycle % n; if (ProdPomo.Phase == ProdPomo.Long) done = n;
            for (int i = 0; i < n; i++)
            {
                var p = new PointF(dx + i * dsp, dy);
                if (i < done) Ui.FillCircle(g, p, c.S(4), c.T.Bad);
                else if (i == done && ProdPomo.Phase == ProdPomo.Focus) { Ui.FillCircle(g, p, c.S(4), c.T.Track); using (var pen = new Pen(c.T.Bad, c.S(1.5f))) g.DrawEllipse(pen, p.X - c.S(4), p.Y - c.S(4), c.S(8), c.S(8)); }
                else Ui.FillCircle(g, p, c.S(4), c.T.Track);
            }
            // buttons
            float by = dy + c.S(16), bh = c.S(32);
            var bMain = new RectangleF(cx - c.S(60), by, c.S(120), bh);
            c.Button(bMain, ProdPomo.Running ? "Pause" : (ProdPomo.Phase == ProdPomo.Idle ? "Start focus" : "Resume"), () => { ProdPomo.StartPause(); Invalidate(); }, true, ProdPomo.Running ? Icons.Pause : Icons.Play);
            var bSkip = new RectangleF(bMain.Right + c.S(10), by, bh, bh); var bReset = new RectangleF(bMain.X - c.S(10) - bh, by, bh, bh);
            Ui.FillRound(g, bSkip, bh / 2, c.IsHover(bSkip) ? c.T.TileHover : c.T.Tile); c.IconButton(bSkip, Icons.Next, () => { ProdPomo.Skip(); Invalidate(); }, false, 12);
            Ui.FillRound(g, bReset, bh / 2, c.IsHover(bReset) ? c.T.TileHover : c.T.Tile); c.IconButton(bReset, Icons.Refresh, () => { ProdPomo.Reset(); Invalidate(); }, false, 12);
            // today summary
            var today = ProdPomoLog.ForDay(DateTime.Today);
            double mins = today.Sum(r => (r.End - r.Start).TotalMinutes);
            float sy = b.Bottom - c.S(34);
            using (var p = new Pen(c.T.Track, 1f)) g.DrawLine(p, b.X + c.S(16), sy - c.S(6), b.Right - c.S(16), sy - c.S(6));
            Ui.Text(g, "Today", c.F(11.5f), c.T.TextDim, new RectangleF(b.X + c.S(16), sy, c.S(100), c.S(24)));
            Ui.Text(g, today.Count + (today.Count == 1 ? " round · " : " rounds · ") + ProdUtil.Dur(mins * 60) + " focused", c.FS(12), c.T.Text, new RectangleF(b.X + c.S(16), sy, b.Width - c.S(32), c.S(24)), 1);
            ProdUtil.Flash(c, ProdPomo.FlashUntil, col);
        }
    }
}
