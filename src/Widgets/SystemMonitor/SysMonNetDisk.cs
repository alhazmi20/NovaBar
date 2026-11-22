using System;
using System.Drawing;
using System.Linq;

namespace NovaBar
{
    [Widget("network", "Network", "System", "Download and upload speed with a live chart.", RefName = "Network", Data = DataKind.Live)]
    public class SysMonNetWidget : SysMonWidget
    {
        readonly double[] down = new double[SysMon.HistLen], up = new double[SysMon.HistLen];
        readonly double[] downS = new double[24], upS = new double[24];
        protected override string[] Keys { get { return new[] { "sysmon.net" }; } }
        Color UpColor(Theme t) { return t.Series[1]; }

        public override int Measure(DrawCtx c)
        {
            return (int)(c.S(10) + c.S(12) + c.S(4) + Ui.MeasureW(c.G, "888 KB/s", c.FS(11.5f)) + c.S(10) + c.S(48) + c.S(10));
        }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var s = SysMon.Net;
            float x = b.X + c.S(10), mid = b.Y + b.Height / 2f;
            float tw = Ui.MeasureW(c.G, "888 KB/s", c.FS(11.5f));
            var r1 = new RectangleF(x, mid - c.S(14), c.S(12), c.S(13)); var r2 = new RectangleF(x, mid + c.S(1), c.S(12), c.S(13));
            Ui.Icon(c.G, "", r1, t.Accent, c.S(10));   // arrow down
            Ui.Icon(c.G, "", r2, UpColor(t), c.S(10)); // arrow up
            Ui.Text(c.G, s != null ? SysMon.RateShort(s.Down) : "—", c.FS(11.5f), t.Text, new RectangleF(r1.Right + c.S(4), r1.Y, tw + c.S(2), r1.Height));
            Ui.Text(c.G, s != null ? SysMon.RateShort(s.Up) : "—", c.FS(11.5f), t.TextDim, new RectangleF(r2.Right + c.S(4), r2.Y, tw + c.S(2), r2.Height));
            // mini chart: last 24 s, shared scale
            var cr = new RectangleF(b.Right - c.S(10) - c.S(48), b.Y + c.S(8), c.S(48), b.Height - c.S(16));
            double m1 = SysMon.DownHist.CopyTo(downS), m2 = SysMon.UpHist.CopyTo(upS), mx = Math.Max(Math.Max(m1, m2) * 1.15, 16 * 1024);
            c.G.FillRectangle(Ui.B(Ui.WithA(t.TextFaint, 45)), cr.X, cr.Bottom, cr.Width, Math.Max(1f, c.S(0.8f)));
            int n = SysMon.DownHist.Count;
            SysMonDraw.ChartN(c, cr, downS, n, mx, t.Accent, true, 1.4f, false);
            SysMonDraw.ChartN(c, cr, upS, n, mx, UpColor(t), false, 1.2f, false);
        }

