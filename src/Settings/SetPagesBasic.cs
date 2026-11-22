// Settings pages: General, Appearance, Dock, About.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NovaBar
{
    // =====================================================================================
    class SetGeneralPage : SetPage
    {
        public override string Title { get { return "General"; } }
        public override string Subtitle { get { return "Startup, display, time, units and weather location"; } }

        class Geo { public string Name, Region, Country; public double Lat, Lon; }
        string geoText = "", geoPending; int geoTypedAt; volatile bool geoBusy; List<Geo> geo; string geoMsg; int geoSeq;

        static readonly string[] currencies = { "USD", "EUR", "GBP", "SAR", "AED", "JPY", "CNY", "INR", "CAD", "AUD", "CHF", "SEK", "NOK", "KRW", "BRL", "TRY", "EGP", "KWD", "QAR", "BHD", "OMR", "SGD", "HKD", "MXN", "ZAR", "PLN", "RUB" };

        public override float Paint(SetG g, float x, float y, float w)
        {
            var c = Cfg;
            // the core purpose: NovaBar replaces the Windows taskbar
            var hero = new RectangleF(x, y, w, g.S(118));
            g.Card(hero);
            Ui.FillRound(g.G, new RectangleF(x + g.S(1), y + g.S(1), g.S(4), hero.Height - g.S(2)), g.S(2), W.T.Accent);
            var hic = new RectangleF(x + g.S(20), y + g.S(20), g.S(40), g.S(40));
            Ui.FillRound(g.G, hic, g.S(10), Ui.WithA(W.T.Accent, W.T.Dark ? 46 : 32));
            Ui.Icon(g.G, Icons.Window, hic, W.T.Accent, g.S(18));
            Ui.Text(g.G, "Replace Windows taskbar", g.FS(16), P.Text, new RectangleF(hic.Right + g.S(16), y + g.S(18), w * 0.6f, g.S(24)));
            Ui.Text(g.G, "Hides the Windows taskbar while NovaBar runs; it comes back when NovaBar quits.", g.F(12.5f), P.Dim, new RectangleF(hic.Right + g.S(16), y + g.S(44), w - g.S(200), g.S(20)));
            g.Toggle(new RectangleF(x, y + g.S(18), w - g.S(22), g.S(28)), c.HideTaskbar, v => { c.HideTaskbar = v; W.Changed(); });
            Ui.Text(g.G, "Taskbar stuck hidden?", g.F(12), P.Faint, new RectangleF(hic.Right + g.S(16), y + g.S(76), g.S(150), g.S(28)));
            g.Button(new RectangleF(hic.Right + g.S(160), y + g.S(76), g.ButtonWidth("Restore Windows taskbar now", Icons.Refresh), g.S(30)), "Restore Windows taskbar now", RestoreTaskbar, false, Icons.Refresh);
            y += hero.Height + g.S(22);

            y = g.Section(x, y, w, "Startup & behavior");
            var a = g.Row(ref y, x, w, Icons.Power, "Launch at startup", "Start NovaBar automatically when you sign in to Windows");
            g.Toggle(a, c.LaunchAtStartup, v => { c.LaunchAtStartup = v; SetStartup(v); W.Changed(); });
            a = g.Row(ref y, x, w, "", "Hide when an app is fullscreen", "Games, videos and presentations get the whole screen");
            g.Toggle(a, c.HideOnFullscreen, v => { c.HideOnFullscreen = v; W.Changed(); });
            a = g.Row(ref y, x, w, Icons.Desktop, "Reserve screen space", "Maximized windows stop at the bar instead of going under it");
            g.Toggle(a, c.ReserveSpace, v => { c.ReserveSpace = v; W.Changed(); });
            a = g.Row(ref y, x, w, "", "Display", "Which monitor shows the bar");
            var screens = Screen.AllScreens;
            var opts = new List<string> { "Primary display" };
            for (int i = 0; i < screens.Length; i++)
                opts.Add("Display " + (i + 1) + "  ·  " + screens[i].Bounds.Width + "×" + screens[i].Bounds.Height + (screens[i].Primary ? "  (primary)" : ""));
            int mi = Math.Max(0, Math.Min(opts.Count - 1, c.Monitor));
            g.Dropdown(new RectangleF(a.Right - g.S(280), a.Y + (a.Height - g.S(34)) / 2, g.S(280), g.S(34)), opts[mi], opts.ToArray(), mi, i => { c.Monitor = i; W.Changed(); });

            y += g.S(18);
            y = g.Section(x, y, w, "Time & region");
            a = g.Row(ref y, x, w, Icons.Clock, "Clock format", "Used by clocks, calendars and timers");
            g.Segmented(a, new[] { "12-hour", "24-hour" }, new[] { "12", "24" }, c.Clock24 ? "24" : "12", v => { c.Clock24 = v == "24"; W.Changed(); });
            a = g.Row(ref y, x, w, Icons.Temperature, "Units", "Temperature, wind speed and distances");
            g.Segmented(a, new[] { "Metric  °C · km/h", "Imperial  °F · mph" }, new[] { "metric", "imperial" }, c.Units, v => { c.Units = v; W.Changed(); });
            a = g.Row(ref y, x, w, Icons.Money, "Currency", "Default currency for prices, revenue and crypto widgets");
            int ci = Array.IndexOf(currencies, (c.Currency ?? "USD").ToUpperInvariant());
            var copts = currencies.ToArray(); if (ci < 0) { copts = new[] { c.Currency }.Concat(currencies).ToArray(); ci = 0; }
            g.Dropdown(new RectangleF(a.Right - g.S(160), a.Y + (a.Height - g.S(34)) / 2, g.S(160), g.S(34)), copts[ci], copts, ci, i => { c.Currency = copts[i]; W.Changed(); });

            y += g.S(18);
            y = g.Section(x, y, w, "Weather location");
            float rowsH = geo != null ? geo.Count * g.S(46) + g.S(8) : 0;
            if (geoMsg != null) rowsH = g.S(40);
            float ch = g.S(68) + g.S(54) + rowsH + g.S(6);
            var card = new RectangleF(x, y, w, ch);
            g.Card(card);
            Ui.Icon(g.G, Icons.Globe, new RectangleF(x + g.S(18), y + g.S(24), g.S(20), g.S(20)), P.Text, g.S(16));
            Ui.Text(g.G, c.City, g.F(14), P.Text, new RectangleF(x + g.S(56), y + g.S(15), w * 0.6f, g.S(20)));
            Ui.Text(g.G, c.Lat.ToString("0.0000", CultureInfo.InvariantCulture) + ", " + c.Lon.ToString("0.0000", CultureInfo.InvariantCulture) + "  ·  used by weather, sunrise and prayer-time widgets",
                g.F(12), P.Dim, new RectangleF(x + g.S(56), y + g.S(35), w - g.S(80), g.S(18)));
            float iy = y + g.S(68);
            var ir = new RectangleF(x + g.S(18), iy, w - g.S(36) - g.S(110), g.S(36));
            g.Input("geo", ir, geoText, "Search for a city…", s => { geoText = s; geoPending = s; geoTypedAt = Environment.TickCount; }, s => { geoText = s; geoPending = null; Search(s); }, false, Icons.Search);
            g.Button(new RectangleF(ir.Right + g.S(10), iy, g.S(100), g.S(36)), geoBusy ? "Searching" : "Search", () => Search(geoText), true);
            float ry = iy + g.S(48);
            if (geoMsg != null) Ui.Text(g.G, geoMsg, g.F(12.5f), P.Dim, new RectangleF(x + g.S(22), ry, w - g.S(44), g.S(30)));
            else if (geo != null)
                foreach (var r in geo)
                {
                    var rr = new RectangleF(x + g.S(10), ry, w - g.S(20), g.S(44));
                    bool hv = g.Hover(rr);
                    if (hv) Ui.FillRound(g.G, rr, g.S(6), P.Sel.A > 0 ? P.Sel : P.CardHover);
                    Ui.Icon(g.G, "\uE707", new RectangleF(rr.X + g.S(10), rr.Y, g.S(18), rr.Height), P.Accent, g.S(14));
                    Ui.Text(g.G, r.Name, g.FS(13.5f), P.Text, new RectangleF(rr.X + g.S(38), rr.Y + g.S(5), rr.Width * 0.6f, g.S(18)));
                    Ui.Text(g.G, string.Join(", ", new[] { r.Region, r.Country }.Where(s => !string.IsNullOrEmpty(s))), g.F(12), P.Dim, new RectangleF(rr.X + g.S(38), rr.Y + g.S(23), rr.Width * 0.6f, g.S(16)));
                    Ui.Text(g.G, r.Lat.ToString("0.00", CultureInfo.InvariantCulture) + ", " + r.Lon.ToString("0.00", CultureInfo.InvariantCulture), g.F(12), P.Faint, new RectangleF(rr.Right - g.S(160), rr.Y, g.S(146), rr.Height), 1);
                    var pick = r;
                    g.Hit(rr, () =>
                    {
                        c.City = pick.Name; c.Lat = Math.Round(pick.Lat, 4); c.Lon = Math.Round(pick.Lon, 4);
                        geo = null; geoText = ""; W.Changed(); W.Toast("Weather location set to " + pick.Name);
                    });
                    ry += g.S(46);
                }
            y += ch + g.S(8);
            return y;
        }
        SetPal P { get { return W.P; } }

        public override void Tick()
        {
            if (geoPending != null && Environment.TickCount - geoTypedAt > 450) { var q = geoPending; geoPending = null; if (q.Trim().Length >= 2) Search(q); }
        }
        void Search(string q)
        {
            q = (q ?? "").Trim(); if (q.Length < 2) return;
            int seq = ++geoSeq; geoBusy = true; W.Invalidate();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var list = new List<Geo>(); string msg = null;
                var o = Http.GetJson("https://geocoding-api.open-meteo.com/v1/search?count=6&language=en&format=json&name=" + Http.Esc(q));
                if (o == null) msg = "Couldn't reach the geocoding service. Check your connection and try again.";
                else
                {
                    var arr = J.Arr(o, "results");
                    if (arr != null) foreach (var r in arr)
                            list.Add(new Geo { Name = J.Str(r, "name"), Region = J.Str(r, "admin1"), Country = J.Str(r, "country"), Lat = J.Num(r, "latitude"), Lon = J.Num(r, "longitude") });
                    if (list.Count == 0) msg = "No places found for “" + q + "”.";
                }
                try
                {
                    W.BeginInvoke(new Action(() =>
                    {
                        if (seq != geoSeq) return;
                        geoBusy = false; geo = list.Count > 0 ? list : null; geoMsg = msg; W.Invalidate();
                    }));
                }
                catch { }
            });
        }

        void RestoreTaskbar()
        {
            try
            {
                var h = Native.FindWindow("Shell_TrayWnd", null); if (h != IntPtr.Zero) Native.ShowWindow(h, Native.SW_SHOW);
                IntPtr s2 = IntPtr.Zero;
                while ((s2 = Native.FindWindowEx(IntPtr.Zero, s2, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) Native.ShowWindow(s2, Native.SW_SHOW);
                W.Toast("Windows taskbar restored");
            }
            catch { }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (on) k.SetValue("NovaBar", "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue("NovaBar") != null) k.DeleteValue("NovaBar", false);
                }
            }
            catch { }
        }
    }

    // =====================================================================================
    class SetAppearancePage : SetPage
    {
        public override string Title { get { return "Appearance"; } }
        public override string Subtitle { get { return "Theme, accent color and font"; } }
        static readonly string[] order = { "dark", "white", "midnight", "glass", "glasslight" };
        static readonly string[] blurbs = { "Solid, deep black", "Clean and bright", "Deep blue, translucent", "Clear liquid glass over your wallpaper", "Light frosted glass" };
        static readonly string[] swatches = { "#7C8CFF", "#0A84FF", "#64D2FF", "#30D158", "#FFD60A", "#FF9F0A", "#FF453A", "#FF375F", "#BF5AF2", "#A2845E" };
        static readonly string[] fonts = { "Segoe UI Variable Text", "Segoe UI Variable Display", "Segoe UI", "Inter", "Aptos", "Bahnschrift", "Calibri", "Arial", "Verdana", "Tahoma", "Trebuchet MS", "Cascadia Code", "Consolas" };
        string hexText;
        float pickHue = -1;   // colour-picker hue (0..360), initialised from the accent on first paint
        static Color FromHsv(double h, double sat, double v)
        {
            h = ((h % 360) + 360) % 360; double c = v * sat, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r = 0, gg = 0, b = 0;
            if (h < 60) { r = c; gg = x; } else if (h < 120) { r = x; gg = c; } else if (h < 180) { gg = c; b = x; } else if (h < 240) { gg = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb(255, (int)Math.Round((r + m) * 255), (int)Math.Round((gg + m) * 255), (int)Math.Round((b + m) * 255));
        }
        static void ToHsv(Color col, out double h, out double sat, out double v)
        {
            double r = col.R / 255.0, gg = col.G / 255.0, b = col.B / 255.0, mx = Math.Max(r, Math.Max(gg, b)), mn = Math.Min(r, Math.Min(gg, b)), d = mx - mn;
            h = d == 0 ? 0 : mx == r ? 60 * (((gg - b) / d) % 6) : mx == gg ? 60 * ((b - r) / d + 2) : 60 * ((r - gg) / d + 4);
            if (h < 0) h += 360; sat = mx == 0 ? 0 : d / mx; v = mx;
        }

        public override float Paint(SetG g, float x, float y, float w)
        {
            var c = Cfg;
            y = g.Section(x, y, w, "Theme");
            var zones = SetBarSample.Pick(W, "ap:", 4);
            float gap = g.S(14);
            for (int row = 0; row < 2; row++)
            {
                int n = row == 0 ? 3 : 2;
                float cw = (w - gap * (n - 1)) / n, chh = g.S(row == 0 ? 172 : 182);
                for (int i = 0; i < n; i++)
                {
                    int k = row == 0 ? i : 3 + i;
                    var r = new RectangleF(x + i * (cw + gap), y, cw, chh);
                    ThemeCard(g, r, order[k], blurbs[k], zones);
                }
                y += chh + gap;
            }
            {
                // the Settings window can have its own look, independent of the bar
                object st; string stCur = W.Cfg.Extra != null && W.Cfg.Extra.TryGetValue("settings.theme", out st) && st != null ? J.AsStr(st) : "same";
                var tr = g.Row(ref y, x, w, Icons.Window, "Settings window theme", "The theme cards above style the bar");
                var ids = new[] { "same", "dark", "white", "glass", "glasslight", "midnight" };
                var names = new[] { "Same as bar", "Black", "White", "Liquid Glass", "iOS Glass", "Midnight" };
                int ci = Array.IndexOf(ids, stCur); if (ci < 0) ci = 0;
                var dr = new RectangleF(tr.Right - g.S(220), tr.Y + (tr.Height - g.S(34)) / 2, g.S(220), g.S(34));
                g.Dropdown(dr, names[ci], names, ci, j => { W.Cfg.Extra["settings.theme"] = ids[j]; W.Changed(true); });
            }

            y += g.S(8);
            y = g.Section(x, y, w, "Accent color");
            float ah = g.S(268);
            var card = new RectangleF(x, y, w, ah); g.Card(card);
            Ui.Text(g.G, "Used for rings, progress, highlights and buttons", g.F(12.5f), W.P.Dim, new RectangleF(x + g.S(20), y + g.S(14), w - g.S(40), g.S(20)));
            float sx = x + g.S(20), sy = y + g.S(48), sd = g.S(32);
            var cur = Theme.Hex(c.Accent, Color.Empty);
            foreach (var hex in swatches)
            {
                var col = Theme.Hex(hex, Color.Gray);
                var sr = new RectangleF(sx, sy, sd, sd);
                bool on = string.Equals(hex, c.Accent, StringComparison.OrdinalIgnoreCase);
                bool hv = g.Hover(sr);
                Ui.FillCircle(g.G, new PointF(sr.X + sd / 2, sr.Y + sd / 2), sd / 2 - (hv && !on ? 0 : g.S(1)), col);
                if (on)
                {
                    using (var pen = new Pen(W.P.Text, g.S(2))) g.G.DrawEllipse(pen, Ui.Inset(sr, -g.S(3.5f), -g.S(3.5f)));
                    Ui.Icon(g.G, Icons.Check, sr, Color.White, g.S(13));
                }
                var hh = hex; g.Hit(sr, () => { c.Accent = hh; hexText = null; pickHue = -1; W.Changed(true); });
                sx += sd + g.S(12);
            }
            // custom hex
            float hx = sx + g.S(14);
            Ui.Text(g.G, "Custom", g.F(12.5f), W.P.Dim, new RectangleF(hx, sy, g.S(56), sd));
            var hr = new RectangleF(hx + g.S(60), sy - g.S(1), g.S(120), sd + g.S(2));
            if (hr.Right < x + w - g.S(16))
                g.Input("hex", hr, hexText ?? c.Accent, "#RRGGBB", s =>
                {
                    hexText = s; var col = Theme.Hex(s, Color.Empty);
                    if (col != Color.Empty && s.Trim().TrimStart('#').Length == 6) { c.Accent = Theme.ToHex(col); W.Changed(true); }
                }, s => { hexText = null; });
            // live accent sample
            float py = y + g.S(92);
            Ui.Progress(g.G, new RectangleF(x + g.S(20), py, g.S(180), g.S(6)), 0.62, W.T.Accent, W.P.T.Track);
            Ui.Ring(g.G, new RectangleF(x + g.S(214), py - g.S(7), g.S(20), g.S(20)), 0.7, W.T.Accent, W.P.T.Track, g.S(3));
            Ui.Text(g.G, Theme.ToHex(W.T.Accent), g.F(12), W.P.Faint, new RectangleF(x + g.S(244), py - g.S(7), g.S(100), g.S(20)));
            // ---- colour picker: saturation/brightness square + hue strip (click to pick), plus the Windows colour dialog ----
            {
                double ch, cs, cv; ToHsv(W.T.Accent, out ch, out cs, out cv);
                if (pickHue < 0) pickHue = (float)ch;
                float top = y + g.S(128), sqW = g.S(260), sqH = g.S(110), hueH = g.S(14);
                var sq = new RectangleF(x + g.S(20), top, sqW, sqH);
                var hueR = new RectangleF(sq.X, sq.Bottom + g.S(10), sqW, hueH);
                // square: hue colour, white→clear left-to-right, clear→black top-to-bottom
                using (var clip = Ui.Round(sq, g.S(8)))
                {
                    var st = g.G.Save(); g.G.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
                    g.G.FillRectangle(Ui.B(FromHsv(pickHue, 1, 1)), sq);
                    using (var wb = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(sq.X - 1, sq.Y, sq.Width + 2, sq.Height), Color.White, Color.FromArgb(0, 255, 255, 255), System.Drawing.Drawing2D.LinearGradientMode.Horizontal)) g.G.FillRectangle(wb, sq);
                    using (var bb = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(sq.X, sq.Y - 1, sq.Width, sq.Height + 2), Color.FromArgb(0, 0, 0, 0), Color.Black, System.Drawing.Drawing2D.LinearGradientMode.Vertical)) g.G.FillRectangle(bb, sq);
                    g.G.Restore(st);
                }
                // hue strip
                using (var clip = Ui.Round(hueR, hueH / 2))
                using (var hb = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(hueR.X - 1, hueR.Y, hueR.Width + 2, hueR.Height), Color.Red, Color.Red, System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                {
                    var blend = new System.Drawing.Drawing2D.ColorBlend(7);
                    blend.Colors = new[] { FromHsv(0, 1, 1), FromHsv(60, 1, 1), FromHsv(120, 1, 1), FromHsv(180, 1, 1), FromHsv(240, 1, 1), FromHsv(300, 1, 1), FromHsv(360, 1, 1) };
                    blend.Positions = new[] { 0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f };
                    hb.InterpolationColors = blend; g.G.FillPath(hb, clip);
                }
                // markers
                bool sameHue = Math.Abs(ch - pickHue) < 1 || cs < 0.02;
                if (sameHue)
                {
                    var mp = new PointF(sq.X + (float)cs * sq.Width, sq.Y + (1 - (float)cv) * sq.Height);
                    using (var pen = new Pen(Color.White, g.S(2))) g.G.DrawEllipse(pen, mp.X - g.S(6), mp.Y - g.S(6), g.S(12), g.S(12));
                    using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1)) g.G.DrawEllipse(pen, mp.X - g.S(7.5f), mp.Y - g.S(7.5f), g.S(15), g.S(15));
                }
                float hx2 = hueR.X + pickHue / 360f * hueR.Width;
                using (var pen = new Pen(Color.White, g.S(2.5f))) g.G.DrawEllipse(pen, hx2 - g.S(7), hueR.Y + hueH / 2 - g.S(7), g.S(14), g.S(14));
                // clicks (position-aware)
                g.Hit(sq, () => { }, p =>
                {
                    double ns = Math.Max(0, Math.Min(1, (p.X - sq.X) / sq.Width)), nv = Math.Max(0, Math.Min(1, 1 - (p.Y - sq.Y) / sq.Height));
                    c.Accent = Theme.ToHex(FromHsv(pickHue, ns, Math.Max(0.15, nv))); hexText = null; W.Changed(true);
                });
                g.Hit(hueR, () => { }, p =>
                {
                    pickHue = (float)Math.Max(0, Math.Min(359.9, (p.X - hueR.X) / hueR.Width * 360));
                    double s0 = cs < 0.25 ? 0.75 : cs, v0 = cv < 0.35 ? 0.9 : cv;
                    c.Accent = Theme.ToHex(FromHsv(pickHue, s0, v0)); hexText = null; W.Changed(true);
                });
                // preview + system dialog
                float px = sq.Right + g.S(24);
                var prev = new RectangleF(px, top, g.S(64), g.S(64));
                Ui.FillRound(g.G, prev, g.S(14), W.T.Accent);
                Ui.Text(g.G, Theme.ToHex(W.T.Accent), g.FS(13), W.P.Text, new RectangleF(px, prev.Bottom + g.S(8), g.S(160), g.S(20)));
                var md = new RectangleF(px, hueR.Bottom - g.S(32), g.S(150), g.S(32));
                g.Button(md, "More colors…", () =>
                {
                    using (var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true, Color = W.T.Accent })
                        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            c.Accent = Theme.ToHex(dlg.Color); double h2, s2, v2; ToHsv(dlg.Color, out h2, out s2, out v2); pickHue = (float)h2; hexText = null; W.Changed(true);
                        }
                }, false, Icons.Edit);
            }
            y += ah + g.S(22);

            y = g.Section(x, y, w, "Font");
            var a = g.Row(ref y, x, w, "", "Text font", "Used for every widget label and value");
            var avail = fonts.Where(f => Ui.HasFamily(f)).ToList();
            if (!avail.Contains(c.Font) && !string.IsNullOrEmpty(c.Font)) avail.Insert(0, c.Font);
            int fi = Math.Max(0, avail.IndexOf(c.Font));
            g.Dropdown(new RectangleF(a.Right - g.S(260), a.Y + (a.Height - g.S(34)) / 2, g.S(260), g.S(34)), avail[fi], avail.ToArray(), fi, i => { c.Font = avail[i]; W.Changed(true); });
            // sample
            var sr2 = new RectangleF(x, y, w, g.S(64)); g.Card(sr2);
            Ui.Text(g.G, "12:45", Ui.FontSemi(g.S(22)), W.P.Text, new RectangleF(x + g.S(20), y, g.S(90), sr2.Height));
            Ui.Text(g.G, "Thursday, 14 March  ·  CPU 23%  ·  24° Partly cloudy", g.F(14), W.P.Dim, new RectangleF(x + g.S(110), y, w - g.S(130), sr2.Height));
            y += sr2.Height + g.S(8);
            return y;
        }

        void ThemeCard(SetG g, RectangleF r, string id, string blurb, List<Widget>[] zones)
        {
            var c = Cfg;
            bool sel = c.Theme == id, hv = g.Hover(r);
            var t = Theme.Make(id, c.Accent);
            g.Card(r, hv);
            var pr = new RectangleF(r.X + g.S(6), r.Y + g.S(6), r.Width - g.S(12), r.Height - g.S(60));
            var st = g.G.Save();
            using (var clip = Ui.Round(pr, g.S(6))) g.G.SetClip(clip, CombineMode.Intersect);
            if (id == "dark") using (var br = new LinearGradientBrush(pr, Color.FromArgb(255, 40, 40, 48), Color.FromArgb(255, 18, 18, 22), 90f)) g.G.FillRectangle(br, pr);
            else if (id == "white") using (var br = new LinearGradientBrush(pr, Color.FromArgb(255, 226, 230, 240), Color.FromArgb(255, 206, 212, 226), 90f)) g.G.FillRectangle(br, pr);
            else SetPreview.Wallpaper(g.G, pr, SetPreview.WallFor(t));
            W.Prev.DrawBar(g.G, t, Ui.Inset(pr, g.S(10), 0), zones, g.Sc * 0.82f, true);
            g.G.Restore(st);
            // footer
            float fy = pr.Bottom + g.S(10);
            var rc = new RectangleF(r.X + g.S(14), fy + g.S(3), g.S(18), g.S(18));
            if (sel) { Ui.FillCircle(g.G, new PointF(rc.X + rc.Width / 2, rc.Y + rc.Height / 2), rc.Width / 2, W.T.Accent); Ui.FillCircle(g.G, new PointF(rc.X + rc.Width / 2, rc.Y + rc.Height / 2), g.S(4), Color.White); }
            else using (var pen = new Pen(W.P.Dim, g.S(1.3f))) g.G.DrawEllipse(pen, Ui.Inset(rc, g.S(1), g.S(1)));
            Ui.Text(g.G, Theme.NameOf(id), g.FS(14), W.P.Text, new RectangleF(rc.Right + g.S(10), fy, r.Width - g.S(50), g.S(22)));
            Ui.Text(g.G, blurb, g.F(12), W.P.Dim, new RectangleF(rc.Right + g.S(10), fy + g.S(21), r.Width - g.S(50), g.S(18)));
            if (sel) Ui.StrokeRound(g.G, r, g.S(8), W.T.Accent, g.S(2));
            g.Hit(r, () => { if (c.Theme != id) { c.Theme = id; W.Changed(true); } });
        }
    }

    /// <summary>Picks a representative set of the user's widgets for the small theme previews.</summary>
    static class SetBarSample
    {
        public static List<Widget>[] Pick(SetWindow w, string prefix, int max)
        {
            var zones = new[] { new List<Widget>(), new List<Widget>(), new List<Widget>() };
            // compact, recognisable widgets read best in the small theme cards
            var pref = new[] { new[] { "weather", "left" }, new[] { "clock", "center" }, new[] { "cpu", "right" }, new[] { "battery", "right" } };
            if (pref.All(p => WidgetRegistry.Meta(p[0]) != null))
            {
                foreach (var p in pref.Take(max))
                {
                    var wd = w.Prev.Get(prefix + p[0], p[0], null, true);
                    if (wd != null) zones[p[1] == "left" ? 0 : p[1] == "center" ? 1 : 2].Add(wd);
                }
                return zones;
            }
            var cfg = w.Cfg.Widgets.Where(i => WidgetRegistry.Meta(i.Id) != null).ToList();
            var order = new List<WidgetInstance>();
            Action<string> take = z => { var f = cfg.FirstOrDefault(i => (i.Zone ?? "right") == z && !order.Contains(i)); if (f != null) order.Add(f); };
            take("center"); take("left"); take("right"); take("right"); take("left"); take("right");
            foreach (var inst in order.Take(max))
            {
                var wd = w.Prev.Get(prefix + inst.Inst, inst.Id, inst, true);
                if (wd == null) continue;
                int z = inst.Zone == "left" ? 0 : inst.Zone == "center" ? 1 : 2;
                zones[z].Add(wd);
            }
            if (zones.All(l => l.Count == 0)) { zones[0].Add(null); zones[1].Add(null); zones[2].Add(null); }
            return zones;
        }
        public static List<Widget>[] All(SetWindow w, string prefix)
        {
            var zones = new[] { new List<Widget>(), new List<Widget>(), new List<Widget>() };
            foreach (var inst in w.Cfg.Widgets)
            {
                var wd = w.Prev.Get(prefix + inst.Inst, inst.Id, inst, true);
                if (wd == null) continue;
                int z = inst.Zone == "left" ? 0 : inst.Zone == "center" ? 1 : 2;
                zones[z].Add(wd);
            }
            if (zones.All(l => l.Count == 0)) { zones[0].Add(null); zones[1].Add(null); zones[2].Add(null); zones[2].Add(null); }
            return zones;
        }
    }

    // =====================================================================================
    class SetDockPage : SetPage
    {
        public override string Title { get { return "Dock"; } }
        public override string Subtitle { get { return "Where the bar sits and how big it is"; } }
        public override float Paint(SetG g, float x, float y, float w)
        {
            var c = Cfg;
            // preview: a mock monitor
            float ph = Math.Min(g.S(300), w * 0.42f);
            var card = new RectangleF(x, y, w, ph + g.S(28)); g.Card(card);
            var scr = new RectangleF(x + g.S(14), y + g.S(14), w - g.S(28), ph);
            var st = g.G.Save();
            using (var clip = Ui.Round(scr, g.S(6))) g.G.SetClip(clip, CombineMode.Intersect);
            SetPreview.Wallpaper(g.G, scr, SetPreview.WallFor(W.T));
            // fake windows on the desktop
            var win = new RectangleF(scr.X + scr.Width * 0.18f, scr.Y + scr.Height * 0.22f, scr.Width * 0.46f, scr.Height * 0.5f);
            Ui.FillRound(g.G, win, g.S(6), W.T.Dark ? Color.FromArgb(170, 30, 30, 36) : Color.FromArgb(190, 250, 250, 252));
            Ui.FillRound(g.G, new RectangleF(win.X, win.Y, win.Width, g.S(16)), g.S(6), W.T.Dark ? Color.FromArgb(120, 50, 50, 58) : Color.FromArgb(150, 235, 235, 240));
            bool pill = c.Style != "full", top = c.Position == "top";
            float scale = g.Sc * 0.62f * BarMetrics.SizeFactor(c.Size);
            float bh = 44 * scale, m = pill ? g.S(8) : 0;
            bool vert = c.Position == "left" || c.Position == "right";
            if (vert)
            {   // vertical sidebar sketch: stacked tiles
                float vw = g.S(70), vh = pill ? scr.Height * 0.72f : scr.Height - 2 * m;
                var va = new RectangleF(c.Position == "left" ? scr.X + m : scr.Right - m - vw, scr.Y + (scr.Height - vh) / 2, vw, vh);
                if (c.BarBackground || c.Style == "shell") SetPreview.FillBarBg(g.G, W.T, va, pill ? g.S(6) : 0);
                float ty = va.Y + g.S(6), th = g.S(15);
                var tileC = W.T.Dark ? Color.FromArgb(150, 40, 40, 48) : Color.FromArgb(200, 255, 255, 255);
                for (int i = 0; ty + th <= va.Bottom - g.S(6); i++)
                {
                    float hh = i == 1 ? th * 3 : th;   // the Apps grid
                    if (ty + hh > va.Bottom - g.S(6)) break;
                    Ui.FillRound(g.G, new RectangleF(va.X + g.S(5), ty, va.Width - g.S(10), hh), g.S(4), tileC);
                    ty += hh + g.S(4);
                }
            }
            else
            {
            var area = new RectangleF(scr.X + m, top ? scr.Y + m : scr.Bottom - m - bh, scr.Width - 2 * m, bh);
            var zones = SetBarSample.All(W, "dk:");
            W.Prev.DrawBar(g.G, W.T, area, zones, scale, pill, true);
            }
            g.G.Restore(st);
            Ui.Text(g.G, "Preview", g.F(11.5f), W.P.Faint, new RectangleF(x + g.S(16), scr.Bottom + g.S(4), g.S(200), g.S(16)));
            y += card.Height + g.S(18);

            y = g.Section(x, y, w, "Layout");
            bool isVert = c.Position == "left" || c.Position == "right";
            var a = g.Row(ref y, x, w, "", "Orientation", "Horizontal runs along the top or bottom; vertical is a sidebar on the left or right");
            g.Segmented(a, new[] { "Horizontal", "Vertical" }, new[] { "h", "v" }, isVert ? "v" : "h", v =>
            {
                if (v == "v" && !isVert) c.Position = "left"; else if (v == "h" && isVert) c.Position = "bottom";
                W.Changed();
            });
            a = g.Row(ref y, x, w, "", "Position", "Screen edge the bar is docked to");
            if (isVert) g.Segmented(a, new[] { "Left", "Right" }, new[] { "left", "right" }, c.Position, v => { c.Position = v; W.Changed(); });
            else g.Segmented(a, new[] { "Top", "Bottom" }, new[] { "top", "bottom" }, c.Position, v => { c.Position = v; W.Changed(); });
            {   // per-display edge override
                var pscr = new List<Screen> { Screen.PrimaryScreen }; pscr.AddRange(Screen.AllScreens.Where(sc => !sc.Primary));
                if (pscr.Count > 1)
                    for (int i = 0; i < pscr.Count; i++)
                    {
                        var dev = pscr[i].DeviceName; object ov;
                        string cur = c.Extra != null && c.Extra.TryGetValue("position." + dev, out ov) ? J.AsStr(ov) : "";
                        a = g.Row(ref y, x, w, "\ue7f4", "Position on Display " + (i + 1), pscr[i].Bounds.Width + "\u00d7" + pscr[i].Bounds.Height + (pscr[i].Primary ? "  \u00b7  primary" : ""));
                        g.Segmented(a, new[] { "Default", "Top", "Bottom", "Left", "Right" }, new[] { "", "top", "bottom", "left", "right" }, cur, v => { c.SetPositionFor(dev, v); W.Changed(); });
                    }
            }
            a = g.Row(ref y, x, w, Icons.Apps, "Style", "Pill floats in the middle; Full width spans the edge; Shell attaches to the edge with curved corners and panels that slide out of the bar");
            g.Segmented(a, new[] { "Pill", "Full width", "Shell" }, new[] { "pill", "full", "shell" }, c.Style, v => { c.Style = v; W.Changed(); });
            a = g.Row(ref y, x, w, Icons.Window, "Bar background", "Off: only the widgets float on screen, with no bar behind them");
            g.Toggle(a, c.BarBackground, b => { c.BarBackground = b; W.Changed(); });
            {
                object ov; string cur = c.Extra != null && c.Extra.TryGetValue("overflow", out ov) && ov != null ? J.AsStr(ov) : "button";
                a = g.Row(ref y, x, w, Icons.More, "When widgets don't fit", "For narrow screens");
                g.Segmented(a, new[] { "Overflow button", "Shrink, then overflow", "Hide extras" }, new[] { "button", "shrink", "hide" }, cur, v => { c.Extra["overflow"] = v; W.Changed(); });
            }
            a = g.Row(ref y, x, w, "", "Size", "Default for every display: scales tiles, text and icons");
            g.Segmented(a, new[] { "Small", "Medium", "Large" }, new[] { "S", "M", "L" }, c.Size, v => { c.Size = v; W.Changed(); });
            {   // per-display override
                var scrs = new List<Screen> { Screen.PrimaryScreen }; scrs.AddRange(Screen.AllScreens.Where(sc => !sc.Primary));
                if (scrs.Count > 1)
                    for (int i = 0; i < scrs.Count; i++)
                    {
                        var dev = scrs[i].DeviceName; object ov;
                        string cur = c.Extra != null && c.Extra.TryGetValue("size." + dev, out ov) ? J.AsStr(ov) : "";
                        a = g.Row(ref y, x, w, "", "Size on Display " + (i + 1), scrs[i].Bounds.Width + "×" + scrs[i].Bounds.Height + (scrs[i].Primary ? "  ·  primary" : ""));
                        g.Segmented(a, new[] { "Default", "Small", "Medium", "Large" }, new[] { "", "S", "M", "L" }, cur, v => { c.SetSizeFor(dev, v); W.Changed(); });
                    }
            }
            y += g.S(10);
            a = g.Row(ref y, x, w, Icons.Apps, "Widgets on the bar", c.Widgets.Count + " widgets  ·  arrange them on the Widgets page", 0, () => W.Navigate("widgets"));
            Ui.Icon(g.G, Icons.ChevronRight, new RectangleF(a.Right - g.S(20), a.Y, g.S(20), a.Height), W.P.Dim, g.S(12));
            return y;
        }
    }

    // =====================================================================================
    class SetAboutPage : SetPage
    {
        public override string Title { get { return "About"; } }
        class ProcInfo { public int Pid; public long Ws, Priv; public DateTime Start; }
        volatile List<ProcInfo> bars = new List<ProcInfo>(); long selfWs, selfPriv; int lastPoll = -100000; volatile bool busy; string status;

        public override void Tick()
        {
            if (busy || Environment.TickCount - lastPoll < 2000) return;
            lastPoll = Environment.TickCount; busy = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var l = new List<ProcInfo>();
                try
                {
                    int me = Process.GetCurrentProcess().Id;
                    foreach (var p in Process.GetProcessesByName("NovaBar"))
                        using (p)
                        {
                            try
                            {
                                if (p.Id == me) { selfWs = p.WorkingSet64; selfPriv = p.PrivateMemorySize64; continue; }
                                if (!IsBar(p.Id)) continue;
                                l.Add(new ProcInfo { Pid = p.Id, Ws = p.WorkingSet64, Priv = p.PrivateMemorySize64, Start = p.StartTime });
                            }
                            catch { }
                        }
                }
                catch { }
                bars = l; busy = false; W.RequestRepaint();
            });
        }

        static readonly Dictionary<int, bool> barPid = new Dictionary<int, bool>();
        /// <summary>The bar runs without arguments; skip --settings/--render/--checklist processes. WMI only once per new PID.</summary>
        static bool IsBar(int pid)
        {
            bool v; lock (barPid) if (barPid.TryGetValue(pid, out v)) return v;
            v = true;
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid))
                    foreach (System.Management.ManagementObject o in s.Get()) using (o) { var cl = o["CommandLine"] as string; if (cl != null && cl.Contains(" --")) v = false; }
            }
            catch { }
            lock (barPid) barPid[pid] = v;
            return v;
        }

        public override float Paint(SetG g, float x, float y, float w)
        {
            if (lastPoll < 0) Tick();
            // hero
            var hero = new RectangleF(x, y, w, g.S(112)); g.Card(hero);
            var logo = new RectangleF(x + g.S(22), y + g.S(24), g.S(64), g.S(64));
            SetWindow.DrawLogo(g.G, logo, W.T.Accent);
            Ui.Text(g.G, "NovaBar", Ui.FontSemi(g.S(22)), W.P.Text, new RectangleF(logo.Right + g.S(18), y + g.S(26), g.S(300), g.S(30)));
            Ui.Text(g.G, "Version " + SettingsApp.Version + "  ·  " + WidgetRegistry.All.Count() + " widgets  ·  native, light and fast", g.F(13), W.P.Dim, new RectangleF(logo.Right + g.S(18), y + g.S(58), w - g.S(140), g.S(20)));
            y += hero.Height + g.S(22);

            y = g.Section(x, y, w, "Bar process");
            var list = bars;
            if (list.Count == 0)
            {
                var a = g.Row(ref y, x, w, Icons.Power, "The bar is not running", status ?? "Start it to see its memory use here");
                g.Button(new RectangleF(a.Right - g.S(120), a.Y + (a.Height - g.S(34)) / 2, g.S(120), g.S(34)), "Start bar", StartBar, true, Icons.Play);
            }
            foreach (var p in list)
            {
                var a = g.Row(ref y, x, w, Icons.Memory, "Memory: " + Ui.Bytes(p.Ws) + " working set", "Private " + Ui.Bytes(p.Priv) + "  ·  PID " + p.Pid + "  ·  running " + Ui.Duration(DateTime.Now - p.Start));
                float mb = p.Ws / 1048576f, frac = Math.Min(1f, mb / 200f);
                Ui.Progress(g.G, new RectangleF(a.Right - g.S(160), a.Y + a.Height / 2 - g.S(3), g.S(160), g.S(6)), frac, mb < 80 ? W.T.Good : mb < 150 ? W.T.Warn : W.T.Bad, W.T.Track);
            }
            if (list.Count > 0)
            {
                var a = g.Row(ref y, x, w, Icons.Refresh, "Restart or quit", status ?? "Restarting reloads every widget");
                float bx = a.Right;
                bx -= g.S(110); g.Button(new RectangleF(bx, a.Y + (a.Height - g.S(34)) / 2, g.S(110), g.S(34)), "Quit bar", () => StopBar(false), false, Icons.Power);
                bx -= g.S(130); g.Button(new RectangleF(bx, a.Y + (a.Height - g.S(34)) / 2, g.S(120), g.S(34)), "Restart bar", () => StopBar(true), true, Icons.Refresh);
            }
            var s = g.Row(ref y, x, w, "", "This Settings window", "Working set " + Ui.Bytes(selfWs) + "  ·  private " + Ui.Bytes(selfPriv) + "  ·  closes completely when you close it");

            y += g.S(18);
            y = g.Section(x, y, w, "Files");
            var f = g.Row(ref y, x, w, Icons.Folder, "Configuration folder", Paths.Root);
            g.Button(new RectangleF(f.Right - g.S(130), f.Y + (f.Height - g.S(34)) / 2, g.S(130), g.S(34)), "Open folder", () => { try { Process.Start("explorer.exe", "\"" + Paths.Root + "\""); } catch { } }, false, Icons.Folder);
            f = g.Row(ref y, x, w, Icons.File, "config.json", "Edited live by this window; the bar reloads it automatically");
            g.Button(new RectangleF(f.Right - g.S(130), f.Y + (f.Height - g.S(34)) / 2, g.S(130), g.S(34)), "Open file", () => { try { W.SaveNow(); Process.Start("notepad.exe", "\"" + Paths.ConfigFile + "\""); } catch { } }, false, Icons.Edit);
            return y;
        }

        void StartBar()
        {
            try { Process.Start(Application.ExecutablePath); status = "Starting…"; lastPoll = Environment.TickCount - 1200; } catch (Exception e) { status = e.Message; }
        }
        void StopBar(bool restart)
        {
            W.SaveNow();
            var pids = bars.Select(b => b.Pid).ToList();
            status = restart ? "Restarting…" : "Quitting…";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (var pid in pids)
                {
                    try
                    {
                        // polite close first (lets the bar restore the taskbar), then force
                        Native.EnumWindows((h, l) => { uint p; Native.GetWindowThreadProcessId(h, out p); if (p == pid) Native.PostMessage(h, SetNative.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); return true; }, IntPtr.Zero);
                        using (var pr = Process.GetProcessById(pid)) { if (!pr.WaitForExit(3000)) { pr.Kill(); pr.WaitForExit(2000); } }
                    }
                    catch { }
                }
                if (restart) try { Process.Start(Application.ExecutablePath); } catch { }
                status = restart ? "Restarted" : "Bar stopped";
                lastPoll = Environment.TickCount - 1000;
                W.RequestRepaint();
            });
        }
    }
}
