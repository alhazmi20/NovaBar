using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>Shared tile for Top CPU / Top RAM: icon + three columns (name over value).</summary>
    public abstract class SysMonTopWidget : SysMonWidget
    {
        protected override string[] Keys { get { return new[] { "sysmon.procs" }; } }
        protected abstract bool IsCpu { get; }
        public override int Measure(DrawCtx c) { return (int)(c.S(10) + c.S(18) + c.S(8) + 3 * c.S(66) + 2 * c.S(8) + c.S(10)); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var s = SysMon.Procs;
            float x = b.X + c.S(10);
            Ui.Icon(c.G, IsCpu ? Icons.Cpu : Icons.Memory, new RectangleF(x, b.Y, c.S(18), b.Height), t.Accent, c.S(15));
            x += c.S(26);
            var list = s == null ? null : IsCpu ? s.TopCpu : s.TopRam;
            float cw = c.S(66), gap = c.S(8);
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(x + i * (cw + gap), b.Y, cw, b.Height);
                if (i > 0) c.G.FillRectangle(Ui.B(Ui.WithA(t.TextFaint, 50)), r.X - gap / 2, b.Y + c.S(9), Math.Max(1f, c.S(0.8f)), b.Height - c.S(18));
                if (list == null || i >= list.Length) { SysMonDraw.LabelValue(c, r, i == 0 ? (s == null || IsCpu ? "Measuring" : "—") : "", i == 0 ? "…" : "", t.TextDim); continue; }
                var p = list[i];
                string val = IsCpu ? (p.Cpu * 100).ToString(p.Cpu < 0.1 ? "0.0" : "0") + "%" : SysMon.MemStr(p.Mem);
                var vc = IsCpu ? (p.Cpu >= 0.5 ? t.Bad : p.Cpu >= 0.2 ? t.Warn : t.Text) : t.Text;
                SysMonDraw.LabelValue(c, r, p.Name, val, vc);
            }
        }
        public override void OnClick(MouseButtons btn)
        {
            if (btn != MouseButtons.Left) return;
            ThreadPool.QueueUserWorkItem(_ => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch { } });
        }
    }

    [Widget("topcpu", "Top CPU", "System", "The three apps using the most CPU right now; a click opens Task Manager.", RefName = "Top CPU", Data = DataKind.Live)]
    public class SysMonTopCpuWidget : SysMonTopWidget { protected override bool IsCpu { get { return true; } } }

    [Widget("topram", "Top RAM", "System", "The three apps holding the most memory, added up as Task Manager does.", RefName = "Top RAM", Data = DataKind.Live)]
    public class SysMonTopRamWidget : SysMonTopWidget { protected override bool IsCpu { get { return false; } } }

    [Widget("temperature", "Temperature", "System", "CPU temperature as a gauge, with CPU and memory load beside it.", RefName = "Temperature", Data = DataKind.Live)]
    public class SysMonTempWidget : SysMonWidget
    {
        protected override string[] Keys { get { return new[] { "sysmon.temp", "sysmon.cpu", "sysmon.mem" }; } }
        public override int Measure(DrawCtx c) { return (int)(c.S(10) + c.S(34) + c.S(10) + Ui.MeasureW(c.G, "MEM 100%", c.F(10.5f)) + c.S(10)); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var ts = SysMon.Temp; var cpu = SysMon.Cpu; var mem = SysMon.Mem;
            double tc = ts != null ? ts.C : double.NaN;
            float x = b.X + c.S(10), gw = c.S(34);
            var gr = new RectangleF(x, b.Y + c.S(7), gw, c.S(17));
            SysMonDraw.TempGauge(c, gr, tc, c.S(3.2f));
            Ui.Text(c.G, SysMon.TempStr(tc), c.FS(12), double.IsNaN(tc) ? t.TextDim : t.Text, new RectangleF(x - c.S(4), b.Y + c.S(14), gw + c.S(8), c.S(16)), 0);
            float tx = x + gw + c.S(10), tw = b.Right - c.S(10) - tx;
            float mid = b.Y + b.Height / 2f;
            MiniRow(c, new RectangleF(tx, mid - c.S(13), tw, c.S(12)), "CPU", cpu != null ? cpu.Load : -1);
            MiniRow(c, new RectangleF(tx, mid + c.S(1), tw, c.S(12)), "MEM", mem != null ? mem.Load : -1);
        }
        static void MiniRow(DrawCtx c, RectangleF r, string label, double v)
        {
            Ui.Text(c.G, label, c.F(9.5f), c.T.TextDim, r);
            Ui.Text(c.G, v < 0 ? "—" : Ui.Pct(v), c.FS(10.5f), v < 0 ? c.T.TextDim : c.T.Load(v), r, 1);
        }

        public override Size PanelSize(float scale) { return new Size((int)(320 * scale), (int)(262 * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var ts = SysMon.Temp; var cpu = SysMon.Cpu; var mem = SysMon.Mem;
            double tc = ts != null ? ts.C : double.NaN;
            float y = c.PanelHeader("Temperature", null, Icons.Temperature);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            var gr = new RectangleF(x + c.S(6), y + c.S(6), c.S(150), c.S(75));
            SysMonDraw.TempGauge(c, gr, tc, c.S(11));
            Ui.Text(c.G, SysMon.TempStr(tc), c.F(28, true), double.IsNaN(tc) ? t.TextDim : t.Text, new RectangleF(gr.X, gr.Bottom - c.S(38), gr.Width, c.S(36)), 0);
            Ui.Text(c.G, double.IsNaN(tc) ? "No sensor" : (SysMon.Imperial ? "Fahrenheit" : "Celsius"), c.F(10.5f), t.TextDim, new RectangleF(gr.X, gr.Bottom + c.S(2), gr.Width, c.S(14)), 0);
            // load rings on the right
            float rx = gr.Right + c.S(20), rw = b.Right - c.S(16) - rx, d = c.S(44);
            DrawLoad(c, new RectangleF(rx, y + c.S(2), rw, d), "CPU", cpu != null ? cpu.Load : -1);
            DrawLoad(c, new RectangleF(rx, y + c.S(2) + d + c.S(10), rw, d), "Memory", mem != null ? mem.Load : -1);
            y = gr.Bottom + c.S(26);
            SysMonDraw.Divider(c, x, y, w); y += c.S(10);
            if (ts == null) Ui.Text(c.G, "Reading sensors…", c.F(11.5f), t.TextDim, new RectangleF(x, y, w, c.S(18)));
            else if (ts.Ok)
            {
                SysMonDraw.Row(c, x, y, w, "Source", ts.Source); y += c.S(22);
                string state = tc >= 85 ? "Hot" : tc >= 70 ? "Warm" : "Normal";
                SysMonDraw.Row(c, x, y, w, "Status", state, SysMonDraw.TempColor(t, tc));
            }
            else
            {
                Ui.TextWrap(c.G, "Windows doesn't expose this CPU's temperature. Run LibreHardwareMonitor (free) or HWiNFO with Shared Memory on, and it shows here automatically.", c.F(11), t.TextDim, new RectangleF(x, y, w, c.S(44)));
                c.Button(new RectangleF(x, y + c.S(46), c.S(190), c.S(28)), "Get LibreHardwareMonitor", () => Ctl.Open("https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/latest"), false, Icons.Download);
            }
        }
        static void DrawLoad(DrawCtx c, RectangleF r, string label, double v)
        {
            var rr = new RectangleF(r.X, r.Y, r.Height, r.Height);
            Ui.Ring(c.G, rr, Math.Max(0, v), c.T.Load(Math.Max(0, v)), c.T.Track, c.S(5));
            Ui.Text(c.G, v < 0 ? "—" : Math.Round(v * 100).ToString(), c.FS(11.5f), c.T.Text, rr, 0);
            SysMonDraw.LabelValue(c, new RectangleF(rr.Right + c.S(10), r.Y, r.Right - rr.Right - c.S(10), r.Height), label, v < 0 ? "—" : Ui.Pct(v), c.T.Text);
        }
    }
}
