using System;
using System.Drawing;

namespace NovaBar
{
    /// <summary>Common battery behaviour: power sampler, shared panel layout. Variants draw their own visual.</summary>
    public abstract class SysMonBatteryBase : SysMonWidget
    {
        protected override string[] Keys { get { return new[] { "sysmon.power" }; } }
        protected SysMonPowerSnap P { get { return SysMon.Power; } }
        protected static bool Has(SysMonPowerSnap p) { return p != null && p.HasBattery; }
        /// <summary>Draws this variant's visual into r (tile or panel size).</summary>
        protected abstract void Visual(DrawCtx c, RectangleF r, SysMonPowerSnap p, bool big);
        protected virtual float PanelVisualW { get { return 116; } }
        protected virtual float PanelVisualH { get { return 116; } }

        public static string StateText(SysMonPowerSnap p)
        {
            if (p == null) return "Reading…";
            if (!p.HasBattery) return "AC power";
            if (p.Charging) return p.Pct >= 100 ? "Fully charged" : "Charging";
            if (p.Plugged) return p.Pct >= 99 ? "Fully charged" : "Plugged in, not charging";
            return "On battery";
        }
        protected static string PctText(SysMonPowerSnap p) { return Has(p) ? p.Pct + "%" : "AC"; }

        public override Size PanelSize(float scale) { return new Size((int)(320 * scale), (int)(214 * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var p = P;
            float y = c.PanelHeader("Battery", p != null && p.Fake ? "Simulated" : null, Has(p) && p.Charging ? Icons.BatteryCharging : Icons.Battery);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            var vr = new RectangleF(x + c.S(2), y + c.S(2), c.S(PanelVisualW), c.S(PanelVisualH));
            Visual(c, vr, p, true);
            float sx = vr.Right + c.S(18), sw = b.Right - c.S(16) - sx;
            if (!Has(p))
            {
                Ui.Text(c.G, "AC power", c.F(22, true), t.Text, new RectangleF(sx, y + c.S(6), sw, c.S(30)));
                Ui.TextWrap(c.G, p == null ? "Reading power status…" : "No battery detected — this PC runs on mains power.", c.F(11.5f), t.TextDim, new RectangleF(sx, y + c.S(40), sw, c.S(56)));
                y = vr.Bottom + c.S(12);
                SysMonDraw.Divider(c, x, y, w); y += c.S(10);
                SysMonDraw.Row(c, x, y, w, "Power source", "Mains (AC)");
                return;
            }
            Ui.Text(c.G, p.Pct + "%", c.F(30, true), t.Text, new RectangleF(sx, y + c.S(2), sw, c.S(38)));
            var sc = p.Charging ? t.Good : p.Pct <= 15 ? t.Bad : p.Pct <= 30 ? t.Warn : t.TextDim;
            Ui.Text(c.G, StateText(p), c.FS(12.5f), sc, new RectangleF(sx, y + c.S(42), sw, c.S(17)));
            string sub = p.Charging ? "Plugged in" : p.SecsLeft > 0 ? Ui.Duration(TimeSpan.FromSeconds(p.SecsLeft)) + " remaining" : p.Plugged ? "Plugged in" : "Estimating time…";
            Ui.Text(c.G, sub, c.F(11), t.TextDim, new RectangleF(sx, y + c.S(60), sw, c.S(16)));
            Ui.Progress(c.G, new RectangleF(sx, y + c.S(86), sw, c.S(6)), p.Pct / 100.0, SysMonDraw.BatColor(t, p, false), t.Track);
            y = vr.Bottom + c.S(12);
            SysMonDraw.Divider(c, x, y, w); y += c.S(10);
            SysMonDraw.Row(c, x, y, w, "Power source", p.Plugged ? "AC adapter" : "Battery");
        }
    }

    [Widget("battery", "Battery", "System", "Charge level as a filling gauge, with the charging state.", RefName = "Battery", Data = DataKind.Live)]
    public class SysMonBatteryWidget : SysMonBatteryBase
    {
        public override int Measure(DrawCtx c) { return (int)(c.S(10) + c.S(26) + c.S(7) + Ui.MeasureW(c.G, "100%", c.FS(12.5f)) + c.S(10)); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var p = P;
            float x = b.X + c.S(10);
            Visual(c, new RectangleF(x, b.Y + (b.Height - c.S(13)) / 2f, c.S(26), c.S(13)), p, false);
            var tr = new RectangleF(x + c.S(33), b.Y, b.Right - x - c.S(33) - c.S(8), b.Height);
            Ui.Text(c.G, p == null ? "—" : PctText(p), c.FS(12.5f), Has(p) ? c.T.Text : c.T.TextDim, tr);
        }
        protected override float PanelVisualH { get { return 100; } }
        protected override void Visual(DrawCtx c, RectangleF r, SysMonPowerSnap p, bool big)
        {
            if (big) { float h = r.Width * 0.5f; r = new RectangleF(r.X, r.Y + (r.Height - h) / 2, r.Width, h); }
            SysMonDraw.BatteryShape(c, r, p);
        }
    }

    [Widget("battery-ring", "Battery Ring", "System", "The charge as a ring round the number; a spark runs round it while plugged in.", RefName = "Battery Ring", Data = DataKind.Live)]
    public class SysMonBatteryRingWidget : SysMonBatteryBase
    {
        // Animate (10 fps) only while actually charging; otherwise no ticking at all.
        public override int RefreshMs { get { var p = P; return Has(p) && p.Charging ? 100 : 0; } }
        public override int Measure(DrawCtx c) { return (int)(c.S(10) + c.S(28) + c.S(10)); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; float d = c.S(28);
            Visual(c, new RectangleF(b.X + (b.Width - d) / 2f, b.Y + (b.Height - d) / 2f, d, d), P, false);
        }
        protected override void Visual(DrawCtx c, RectangleF r, SysMonPowerSnap p, bool big)
        {
            double spark = -1;
            if (Has(p) && p.Charging) spark = (Environment.TickCount % 2400) / 2400.0 * 360;
            else if (Has(p) && p.Plugged) spark = p.Pct * 3.6;
            var f = big ? c.F(30, true) : c.FS(p != null && p.Pct >= 100 ? 8.5f : 10);
            SysMonDraw.BatteryRing(c, r, p, spark, big ? c.S(10) : c.S(3), f);
        }
    }

    [Widget("battery-core", "Battery Core", "System", "An energy core with twelve cells lit to the charge.", RefName = "Battery Core", Data = DataKind.Live)]
    public class SysMonBatteryCoreWidget : SysMonBatteryBase
    {
        public override int Measure(DrawCtx c) { return (int)(c.S(10) + c.S(26) + c.S(8) + Ui.MeasureW(c.G, "100%", c.FS(12.5f)) + c.S(10)); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var p = P; float d = c.S(26), x = b.X + c.S(10);
            Visual(c, new RectangleF(x, b.Y + (b.Height - d) / 2f, d, d), p, false);
            Ui.Text(c.G, p == null ? "—" : PctText(p), c.FS(12.5f), Has(p) ? c.T.Text : c.T.TextDim, new RectangleF(x + d + c.S(8), b.Y, b.Right - x - d - c.S(14), b.Height));
        }
        protected override void Visual(DrawCtx c, RectangleF r, SysMonPowerSnap p, bool big) { SysMonDraw.BatteryCore(c, r, p); }
    }

    [Widget("battery-dots", "Battery Dots", "System", "A dot-matrix battery: the number and the charge in dots.", RefName = "Battery Dots", Data = DataKind.Live)]
    public class SysMonBatteryDotsWidget : SysMonBatteryBase
    {
        const float Pitch = 3.6f; const int Cols = 10;
        string Num(SysMonPowerSnap p) { return p == null ? "--" : Has(p) ? p.Pct.ToString() : "AC"; }
        public override int Measure(DrawCtx c)
        {
            float pt = c.S(Pitch);
            return (int)(c.S(10) + SysMonDraw.DotTextW("100", pt) + c.S(9) + pt * (Cols + 1) + c.S(10));
        }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var p = P; float pt = c.S(Pitch);
            string n = Num(p);
            float x = b.X + c.S(10), nw = SysMonDraw.DotTextW("100", pt);
            float ty = b.Y + (b.Height - pt * 5) / 2f;
            var on = Has(p) ? (p.Charging ? c.T.Good : p.Pct <= 15 ? c.T.Bad : c.T.Text) : c.T.TextDim;
            var off = Ui.WithA(c.T.TextFaint, 40);
            // right-align the number in the 3-digit slot
            SysMonDraw.DotText(c.G, n, x + nw - SysMonDraw.DotTextW(n, pt), ty, pt, on, off);
            float bx = x + nw + c.S(9);
            SysMonDraw.DotBattery(c.G, bx, b.Y + (b.Height - pt * 3) / 2f, pt, Cols, p, SysMonDraw.BatColor(c.T, p, false), Ui.WithA(c.T.TextFaint, 60));
        }
        protected override float PanelVisualH { get { return 100; } }
        protected override void Visual(DrawCtx c, RectangleF r, SysMonPowerSnap p, bool big)
        {
            // big: number on top, battery dots below
            float pt = r.Width / (Cols + 1.4f);
            string n = Num(p);
            float npt = Math.Min(pt * 1.25f, r.Width / SysMonDraw.DotTextW("100", 1));
            var on = Has(p) ? (p.Charging ? c.T.Good : c.T.Text) : c.T.TextDim;
            float nw = SysMonDraw.DotTextW(n, npt);
            SysMonDraw.DotText(c.G, n, r.X + (r.Width - nw) / 2, r.Y + c.S(4), npt, on, Color.Empty);
            SysMonDraw.DotBattery(c.G, r.X + (r.Width - pt * (Cols + 1)) / 2, r.Y + c.S(4) + npt * 5 + c.S(14), pt, Cols, p, SysMonDraw.BatColor(c.T, p, false), Ui.WithA(c.T.TextFaint, 60));
        }
    }

    [Widget("battery-gauge", "Battery Gauge", "System", "A car's fuel gauge, E to F, for your battery.", RefName = "Battery Gauge", Data = DataKind.Live)]
    public class SysMonBatteryGaugeWidget : SysMonBatteryBase
    {
        public override int Measure(DrawCtx c) { return (int)(c.S(10) + c.S(40) + c.S(8) + Ui.MeasureW(c.G, "100%", c.FS(12.5f)) + c.S(10)); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var p = P; float x = b.X + c.S(10);
            Visual(c, new RectangleF(x, b.Y + c.S(5), c.S(40), c.S(24)), p, false);
            Ui.Text(c.G, p == null ? "—" : PctText(p), c.FS(12.5f), Has(p) ? c.T.Text : c.T.TextDim, new RectangleF(x + c.S(48), b.Y, b.Right - x - c.S(54), b.Height));
        }
        protected override float PanelVisualW { get { return 124; } }
        protected override float PanelVisualH { get { return 100; } }
        protected override void Visual(DrawCtx c, RectangleF r, SysMonPowerSnap p, bool big) { SysMonDraw.FuelGauge(c, r, p, big); }
    }
}
