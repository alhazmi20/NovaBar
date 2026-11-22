// Currency — open.er-api.com (free, no key), USD base, cached 6 h in memory + on disk. Hub key "prod.fx".
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NovaBar
{
    public class ProdFxSnap { public Dictionary<string, double> Rates; public DateTime Updated; public bool Offline; }

    public static class ProdFx
    {
        public const string Key = "prod.fx";
        const string Url = "https://open.er-api.com/v6/latest/USD";
        static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
        static ProdFxSnap cur; static DateTime lastTry;
        public static void Define() { Hub.Define(Key, 30 * 60 * 1000, Sample); }
        static string CacheFile { get { return Path.Combine(Paths.CacheDir, "fx-usd.json"); } }

        static ProdFxSnap FromJson(string json, bool offline)
        {
            var d = J.ParseObj(json); if (d == null || J.Str(d, "result") != "success") return null;
            var r = J.Obj(d, "rates"); if (r == null || r.Count < 10) return null;
            return new ProdFxSnap { Rates = r.ToDictionary(x => x.Key, x => J.AsNum(x.Value), StringComparer.OrdinalIgnoreCase), Updated = ProdUtil.FromUnix(J.Num(d, "time_last_update_unix")), Offline = offline };
        }
        static object Sample()
        {
            TimeSpan age = TimeSpan.MaxValue;
            try { if (File.Exists(CacheFile)) { age = DateTime.Now - File.GetLastWriteTime(CacheFile); if (cur == null) cur = FromJson(File.ReadAllText(CacheFile, Encoding.UTF8), false); } } catch { }
            if (cur != null && age < Ttl) return cur;                                        // fresh enough (6 h)
            if (cur != null && DateTime.Now - lastTry < TimeSpan.FromMinutes(30)) return cur;  // throttle retries
            lastTry = DateTime.Now;
            var body = Http.Get(Url);
            var s = body != null ? FromJson(body, false) : null;
            if (s != null) { cur = s; try { File.WriteAllText(CacheFile, body, Encoding.UTF8); } catch { } }
            else if (cur != null && !cur.Offline) cur = new ProdFxSnap { Rates = cur.Rates, Updated = cur.Updated, Offline = true };
            return cur;
        }
        public static double Rate(ProdFxSnap s, string from, string to)
        {
            double a, b;
            if (s == null || !s.Rates.TryGetValue(from, out a) || !s.Rates.TryGetValue(to, out b) || a <= 0) return double.NaN;
            return b / a;
        }
        public static string Fmt(double v)
        {
            if (double.IsNaN(v)) return "—";
            double a = Math.Abs(v);
            string f = a >= 1000 ? "#,0.00" : a >= 1 ? "#,0.00##" : a >= 0.01 ? "0.0000" : "0.000000";
            return v.ToString(f, CultureInfo.InvariantCulture);
        }
        public static readonly string[][] Common = {
            new[]{"USD","US Dollar"}, new[]{"EUR","Euro"}, new[]{"GBP","British Pound"}, new[]{"SAR","Saudi Riyal"}, new[]{"AED","UAE Dirham"}, new[]{"JPY","Japanese Yen"},
            new[]{"CNY","Chinese Yuan"}, new[]{"INR","Indian Rupee"}, new[]{"CAD","Canadian Dollar"}, new[]{"AUD","Australian Dollar"}, new[]{"CHF","Swiss Franc"}, new[]{"KWD","Kuwaiti Dinar"},
            new[]{"QAR","Qatari Riyal"}, new[]{"BHD","Bahraini Dinar"}, new[]{"OMR","Omani Rial"}, new[]{"EGP","Egyptian Pound"}, new[]{"TRY","Turkish Lira"}, new[]{"KRW","Korean Won"},
            new[]{"SGD","Singapore Dollar"}, new[]{"HKD","Hong Kong Dollar"}, new[]{"SEK","Swedish Krona"}, new[]{"BRL","Brazilian Real"}, new[]{"MXN","Mexican Peso"}, new[]{"PKR","Pakistani Rupee"} };
        /// <summary>Offline sample rates (per USD) used until the first fetch completes.</summary>
        public static ProdFxSnap SampleSnap()
        {
            var r = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { "USD", 1 }, { "EUR", 0.92 }, { "GBP", 0.79 }, { "SAR", 3.75 }, { "AED", 3.6725 }, { "JPY", 151.2 }, { "CNY", 7.21 }, { "INR", 83.4 }, { "CAD", 1.37 }, { "AUD", 1.52 }, { "CHF", 0.88 }, { "KWD", 0.307 }, { "QAR", 3.64 }, { "BHD", 0.376 }, { "OMR", 0.385 }, { "EGP", 48.5 }, { "TRY", 34.1 }, { "KRW", 1380 }, { "SGD", 1.34 }, { "HKD", 7.8 }, { "SEK", 10.6 }, { "BRL", 5.6 }, { "MXN", 18.9 }, { "PKR", 278 } };
            return new ProdFxSnap { Rates = r, Updated = DateTime.Now, Offline = true };
        }
    }

    [Widget("currency", "Currency", "Productivity", "Convert between currencies at current rates.", RefName = "Currency", Data = DataKind.Live)]
    public class ProdCurrencyWidget : Widget
    {
        int picking = -1; // -1 none, 0 picking "from", 1 picking "to"
        public override void OnStart() { ProdFx.Define(); Hub.Subscribe(ProdFx.Key, this); }
        public override void OnStop() { Hub.Unsubscribe(ProdFx.Key, this); }

        string From { get { return J.Str(State, "from", "USD"); } set { State["from"] = value; SaveState(); } }
        string To { get { var d = AppConfig.Current.Currency; if (string.IsNullOrEmpty(d) || d == "USD") d = "SAR"; return J.Str(State, "to", d); } set { State["to"] = value; SaveState(); } }
        double Amount { get { return J.Num(State, "amount", 1); } set { State["amount"] = value; SaveState(); } }
        List<string> Favs
        {
            get
            {
                var a = J.Arr(State, "favs");
                if (a == null) return new List<string> { "USD/SAR", "EUR/USD", "GBP/USD", "USD/AED", "EUR/SAR" };
                return a.Cast<object>().Select(o => J.AsStr(o)).Where(x => x.Contains("/")).ToList();
            }
            set { State["favs"] = value.Cast<object>().ToArray(); SaveState(); }
        }
        ProdFxSnap Snap { get { return Hub.Get<ProdFxSnap>(ProdFx.Key) ?? ProdFx.SampleSnap(); } }

        public override int Measure(DrawCtx c)
        {
            var s = Snap; string t = Line(s);
            return Math.Max(c.Si(120), Ui.MeasureW(c.G, t, c.FS(12.5f)) + c.Si(48));
        }
        string Line(ProdFxSnap s) { return "1 " + From + " = " + ProdFx.Fmt(ProdFx.Rate(s, From, To)) + " " + To; }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var g = c.G; var s = Snap;
            Ui.Icon(g, Icons.Money, new RectangleF(b.X + c.S(9), b.Y, c.S(18), b.Height), c.T.Good, c.S(15));
            float x = b.X + c.S(32), w = b.Right - c.S(10) - x;
            Ui.Text(g, Line(s), c.FS(12.5f), c.T.Text, new RectangleF(x, b.Y + c.S(3), w, c.S(17)));
            Ui.Text(g, Hub.Get<ProdFxSnap>(ProdFx.Key) == null ? "loading rates…" : s.Offline ? "offline · cached" : "updated " + Ago(s.Updated), c.F(10), c.T.TextDim, new RectangleF(x, b.Y + c.S(19), w, c.S(13)));
        }
        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t; if (d.TotalMinutes < 60) return Math.Max(1, (int)d.TotalMinutes) + "m ago";
            if (d.TotalHours < 48) return (int)d.TotalHours + "h ago"; return t.ToString("MMM d", ProdUtil.Inv);
        }

        public override Size PanelSize(float s) { return new Size((int)(340 * s), (int)(392 * s)); }
        public override void OnPanelOpen() { picking = -1; }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var g = c.G; var s = Snap;
            c.PanelHeader("Currency", Hub.Get<ProdFxSnap>(ProdFx.Key) == null ? "sample rates" : "rates " + Ago(s.Updated), Icons.Money);
            float x = b.X + c.S(16), w = b.Width - c.S(32), y = b.Y + c.S(46);
            string from = From, to = To; double amt = Amount;
            // amount + from | swap | to
            float ch = c.S(34), cw = c.S(72), sw = c.S(30);
            var toR = new RectangleF(x + w - cw, y, cw, ch);
            var swR = new RectangleF(toR.X - c.S(6) - sw, y + (ch - sw) / 2, sw, sw);
            var frR = new RectangleF(swR.X - c.S(6) - cw, y, cw, ch);
            var inR = new Rectangle((int)x, (int)y + c.Si(2), (int)(frR.X - c.S(8) - x), c.Si(30));
            c.Input("fx-amount", inR, "Amount", v => { }, v => { double d; if (double.TryParse((v ?? "").Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= 0) { Amount = d; Invalidate(); } }, false, amt.ToString("0.##", CultureInfo.InvariantCulture));
            Chip(c, frR, from, picking == 0, () => { picking = picking == 0 ? -1 : 0; Invalidate(); });
            Chip(c, toR, to, picking == 1, () => { picking = picking == 1 ? -1 : 1; Invalidate(); });
            Ui.FillRound(g, swR, sw / 2, c.IsHover(swR) ? c.T.TileHover : c.T.Tile);
            c.IconButton(swR, "", () => { var f = From; From = To; To = f; Invalidate(); }, false, 12);
            y += ch + c.S(16);
            // result
            double rate = ProdFx.Rate(s, from, to);
            Ui.Text(g, ProdFx.Fmt(amt * rate) + " " + to, c.F(26, true), c.T.Text, new RectangleF(x, y, w - c.S(30), c.S(34)));
            var fav = from + "/" + to; bool isFav = Favs.Contains(fav);
            var star = new RectangleF(x + w - c.S(28), y + c.S(4), c.S(28), c.S(28));
            Ui.Icon(g, isFav ? "" : Icons.Star, star, isFav ? c.T.Warn : c.T.TextDim, c.S(14));
            c.Hit(star, () => { var l = Favs; if (l.Contains(fav)) l.Remove(fav); else { l.Insert(0, fav); if (l.Count > 6) l.RemoveAt(l.Count - 1); } Favs = l; Invalidate(); });
            y += c.S(36);
            Ui.Text(g, ProdFx.Fmt(amt) + " " + from + "  ·  1 " + to + " = " + ProdFx.Fmt(1 / rate) + " " + from, c.F(11), c.T.TextDim, new RectangleF(x, y, w, c.S(16)));
            y += c.S(26);
            using (var p = new Pen(c.T.Track, 1f)) g.DrawLine(p, x, y, x + w, y);
            y += c.S(10);

            if (picking >= 0)
            {
                Ui.Text(g, picking == 0 ? "CONVERT FROM" : "CONVERT TO", c.FS(9.5f), c.T.TextFaint, new RectangleF(x, y, w, c.S(14)));
                y += c.S(20);
                int cols = 4; float gw = (w - c.S(6) * (cols - 1)) / cols, gh = c.S(30);
                for (int i = 0; i < ProdFx.Common.Length; i++)
                {
                    var r = new RectangleF(x + (i % cols) * (gw + c.S(6)), y + (i / cols) * (gh + c.S(6)), gw, gh);
                    if (r.Bottom > b.Bottom - c.S(8)) break;
                    var code = ProdFx.Common[i][0]; bool sel = code == (picking == 0 ? from : to);
                    Ui.FillRound(g, r, c.S(8), sel ? c.T.AccentA(60) : c.IsHover(r) ? c.T.TileHover : c.T.Tile);
                    Ui.Text(g, code, c.FS(12), sel ? c.T.Accent : c.T.Text, r, 0);
                    int side = picking;
                    c.Hit(r, () => { if (side == 0) From = code; else To = code; picking = -1; Invalidate(); });
                }
                return;
            }
            Ui.Text(g, "FAVOURITES", c.FS(9.5f), c.T.TextFaint, new RectangleF(x, y, w, c.S(14)));
            y += c.S(20);
            float rh = c.S(32);
            foreach (var f in Favs)
            {
                if (y + rh > b.Bottom - c.S(8)) break;
                var parts = f.Split('/'); string a = parts[0], bb = parts[1];
                var rr = new RectangleF(x - c.S(6), y, w + c.S(12), rh - c.S(2));
                bool on = a == from && bb == to;
                if (on) Ui.FillRound(g, rr, c.S(8), c.T.AccentA(c.T.Dark ? 45 : 32)); else if (c.IsHover(rr)) Ui.FillRound(g, rr, c.S(8), c.T.Tile);
                Ui.Text(g, a + "  →  " + bb, c.FS(12.5f), c.T.Text, new RectangleF(x, y, w, rh - c.S(2)));
                Ui.Text(g, ProdFx.Fmt(ProdFx.Rate(s, a, bb)), c.Mono(12.5f), on ? c.T.Accent : c.T.TextDim, new RectangleF(x, y, w, rh - c.S(2)), 1);
                c.Hit(rr, () => { From = a; To = bb; Invalidate(); });
                y += rh;
            }
        }
        static void Chip(DrawCtx c, RectangleF r, string code, bool active, Action onClick)
        {
            Ui.FillRound(c.G, r, c.S(10), active ? c.T.AccentA(60) : c.IsHover(r) ? c.T.TileHover : c.T.Tile);
            Ui.Text(c.G, code, c.FS(13), active ? c.T.Accent : c.T.Text, new RectangleF(r.X + c.S(10), r.Y, r.Width - c.S(10), r.Height));
            Ui.Icon(c.G, Icons.ChevronDown, new RectangleF(r.Right - c.S(22), r.Y, c.S(16), r.Height), c.T.TextDim, c.S(9));
            c.Hit(r, onClick);
        }
    }
}
