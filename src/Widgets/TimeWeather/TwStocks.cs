// Stocks (Yahoo chart API, no key) and Crypto (Binance public API). Hub keys "tw.stock.<SYM>" and "tw.crypto.<SYM>".
// Stocks poll every 2 min near market hours, otherwise at most every 30 min. Last values cached to disk.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;

namespace NovaBar
{
    public class TwQuote
    {
        public string Sym = "", Name = "", Currency = "USD"; public double Price = double.NaN, Prev = double.NaN, High = double.NaN, Low = double.NaN, Open = double.NaN;
        public double[] Line = new double[0]; public long RegStart, RegEnd; public DateTime FetchedUtc; public bool Stale, Offline;
        public double Change { get { return Price - Prev; } }
        public double Pct { get { return Prev > 0 ? (Price - Prev) / Prev * 100 : 0; } }
        public bool Ok { get { return !Offline && !double.IsNaN(Price); } }
        public TwQuote AsStale() { var q = (TwQuote)MemberwiseClone(); q.Stale = true; return q; }
    }

    public static class TwMarket
    {
        public const string UA = "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
        static readonly Dictionary<string, Timer> retries = new Dictionary<string, Timer>();
        public static string Money(double v, string cur)
        {
            if (double.IsNaN(v)) return "—";
            string pre = cur == "USD" || cur == "USDT" || cur == "USDC" || cur == "FDUSD" || cur == "BUSD" ? "$" : cur == "EUR" ? "€" : cur == "GBP" || cur == "GBp" ? "£" : cur == "JPY" ? "¥" : "";
            string num = Math.Abs(v) >= 1000 ? v.ToString("#,0.00", TwT.Inv) : Math.Abs(v) >= 1 ? v.ToString("#,0.00", TwT.Inv) : v.ToString("0.0000", TwT.Inv);
            if (Math.Abs(v) >= 100000) num = v.ToString("#,0", TwT.Inv);
            return pre.Length > 0 ? pre + num : num + (string.IsNullOrEmpty(cur) ? "" : " " + cur);
        }
        public static string Pct(double p) { return (p >= 0 ? "+" : "−") + Math.Abs(p).ToString("0.00", TwT.Inv) + "%"; }
        public static Color Col(Theme t, double p) { return p >= 0 ? t.Good : t.Bad; }
        static void Retry(string key)
        {
            lock (retries) { if (retries.ContainsKey(key)) return; retries[key] = new Timer(_ => { lock (retries) { retries[key].Dispose(); retries.Remove(key); } Hub.Refresh(key); }, null, 60000, Timeout.Infinite); }
        }

