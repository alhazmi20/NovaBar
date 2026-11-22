// Vector Wi-Fi glyph (wedge + 3 arcs = 4 signal levels), demo snapshots.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace NovaBar
{
    public static class WifiDraw
    {
        /// <summary>Draw the Wi-Fi fan. box = bounding square; level 0..4 lit elements; lit/dim colors; slash = diagonal strike.</summary>
        public static void Glyph(Graphics g, RectangleF box, int level, Color lit, Color dim, bool slash = false, Color? slashColor = null)
        {
            float O = box.Width * 0.707f;                // outer radius: fan spans 2·O·sin45 = box width, height O
            float pen = Math.Max(1.3f, O * 0.15f), gap = O * 0.095f, r0 = O - 3 * pen - 3 * gap;
            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f + O / 2f;    // fan apex; vertical extent centered in the box
            var oldMode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new GraphicsPath())
            {
                p.AddPie(cx - r0, cy - r0, r0 * 2, r0 * 2, 225, 90);
                g.FillPath(Ui.B(level >= 1 ? lit : dim), p);
            }
            for (int i = 0; i < 3; i++)
            {
                float r = r0 + gap + pen / 2f + i * (pen + gap);
                using (var pp = Ui.P(level >= i + 2 ? lit : dim, pen))
                    g.DrawArc(pp, cx - r, cy - r, r * 2, r * 2, 225, 90);
            }
            if (slash)
            {
                var sc = slashColor ?? lit;
                using (var pp = Ui.P(sc, pen * 0.9f))
                    g.DrawLine(pp, cx - O * 0.62f, cy - O * 0.98f, cx + O * 0.62f, cy + pen * 0.1f);
            }
            g.SmoothingMode = oldMode;
        }

        /// <summary>Toggle switch (pill). Returns nothing; caller registers the hit.</summary>
        public static void Switch(DrawCtx c, RectangleF r, bool on, bool hover, bool enabled = true)
        {
            var t = c.T;
            Color bg = on ? (hover ? Ui.Mix(t.Accent, Color.White, 0.12) : t.Accent) : (hover ? Ui.Mix(t.Track, t.Text, 0.12) : t.Track);
            if (!enabled) bg = Ui.WithA(bg, bg.A / 2);
            Ui.FillRound(c.G, r, r.Height / 2, bg);
            if (!on) Ui.StrokeRound(c.G, r, r.Height / 2, Ui.WithA(t.Text, 40), 1f);
            float k = r.Height - c.S(6);
            float kx = on ? r.Right - c.S(3) - k : r.X + c.S(3);
            Ui.FillCircle(c.G, new PointF(kx + k / 2, r.Y + r.Height / 2), k / 2, on ? Color.White : (t.Dark ? Ui.WithA(t.Text, 210) : Color.White));
            if (!on && !t.Dark) using (var p = new Pen(Color.FromArgb(40, 0, 0, 0), 1f)) c.G.DrawEllipse(p, kx, r.Y + c.S(3), k, k);
        }

        public static string BandsText(WifiNet n)
        {
            var l = new List<string>();
            if ((n.BandMask & 4) != 0) l.Add("2.4"); if ((n.BandMask & 32) != 0) l.Add("5"); if ((n.BandMask & 64) != 0) l.Add("6");
            if (l.Count <= 1) return BandText(n.Band);
            return string.Join(" / ", l) + " GHz";
        }
        public static string BandText(int band) { return band == 2 ? "2.4 GHz" : band == 5 ? "5 GHz" : band == 6 ? "6 GHz" : ""; }
    }

    /// <summary>NOVABAR_WIFI_DEMO=connecting|off|disconnected|password|fail|ethernet|noadapter — synthetic states for visual checks.</summary>
    public static class WifiDemo
    {
        static List<WifiNet> Nets(bool connected)
        {
            Func<string, int, bool, int, bool, int, WifiNet> n = (s, q, sec, auth, prof, band) => new WifiNet { Ssid = s, Raw = System.Text.Encoding.UTF8.GetBytes(s), Quality = q, Secured = sec, Auth = auth, HasProfile = prof, Band = band, Profile = prof ? s : "" };
            var l = new List<WifiNet>();
            var me = n("NoOne", 86, true, 7, true, 5); me.Connected = connected; l.Add(me);
            l.Add(n("Vodafone-7C21", 71, true, 7, false, 5));
            l.Add(n("NoOne_2.4G", 64, true, 7, true, 2));
            l.Add(n("Cafe Lumière Guest", 48, false, 1, false, 2));
            l.Add(n("DIRECT-HP OfficeJet", 37, true, 7, false, 2));
            l.Add(n("TP-Link_5G_A1F0", 28, true, 9, false, 5));
            l.Add(n("Neighbours", 14, true, 7, false, 2));
            return l;
        }
        public static WifiSnap Make(string mode)
        {
            var s = new WifiSnap { NetsLoaded = true, Ready = true };
            switch (mode)
            {
                case "connecting": s.State = WifiState.Connecting; s.Ssid = "NoOne"; s.ConnectingSsid = "NoOne"; s.Nets = Nets(false); break;
                case "off": s.State = WifiState.RadioOff; s.Nets = new List<WifiNet>(); s.NetsLoaded = false; break;
                case "noadapter": s.State = WifiState.NoAdapter; s.NetsLoaded = false; break;
                case "ethernet": s.State = WifiState.NoAdapter; s.NetsLoaded = false; s.Wired = true; s.WiredName = "Ethernet"; s.WiredIp = "192.168.1.40"; s.WiredMbps = 1000; s.Internet = 1; break;
                case "fail": s.State = WifiState.Disconnected; s.Nets = Nets(false); s.FailSsid = "Vodafone-7C21"; s.FailText = "Couldn't connect — check the password"; break;
                case "nointernet":
                    s.State = WifiState.Connected; s.Ssid = "NoOne"; s.Quality = 58; s.Rssi = -67; s.Band = 2; s.Channel = 6; s.Phy = "Wi-Fi 4"; s.Secured = true; s.AuthText = "WPA2";
                    s.Ip = "169.254.12.7"; s.RxMbps = 144; s.TxMbps = 72; s.Internet = 0; s.Nets = Nets(true); break;
                case "password": s.State = WifiState.Disconnected; s.Nets = Nets(false); break;
                default: s.State = WifiState.Disconnected; s.Nets = Nets(false); break;
            }
            s.Sig = mode;
            return s;
        }
    }
}
