using System;
using System.Drawing;
using System.Linq;

namespace NovaBar
{
    [Widget("cpu", "CPU", "System", "Processor load as a ring.", RefName = "CPU", Data = DataKind.Live)]
    public class SysMonCpuWidget : SysMonWidget
    {
        readonly double[] hist = new double[SysMon.HistLen];
        bool panelOpen;
        protected override string[] Keys { get { return new[] { "sysmon.cpu" }; } }

        public override int Measure(DrawCtx c) { return SysMonDraw.RingTileW(c, "CPU"); }
        public override void Draw(DrawCtx c)
        {
            var s = SysMon.Cpu;
            double v = s != null ? s.Load : 0;
            SysMonDraw.RingTile(c, v, s != null, "CPU", Ui.Pct(v), c.T.Load(v));
        }

        public override Size PanelSize(float scale) { return new Size((int)(340 * scale), (int)(332 * scale)); }
        public override void OnPanelOpen() { panelOpen = true; SysMon.Ensure(); Hub.Subscribe("sysmon.cores", this); }
        public override void OnPanelClose() { panelOpen = false; Hub.Unsubscribe("sysmon.cores", this); SysMon.ResetCores(); }
        public override void OnStop() { base.OnStop(); if (panelOpen) SysMon.ResetCores(); }

        public override void DrawPanel(DrawCtx c)
        {
            var s = SysMon.Cpu; var b = c.Bounds; var t = c.T;
            double v = s != null ? s.Load : 0;
            float y = c.PanelHeader("CPU", null, Icons.Cpu);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            // big value + name
            Ui.Text(c.G, s != null ? Ui.Pct(v) : "—", c.F(30, true), t.Text, new RectangleF(x, y - c.S(2), c.S(110), c.S(38)));
            Ui.Text(c.G, SysMon.CpuName, c.F(11.5f), t.TextDim, new RectangleF(x + c.S(100), y + c.S(2), w - c.S(100), c.S(16)), 1);
            Ui.Text(c.G, Environment.ProcessorCount + " logical processors", c.F(10.5f), t.TextFaint, new RectangleF(x + c.S(100), y + c.S(18), w - c.S(100), c.S(15)), 1);
            y += c.S(44);
            // history
            var card = new RectangleF(x, y, w, c.S(92));
            SysMonDraw.Card(c, card);
            SysMon.CpuHist.CopyTo(hist);
            var cr = new RectangleF(card.X + c.S(10), card.Y + c.S(20), card.Width - c.S(20), card.Height - c.S(28));
            Ui.Text(c.G, "Last 60 seconds", c.F(10), t.TextDim, new RectangleF(card.X + c.S(10), card.Y + c.S(5), c.S(140), c.S(14)));
            Ui.Text(c.G, "100%", c.F(9.5f), t.TextFaint, new RectangleF(card.Right - c.S(60), card.Y + c.S(5), c.S(50), c.S(14)), 1);
            SysMonDraw.ChartN(c, cr, hist, SysMon.CpuHist.Count, 1.0, t.Load(v));
            y = card.Bottom + c.S(12);
            // per core
            var cores = SysMon.Cores;
            Ui.Text(c.G, "Per core", c.F(10.5f), t.TextDim, new RectangleF(x, y, c.S(120), c.S(14)));
            y += c.S(18);
            var br = new RectangleF(x, y, w, c.S(58));
            int n = cores != null ? cores.Loads.Length : Environment.ProcessorCount;
            float gap = n > 24 ? c.S(2) : c.S(3.5f), bw = (br.Width - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                var col = new RectangleF(br.X + i * (bw + gap), br.Y, bw, br.Height);
                float rad = Math.Min(bw / 2, c.S(3));
                Ui.FillRound(c.G, col, rad, t.Track);
                if (cores != null)
                {
                    double l = cores.Loads[i]; float h = Math.Max(bw > rad * 2 ? rad * 2 : 2, (float)(l * col.Height));
                    Ui.FillRound(c.G, new RectangleF(col.X, col.Bottom - h, col.Width, h), rad, t.Load(l));
                }
            }
            y = br.Bottom + c.S(14);
            SysMonDraw.Divider(c, x, y, w); y += c.S(10);
            float cw = w / 3;
            int hn = Math.Max(1, Math.Min(SysMon.CpuHist.Count, hist.Length)); double pk = 0, sum = 0;
            for (int i = hist.Length - hn; i < hist.Length; i++) { pk = Math.Max(pk, hist[i]); sum += hist[i]; }
            SysMonDraw.Stat(c, new RectangleF(x, y, cw, c.S(34)), "Uptime", SysMon.Uptime());
            SysMonDraw.Stat(c, new RectangleF(x + cw, y, cw, c.S(34)), "Peak (60s)", Ui.Pct(pk), null, 0);
            SysMonDraw.Stat(c, new RectangleF(x + 2 * cw, y, cw, c.S(34)), "Average", Ui.Pct(sum / hn), null, 1);
        }
    }