        // ---------- stocks ----------
        public static string StockKey(string sym)
        {
            sym = (sym ?? "").Trim().ToUpperInvariant(); if (sym.Length == 0) sym = "AAPL";
            var key = "tw.stock." + sym; var s = sym;
            Hub.Define(key, 120000, () => SampleStock(key, s));
            return key;
        }
        static bool Near(TwQuote q)
        {
            long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            if (q.RegStart > 0 && now >= q.RegStart - 45 * 60 && now <= q.RegEnd + 30 * 60) return true;
            // period may be yesterday's: also wake up for the usual US session window on weekdays
            var u = DateTime.UtcNow; if (u.DayOfWeek == DayOfWeek.Saturday || u.DayOfWeek == DayOfWeek.Sunday) return false;
            double h = u.Hour + u.Minute / 60.0; return h >= 12.75 && h <= 21.5 && (q.RegEnd == 0 || now > q.RegEnd + 12 * 3600);
        }
        static object SampleStock(string key, string sym)
        {
            var prev = Hub.Get<TwQuote>(key) ?? CachedStock(sym);
            if (prev != null && prev.Ok && !prev.Stale && !Near(prev) && (DateTime.UtcNow - prev.FetchedUtc).TotalMinutes < 30) return prev;
            var txt = Http.Get("https://query1.finance.yahoo.com/v8/finance/chart/" + Http.Esc(sym) + "?range=1d&interval=5m", UA);
            var q = ParseStock(txt, sym);
            if (q != null) { q.FetchedUtc = DateTime.UtcNow; TwCache.Write("stock-" + sym, new Dictionary<string, object> { { "t", q.FetchedUtc.Ticks.ToString(TwT.Inv) }, { "raw", txt } }); return q; }
            Retry(key);
            if (prev == null) return new TwQuote { Sym = sym, Offline = true, Stale = true };
            return prev.Stale ? prev : prev.AsStale();
        }
        static readonly Dictionary<string, TwQuote> diskStock = new Dictionary<string, TwQuote>();
        public static TwQuote CachedStock(string sym)
        {
            lock (diskStock)
            {
                TwQuote q; if (diskStock.TryGetValue(sym, out q)) return q;
                var d = TwCache.Read("stock-" + sym);
                if (d != null) { q = ParseStock(J.Str(d, "raw"), sym); long tk; if (q != null && long.TryParse(J.Str(d, "t"), out tk)) q.FetchedUtc = new DateTime(tk, DateTimeKind.Utc); }
                diskStock[sym] = q; return q;
            }
        }
        public static TwQuote Stock(string sym) { var k = StockKey(sym); return Hub.Get<TwQuote>(k) ?? CachedStock(k.Substring(9)); }
        static TwQuote ParseStock(string txt, string sym)
        {
            var o = J.ParseObj(txt); var r = J.Get(o, "chart.result.0"); if (r == null) return null;
            var m = J.Obj(r, "meta"); if (m == null) return null;
            var q = new TwQuote { Sym = sym, Currency = J.Str(m, "currency", "USD"), Name = J.Str(m, "shortName", J.Str(m, "longName", "")) };
            q.Price = J.Num(m, "regularMarketPrice", double.NaN);
            q.Prev = J.Num(m, "chartPreviousClose", J.Num(m, "previousClose", double.NaN));
            q.High = J.Num(m, "regularMarketDayHigh", double.NaN); q.Low = J.Num(m, "regularMarketDayLow", double.NaN);
            q.RegStart = (long)J.Num(m, "currentTradingPeriod.regular.start"); q.RegEnd = (long)J.Num(m, "currentTradingPeriod.regular.end");
            var closes = J.Arr(r, "indicators.quote.0.close"); var opens = J.Arr(r, "indicators.quote.0.open");
            var l = new List<double>();
            if (closes != null) foreach (var v in closes) if (v != null) l.Add(J.Num(v, null));
            q.Line = l.ToArray();
            if (opens != null) foreach (var v in opens) if (v != null) { q.Open = J.Num(v, null); break; }
            if (double.IsNaN(q.Price) && l.Count > 0) q.Price = l[l.Count - 1];
            if (double.IsNaN(q.Price)) return null;
            if (double.IsNaN(q.High) && l.Count > 0) q.High = l.Max(); if (double.IsNaN(q.Low) && l.Count > 0) q.Low = l.Min();
            return q;
        }

