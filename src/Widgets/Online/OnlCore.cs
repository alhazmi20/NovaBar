// Shared plumbing for the Online / Developer / Social widgets (prefix Onl).
// - OnlData: one immutable snapshot type (numbers, strings, item lists, series) with value-equality + JSON cache.
// - OnlNet: HTTP that reports status codes as friendly errors.
// - OnlWidget: base class that binds a Hub key, falls back to disk cache / deterministic sample data,
//   and re-binds when the user edits accounts in Settings.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace NovaBar
{
    public class OnlErr : Exception { public OnlErr(string m) : base(m) { } }

    public class OnlItem
    {
        public string Title = "", Sub = "", Url = "", Tag = "", When = "";
        public int Kind;          // widget-specific category (state type, status...)
        public double Val;        // widget-specific number (stars, count...)
        public Dictionary<string, object> ToD()
        {
            return new Dictionary<string, object> { { "t", Title }, { "s", Sub }, { "u", Url }, { "g", Tag }, { "w", When }, { "k", Kind }, { "v", Val } };
        }
        public static OnlItem FromD(object o)
        {
            return new OnlItem { Title = J.Str(o, "t"), Sub = J.Str(o, "s"), Url = J.Str(o, "u"), Tag = J.Str(o, "g"), When = J.Str(o, "w"), Kind = (int)J.Num(o, "k"), Val = J.Num(o, "v") };
        }
        public DateTime WhenUtc { get { return OnlUtil.ParseTime(When); } }
    }

    /// <summary>Immutable-by-convention snapshot. Equality = same serialized content (cheap: built at most every few minutes).</summary>
    public class OnlData
    {
        public bool Sample, Stale;
        public string Error;
        public DateTime At = DateTime.UtcNow;
        public Dictionary<string, double> N = new Dictionary<string, double>();
        public Dictionary<string, string> S = new Dictionary<string, string>();
        public List<OnlItem> Items = new List<OnlItem>();
        public List<OnlItem> Items2 = new List<OnlItem>();
        public double[] Series = new double[0];
        public int[] Levels = new int[0];
        string sig;

        public double Num(string k, double def = 0) { double v; return N.TryGetValue(k, out v) ? v : def; }
        public string Str(string k, string def = "") { string v; return S.TryGetValue(k, out v) && v != null ? v : def; }
        public bool Has(string k) { return N.ContainsKey(k); }

        public Dictionary<string, object> ToD()
        {
            var d = new Dictionary<string, object>();
            d["sample"] = Sample; d["stale"] = Stale; d["err"] = Error ?? ""; d["at"] = At.ToString("o", CultureInfo.InvariantCulture);
            d["n"] = N.ToDictionary(x => x.Key, x => (object)x.Value);
            d["s"] = S.ToDictionary(x => x.Key, x => (object)x.Value);
            d["i"] = Items.Select(x => (object)x.ToD()).ToArray();
            d["i2"] = Items2.Select(x => (object)x.ToD()).ToArray();
            d["ser"] = Series.Select(x => (object)x).ToArray();
            d["lv"] = Levels.Select(x => (object)x).ToArray();
            return d;
        }
        public static OnlData FromD(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var r = new OnlData();
            r.Sample = J.Bool(d, "sample"); r.Stale = J.Bool(d, "stale");
            var e = J.Str(d, "err"); r.Error = string.IsNullOrEmpty(e) ? null : e;
            r.At = OnlUtil.ParseTime(J.Str(d, "at"));
            var n = J.Obj(d, "n"); if (n != null) foreach (var kv in n) r.N[kv.Key] = J.AsNum(kv.Value);
            var s = J.Obj(d, "s"); if (s != null) foreach (var kv in s) r.S[kv.Key] = J.AsStr(kv.Value);
            var i = J.Arr(d, "i"); if (i != null) foreach (var o in i) r.Items.Add(OnlItem.FromD(o));
            var i2 = J.Arr(d, "i2"); if (i2 != null) foreach (var o in i2) r.Items2.Add(OnlItem.FromD(o));
            var ser = J.Arr(d, "ser"); if (ser != null) r.Series = ser.Cast<object>().Select(x => J.AsNum(x)).ToArray();
            var lv = J.Arr(d, "lv"); if (lv != null) r.Levels = lv.Cast<object>().Select(x => (int)J.AsNum(x)).ToArray();
            return r;
        }
        public OnlData Clone() { return FromD(J.ParseObj(J.Stringify(ToD()))); }
        public string Sig { get { if (sig == null) sig = J.Stringify(ToD()); return sig; } }
        public override bool Equals(object o) { var x = o as OnlData; return x != null && x.Sig == Sig; }
        public override int GetHashCode() { return Sig.GetHashCode(); }
    }

    public static class OnlUtil
    {
        public static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!url.StartsWith("http://") && !url.StartsWith("https://")) return; // only open web links
            ThreadPool.QueueUserWorkItem(_ => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } });
        }

        static string Safe(string k) { foreach (var c in Path.GetInvalidFileNameChars()) k = k.Replace(c, '_'); return k.Replace(':', '_').Replace('#', '_'); }
        public static string CacheFile(string key) { return Path.Combine(Paths.CacheDir, "onl-" + Safe(key) + ".json"); }
        public static void SaveCache(string key, OnlData d)
        {
            try { File.WriteAllText(CacheFile(key), J.Stringify(d.ToD()), Encoding.UTF8); } catch { }
        }
        public static OnlData LoadCache(string key)
        {
            try { var f = CacheFile(key); if (File.Exists(f)) return OnlData.FromD(J.ParseObj(File.ReadAllText(f, Encoding.UTF8))); } catch { }
            return null;
        }

        public static DateTime ParseTime(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            DateTime t;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return t;
            return DateTime.MinValue;
        }
        public static string Iso(DateTime utc) { return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture); }

        /// <summary>"now", "5m", "3h", "2d", "4w".</summary>
        public static string Ago(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "";
            var d = DateTime.UtcNow - utc;
            if (d.TotalMinutes < 1) return "now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + "m";
            if (d.TotalDays < 1) return (int)d.TotalHours + "h";
            if (d.TotalDays < 14) return (int)d.TotalDays + "d";
            if (d.TotalDays < 60) return (int)(d.TotalDays / 7) + "w";
            return utc.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
        }
        public static string Ago(string iso) { return Ago(ParseTime(iso)); }

        public static string Num(double n) { return Ui.Compact(n); }
        public static string Signed(double n) { return (n > 0 ? "+" : n < 0 ? "−" : "") + Ui.Compact(Math.Abs(n)); }

        // deterministic pseudo random for sample data (stable across runs → no flicker, no "random" repaints)
        public class Rng
        {
            uint s;
            public Rng(int seed) { s = (uint)seed * 2654435761u + 1013904223u; if (s == 0) s = 1; }
            public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
            public double D() { return (Next() % 1000000) / 1000000.0; }
            public int I(int lo, int hiExcl) { return lo + (int)(Next() % (uint)Math.Max(1, hiExcl - lo)); }
        }
        /// <summary>Seed that changes once per day (sample data drifts gently, but is stable within a day).</summary>
        public static int DaySeed(string salt) { return (salt.GetHashCode() & 0xffff) ^ (DateTime.Today.DayOfYear * 7919); }

        public static IList Arr(object o, string path) { return J.Arr(o, path) ?? new object[0]; }
    }

    public static class OnlNet
    {
        static OnlNet() { try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288; } catch { try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { } } }

        /// <summary>HTTP request. Returns body; throws OnlErr with a short user-facing message on failure.</summary>
        public static string Req(string method, string url, string body, params string[] headers)
        {
            HttpWebRequest req;
            try { req = (HttpWebRequest)WebRequest.Create(url); }
            catch { throw new OnlErr("Bad address"); }
            req.Method = method; req.Timeout = 15000; req.ReadWriteTimeout = 15000;
            req.UserAgent = "NovaBar/1.0 (Windows desktop widget)";
            req.Accept = "application/json";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            string ctype = "application/json";
            foreach (var h in headers)
            {
                if (h == null) continue;
                int i = h.IndexOf(':'); if (i <= 0) continue;
                var n = h.Substring(0, i).Trim(); var v = h.Substring(i + 1).Trim();
                if (n.Equals("Accept", StringComparison.OrdinalIgnoreCase)) req.Accept = v;
                else if (n.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) ctype = v;
                else if (n.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) req.UserAgent = v;
                else if (n.Equals("Referer", StringComparison.OrdinalIgnoreCase)) req.Referer = v;
                else req.Headers[n] = v;
            }
            try
            {
                if (body != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(body); req.ContentType = ctype; req.ContentLength = bytes.Length;
                    using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
            }
            catch (WebException we)
            {
                var hr = we.Response as HttpWebResponse;
                if (hr == null) throw new OnlErr(we.Status == WebExceptionStatus.Timeout ? "Timed out" : "Offline");
                int code = (int)hr.StatusCode;
                try { hr.Close(); } catch { }
                if (code == 400) throw new OnlErr("Request rejected (check key / id)");
                if (code == 401) throw new OnlErr("Invalid or expired token");
                if (code == 403) throw new OnlErr("Access denied (check token scopes)");
                if (code == 404) throw new OnlErr("Not found (check username / id)");
                if (code == 429) throw new OnlErr("Rate limited, retrying later");
                throw new OnlErr("Service error " + code);
            }
            catch (OnlErr) { throw; }
            catch (Exception) { throw new OnlErr("Offline"); }
        }
        public static object Json(string url, params string[] headers)
        {
            var o = J.Parse(Req("GET", url, null, headers));
            if (o == null) throw new OnlErr("Unexpected response");
            return o;
        }
        public static object PostJson(string url, string body, params string[] headers)
        {
            var o = J.Parse(Req("POST", url, body, headers));
            if (o == null) throw new OnlErr("Unexpected response");
            return o;
        }
    }

    /// <summary>
    /// Base for account-backed web widgets. Subclasses provide HubKey, IsConnected, Fetch (thread pool) and Sample.
    /// Hub key gets a "#sample" suffix while not connected so the sample never pollutes the live cache.
    /// </summary>
    public abstract class OnlWidget : Widget
    {
        protected abstract string HubKey { get; }
        protected virtual int IntervalMs { get { return 10 * 60 * 1000; } }
        protected abstract bool IsConnected { get; }
        protected abstract OnlData Fetch();
        protected abstract OnlData MakeSample();
        protected virtual string[] AccountIds { get { return string.IsNullOrEmpty(Meta.Account) ? new string[0] : new[] { Meta.Account }; } }

        string boundKey, boundFp;
        static readonly Dictionary<string, OnlData> diskCache = new Dictionary<string, OnlData>();
        static readonly Dictionary<string, OnlData> samples = new Dictionary<string, OnlData>();

        public override void OnStart() { Bind(); AppConfig.Changed += OnConfig; }
        public override void OnStop() { AppConfig.Changed -= OnConfig; if (boundKey != null) Hub.Unsubscribe(boundKey, this); boundKey = null; }

        string Fingerprint()
        {
            var sb = new StringBuilder(HubKey).Append(IsConnected ? "|c" : "|s");
            foreach (var a in AccountIds)
            {
                Dictionary<string, string> f;
                if (AppConfig.Current.Accounts.TryGetValue(a, out f)) foreach (var kv in f.OrderBy(x => x.Key)) sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        void Bind()
        {
            bool conn = IsConnected;
            var live = HubKey;
            var k = live + (conn ? "" : "#sample");
            boundKey = k; boundFp = Fingerprint();
            var self = this;
            Hub.Define(k, conn ? IntervalMs : 6 * 3600 * 1000, () => self.Sampler(live, conn));
            Hub.Subscribe(k, this);
        }

        void OnConfig()
        {
            var fp = Fingerprint();
            if (fp == boundFp) return;
            if (boundKey != null) Hub.Unsubscribe(boundKey, this);
            Bind();
            Hub.Refresh(boundKey);
            Invalidate();
        }

        /// <summary>Force a refetch (refresh button).</summary>
        protected void RefreshNow() { if (boundKey != null) Hub.Refresh(boundKey); }

        object Sampler(string liveKey, bool conn)
        {
            if (!conn) { var s = MakeSample(); s.Sample = true; return s; }
            string err = null; OnlData d = null;
            try { d = Fetch(); }
            catch (OnlErr e) { err = e.Message; }
            catch (Exception) { err = "Couldn't read the response"; }
            if (d != null)
            {
                d.At = DateTime.UtcNow;
                OnlUtil.SaveCache(liveKey, d);
                lock (diskCache) diskCache[liveKey] = d;
                return d;
            }
            var c = Cached(liveKey);
            if (c != null) { var x = c.Clone(); x.Stale = true; x.Error = err; return x; }
            return new OnlData { Error = err ?? "No data yet" };
        }

        static OnlData Cached(string liveKey)
        {
            lock (diskCache)
            {
                OnlData d;
                if (!diskCache.TryGetValue(liveKey, out d)) { d = OnlUtil.LoadCache(liveKey); diskCache[liveKey] = d; }
                return d;
            }
        }

        /// <summary>Current snapshot: live hub value → disk cache (while first fetch runs) → sample. Never null.</summary>
        protected OnlData Data
        {
            get
            {
                var k = boundKey;
                var d = k == null ? null : Hub.Get<OnlData>(k);
                if (d != null) return d;
                if (IsConnected)
                {
                    var c = Cached(HubKey);
                    if (c != null) return c;
                    return new OnlData { Error = null, Stale = true, S = { { "loading", "1" } } };
                }
                lock (samples)
                {
                    OnlData s;
                    var sk = HubKey + DateTime.Today.DayOfYear;
                    if (!samples.TryGetValue(sk, out s)) { s = MakeSample(); s.Sample = true; samples[sk] = s; }
                    return s;
                }
            }
        }
        protected bool Loading(OnlData d) { return d.Str("loading") == "1"; }
    }
}