    [Widget("ram", "RAM", "System", "Memory in use as a ring.", RefName = "RAM", Data = DataKind.Live)]
    public class SysMonRamWidget : SysMonWidget
    {
        readonly double[] hist = new double[SysMon.HistLen];
        protected override string[] Keys { get { return new[] { "sysmon.mem" }; } }

        public override int Measure(DrawCtx c) { return SysMonDraw.RingTileW(c, "RAM"); }
        public override void Draw(DrawCtx c)
        {
            var s = SysMon.Mem;
            double v = s != null ? s.Load : 0;
            SysMonDraw.RingTile(c, v, s != null, "RAM", Ui.Pct(v), c.T.Load(v));
        }

        public override Size PanelSize(float scale) { return new Size((int)(330 * scale), (int)(286 * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            var s = SysMon.Mem; var b = c.Bounds; var t = c.T;
            float y = c.PanelHeader("Memory", s != null ? SysMon.Gb(s.Total) + " installed" : null, Icons.Memory);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            double v = s != null ? s.Load : 0;
            // ring
            float d = c.S(104);
            var rr = new RectangleF(x + c.S(2), y + c.S(4), d, d);
            Ui.Ring(c.G, rr, v, t.Load(v), t.Track, c.S(10));
            Ui.Text(c.G, s != null ? Ui.Pct(v) : "—", c.F(22, true), t.Text, new RectangleF(rr.X, rr.Y + d / 2 - c.S(17), d, c.S(28)), 0);
            Ui.Text(c.G, "in use", c.F(10.5f), t.TextDim, new RectangleF(rr.X, rr.Y + d / 2 + c.S(9), d, c.S(14)), 0);
            // stats
            float sx = rr.Right + c.S(18), sw = b.Right - c.S(16) - sx, sy = y + c.S(6);
            if (s != null)
            {
                SysMonDraw.Row(c, sx, sy, sw, "Used", SysMon.Gb(s.Total - s.Avail)); sy += c.S(25);
                SysMonDraw.Row(c, sx, sy, sw, "Available", SysMon.Gb(s.Avail), t.Good); sy += c.S(25);
                SysMonDraw.Row(c, sx, sy, sw, "Total", SysMon.Gb(s.Total)); sy += c.S(25);
                SysMonDraw.Row(c, sx, sy, sw, "Committed", SysMon.Gb(s.CommitUsed).Replace(" GB", "") + " / " + SysMon.Gb(s.CommitLimit));
            }
            y = rr.Bottom + c.S(14);
            var card = new RectangleF(x, y, w, c.S(100));
            SysMonDraw.Card(c, card);
            SysMon.MemHist.CopyTo(hist);
            Ui.Text(c.G, "Last 60 seconds", c.F(10), t.TextDim, new RectangleF(card.X + c.S(10), card.Y + c.S(5), c.S(140), c.S(14)));
            Ui.Text(c.G, "100%", c.F(9.5f), t.TextFaint, new RectangleF(card.Right - c.S(60), card.Y + c.S(5), c.S(50), c.S(14)), 1);
            SysMonDraw.ChartN(c, new RectangleF(card.X + c.S(10), card.Y + c.S(20), card.Width - c.S(20), card.Height - c.S(28)), hist, SysMon.MemHist.Count, 1.0, t.Load(v));
        }
    }
}