        // ---------- crypto ----------
        public static string CryptoKey(string sym)
        {
            sym = (sym ?? "").Trim().ToUpperInvariant().Replace("/", "").Replace("-", ""); if (sym.Length == 0) sym = "BTCUSDT";
            var key = "tw.crypto." + sym; var s = sym;
            Hub.Define(key, 60000, () => SampleCoin(key, s));
            return key;
        }
        static readonly string[] hosts = { "https://api.binance.com", "https://data-api.binance.vision" };
        static string BGet(string path) { foreach (var h in hosts) { var t = Http.Get(h + path); if (t != null) return t; } return null; }
        static object SampleCoin(string key, string sym)
        {
            var prev = Hub.Get<TwCoin>(key) ?? CachedCoin(sym);
            var tk = J.ParseObj(BGet("/api/v3/ticker/24hr?symbol=" + Http.Esc(sym)));
            if (tk == null || J.Get(tk, "lastPrice") == null)
            {
                Retry(key);
                if (prev == null) return new TwCoin { Sym = sym, Offline = true, Stale = true };
                return prev.Stale ? prev : prev.AsStale();
            }
            var c = new TwCoin { Sym = sym, Price = J.Num(tk, "lastPrice"), Pct = J.Num(tk, "priceChangePercent"), High = J.Num(tk, "highPrice"), Low = J.Num(tk, "lowPrice"), Vol = J.Num(tk, "quoteVolume"), Open = J.Num(tk, "openPrice"), FetchedUtc = DateTime.UtcNow };
            // the day's line: 96 x 15m klines, refreshed every 5 minutes (live price appended in between)
            if (prev != null && prev.Ok && prev.Line.Length > 1 && (DateTime.UtcNow - prev.LineUtc).TotalMinutes < 5) { c.Line = prev.Line; c.LineUtc = prev.LineUtc; }
            else
            {
                var kl = J.Parse(BGet("/api/v3/klines?symbol=" + Http.Esc(sym) + "&interval=15m&limit=96")) as IList;
                if (kl != null && kl.Count > 1) { c.Line = kl.Cast<object>().Select(k => J.Num(J.Get(k, "4"), null)).ToArray(); c.LineUtc = DateTime.UtcNow; }
                else if (prev != null) { c.Line = prev.Line; c.LineUtc = prev.LineUtc; }
            }
            TwCache.Write("crypto-" + sym, new Dictionary<string, object> { { "p", c.Price }, { "pct", c.Pct }, { "h", c.High }, { "l", c.Low }, { "v", c.Vol }, { "o", c.Open }, { "t", c.FetchedUtc.Ticks.ToString(TwT.Inv) }, { "line", c.Line.Cast<object>().ToArray() } });
            return c;
        }
        static readonly Dictionary<string, TwCoin> diskCoin = new Dictionary<string, TwCoin>();
        public static TwCoin CachedCoin(string sym)
        {
            lock (diskCoin)
            {
                TwCoin c; if (diskCoin.TryGetValue(sym, out c)) return c;
                var d = TwCache.Read("crypto-" + sym);
                if (d != null)
                {
                    c = new TwCoin { Sym = sym, Price = J.Num(d, "p"), Pct = J.Num(d, "pct"), High = J.Num(d, "h"), Low = J.Num(d, "l"), Vol = J.Num(d, "v"), Open = J.Num(d, "o") };
                    long t; if (long.TryParse(J.Str(d, "t"), out t)) c.FetchedUtc = new DateTime(t, DateTimeKind.Utc);
                    var l = J.Arr(d, "line"); if (l != null) c.Line = l.Cast<object>().Select(v => J.Num(v, null)).ToArray();
                }
                diskCoin[sym] = c; return c;
            }
        }
        public static TwCoin Coin(string sym) { var k = CryptoKey(sym); return Hub.Get<TwCoin>(k) ?? CachedCoin(k.Substring(10)); }
        public static void SplitPair(string sym, out string bas, out string quote)
        {
            foreach (var qq in new[] { "FDUSD", "USDT", "USDC", "BUSD", "TUSD", "USD", "EUR", "TRY", "BTC", "ETH", "BNB" })
                if (sym.Length > qq.Length && sym.EndsWith(qq)) { bas = sym.Substring(0, sym.Length - qq.Length); quote = qq; return; }
            bas = sym; quote = "";
        }

