// Finance / Analytics shared core: data snapshot, builder, formatting, HTTP with errors, provider base,
// background store (Hub + disk cache) and deterministic sample data.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace NovaBar
{
    public class FinTxn { public DateTime When; public double Amount; public string Label; }

    /// <summary>Immutable per-provider snapshot: one value per local day, oldest first, last = End.</summary>
    public class FinSeries
    {
        public string Provider;
        public double[] Values = new double[0];
        public DateTime End = DateTime.Today;
        public string Currency = "USD";
        public List<FinTxn> Recent = new List<FinTxn>();
        public int Count = -1;           // orders/sales in the last 30 days (-1 = unknown)
        public bool Sample, Stale;       // Stale = cached copy shown because the last refresh failed
        public string Error;
        public DateTime Fetched;

        public int Length { get { return Values.Length; } }
        /// <summary>Sum of `days` values ending `skip` days before End.</summary>
        public double Sum(int days, int skip = 0)
        {
            double s = 0; int last = Values.Length - 1 - skip;
            for (int i = last; i > last - days && i >= 0; i--) s += Values[i];
            return s;
        }
        public bool HasPrev(int days) { return Values.Length >= days * 2; }
        /// <summary>Change of the last `days` complete days vs the `days` before (today's partial day is left out). NaN if no baseline.</summary>
        public double ChangeOver(int days)
        {
            int skip = End == DateTime.Today ? 1 : 0;
            if (Values.Length < days * 2 + skip) { if (Values.Length < days * 2) return double.NaN; skip = 0; }
            return FinFmt.Change(Sum(days, skip), Sum(days, days + skip));
        }
        public double[] Tail(int n)
        {
            n = Math.Min(n, Values.Length); var r = new double[n];
            Array.Copy(Values, Values.Length - n, r, 0, n); return r;
        }
        public DateTime DayOf(int index) { return End.AddDays(index - (Values.Length - 1)); }
        public double ValueOn(DateTime d)
        {
            int i = Values.Length - 1 - (int)Math.Round((End - d.Date).TotalDays);
            return i >= 0 && i < Values.Length ? Values[i] : 0;
        }
    }

    /// <summary>Accumulates raw records into daily buckets.</summary>
    public class FinBuilder
    {
        readonly FinSeries s; readonly int n; int count30; readonly List<FinTxn> txns = new List<FinTxn>();
        readonly Dictionary<string, int> curVotes = new Dictionary<string, int>();
        public FinBuilder(string provider, int days, DateTime? end = null)
        {
            n = days; s = new FinSeries { Provider = provider, Values = new double[days], End = (end ?? DateTime.Today).Date };
        }
        public DateTime Start { get { return s.End.AddDays(-(n - 1)); } }
        public DateTime End { get { return s.End; } }
        public bool InRange(DateTime local) { var d = local.Date; return d >= Start && d <= s.End; }
        public bool Add(DateTime local, double amount, int orders = 1)
        {
            int idx = n - 1 - (int)Math.Round((s.End - local.Date).TotalDays);
            if (idx < 0 || idx >= n) return false;
            s.Values[idx] += amount; if (idx >= n - 30) count30 += orders;
            return true;
        }
        /// <summary>Add to a calendar day given as "yyyy-MM-dd..." (already in the right time zone).</summary>
        public bool AddDay(string isoDate, double amount, int orders = 0)
        {
            DateTime d; if (isoDate == null || isoDate.Length < 10) return false;
            if (!DateTime.TryParseExact(isoDate.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return false;
            return Add(d, amount, orders);
        }
        public void Currency(string cur)
        {
            if (string.IsNullOrEmpty(cur)) return; cur = cur.ToUpperInvariant();
            int v; curVotes.TryGetValue(cur, out v); curVotes[cur] = v + 1;
        }
        public void Txn(DateTime when, double amount, string label) { txns.Add(new FinTxn { When = when, Amount = amount, Label = string.IsNullOrEmpty(label) ? "Payment" : label }); }
        public void SetCount(int c) { count30 = c; }
        public FinSeries Done(bool counts = true)
        {
            if (curVotes.Count > 0) s.Currency = curVotes.OrderByDescending(kv => kv.Value).First().Key;
            s.Recent = txns.OrderByDescending(t => t.When).Take(5).ToList();
            s.Count = counts ? count30 : -1; s.Fetched = DateTime.Now;
            return s;
        }
    }

    public static class FinFmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly HashSet<string> zeroDec = new HashSet<string> { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF" };
        /// <summary>Divisor from minor units (cents) to major units.</summary>
        public static double Minor(string cur) { return cur != null && zeroDec.Contains(cur.ToUpperInvariant()) ? 1.0 : 100.0; }
        static string Sym(string cur)
        {
            switch ((cur ?? "USD").ToUpperInvariant()) { case "USD": return "$"; case "EUR": return "€"; case "GBP": return "£"; case "JPY": return "¥"; case "INR": return "₹"; default: return null; }
        }
        static string Wrap(string num, string cur, bool neg)
        {
            var sym = Sym(cur);
            return (neg ? "-" : "") + (sym != null ? sym + num : num + " " + cur.ToUpperInvariant());
        }
        /// <summary>"$12.4k", "$842", "$1.2M".</summary>
        public static string Money(double v, string cur = "USD")
        {
            double a = Math.Abs(v); string s;
            if (a >= 1e6) s = (a / 1e6).ToString("0.0#", Inv) + "M";
            else if (a >= 1e3) s = (a / 1e3).ToString(a >= 1e5 ? "0" : "0.0", Inv) + "k";
            else s = a.ToString("0", Inv);
            return Wrap(s, cur ?? "USD", v < 0 && Math.Round(a) > 0);
        }
        /// <summary>"$12,431".</summary>
        public static string MoneyFull(double v, string cur = "USD")
        {
            double a = Math.Abs(v);
            return Wrap(a.ToString(a < 100 ? "#,0.00" : "#,0", Inv), cur ?? "USD", v < 0 && a >= 0.005);
        }
        public static string Count(double v)
        {
            double a = Math.Abs(v);
            if (a >= 1e6) return (v / 1e6).ToString("0.0#", Inv) + "M";
            if (a >= 1e4) return (v / 1e3).ToString(a >= 1e5 ? "0" : "0.0", Inv) + "k";
            return v.ToString("#,0", Inv);
        }
        public static string CountFull(double v) { return v.ToString("#,0", Inv); }
        public static string Value(double v, bool money, string cur, bool full = false) { return money ? (full ? MoneyFull(v, cur) : Money(v, cur)) : (full ? CountFull(v) : Count(v)); }
        /// <summary>Relative change, or NaN when there is no baseline.</summary>
        public static double Change(double cur, double prev) { return prev > 0.0001 ? (cur - prev) / prev : double.NaN; }
        public static string Pct(double ch)
        {
            if (double.IsNaN(ch)) return "new";
            double p = ch * 100; string s = Math.Abs(p) >= 10 ? Math.Abs(p).ToString("0", Inv) : Math.Abs(p).ToString("0.#", Inv);
            return (p >= 0 ? "+" : "−") + s + "%";
        }
        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + "m ago";
            if (d.TotalHours < 24) return (int)d.TotalHours + "h ago";
            if (d.TotalDays < 7) return (int)d.TotalDays + "d ago";
            return t.ToString("MMM d", Inv);
        }
        public static string Day(DateTime d) { return d.ToString("MMM d", Inv); }
        public static string Iso(DateTime d) { return d.ToString("yyyy-MM-dd", Inv); }
        public static string IsoUtc(DateTime local) { return local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", Inv); }
        public static long Unix(DateTime local) { return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
        public static DateTime FromUnix(double s) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s).ToLocalTime(); }
        /// <summary>Parse an ISO-8601 timestamp to local time (DateTime.MinValue on failure).</summary>
        public static DateTime Local(string iso)
        {
            DateTimeOffset o;
            if (!string.IsNullOrEmpty(iso) && DateTimeOffset.TryParse(iso, Inv, DateTimeStyles.AssumeUniversal, out o)) return o.LocalDateTime;
            return DateTime.MinValue;
        }
    }

    public class FinHttpException : Exception { public int Status; public FinHttpException(string m, int status) : base(m) { Status = status; } }

    /// <summary>HTTP that reports errors (status codes) and exposes the Link header for cursor pagination.</summary>
    public static class FinNet
    {
        static FinNet() { Http.Esc(""); } // runs Http's static ctor (TLS 1.2/1.3)

        public static string Send(string method, string url, string body, string contentType, out string link, params string[] headers)
        {
            link = null;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method; req.Timeout = 15000; req.ReadWriteTimeout = 15000;
            req.UserAgent = "NovaBar/1.0 (Windows desktop widget)";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.Accept = "application/json";
            foreach (var h in headers)
            {
                int i = h.IndexOf(':'); if (i <= 0) continue;
                var n = h.Substring(0, i).Trim(); var v = h.Substring(i + 1).Trim();
                if (n.Equals("Accept", StringComparison.OrdinalIgnoreCase)) req.Accept = v;
                else if (n.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = v;
                else req.Headers[n] = v;
            }
            try
            {
                if (body != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(body); req.ContentType = contentType ?? "application/json"; req.ContentLength = bytes.Length;
                    using (var st = req.GetRequestStream()) st.Write(bytes, 0, bytes.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    link = resp.Headers["Link"];
                    return r.ReadToEnd();
                }
            }
            catch (WebException e)
            {
                var hr = e.Response as HttpWebResponse;
                if (hr == null) throw new FinHttpException("Can't reach the server", 0);
                int code = (int)hr.StatusCode; hr.Close();
                string msg;
                if (code == 401 || code == 403) msg = "Key rejected (" + code + ") — check it in Settings";
                else if (code == 404) msg = "Not found (404) — check the account details";
                else if (code == 429) msg = "Rate limited — will retry";
                else if (code >= 500) msg = "Service error (" + code + ")";
                else msg = "Request failed (" + code + ")";
                throw new FinHttpException(msg, code);
            }
        }
        public static object Json(string method, string url, string body, params string[] headers)
        {
            string link; return Parse(Send(method, url, body, "application/json", out link, headers));
        }
        public static object Parse(string s)
        {
            var o = J.Parse(s); if (o == null) throw new FinHttpException("Unexpected response", 0);
            return o;
        }
        /// <summary>Extract the rel="next" URL from an RFC 5988 Link header.</summary>
        public static string NextLink(string link)
        {
            if (string.IsNullOrEmpty(link)) return null;
            foreach (var part in link.Split(','))
            {
                if (part.IndexOf("rel=\"next\"", StringComparison.OrdinalIgnoreCase) < 0) continue;
                int a = part.IndexOf('<'), b = part.IndexOf('>');
                if (a >= 0 && b > a) return part.Substring(a + 1, b - a - 1);
            }
            return null;
        }
    }

    /// <summary>One data source (store or analytics site). Widgets are thin views over a provider.</summary>
    public abstract class FinProvider
    {
        public string Id, Name, Account, Letter = "?", Dashboard;
        public Color Brand = Color.Gray;
        public bool Money = true;       // money vs counts (visitors/clicks)
        public int History = 60;        // days fetched (period + previous period)
        public string Unit = "orders";  // noun for Count, or for the metric (visitors / clicks)
        public double SampleBase = 200; // sample: typical value per day
        public int Seed = 1;
        public string[] SampleLabels = { "Pro plan", "Starter plan", "Lifetime license", "Team plan" };
        public string HubKey { get { return "fin." + Id; } }
        /// <summary>Blocking fetch (runs in the Hub thread pool). Throw FinHttpException with a readable message on errors.</summary>
        public abstract FinSeries Fetch();
        protected string Acc(string field) { return Accounts.Get(Account, field); }
    }

    /// <summary>Hub keys (15 min), disk cache of the last good result, and sample fallback.</summary>
    public static class FinStore
    {
        public const int Interval = 15 * 60 * 1000;
        static readonly Dictionary<string, FinSeries> diskMemo = new Dictionary<string, FinSeries>();
        static readonly Dictionary<string, FinSeries> samples = new Dictionary<string, FinSeries>();
        static readonly Dictionary<string, bool> lastConnected = new Dictionary<string, bool>();
        static readonly object gate = new object();

        static FinStore()
        {
            AppConfig.Changed += () =>
            {
                foreach (var p in FinProviders.All)
                {
                    bool c = Accounts.Connected(p.Account), was;
                    bool changed;
                    lock (gate) { changed = !lastConnected.TryGetValue(p.Id, out was) || was != c; lastConnected[p.Id] = c; if (changed) diskMemo.Remove(p.Id); }
                    if (changed) Hub.Refresh(p.HubKey);
                }
            };
        }

        public static void Ensure(FinProvider p)
        {
            lock (gate) lastConnected[p.Id] = Accounts.Connected(p.Account);
            Hub.Define(p.HubKey, Interval, () => Sample(p));
        }

        /// <summary>Current snapshot for drawing: live value, else cached-from-disk, else sample (not connected), else null (loading).</summary>
        public static FinSeries Peek(FinProvider p)
        {
            bool conn = Accounts.Connected(p.Account);
            var v = Hub.Get<FinSeries>(p.HubKey);
            if (v != null && v.Sample != !conn) v = null; // account state flipped since the last sample
            if (v != null) return v;
            if (!conn) return SampleOf(p);
            return LoadCache(p);
        }

        static object Sample(FinProvider p)
        {
            if (!Accounts.Connected(p.Account)) return SampleOf(p);
            string err = null;
            try
            {
                var s = p.Fetch();
                if (s != null) { s.Provider = p.Id; SaveCache(p, s); return s; }
            }
            catch (FinHttpException e) { err = e.Message; }
            catch (Exception e) { err = "Couldn't read data (" + e.GetType().Name + ")"; }
            var c = LoadCache(p);
            if (c != null)
            {
                return new FinSeries { Provider = c.Provider, Values = c.Values, End = c.End, Currency = c.Currency, Recent = c.Recent, Count = c.Count, Fetched = c.Fetched, Stale = true, Error = err ?? "Refresh failed" };
            }
            return new FinSeries { Provider = p.Id, Values = new double[0], Error = err ?? "No data", Fetched = DateTime.Now };
        }

        // ---------- disk cache ----------
        static string Fingerprint(FinProvider p)
        {
            var d = Accounts.GetDef(p.Account); uint h = 2166136261;
            if (d != null) foreach (var f in d.Fields) foreach (var ch in (Accounts.Get(p.Account, f.Key) ?? "") + "|") { h ^= ch; h *= 16777619; }
            return h.ToString("x8");
        }
        static string CacheFile(FinProvider p) { return Path.Combine(Paths.CacheDir, "fin-" + p.Id + ".json"); }
        static void SaveCache(FinProvider p, FinSeries s)
        {
            try
            {
                var d = new Dictionary<string, object>();
                d["acct"] = Fingerprint(p); d["end"] = FinFmt.Iso(s.End); d["cur"] = s.Currency; d["count"] = s.Count;
                d["fetched"] = s.Fetched.Ticks.ToString(CultureInfo.InvariantCulture);
                d["values"] = s.Values.Select(x => (object)Math.Round(x, 2)).ToArray();
                d["recent"] = s.Recent.Select(t => (object)new Dictionary<string, object> { { "w", t.When.Ticks.ToString(CultureInfo.InvariantCulture) }, { "a", Math.Round(t.Amount, 2) }, { "l", t.Label } }).ToArray();
                File.WriteAllText(CacheFile(p), J.Stringify(d), Encoding.UTF8);
                lock (gate) diskMemo[p.Id] = s;
            }
            catch { }
        }
        static FinSeries LoadCache(FinProvider p)
        {
            lock (gate) { FinSeries m; if (diskMemo.TryGetValue(p.Id, out m)) return m; }
            FinSeries s = null;
            try
            {
                var f = CacheFile(p);
                if (File.Exists(f))
                {
                    var d = J.ParseObj(File.ReadAllText(f, Encoding.UTF8));
                    if (d != null && J.Str(d, "acct") == Fingerprint(p))
                    {
                        s = new FinSeries { Provider = p.Id, Currency = J.Str(d, "cur", "USD"), Count = (int)J.Num(d, "count", -1) };
                        DateTime end; DateTime.TryParseExact(J.Str(d, "end"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end); s.End = end;
                        long ticks; long.TryParse(J.Str(d, "fetched"), out ticks); s.Fetched = new DateTime(ticks);
                        var vals = J.Arr(d, "values"); s.Values = vals == null ? new double[0] : vals.Cast<object>().Select(x => J.AsNum(x)).ToArray();
                        var rec = J.Arr(d, "recent");
                        if (rec != null) foreach (var r in rec) { long w; long.TryParse(J.Str(r, "w"), out w); s.Recent.Add(new FinTxn { When = new DateTime(w), Amount = J.Num(r, "a"), Label = J.Str(r, "l") }); }
                        s = Shift(s);
                    }
                }
            }
            catch { s = null; }
            lock (gate) diskMemo[p.Id] = s;
            return s;
        }
        /// <summary>Re-align a cached series so End = today (days with no data since the fetch become 0).</summary>
        static FinSeries Shift(FinSeries s)
        {
            int gap = (int)Math.Round((DateTime.Today - s.End).TotalDays);
            if (gap <= 0 || s.Values.Length == 0 || s.Provider == "searchconsole") return s;
            var v = new double[s.Values.Length];
            for (int i = 0; i < v.Length; i++) { int j = i + gap; if (j < v.Length) v[i] = s.Values[j]; }
            s.Values = v; s.End = DateTime.Today; return s;
        }

        // ---------- deterministic sample ----------
        public static FinSeries SampleOf(FinProvider p)
        {
            lock (gate)
            {
                FinSeries s;
                if (samples.TryGetValue(p.Id, out s) && s.End == DateTime.Today) return s;
                s = MakeSample(p); samples[p.Id] = s; return s;
            }
        }
        static FinSeries MakeSample(FinProvider p)
        {
            int n = p.History; var rnd = new Random(p.Seed * 7919 + 13);
            var s = new FinSeries { Provider = p.Id, Values = new double[n], End = DateTime.Today, Sample = true, Currency = "USD", Fetched = DateTime.Now };
            double avgOrder = p.Money ? 18 + (p.Seed % 5) * 9 : 1; int orders = 0;
            double phase = rnd.NextDouble() * 6;
            for (int i = 0; i < n; i++)
            {
                var day = s.DayOf(i);
                double trend = 0.80 + 0.34 * i / Math.Max(1, n - 1);
                double weekly = (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday) ? (p.Money ? 0.82 : 0.68) : 1.0;
                double wave = 1 + 0.08 * Math.Sin(i / 4.3 + phase);
                double noise = 0.72 + 0.56 * rnd.NextDouble();
                if (rnd.NextDouble() < 0.05) noise *= 1.9;          // launch / newsletter spike
                double v = p.SampleBase * trend * weekly * wave * noise;
                if (i == n - 1) v *= Math.Max(0.25, Math.Min(1, DateTime.Now.TimeOfDay.TotalHours / 24 + 0.15)) ;
                v = p.Money ? Math.Round(v, 2) : Math.Round(v);
                s.Values[i] = v;
                if (i >= n - 30) orders += Math.Max(1, (int)Math.Round(v / avgOrder));
            }
            s.Count = p.Money ? orders : -1;
            if (p.Money)
            {
                var t = DateTime.Now; var who = new[] { "Maya R.", "Jonas K.", "Priya S.", "Liam O.", "Sofia M.", "Noah T." };
                for (int k = 0; k < 4; k++)
                {
                    t = t.AddMinutes(-(17 + rnd.Next(160)));
                    var label = p.SampleLabels[rnd.Next(p.SampleLabels.Length)] + " · " + who[rnd.Next(who.Length)];
                    double amt = Math.Round(avgOrder * (0.6 + rnd.NextDouble() * 1.8)) - 0.01;
                    s.Recent.Add(new FinTxn { When = t, Amount = amt, Label = label });
                }
            }
            return s;
        }
    }
}