        public override Size PanelSize(float scale) { return new Size((int)(340 * scale), (int)(318 * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var s = SysMon.Net;
            float y = c.PanelHeader("Network", s != null ? s.Kind : null, Icons.Network);
            float x = b.X + c.S(16), w = b.Width - c.S(32), hw = (w - c.S(10)) / 2;
            // big rates
            BigRate(c, new RectangleF(x, y, hw, c.S(44)), "", "Download", s != null ? s.Down : -1, t.Accent);
            BigRate(c, new RectangleF(x + hw + c.S(10), y, hw, c.S(44)), "", "Upload", s != null ? s.Up : -1, UpColor(t));
            y += c.S(52);
            var card = new RectangleF(x, y, w, c.S(110));
            SysMonDraw.Card(c, card);
            double m1 = SysMon.DownHist.CopyTo(down), m2 = SysMon.UpHist.CopyTo(up), mx = NiceMax(Math.Max(m1, m2) * 1.1);
            Ui.Text(c.G, "Last 60 seconds", c.F(10), t.TextDim, new RectangleF(card.X + c.S(10), card.Y + c.S(5), c.S(140), c.S(14)));
            Ui.Text(c.G, SysMon.RateShort(mx), c.F(9.5f), t.TextFaint, new RectangleF(card.Right - c.S(110), card.Y + c.S(5), c.S(100), c.S(14)), 1);
            var cr = new RectangleF(card.X + c.S(10), card.Y + c.S(22), card.Width - c.S(20), card.Height - c.S(30));
            SysMonDraw.ChartN(c, cr, down, SysMon.DownHist.Count, mx, t.Accent, true, 1.8f);
            SysMonDraw.ChartN(c, cr, up, SysMon.UpHist.Count, mx, UpColor(t), true, 1.5f, false);
            y = card.Bottom + c.S(12);
            if (s == null) return;
            SysMonDraw.Row(c, x, y, w, "Adapter", s.Adapter); y += c.S(22);
            SysMonDraw.Row(c, x, y, w, "IPv4 address", s.Ip); y += c.S(22);
            SysMonDraw.Row(c, x, y, w, "Received since boot", Ui.Bytes(s.TotalRx)); y += c.S(22);
            SysMonDraw.Row(c, x, y, w, "Sent since boot", Ui.Bytes(s.TotalTx));
        }
        static double NiceMax(double v)
        {
            v = Math.Max(v, 64 * 1024);
            double p = Math.Pow(2, Math.Ceiling(Math.Log(v, 2)));
            return p * 0.75 >= v ? p * 0.75 : p;
        }
        static void BigRate(DrawCtx c, RectangleF r, string glyph, string label, double v, Color col)
        {
            SysMonDraw.Card(c, r);
            var ir = new RectangleF(r.X + c.S(8), r.Y + (r.Height - c.S(26)) / 2, c.S(26), c.S(26));
            Ui.FillRound(c.G, ir, c.S(13), Ui.WithA(col, 40));
            Ui.Icon(c.G, glyph, ir, col, c.S(12));
            var tr = new RectangleF(ir.Right + c.S(8), r.Y, r.Right - ir.Right - c.S(14), r.Height);
            Ui.Text(c.G, label, c.F(10), c.T.TextDim, new RectangleF(tr.X, tr.Y + c.S(6), tr.Width, c.S(13)));
            Ui.Text(c.G, v < 0 ? "—" : SysMon.RateShort(v), c.FS(14), c.T.Text, new RectangleF(tr.X, tr.Y + c.S(20), tr.Width, c.S(18)));
        }
    }

    [Widget("storage", "Storage", "Apps & Files", "Free space on your drive, with a usage bar.", RefName = "Storage", Data = DataKind.Live)]
    public class SysMonStorageWidget : SysMonWidget
    {
        protected override string[] Keys { get { return new[] { "sysmon.disk" }; } }
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Text("drive", "Drive shown on the bar", "C:") }; } }

        SysMonDrive Main(SysMonDiskSnap s)
        {
            if (s == null || s.Drives.Length == 0) return null;
            string want = (SStr("drive") ?? "C:").Trim().TrimEnd('\\').ToUpperInvariant();
            if (want.Length == 1) want += ":";
            return s.Drives.FirstOrDefault(d => d.Name.ToUpperInvariant() == want) ?? s.Drives[0];
        }
        static Color UseColor(Theme t, double f) { return f >= 0.92 ? t.Bad : f >= 0.8 ? t.Warn : t.Accent; }

        public override int Measure(DrawCtx c) { return (int)(c.S(10) + c.S(18) + c.S(8) + c.S(96) + c.S(10)); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var d = Main(SysMon.Disk);
            float x = b.X + c.S(10), mid = b.Y + b.Height / 2f;
            Ui.Icon(c.G, Icons.Storage, new RectangleF(x, b.Y, c.S(18), b.Height), t.Accent, c.S(15));
            float tx = x + c.S(26), tw = b.Right - c.S(10) - tx;
            var tr = new RectangleF(tx, mid - c.S(12), tw, c.S(15));
            if (d == null) { Ui.Text(c.G, "—", c.FS(12), t.TextDim, tr); return; }
            double used = 1 - d.Free / (double)Math.Max(1, d.Total);
            Ui.Text(c.G, SysMon.Gb(d.Free) + " free", c.FS(12), t.Text, tr);
            Ui.Text(c.G, d.Name, c.F(9.5f), t.TextDim, tr, 1);
            Ui.Progress(c.G, new RectangleF(tx, mid + c.S(6), tw, c.S(4)), used, UseColor(t, used), t.Track);
        }

        public override Size PanelSize(float scale)
        {
            var s = SysMon.Disk; int n = s == null ? 1 : Math.Max(1, s.Drives.Length);
            return new Size((int)(320 * scale), (int)((58 + n * 62 + 6) * scale));
        }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var s = SysMon.Disk;
            float y = c.PanelHeader("Storage", s != null ? s.Drives.Length + (s.Drives.Length == 1 ? " drive" : " drives") : null, Icons.Storage);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            if (s == null) { Ui.Text(c.G, "Reading drives…", c.F(11.5f), t.TextDim, new RectangleF(x, y, w, c.S(18))); return; }
            foreach (var d in s.Drives)
            {
                var r = new RectangleF(x, y, w, c.S(54));
                SysMonDraw.Card(c, r);
                double used = 1 - d.Free / (double)Math.Max(1, d.Total);
                var ir = new RectangleF(r.X + c.S(10), r.Y + c.S(10), c.S(34), c.S(34));
                Ui.FillRound(c.G, ir, c.S(9), Ui.WithA(t.Accent, 36));
                Ui.Icon(c.G, Icons.Storage, ir, t.Accent, c.S(16));
                float tx = ir.Right + c.S(10), tw = r.Right - c.S(12) - tx;
                Ui.Text(c.G, d.Label + " (" + d.Name + ")", c.FS(12.5f), t.Text, new RectangleF(tx, r.Y + c.S(8), tw * 0.62f, c.S(17)));
                Ui.Text(c.G, Ui.Pct(used) + " used", c.F(10.5f), UseColor(t, used) == t.Accent ? t.TextDim : UseColor(t, used), new RectangleF(tx + tw * 0.5f, r.Y + c.S(8), tw * 0.5f, c.S(17)), 1);
                Ui.Progress(c.G, new RectangleF(tx, r.Y + c.S(28), tw, c.S(5)), used, UseColor(t, used), t.Track);
                Ui.Text(c.G, SysMon.Gb(d.Free) + " free of " + SysMon.Gb(d.Total), c.F(10), t.TextDim, new RectangleF(tx, r.Y + c.S(35), tw, c.S(14)));
                y += c.S(62);
            }
        }
    }
}
