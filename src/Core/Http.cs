// Minimal HTTP for widgets. Blocking calls are meant to run inside Hub samplers (thread pool).
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace NovaBar
{
    public static class Http
    {
        static Http() { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 /*TLS1.2*/ | (SecurityProtocolType)12288 /*TLS1.3*/; ServicePointManager.DefaultConnectionLimit = 8; }
        static readonly Dictionary<string, KeyValuePair<DateTime, string>> cache = new Dictionary<string, KeyValuePair<DateTime, string>>();

        /// <summary>GET/POST returning body text or null on failure. headers: "Name: value" strings.</summary>
        public static string Request(string method, string url, string body = null, string contentType = "application/json", int timeoutMs = 10000, params string[] headers)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method; req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "NovaBar/1.0 (Windows desktop widget)";
                req.AutomaticDecompression = DecompressionMethods.GZip; // no Deflate: some APIs (Open-Meteo) answer 503 when it is offered
                req.Accept = "application/json, text/plain, */*";
                foreach (var h in headers)
                {
                    int i = h.IndexOf(':'); if (i <= 0) continue;
                    var n = h.Substring(0, i).Trim(); var v = h.Substring(i + 1).Trim();
                    if (n.Equals("Accept", StringComparison.OrdinalIgnoreCase)) req.Accept = v;
                    else if (n.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = v;
                    else if (n.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) req.UserAgent = v;
                    else req.Headers[n] = v;
                }
                if (body != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(body); req.ContentType = contentType; req.ContentLength = bytes.Length;
                    using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
            }
            catch { return null; }
        }
        public static string Get(string url, params string[] headers) { return Request("GET", url, null, null, 10000, headers); }
        public static object GetJson(string url, params string[] headers) { return J.Parse(Get(url, headers)); }
        public static object PostJson(string url, string jsonBody, params string[] headers) { return J.Parse(Request("POST", url, jsonBody, "application/json", 10000, headers)); }

        /// <summary>Cached GET (in-memory) — avoids hammering APIs when several widgets share an endpoint.</summary>
        public static string GetCached(string url, TimeSpan ttl, params string[] headers)
        {
            lock (cache) { KeyValuePair<DateTime, string> e; if (cache.TryGetValue(url, out e) && DateTime.UtcNow - e.Key < ttl) return e.Value; }
            var v = Get(url, headers);
            if (v != null) lock (cache) cache[url] = new KeyValuePair<DateTime, string>(DateTime.UtcNow, v);
            return v;
        }
        public static byte[] GetBytes(string url, params string[] headers)
        {
            try { using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "NovaBar/1.0"; foreach (var h in headers) { int i = h.IndexOf(':'); if (i > 0) wc.Headers[h.Substring(0, i).Trim()] = h.Substring(i + 1).Trim(); } return wc.DownloadData(url); } }
            catch { return null; }
        }
        public static string Esc(string s) { return Uri.EscapeDataString(s ?? ""); }
    }
}
