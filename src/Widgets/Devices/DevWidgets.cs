// Device Batteries / Device Battery widgets — NovaBar version of the user's bt-battery-tray (one chip per device,
// or one tile per device like the original's one-tray-icon-per-device).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    public abstract class DevWidgetBase : Widget
    {
        protected DevSnap Snap { get { return DevHub.Snap; } }
        public override void OnStart() { DevHub.Ensure(); Hub.Subscribe(DevHub.Key, this); }
        public override void OnStop() { Hub.Unsubscribe(DevHub.Key, this); }
        public override void AddMenuItems(List<KeyValuePair<string, Action>> items)
        {
            items.Add(new KeyValuePair<string, Action>("Refresh device batteries", DevHub.Refresh));
            items.Add(new KeyValuePair<string, Action>("Bluetooth settings", () => DevHub.Open("ms-settings:bluetooth")));
        }

        protected const float RowH = 64, RowGap = 12;
        protected static float PanelH(int rows) { return 50 + Math.Max(1, rows) * RowH + (Math.Max(1, rows) - 1) * RowGap + 16 + 32 + 16; }

        protected string Subtitle(DevSnap s)
        {
            if (s == null) return "Scanning…";
            if (s.Demo) return "Sample";
            return "Updated " + Ui.Clock(s.ScanTime);
        }

        /// <summary>Panel body shared by both widgets: device rows (or an empty state) + footer buttons.</summary>
        protected void DrawList(DrawCtx c, string title, IList<DevDevice> list, DevSnap s)
        {
            var b = c.Bounds; var t = c.T;
            float y = c.PanelHeader(title, Subtitle(s), Icons.Bluetooth) + c.S(8);
            float x = b.X + c.S(16), w = b.Width - c.S(32);
            if (list.Count == 0)
            {
                var er = new RectangleF(x, y, w, c.S(RowH));
                Ui.FillRound(c.G, new RectangleF(x, y + c.S(2), c.S(38), c.S(38)), c.S(11), Ui.WithA(t.Track, t.Track.A / 2 + 10));
                Ui.Icon(c.G, Icons.Bluetooth, new RectangleF(x, y + c.S(2), c.S(38), c.S(38)), t.TextDim, c.S(16));
                float tx = x + c.S(50);
                string head = s == null ? "Looking for devices…" : "No battery info";
                string sub = s == null ? "Reading Bluetooth and Logitech receivers." :
                    s.Errors.Length > 0 ? s.Errors[0] : "No connected device reports a battery level.";
                Ui.Text(c.G, head, c.FS(13), t.Text, new RectangleF(tx, y, b.Right - c.S(16) - tx, c.S(19)));
                Ui.TextWrap(c.G, sub, c.F(11.5f), t.TextDim, new RectangleF(tx, y + c.S(21), b.Right - c.S(16) - tx, c.S(40)));
                y += c.S(RowH);
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                    {
                        float dy = y - c.S(RowGap / 2);
                        c.G.FillRectangle(Ui.B(Ui.WithA(t.TextFaint, t.Dark ? 40 : 50)), x + c.S(50), dy, w - c.S(50), Math.Max(1f, c.S(0.8f)));
                    }
                    DevDraw.Row(c, new RectangleF(x, y, w, c.S(RowH)), list[i]);
                    y += c.S(RowH + RowGap);
                }
                y -= c.S(RowGap);
            }
            y += c.S(16);
            float bw = (w - c.S(8)) / 2;
            c.Button(new RectangleF(x, y, bw, c.S(32)), "Refresh now", DevHub.Refresh, false, Icons.Refresh);
            c.Button(new RectangleF(x + bw + c.S(8), y, bw, c.S(32)), "Bluetooth settings", () => DevHub.Open("ms-settings:bluetooth"), false, Icons.Bluetooth);
        }
    }

    [Widget("devbatteries", "Device Batteries", "System", "Battery levels of your Bluetooth and Logitech wireless devices (keyboard, mouse, headset, controller).", RefName = "Device Batteries", Data = DataKind.Live)]
    public class DevBatteriesWidget : DevWidgetBase
    {
        public override SettingDef[] SettingDefs
        {
            get
            {
                return new[] {
                    SettingDef.Toggle("letters", "Show letters instead of icons", false),
                    SettingDef.Number("hideabove", "Hide devices above X% (100 = show all)", 100, 0, 100),
                };
            }
        }

        List<DevDevice> Visible(DevSnap s)
        {
            var l = new List<DevDevice>();
            if (s == null) return l;
            double hide = SNum("hideabove"); if (hide <= 0 || double.IsNaN(hide)) hide = 100;
            foreach (var d in s.Devices) if (hide >= 100 || d.Charging || d.Battery <= hide) l.Add(d);
            return l;
        }

        string Num(DevDevice d) { return d.Battery >= 100 ? null : d.Battery.ToString(); }
        float ChipW(DrawCtx c, DevDevice d, bool letters)
        {
            float gw = letters ? Ui.MeasureW(c.G, DevKinds.Letter(d.Kind), c.F(12.5f, true)) : c.S(15);
            string n = Num(d);
            float nw = n == null ? c.S(12) : Ui.MeasureW(c.G, n, c.FS(13));
            return gw + c.S(letters ? 3 : 4) + nw + (d.Charging ? c.S(7) : 0);
        }

        public override int Measure(DrawCtx c)
        {
            var s = Snap; var l = Visible(s); bool letters = SBool("letters");
            if (l.Count == 0) return (int)(c.S(10) + c.S(16) + (s == null || s.Devices.Length == 0 ? c.S(6) + Ui.MeasureW(c.G, "—", c.FS(13)) : 0) + c.S(10));
            float w = c.S(10);
            for (int i = 0; i < l.Count; i++) { if (i > 0) w += c.S(12); w += ChipW(c, l[i], letters); }
            return (int)Math.Ceiling(w + c.S(10));
        }

        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var s = Snap; var l = Visible(s); bool letters = SBool("letters");
            float x = b.X + c.S(10), cy = b.Y + b.Height / 2f;
            if (l.Count == 0)
            {
                bool none = s == null || s.Devices.Length == 0;
                if (none)
                {
                    Ui.Icon(c.G, Icons.Bluetooth, new RectangleF(x, b.Y, c.S(16), b.Height), t.TextDim, c.S(14));
                    Ui.Text(c.G, "—", c.FS(13), t.TextDim, new RectangleF(x + c.S(22), b.Y, b.Right - x - c.S(22), b.Height));
                }
                else DevDraw.Check(c.G, new RectangleF(x + c.S(2), cy - c.S(6), c.S(13), c.S(11)), t.Good, Math.Max(1.3f, c.S(1.8f))); // everything above the threshold
                return;
            }
            for (int i = 0; i < l.Count; i++)
            {
                var d = l[i];
                if (i > 0)
                {
                    c.G.FillEllipse(Ui.B(t.TextFaint), x + c.S(4.5f), cy - c.S(1.5f), c.S(3), c.S(3));
                    x += c.S(12);
                }
                var lc = DevDraw.Level(t, d);
                var fg = d.Asleep ? t.TextFaint : t.Text;
                var nc = d.Asleep ? t.TextDim : lc;
                float gw;
                if (letters)
                {
                    var lf = c.F(12.5f, true); string ch = DevKinds.Letter(d.Kind);
                    gw = Ui.MeasureW(c.G, ch, lf);
                    Ui.Text(c.G, ch, lf, fg, new RectangleF(x, b.Y, gw + c.S(2), b.Height));
                    x += gw + c.S(3);
                }
                else
                {
                    gw = c.S(15);
                    DevDraw.Glyph(c.G, d.Kind, new RectangleF(x, cy - gw / 2, gw, gw), fg, Math.Max(1.1f, c.S(1.35f)));
                    x += gw + c.S(4);
                }
                string n = Num(d);
                if (n == null) { DevDraw.Check(c.G, new RectangleF(x, cy - c.S(5), c.S(12), c.S(10)), d.Asleep ? t.TextDim : (d.Charging ? lc : t.Good), Math.Max(1.3f, c.S(1.8f))); x += c.S(12); }
                else
                {
                    var nf = c.FS(13); float nw = Ui.MeasureW(c.G, n, nf);
                    Ui.Text(c.G, n, nf, nc, new RectangleF(x, b.Y, nw + c.S(4), b.Height));
                    x += nw;
                }
                if (d.Charging) { DevDraw.Bolt(c.G, new RectangleF(x + c.S(1.5f), cy - c.S(5), c.S(6), c.S(10)), d.Asleep ? t.TextDim : lc); x += c.S(7); }
            }
        }

        public override Size PanelSize(float scale)
        {
            var s = Snap; int n = s == null ? 0 : s.Devices.Length;
            return new Size((int)(340 * scale), (int)(PanelH(n) * scale));
        }
        public override void OnPanelOpen() { var s = Snap; if (s != null && (DateTime.Now - s.ScanTime).TotalSeconds > 10) DevHub.Refresh(); }
        public override void DrawPanel(DrawCtx c)
        {
            var s = Snap;
            DrawList(c, "Device batteries", s == null ? (IList<DevDevice>)new DevDevice[0] : s.Devices, s);
        }
    }

    [Widget("devbattery", "Device Battery", "System", "Battery level of one Bluetooth or Logitech wireless device — add one per device.", RefName = "Device Battery", Data = DataKind.Live)]
    public class DevBatteryWidget : DevWidgetBase
    {
        static string[] knownCache; static int knownAt;
        public override SettingDef[] SettingDefs
        {
            get
            {
                if (knownCache == null || Environment.TickCount - knownAt > 10000) { knownCache = DevHub.KnownNames(); knownAt = Environment.TickCount; }
                var opts = new List<string> { DevHub.FirstDevice }; opts.AddRange(knownCache);
                return new[] {
                    SettingDef.Choice("device", "Device", DevHub.FirstDevice, opts.ToArray()),
                    SettingDef.Toggle("showicon", "Show device icon", true),
                };
            }
        }

        DevDevice Dev(DevSnap s) { return s == null ? null : s.Find(SStr("device")); }

        public override int Measure(DrawCtx c)
        {
            float w = c.S(10);
            if (SBool("showicon")) w += c.S(15) + c.S(6);
            w += c.S(23) + c.S(6) + Ui.MeasureW(c.G, "100%", c.FS(12.5f)) + c.S(10);
            return (int)Math.Ceiling(w);
        }

        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T; var d = Dev(Snap);
            float x = b.X + c.S(10), cy = b.Y + b.Height / 2f;
            if (SBool("showicon"))
            {
                float gw = c.S(15);
                DevDraw.Glyph(c.G, d != null ? d.Kind : DevKind.Other, new RectangleF(x, cy - gw / 2, gw, gw), d == null || d.Asleep ? t.TextDim : t.Text, Math.Max(1.1f, c.S(1.35f)));
                x += gw + c.S(6);
            }
            DevDraw.Battery(c, new RectangleF(x, cy - c.S(6), c.S(23), c.S(12)), d);
            x += c.S(23) + c.S(6);
            string txt = d == null ? "—" : d.Battery + "%";
            var col = d == null ? t.TextDim : d.Asleep ? t.TextDim : (d.Battery <= 20 && !d.Charging ? t.Bad : t.Text);
            Ui.Text(c.G, txt, c.FS(12.5f), col, new RectangleF(x, b.Y, b.Right - x - c.S(4), b.Height));
        }

        public override Size PanelSize(float scale) { return new Size((int)(340 * scale), (int)(PanelH(1) * scale)); }
        public override void OnPanelOpen() { var s = Snap; if (s != null && (DateTime.Now - s.ScanTime).TotalSeconds > 10) DevHub.Refresh(); }
        public override void DrawPanel(DrawCtx c)
        {
            var s = Snap; var d = Dev(s);
            DrawList(c, d != null ? d.Name : "Device battery", d == null ? (IList<DevDevice>)new DevDevice[0] : new[] { d }, s);
        }
    }
}