        /// <summary>Line chart with optional dashed baseline (previous close).</summary>
        public static void Chart(Graphics g, RectangleF r, double[] line, double baseLine, Color col, Theme t, float sc, bool fill = true)
        {
            if (line == null || line.Length < 2) return;
            double lo = line.Min(), hi = line.Max();
            if (!double.IsNaN(baseLine)) { lo = Math.Min(lo, baseLine); hi = Math.Max(hi, baseLine); }
            double pad = (hi - lo) * 0.08; if (pad <= 0) pad = Math.Abs(hi) * 0.001 + 1e-9; lo -= pad; hi += pad;
            if (!double.IsNaN(baseLine))
            {
                float by = r.Bottom - (float)((baseLine - lo) / (hi - lo)) * r.Height;
                using (var p = new Pen(Ui.WithA(t.TextFaint, 160), Math.Max(1f, sc)) { DashStyle = DashStyle.Dot }) g.DrawLine(p, r.X, by, r.Right, by);
            }
            Ui.Sparkline(g, r, line, col, fill, Math.Max(1.3f, 1.5f * sc), lo, hi);
        }
    }

    public class TwCoin
    {
        public string Sym = ""; public double Price = double.NaN, Pct, High, Low, Vol, Open; public double[] Line = new double[0];
        public DateTime FetchedUtc, LineUtc; public bool Stale, Offline;
        public bool Ok { get { return !Offline && !double.IsNaN(Price); } }
        public TwCoin AsStale() { var c = (TwCoin)MemberwiseClone(); c.Stale = true; return c; }
        public double[] LiveLine { get { if (Line.Length < 2 || double.IsNaN(Price)) return Line; var l = Line.ToList(); l[l.Count - 1] = Price; return l.ToArray(); } }
    }

    // =====================================================================================

