// Wi-Fi widget: tile with hand-drawn signal fan + SSID; panel with radio toggle, current network, nearby networks, connect/password flow.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    [Widget("wifi", "Wi-Fi", "System", "Wi-Fi status, signal and nearby networks — connect, disconnect and toggle Wi-Fi.", Data = DataKind.Live)]
    public class WifiWidget : Widget
    {
        const string EthGlyph = "";
        int frame;                       // connecting animation step
        bool panelOpen;
        string expandSsid;               // row with the inline password field
        string passText = "";
        int passGen;                     // bump to clear the text box (Initial must change)
        float scroll;                    // list scroll offset (device px)
        bool demoExpanded;

        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Toggle("showName", "Show network name", true) }; } }

        public override void OnStart() { WifiSvc.Attach(this); }
        public override void OnStop() { WifiSvc.Detach(this); }
        public override void OnPanelOpen() { panelOpen = true; scroll = 0; WifiSvc.PanelOpened(this); }
        public override void OnPanelClose() { panelOpen = false; expandSsid = null; passText = ""; WifiSvc.PanelClosed(this); }

        static WifiSnap Snap { get { return WifiSvc.Snap; } }

        // animate only while connecting (tile) — otherwise no ticking at all
        public override int RefreshMs { get { return Snap.State == WifiState.Connecting ? 300 : 0; } }
        public override void OnTick() { frame++; }

        // ======================= tile =======================
        string TileText(WifiSnap s)
        {
            switch (s.State)
            {
                case WifiState.Connected: return s.Ssid.Length > 0 ? s.Ssid : "Connected";
                case WifiState.Connecting: return "Connecting…";
                case WifiState.Disconnected: return s.Wired ? "Ethernet" : "Not connected";
                case WifiState.RadioOff: return s.Wired ? "Ethernet" : "Wi-Fi off";
                default: return !s.Ready ? "Wi-Fi" : s.Wired ? "Ethernet" : "No Wi-Fi";
            }
        }
        bool ShowsEthernet(WifiSnap s) { return s.Wired && (s.State == WifiState.NoAdapter || s.State == WifiState.RadioOff || s.State == WifiState.Disconnected); }

        public override int Measure(DrawCtx c)
        {
            var s = Snap;
            if (!SBool("showName")) return c.Si(38);
            int tw = Math.Min(Ui.MeasureW(c.G, TileText(s), c.FS(12.5f)), c.Si(120));
            return c.Si(10 + 20 + 8 + 10) + tw;
        }

        public override void Draw(DrawCtx c)
        {
            var s = Snap; var b = c.Bounds; var t = c.T;
            bool name = SBool("showName");
            float gs = c.S(19);
            float gx = name ? b.X + c.S(10) : b.X + (b.Width - gs) / 2f;
            var box = new RectangleF(gx, b.Y + (b.Height - gs) / 2f, gs, gs);
            DrawStateGlyph(c, s, box, true);
            if (name)
            {
                var tr = new RectangleF(box.Right + c.S(8), b.Y, b.Right - box.Right - c.S(8) - c.S(8), b.Height);
                bool dimText = s.State != WifiState.Connected && !ShowsEthernet(s);
                Ui.Text(c.G, TileText(s), c.FS(12.5f), dimText ? t.TextDim : t.Text, tr);
            }
        }

        /// <summary>Glyph for the current state (tile and panel card).</summary>
        void DrawStateGlyph(DrawCtx c, WifiSnap s, RectangleF box, bool tile)
        {
            var t = c.T;
            Color dim = Ui.WithA(t.Text, t.Dark ? 52 : 80);
            if (ShowsEthernet(s)) { Ui.Icon(c.G, EthGlyph, box, t.Text, box.Width * 0.86f); return; }
            switch (s.State)
            {
                case WifiState.Connected:
                    WifiDraw.Glyph(c.G, box, Math.Max(1, s.Bars), t.Text, dim);
                    if (s.Internet == 0)
                    {
                        // small "no internet" badge (amber dot with ring) at the upper-right of the fan
                        float r = box.Width * 0.2f; var pc = new PointF(box.Right - r * 0.4f, box.Y + box.Height * 0.2f + r * 0.2f);
                        Ui.FillCircle(c.G, pc, r + c.S(1.2f), tile ? (t.IsGlass ? Color.FromArgb(160, 0, 0, 0) : t.Tile) : t.PanelBg);
                        Ui.FillCircle(c.G, pc, r, t.Warn);
                    }
                    break;
                case WifiState.Connecting:
                    WifiDraw.Glyph(c.G, box, 1 + frame % 4, t.Accent, dim);
                    break;
                case WifiState.Disconnected:
                    WifiDraw.Glyph(c.G, box, 0, t.Text, Ui.WithA(t.Text, t.Dark ? 70 : 92), true, t.TextDim);
                    break;
                case WifiState.RadioOff:
                    WifiDraw.Glyph(c.G, box, 0, t.Text, Ui.WithA(t.Text, t.Dark ? 40 : 66), true, Ui.Mix(t.TextFaint, t.TextDim, 0.5));
                    break;
                default:
                    WifiDraw.Glyph(c.G, box, 0, t.Text, Ui.WithA(t.Text, s.Ready ? (t.Dark ? 40 : 66) : 70), s.Ready, t.TextFaint);
                    break;
            }
        }

        // ======================= right-click =======================
        public override void AddMenuItems(List<KeyValuePair<string, Action>> items)
        {
            var s = Snap;
            if (s.State == WifiState.Connected) items.Add(new KeyValuePair<string, Action>("Disconnect from " + Trunc(s.Ssid, 28), () => WifiSvc.Disconnect()));
            items.Add(new KeyValuePair<string, Action>("Wi-Fi settings", () => WifiSvc.OpenUri("ms-settings:network-wifi")));
            if (s.State != WifiState.NoAdapter && !s.HwOff)
            {
                bool on = s.State != WifiState.RadioOff;
                items.Add(new KeyValuePair<string, Action>(on ? "Turn Wi-Fi off" : "Turn Wi-Fi on", () => WifiSvc.SetRadio(!on)));
            }
        }
        static string Trunc(string s, int n) { return s.Length > n ? s.Substring(0, n - 1) + "…" : s; }

        // ======================= panel =======================
        public override Size PanelSize(float sc) { return new Size((int)(340 * sc), (int)(452 * sc)); }

        public override void DrawPanel(DrawCtx c)
        {
            var s = Snap; var b = c.Bounds; var t = c.T; var g = c.G;
            if (WifiSvc.Demo == "password" && !demoExpanded) { demoExpanded = true; expandSsid = "Vodafone-7C21"; }
            float L = b.X + c.S(16), R = b.Right - c.S(16), W = R - L;

            // ---- header: title + radio switch ----
            float hy = b.Y + c.S(14);
            Ui.Text(g, "Wi-Fi", c.FS(15), t.Text, new RectangleF(L, hy, W, c.S(22)));
            bool hasRadio = s.State != WifiState.NoAdapter;
            if (hasRadio)
            {
                bool on = s.State != WifiState.RadioOff;
                var sr = new RectangleF(R - c.S(40), hy + c.S(1), c.S(40), c.S(21));
                var hitR = new RectangleF(sr.X - c.S(70), sr.Y - c.S(4), sr.Width + c.S(74), sr.Height + c.S(8));
                bool hv = c.IsHover(hitR);
                WifiDraw.Switch(c, sr, on, hv, !s.HwOff);
                Ui.Text(g, s.HwOff ? "Hardware off" : on ? "On" : "Off", c.F(12), t.TextDim, new RectangleF(sr.X - c.S(80), hy, c.S(72), c.S(22)), 1);
                if (!s.HwOff) c.Hit(hitR, () => { WifiSvc.SetRadio(!on); });
            }
            float y = hy + c.S(34);

            // ---- current network card ----
            y = DrawCard(c, s, new RectangleF(L, y, W, 0));
            y += c.S(16);

            // ---- footer ----
            float fh = c.S(32), fy = b.Bottom - c.S(14) - fh;
            float rw = c.S(96);
            c.Button(new RectangleF(L, fy, W - rw - c.S(8), fh), "Network settings", () => { Host.ClosePanel(); WifiSvc.OpenUri("ms-settings:network-wifi"); }, false, Icons.Settings);
            c.Button(new RectangleF(R - rw, fy, rw, fh), s.Scanning ? "Scanning" : "Refresh", () => { WifiSvc.Scan(); WifiSvc.Request(0, true); }, false, Icons.Refresh);

            // ---- list ----
            float listBottom = fy - c.S(12);
            if (s.State == WifiState.RadioOff || s.State == WifiState.NoAdapter)
            {
                DrawEmpty(c, s, new RectangleF(L, y, W, listBottom - y));
                return;
            }
            Ui.Text(g, "Available networks", c.FS(11.5f), t.TextDim, new RectangleF(L + c.S(2), y, W, c.S(16)));
            string right = s.Scanning ? "Scanning…" : s.NetsLoaded ? s.Nets.Count + (s.Nets.Count == 1 ? " network" : " networks") : "";
            Ui.Text(g, right, c.F(11), t.TextFaint, new RectangleF(L, y, W - c.S(2), c.S(16)), 1);
            y += c.S(22);
            DrawList(c, s, new RectangleF(L - c.S(6), y, W + c.S(12), listBottom - y));
        }

        float DrawCard(DrawCtx c, WifiSnap s, RectangleF area)
        {
            var t = c.T; var g = c.G;
            bool connected = s.State == WifiState.Connected, connecting = s.State == WifiState.Connecting, eth = ShowsEthernet(s) && !connected;
            float h = connected ? c.S(150) : c.S(66);
            var r = new RectangleF(area.X, area.Y, area.Width, h);
            Ui.FillRound(g, r, c.S(14), t.Tile);
            Ui.StrokeRound(g, r, c.S(14), t.TileBorder);

            // icon chip
            var chip = new RectangleF(r.X + c.S(12), r.Y + c.S(12), c.S(42), c.S(42));
            bool active = connected || connecting || eth;
            Ui.FillRound(g, chip, c.S(12), active ? t.AccentA(t.Dark ? 46 : 30) : Ui.WithA(t.Text, 14));
            var gl = Ui.Inset(chip, c.S(10), c.S(10));
            if (connected && !eth)
            {
                Color dim = Ui.WithA(t.Accent, 70);
                WifiDraw.Glyph(g, gl, Math.Max(1, s.Bars), t.Accent, dim);
            }
            else if (eth) Ui.Icon(g, EthGlyph, chip, t.Accent, c.S(19));
            else DrawStateGlyph(c, s, gl, false);

            float tx = chip.Right + c.S(12);
            string title, sub; Color subC = t.TextDim; Color dot = Color.Empty;
            if (connected) { title = s.Ssid.Length > 0 ? s.Ssid : "Connected"; sub = s.Internet == 0 ? "No internet access" : s.Internet == 1 ? "Connected · Internet" : "Connected"; dot = s.Internet == 0 ? t.Warn : t.Good; }
            else if (connecting) { title = s.Ssid.Length > 0 ? s.Ssid : "Wi-Fi"; sub = "Connecting…"; subC = t.Accent; }
            else if (eth) { title = s.WiredName.Length > 0 ? s.WiredName : "Ethernet"; sub = (s.Internet == 0 ? "No internet" : "Connected") + (s.WiredIp.Length > 0 ? " · " + s.WiredIp : "") + (s.WiredMbps > 0 ? " · " + SpeedText(s.WiredMbps) : ""); dot = s.Internet == 0 ? t.Warn : t.Good; }
            else if (s.State == WifiState.RadioOff) { title = "Wi-Fi is off"; sub = s.HwOff ? "Turned off by a hardware switch" : "Turn it on to see networks"; }
            else if (s.State == WifiState.NoAdapter) { title = s.Ready ? "No Wi-Fi adapter" : "Wi-Fi"; sub = s.Ready ? "No wireless adapter was found" : "Loading…"; }
            else { title = "Not connected"; sub = "Choose a network below"; }

            float btnW = connected ? c.S(92) : 0;
            Ui.Text(g, title, c.FS(14.5f), t.Text, new RectangleF(tx, chip.Y + c.S(1), r.Right - tx - c.S(12) - btnW, c.S(22)));
            float sx = tx;
            if (dot != Color.Empty) { Ui.FillCircle(g, new PointF(sx + c.S(3.5f), chip.Y + c.S(32)), c.S(3.5f), dot); sx += c.S(12); }
            Ui.Text(g, sub, c.F(12), subC, new RectangleF(sx, chip.Y + c.S(23), r.Right - sx - c.S(12) - btnW, c.S(18)));

            if (connected)
            {
                var br = new RectangleF(r.Right - c.S(12) - btnW + c.S(6), chip.Y + c.S(7), btnW - c.S(6), c.S(28));
                bool hv = c.IsHover(br);
                Ui.FillRound(g, br, br.Height / 2, hv ? t.TileHover : Ui.WithA(t.Text, t.Dark ? 16 : 12));
                Ui.StrokeRound(g, br, br.Height / 2, Ui.WithA(t.Text, hv ? 50 : 30));
                Ui.Text(g, "Disconnect", c.FS(12), t.Text, br, 0);
                c.Hit(br, () => WifiSvc.Disconnect());

                // divider + 3×2 stats
                float dy = r.Y + c.S(66);
                using (var p = new Pen(Ui.WithA(t.Text, t.Dark ? 22 : 26), 1f)) g.DrawLine(p, r.X + c.S(14), dy, r.Right - c.S(14), dy);
                float cw = (r.Width - c.S(28)) / 3f, cx0 = r.X + c.S(14), ry = dy + c.S(9);
                Stat(c, cx0, ry, cw, "Signal", s.Quality + "%", s.Rssi < 0 ? s.Rssi + " dBm" : null);
                Stat(c, cx0 + cw, ry, cw, "Band", s.Band > 0 ? WifiDraw.BandText(s.Band) : "—", s.Channel > 0 ? "ch " + s.Channel : null);
                Stat(c, cx0 + cw * 2, ry, cw, "Standard", s.Phy.Length > 0 ? s.Phy : "—", null);
                ry += c.S(34);
                Stat(c, cx0, ry, cw, "Security", s.Secured ? s.AuthText : "Open", null);
                Stat(c, cx0 + cw, ry, cw, "IPv4", s.Ip.Length > 0 ? s.Ip : "—", null);
                long link = Math.Max(s.RxMbps, s.TxMbps);
                Stat(c, cx0 + cw * 2, ry, cw, "Link speed", link > 0 ? SpeedText(link) : "—", null);
            }
            return r.Bottom;
        }
        static string SpeedText(long mbps) { return mbps >= 1000 ? (mbps / 1000.0).ToString("0.#") + " Gbps" : mbps + " Mbps"; }

        void Stat(DrawCtx c, float x, float y, float w, string label, string value, string extra)
        {
            var t = c.T;
            Ui.Text(c.G, label, c.F(10.5f), t.TextFaint, new RectangleF(x, y, w - c.S(6), c.S(13)));
            var vf = c.FS(12.5f);
            Ui.Text(c.G, value, vf, t.Text, new RectangleF(x, y + c.S(14), w - c.S(6), c.S(17)));
            if (extra != null)
            {
                float vw = Ui.MeasureW(c.G, value, vf) + c.S(5);
                if (vw < w - c.S(30)) Ui.Text(c.G, extra, c.F(10.5f), t.TextDim, new RectangleF(x + vw, y + c.S(15), w - vw - c.S(6), c.S(16)));
            }
        }

        void DrawEmpty(DrawCtx c, WifiSnap s, RectangleF r)
        {
            var t = c.T; var g = c.G;
            float cy = r.Y + r.Height * 0.42f;
            var gb = new RectangleF(r.X + r.Width / 2 - c.S(24), cy - c.S(44), c.S(48), c.S(48));
            if (s.State == WifiState.RadioOff)
            {
                WifiDraw.Glyph(g, gb, 0, t.Text, Ui.WithA(t.Text, t.Dark ? 34 : 64), true, t.TextFaint);
                Ui.Text(g, s.HwOff ? "Use your device's wireless switch or key to turn Wi-Fi on." : "Wi-Fi is turned off", c.F(12.5f), t.TextDim, new RectangleF(r.X, cy + c.S(14), r.Width, c.S(18)), 0);
                if (!s.HwOff)
                {
                    var br = new RectangleF(r.X + r.Width / 2 - c.S(70), cy + c.S(44), c.S(140), c.S(32));
                    c.Button(br, "Turn on Wi-Fi", () => WifiSvc.SetRadio(true), true, Icons.Wifi);
                }
            }
            else
            {
                if (s.Wired) Ui.Icon(g, EthGlyph, gb, t.TextFaint, c.S(34));
                else WifiDraw.Glyph(g, gb, 0, t.Text, Ui.WithA(t.Text, t.Dark ? 34 : 64), true, t.TextFaint);
                Ui.Text(g, s.Wired ? "Using a wired connection" : "Wi-Fi isn't available on this PC", c.F(12.5f), t.TextDim, new RectangleF(r.X, cy + c.S(14), r.Width, c.S(18)), 0);
            }
        }

        // ---- nearby networks list ----
        void DrawList(DrawCtx c, WifiSnap s, RectangleF area)
        {
            var t = c.T; var g = c.G;
            var nets = s.Nets;
            if (!s.NetsLoaded || nets.Count == 0)
            {
                Ui.Text(g, s.NetsLoaded && !s.Scanning ? "No networks found" : "Looking for networks…", c.F(12), t.TextFaint, new RectangleF(area.X, area.Y + c.S(20), area.Width, c.S(20)), 0);
                return;
            }
            float rowH = c.S(46), expH = c.S(48);
            // total height for scroll clamp
            float total = 0;
            foreach (var n in nets) total += rowH + (IsExpanded(n, s) ? expH : 0);
            float maxScroll = Math.Max(0, total - area.Height);
            if (scroll > maxScroll) scroll = maxScroll; if (scroll < 0) scroll = 0;
            var listRect = Rectangle.Round(area);
            c.Wheel(listRect, d => { scroll -= d * rowH; Invalidate(); });

            var clip = g.Clip; g.SetClip(area);
            float y = area.Y - scroll;
            foreach (var n in nets)
            {
                bool exp = IsExpanded(n, s);
                float h = rowH + (exp ? expH : 0);
                if (y + h > area.Y && y < area.Bottom) DrawRow(c, s, n, new RectangleF(area.X, y, area.Width, h), rowH, exp, area);
                y += h;
            }
            g.Clip = clip; clip.Dispose();

            // scroll fades + thumb
            if (maxScroll > 0)
            {
                float th = Math.Max(c.S(24), area.Height * area.Height / total);
                float ty = area.Y + (area.Height - th) * (scroll / maxScroll);
                Ui.FillRound(g, new RectangleF(area.Right - c.S(3), ty, c.S(3), th), c.S(1.5f), Ui.WithA(t.Text, 60));
            }
        }

        bool IsExpanded(WifiNet n, WifiSnap s)
        {
            return expandSsid != null && n.Ssid == expandSsid && !n.Connected && s.ConnectingSsid != n.Ssid;
        }
        bool NeedsPassword(WifiNet n) { return n.Secured && !n.HasProfile && n.Auth != 10 && !WifiNative.IsEnterprise(n.Auth) && n.Auth != 2; }

        void DrawRow(DrawCtx c, WifiSnap s, WifiNet n, RectangleF r, float rowH, bool exp, RectangleF clipArea)
        {
            var t = c.T; var g = c.G;
            var head = new RectangleF(r.X, r.Y, r.Width, rowH);
            bool connecting = s.ConnectingSsid.Length > 0 && s.ConnectingSsid == n.Ssid;
            bool failed = s.FailSsid.Length > 0 && s.FailSsid == n.Ssid;
            bool hv = c.IsHover(head) && clipArea.Contains(c.Mouse);
            var bgR = Ui.Inset(r, 0, c.S(1));
            if (exp) Ui.FillRound(g, bgR, c.S(10), t.Tile);
            else if (n.Connected) Ui.FillRound(g, bgR, c.S(10), t.AccentA(t.Dark ? 30 : 22));
            else if (hv) Ui.FillRound(g, bgR, c.S(10), t.TileHover);

            // signal glyph
            var gb = new RectangleF(head.X + c.S(10), head.Y + (rowH - c.S(18)) / 2, c.S(18), c.S(18));
            Color lit = n.Connected ? t.Accent : t.Text, dim = Ui.WithA(n.Connected ? t.Accent : t.Text, t.Dark ? 50 : 78);
            if (connecting) WifiDraw.Glyph(g, gb, 1 + frame % 4, t.Accent, dim); else WifiDraw.Glyph(g, gb, Math.Max(1, n.Bars), lit, dim);

            // right side: lock + saved badge
            float rx = head.Right - c.S(10);
            if (n.Secured)
            {
                var lr = new RectangleF(rx - c.S(16), head.Y, c.S(16), rowH);
                Ui.Icon(g, Icons.Lock, lr, t.TextFaint, c.S(12));
                rx -= c.S(22);
            }
            if (n.HasProfile && !n.Connected)
            {
                var bf = c.F(10.5f); float bw = Ui.MeasureW(g, "Saved", bf) + c.S(14);
                var pr = new RectangleF(rx - bw, head.Y + (rowH - c.S(18)) / 2, bw, c.S(18));
                Ui.FillRound(g, pr, pr.Height / 2, Ui.WithA(t.Text, t.Dark ? 20 : 14));
                Ui.Text(g, "Saved", bf, t.TextDim, pr, 0);
                rx = pr.X - c.S(6);
            }

            float tx = gb.Right + c.S(12);
            string sub; Color subC = t.TextFaint;
            if (connecting) { sub = "Connecting…"; subC = t.Accent; }
            else if (failed) { sub = s.FailText; subC = t.Bad; }
            else if (n.Connected) { sub = "Connected" + (n.Band > 0 ? " · " + WifiDraw.BandText(n.Band) : ""); subC = t.Accent; }
            else
            {
                string sec = !n.Secured ? "Open" : WifiNative.IsEnterprise(n.Auth) ? "Enterprise" : WifiNative.AuthName(n.Auth);
                string bt = WifiDraw.BandsText(n); sub = sec + (bt.Length > 0 ? " · " + bt : "");
            }
            Ui.Text(g, n.Ssid, c.FS(12.5f), t.Text, new RectangleF(tx, head.Y + c.S(7), rx - tx - c.S(4), c.S(18)));
            Ui.Text(g, sub, c.F(11), subC, new RectangleF(tx, head.Y + c.S(25), (failed ? head.Right - c.S(10) : rx) - tx - c.S(4), c.S(15)));

            var hitHead = RectangleF.Intersect(head, clipArea);
            if (hitHead.Height > c.S(6)) c.Hit(hitHead, () => OnRowClick(n));

            if (exp)
            {
                // inline password field + Connect
                float ey = head.Bottom + c.S(4);
                float bw = c.S(84);
                var ir = new RectangleF(tx, ey, head.Right - c.S(10) - tx - bw - c.S(8), c.S(32));
                var cb = new RectangleF(head.Right - c.S(10) - bw, ey, bw, c.S(32));
                if (clipArea.Contains(ir))
                {
                    c.Input("wifi.pass." + passGen, Rectangle.Round(ir), "Network password", txt => SubmitPassword(n, txt), txt => { passText = txt ?? ""; }, false, passText.Length > 0 ? passText : null);
                    MaskPassword(Rectangle.Round(ir));
                }
                else
                {
                    Ui.FillRound(g, ir, c.S(8), t.Input);
                    Ui.Text(g, passText.Length > 0 ? new string('●', passText.Length) : "Network password", c.F(12.5f), t.TextFaint, Ui.Inset(ir, c.S(8), 0));
                }
                bool ok = passText.Length >= 8 || (passText.Length == 64);
                var cbv = RectangleF.Intersect(cb, clipArea);
                if (cbv.Height > c.S(8))
                {
                    bool hb = c.IsHover(cb);
                    Ui.FillRound(g, cb, cb.Height / 2, ok ? (hb ? Ui.Mix(t.Accent, Color.White, 0.15) : t.Accent) : Ui.WithA(t.Accent, 90));
                    Ui.Text(g, "Connect", c.FS(12.5f), Color.White, cb, 0);
                    c.Hit(cbv, () => SubmitPassword(n, passText));
                }
            }
        }

        void OnRowClick(WifiNet n)
        {
            var s = Snap;
            if (n.Connected) return;
            if (s.ConnectingSsid == n.Ssid) return;
            if (n.Secured && !n.HasProfile && (WifiNative.IsEnterprise(n.Auth) || n.Auth == 2))
            {
                Host.ClosePanel(); WifiSvc.OpenUri("ms-availablenetworks:"); return; // enterprise / WEP → Windows network flyout
            }
            if (NeedsPassword(n))
            {
                if (expandSsid == n.Ssid) { expandSsid = null; }
                else { expandSsid = n.Ssid; passText = ""; passGen++; }
                if (s.FailSsid == n.Ssid) WifiSvc.ClearFailure();
                Invalidate(); return;
            }
            expandSsid = null;
            WifiSvc.Connect(n, null);
        }

        void SubmitPassword(WifiNet n, string pw)
        {
            pw = pw ?? "";
            if (!(pw.Length >= 8 && pw.Length <= 63) && !(pw.Length == 64 && pw.All(Uri.IsHexDigit))) return; // WPA passphrase rules
            expandSsid = null; passText = ""; passGen++;
            WifiSvc.Connect(n, pw);
            Invalidate();
        }

        /// <summary>The host's c.Input is a plain TextBox: switch it to a password box (EM_SETPASSWORDCHAR via PasswordChar — no handle re-creation).</summary>
        static void MaskPassword(Rectangle r)
        {
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f.GetType().Name != "PanelForm") continue;
                    foreach (Control ctl in f.Controls)
                    {
                        var tb = ctl as TextBox;
                        if (tb != null && !tb.Multiline && r.Contains(new Point(tb.Left + 2, tb.Top + tb.Height / 2)) && tb.PasswordChar != '●') tb.PasswordChar = '●';
                    }
                }
            }
            catch { }
        }
    }
}