    [Widget("stock", "Stock Chart", "Stocks", "One ticker with its intraday line.", RefName = "Stock Chart", Data = DataKind.Live)]
    public class TwStockWidget : TwBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Text("ticker", "Ticker", "AAPL") }; } }
        string key;
        string Sym { get { var s = SStr("ticker").Trim().ToUpperInvariant(); return s.Length == 0 ? "AAPL" : s; } }
        void Ensure() { var k = TwMarket.StockKey(Sym); if (k != key) { if (key != null) Hub.Unsubscribe(key, this); key = k; Hub.Subscribe(key, this); } }
        public override void OnStart() { Ensure(); }
        public override void OnStop() { if (key != null) Hub.Unsubscribe(key, this); key = null; }
        TwQuote Q { get { return TwMarket.Stock(Sym); } }
        public override int Measure(DrawCtx c)
        {
            var q = Q; float a = Math.Max(TwT.W(c.G, Sym, c.FS(10.5f)), TwT.W(c.G, q != null && q.Ok ? TwMarket.Pct(q.Pct) : "Offline", c.FS(9.5f)));
            float p = TwT.W(c.G, q != null && q.Ok ? TwMarket.Money(q.Price, q.Currency) : "—", c.FS(13));
            return (int)Math.Ceiling(c.S(11) + a + c.S(10) + c.S(52) + c.S(10) + p + c.S(11));
        }
        public override void Draw(DrawCtx c)
        {
            if (key == null || !key.EndsWith("." + Sym)) Ensure();
            var g = c.G; var t = c.T; var b = c.Bounds; var q = Q; float cy = b.Y + b.Height / 2f, x = b.X + c.S(11);
            bool ok = q != null && q.Ok; var col = ok ? TwMarket.Col(t, q.Pct) : t.TextFaint;
            float a = Math.Max(TwT.Str(g, Sym, c.FS(10.5f), t.Text, x, cy - c.S(2)),
                               TwT.Str(g, ok ? TwMarket.Pct(q.Pct) : (q == null ? "Loading" : "Offline"), c.FS(9.5f), col, x, cy + c.S(10)));
            x += a + c.S(10);
            var cr = new RectangleF(x, cy - c.S(9), c.S(52), c.S(18));
            if (ok && q.Line.Length > 1) TwMarket.Chart(g, cr, q.Line, q.Prev, col, t, c.Scale);
            else using (var p = new Pen(t.Track, c.S(1.5f)) { DashStyle = DashStyle.Dot }) g.DrawLine(p, cr.X, cy, cr.Right, cy);
            x += cr.Width + c.S(10);
            var f = c.FS(13);
            TwT.Str(g, ok ? TwMarket.Money(q.Price, q.Currency) : "—", f, ok ? (q.Stale ? t.TextDim : t.Text) : t.TextFaint, x, TwT.Mid(b, f));
        }
        public override Size PanelSize(float s) { return new Size((int)(340 * s), (int)(268 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var q = Q;
            bool ok = q != null && q.Ok;
            float y = c.PanelHeader(Sym, ok ? (q.Stale ? "Offline · last values" : q.Name) : null, Icons.Chart);
            float x0 = b.X + c.S(16), x1 = b.Right - c.S(16);
            if (!ok)
            {
                Ui.Text(g, q == null ? "Loading quote…" : "Market data is offline", c.FS(13), t.Text, new RectangleF(b.X, b.Y + b.Height / 2 - c.S(20), b.Width, c.S(20)), 0);
                Ui.Text(g, "Yahoo Finance · retrying every minute", c.F(11), t.TextDim, new RectangleF(b.X, b.Y + b.Height / 2 + c.S(2), b.Width, c.S(18)), 0);
                return;
            }
            var col = TwMarket.Col(t, q.Pct);
            var pf = TwF.Semi(c.S(30)); float bl = y + c.S(4) + TwT.Cap(pf);
            float pw = TwT.Str(g, TwMarket.Money(q.Price, q.Currency), pf, t.Text, x0, bl);
            TwT.Str(g, (q.Change >= 0 ? "+" : "−") + Math.Abs(q.Change).ToString("0.00", TwT.Inv) + "  " + TwMarket.Pct(q.Pct), c.FS(13), col, x0 + pw + c.S(10), bl);
            y = bl + c.S(14);
            var cr = new RectangleF(x0, y, x1 - x0, c.S(110));
            TwMarket.Chart(g, cr, q.Line, q.Prev, col, t, c.Scale);
            if (!double.IsNaN(q.Prev)) Ui.Text(g, "Prev " + q.Prev.ToString("0.00", TwT.Inv), c.F(9.5f), t.TextFaint, new RectangleF(x0, cr.Bottom + c.S(2), x1 - x0, c.S(14)), 1);
            y = cr.Bottom + c.S(22);
            string[] lab = { "Open", "High", "Low", "Prev close" };
            double[] val = { q.Open, q.High, q.Low, q.Prev };
            float cw = (x1 - x0) / 4;
            for (int i = 0; i < 4; i++)
            {
                TwT.Str(g, lab[i], c.F(10), t.TextDim, x0 + i * cw, y + c.S(8));
                TwT.Str(g, double.IsNaN(val[i]) ? "—" : val[i].ToString(val[i] >= 1000 ? "#,0.0" : "0.00", TwT.Inv), c.FS(12.5f), t.Text, x0 + i * cw, y + c.S(26));
            }
        }
    }

    [Widget("watchlist", "Stock Watchlist", "Stocks", "Three tickers at a glance.", RefName = "Stock Watchlist", Data = DataKind.Live)]
    public class TwWatchlistWidget : TwBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Text("tickers", "Tickers (comma separated)", "AAPL, MSFT, NVDA") }; } }
        List<string> keys = new List<string>(); string sig;
        List<string> Syms { get { var l = SStr("tickers").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().Take(3).ToList(); if (l.Count == 0) l.Add("AAPL"); return l; } }
        void Ensure()
        {
            var syms = Syms; var s = string.Join(",", syms); if (s == sig) return;
            foreach (var k in keys) Hub.Unsubscribe(k, this);
            keys = syms.Select(TwMarket.StockKey).ToList(); sig = s;
            foreach (var k in keys) Hub.Subscribe(k, this);
        }
        public override void OnStart() { Ensure(); }
        public override void OnStop() { foreach (var k in keys) Hub.Unsubscribe(k, this); keys.Clear(); sig = null; }
        float BlockW(DrawCtx c, string s, TwQuote q)
        {
            return Math.Max(TwT.W(c.G, s, c.FS(10.5f)) + c.S(4) + c.S(16), TwT.W(c.G, q != null && q.Ok ? TwMarket.Pct(q.Pct) : "—", c.FS(10)));
        }
        public override int Measure(DrawCtx c)
        {
            float w = c.S(11) * 2; var l = Syms;
            for (int i = 0; i < l.Count; i++) w += BlockW(c, l[i], TwMarket.Stock(l[i])) + (i < l.Count - 1 ? c.S(19) : 0);
            return (int)Math.Ceiling(w);
        }
        public override void Draw(DrawCtx c)
        {
            Ensure();
            var g = c.G; var t = c.T; var b = c.Bounds; float cy = b.Y + b.Height / 2f, x = b.X + c.S(11); var l = Syms;
            for (int i = 0; i < l.Count; i++)
            {
                var q = TwMarket.Stock(l[i]); bool ok = q != null && q.Ok; var col = ok ? TwMarket.Col(t, q.Pct) : t.TextFaint;
                float sw = TwT.Str(g, l[i], c.FS(10.5f), t.Text, x, cy - c.S(2));
                if (ok && q.Line.Length > 1) TwMarket.Chart(g, new RectangleF(x + sw + c.S(4), cy - c.S(10), c.S(16), c.S(8)), q.Line, double.NaN, col, t, c.Scale * 0.8f, false);
                TwT.Str(g, ok ? TwMarket.Pct(q.Pct) : "—", c.FS(10), col, x, cy + c.S(10));
                x += BlockW(c, l[i], q) + c.S(9);
                if (i < l.Count - 1) using (var p = new Pen(TwDraw.Divider(t), 1f)) g.DrawLine(p, x, cy - c.S(9), x, cy + c.S(9));
                x += c.S(10);
            }
        }
        public override Size PanelSize(float s) { return new Size((int)(330 * s), (int)((52 + 3 * 58 + 8) * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            Ensure();
            var g = c.G; var t = c.T; var b = c.Bounds;
            var anyStale = Syms.Any(s => { var q = TwMarket.Stock(s); return q == null || q.Stale; });
            float y = c.PanelHeader("Watchlist", anyStale ? "Offline · last values" : "Yahoo Finance", Icons.Chart);
            float x0 = b.X + c.S(16), x1 = b.Right - c.S(16), rh = c.S(58);
            var l = Syms;
            for (int i = 0; i < l.Count; i++)
            {
                var q = TwMarket.Stock(l[i]); bool ok = q != null && q.Ok; var col = ok ? TwMarket.Col(t, q.Pct) : t.TextFaint;
                var r = new RectangleF(x0, y + i * rh, x1 - x0, rh - c.S(8));
                Ui.FillRound(g, r, c.S(12), TwDraw.Card(t));
                float cy = r.Y + r.Height / 2;
                TwT.Str(g, l[i], c.FS(13.5f), t.Text, r.X + c.S(12), cy - c.S(2));
                string nm = ok && !string.IsNullOrEmpty(q.Name) ? q.Name : (ok ? q.Currency : "Offline");
                Ui.Text(g, nm, c.F(10.5f), t.TextDim, new RectangleF(r.X + c.S(12), cy + c.S(2), c.S(100), c.S(14)));
                if (ok && q.Line.Length > 1) TwMarket.Chart(g, new RectangleF(r.X + c.S(118), r.Y + c.S(10), c.S(80), r.Height - c.S(20)), q.Line, q.Prev, col, t, c.Scale);
                TwT.Str(g, ok ? TwMarket.Money(q.Price, q.Currency) : "—", c.FS(13.5f), t.Text, r.Right - c.S(12), cy - c.S(2), 1);
                var pr = TwMarket.Pct(ok ? q.Pct : 0); var pf = c.FS(10.5f); float pw = TwT.W(g, pr, pf) + c.S(10);
                if (ok)
                {
                    var pill = new RectangleF(r.Right - c.S(12) - pw, cy + c.S(3), pw, c.S(15));
                    Ui.FillRound(g, pill, pill.Height / 2, Ui.WithA(col, 40));
                    Ui.Text(g, pr, pf, col, pill, 0);
                }
            }
        }
    }

    [Widget("crypto", "Crypto", "Stocks", "A coin's price, its change over 24 hours and the day's line, from Binance.", RefName = "Crypto", Data = DataKind.Live)]
    public class TwCryptoWidget : TwBase
    {
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Text("symbol", "Binance symbol", "BTCUSDT") }; } }
        string key;
        string Sym { get { var s = SStr("symbol").Trim().ToUpperInvariant().Replace("/", "").Replace("-", ""); return s.Length == 0 ? "BTCUSDT" : s; } }
        void Ensure() { var k = TwMarket.CryptoKey(Sym); if (k != key) { if (key != null) Hub.Unsubscribe(key, this); key = k; Hub.Subscribe(key, this); } }
        public override void OnStart() { Ensure(); }
        public override void OnStop() { if (key != null) Hub.Unsubscribe(key, this); key = null; }
        TwCoin C { get { return TwMarket.Coin(Sym); } }
        static Color Badge(string bas, Theme t)
        {
            switch (bas) { case "BTC": return Color.FromArgb(255, 247, 147, 26); case "ETH": return Color.FromArgb(255, 98, 126, 234); case "SOL": return Color.FromArgb(255, 153, 69, 255); case "BNB": return Color.FromArgb(255, 220, 170, 20); case "XRP": return Color.FromArgb(255, 60, 70, 85); case "DOGE": return Color.FromArgb(255, 194, 166, 51); default: return t.Accent; }
        }
        static string Mark(string bas) { switch (bas) { case "BTC": return "₿"; case "ETH": return "Ξ"; default: return bas.Substring(0, 1); } }
        void BadgeAt(DrawCtx c, PointF ctr, float r, string bas)
        {
            Ui.FillCircle(c.G, ctr, r, Badge(bas, c.T));
            var f = TwF.Bold(r * 1.15f);
            TwT.Str(c.G, Mark(bas), f, Color.White, ctr.X, ctr.Y + TwT.Cap(f) / 2, 0);
        }
        public override int Measure(DrawCtx c)
        {
            var k = C; string bas, quo; TwMarket.SplitPair(Sym, out bas, out quo);
            bool ok = k != null && k.Ok;
            float a = Math.Max(TwT.W(c.G, ok ? TwMarket.Money(k.Price, quo) : "—", c.FS(12)), TwT.W(c.G, bas + "  " + (ok ? TwMarket.Pct(k.Pct) : "Offline"), c.FS(9.5f)));
            return (int)Math.Ceiling(c.S(8) + c.S(20) + c.S(7) + a + c.S(10) + c.S(46) + c.S(10));
        }
        public override void Draw(DrawCtx c)
        {
            if (key == null || !key.EndsWith("." + Sym)) Ensure();
            var g = c.G; var t = c.T; var b = c.Bounds; var k = C; float cy = b.Y + b.Height / 2f, x = b.X + c.S(8);
            string bas, quo; TwMarket.SplitPair(Sym, out bas, out quo);
            bool ok = k != null && k.Ok; var col = ok ? TwMarket.Col(t, k.Pct) : t.TextFaint;
            BadgeAt(c, new PointF(x + c.S(10), cy), c.S(10), bas);
            x += c.S(27);
            float a = TwT.Str(g, ok ? TwMarket.Money(k.Price, quo) : "—", c.FS(12), ok ? (k.Stale ? t.TextDim : t.Text) : t.TextFaint, x, cy - c.S(1.5f));
            float bw = TwT.Str(g, bas + "  ", c.FS(9.5f), t.TextDim, x, cy + c.S(10));
            a = Math.Max(a, bw + TwT.Str(g, ok ? TwMarket.Pct(k.Pct) : (k == null ? "Loading" : "Offline"), c.FS(9.5f), col, x + bw, cy + c.S(10)));
            x += a + c.S(10);
            var cr = new RectangleF(x, cy - c.S(9), c.S(46), c.S(18));
            if (ok && k.Line.Length > 1) TwMarket.Chart(g, cr, k.LiveLine, k.Line[0], col, t, c.Scale);
            else using (var p = new Pen(t.Track, c.S(1.5f)) { DashStyle = DashStyle.Dot }) g.DrawLine(p, cr.X, cy, cr.Right, cy);
        }
        public override Size PanelSize(float s) { return new Size((int)(340 * s), (int)(268 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds; var k = C;
            string bas, quo; TwMarket.SplitPair(Sym, out bas, out quo);
            bool ok = k != null && k.Ok;
            float x0 = b.X + c.S(16), x1 = b.Right - c.S(16), y = b.Y + c.S(14);
            BadgeAt(c, new PointF(x0 + c.S(10), y + c.S(10)), c.S(10), bas);
            Ui.Text(g, bas + (quo.Length > 0 ? " / " + quo : ""), c.FS(14), t.Text, new RectangleF(x0 + c.S(28), y, x1 - x0, c.S(20)));
            Ui.Text(g, ok ? (k.Stale ? "Offline · last values" : "Binance · 24h") : "", c.F(11), ok && k.Stale ? t.Warn : t.TextFaint, new RectangleF(x0, y, x1 - x0, c.S(20)), 1);
            y += c.S(30);
            if (!ok)
            {
                Ui.Text(g, k == null ? "Loading price…" : "Binance is unreachable", c.FS(13), t.Text, new RectangleF(b.X, b.Y + b.Height / 2 - c.S(20), b.Width, c.S(20)), 0);
                Ui.Text(g, "Retrying every minute", c.F(11), t.TextDim, new RectangleF(b.X, b.Y + b.Height / 2 + c.S(2), b.Width, c.S(18)), 0);
                return;
            }
            var col = TwMarket.Col(t, k.Pct);
            var pf = TwF.Semi(c.S(30)); float bl = y + c.S(2) + TwT.Cap(pf);
            float pw = TwT.Str(g, TwMarket.Money(k.Price, quo), pf, t.Text, x0, bl);
            TwT.Str(g, TwMarket.Pct(k.Pct), c.FS(13), col, x0 + pw + c.S(10), bl);
            y = bl + c.S(14);
            var cr = new RectangleF(x0, y, x1 - x0, c.S(110));
            TwMarket.Chart(g, cr, k.LiveLine, k.Line.Length > 0 ? k.Line[0] : double.NaN, col, t, c.Scale);
            Ui.Text(g, "24h · 15m", c.F(9.5f), t.TextFaint, new RectangleF(x0, cr.Bottom + c.S(2), x1 - x0, c.S(14)), 1);
            y = cr.Bottom + c.S(22);
            string[] lab = { "24h high", "24h low", "Open", "Volume" };
            string[] val = { TwMarket.Money(k.High, quo), TwMarket.Money(k.Low, quo), TwMarket.Money(k.Open, quo), (quo.StartsWith("USD") || quo == "FDUSD" ? "$" : "") + Ui.Compact(k.Vol) };
            float cw = (x1 - x0) / 4;
            for (int i = 0; i < 4; i++)
            {
                TwT.Str(g, lab[i], c.F(10), t.TextDim, x0 + i * cw, y + c.S(8));
                Ui.Text(g, val[i], c.FS(12f), t.Text, new RectangleF(x0 + i * cw, y + c.S(14), cw - c.S(4), c.S(16)));
            }
        }
    }
}
