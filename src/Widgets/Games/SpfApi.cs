// Optional Spotify Web API layer (account "spotify"): OAuth Authorization Code + PKCE with a loopback redirect
// (http://127.0.0.1:43821/callback), refresh-token storage through Accounts, in-memory access token (refreshed on
// expiry / 401), like / shuffle / repeat / playlists. Nothing here runs unless the user connected the account and
// a panel / menu action asks for it — fetches are triggered by track changes seen while the panel is open, never polled.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NovaBar
{
    public sealed class SpfPlaylist { public string Name = "", Uri = "", Image = "", Owner = ""; public int Tracks; }

    public sealed class SpfApiSnap
    {
        public string TrackKey = "", TrackId = "", Device = "", Repeat = "off", Error;
        public bool Known, Liked, Shuffle;
        public List<SpfPlaylist> Playlists;
        public SpfApiSnap Clone() { var c = (SpfApiSnap)MemberwiseClone(); return c; }
    }

    public static class SpfApi
    {
        public const string Redirect = "http://127.0.0.1:43821/callback";
        const int Port = 43821;
        const string Scopes = "user-read-playback-state user-modify-playback-state user-library-read user-library-modify playlist-read-private";

        static SpfApi()
        {
            Accounts.Define("spotify", "Spotify",
                "Optional — the Spotify widgets work without it (local now-playing). To enable like/unlike, shuffle/repeat and playlists: " +
                "1) open developer.spotify.com/dashboard, Create app (any name), Redirect URI = http://127.0.0.1:43821/callback, tick \"Web API\", save. " +
                "2) Copy the app's Client ID here. 3) Open the Spotify widget's panel and press \"Connect Spotify\" — the refresh token below is filled in automatically. " +
                "Playback control (shuffle, repeat, playing a playlist) needs Spotify Premium.",
                "https://developer.spotify.com/dashboard",
                Accounts.F("client", "Client ID", false, "32-character client id"),
                Accounts.F("refresh", "Refresh token (filled by Connect Spotify)"));
        }
        public static void Init() { }

        static bool DemoApi { get { return SpfSession.DemoMode == "api"; } }
        public static bool HasClient { get { return DemoApi || !string.IsNullOrEmpty(Accounts.Get("spotify", "client")); } }
        public static bool Connected { get { return DemoApi || Accounts.Connected("spotify"); } }

        static volatile SpfApiSnap state = new SpfApiSnap();
        public static SpfApiSnap State { get { return state; } }
        static readonly HashSet<Widget> watchers = new HashSet<Widget>();
        public static void Watch(Widget w, bool on) { lock (watchers) { if (on) watchers.Add(w); else watchers.Remove(w); } }
        static void Notify() { Widget[] ws; lock (watchers) ws = watchers.ToArray(); foreach (var w in ws) w.Invalidate(); SpfSession.Changed(); }
        static void Set(SpfApiSnap s) { state = s; Notify(); }

        // ---------------- connect (PKCE + loopback) ----------------
        public static volatile string ConnectMsg;      // non-null while connecting / after a failure
        public static volatile bool Connecting;
        static int connectGen;

        public static void Connect()
        {
            var client = Accounts.Get("spotify", "client");
            if (string.IsNullOrEmpty(client) || Connecting) return;
            Connecting = true; ConnectMsg = "Waiting for you to approve in the browser…"; Notify();
            int g = Interlocked.Increment(ref connectGen);
            var t = new Thread(() => ConnectRun(client, g)) { IsBackground = true, Name = "SpotifyAuth" };
            t.Start();
        }
        public static void CancelConnect() { Interlocked.Increment(ref connectGen); Connecting = false; ConnectMsg = null; Notify(); }

        static string B64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        static string Rand(int bytes) { var b = new byte[bytes]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return B64Url(b); }

        static void ConnectRun(string client, int g)
        {
            TcpListener l = null; string err = null;
            try
            {
                var verifier = Rand(48);
                string challenge; using (var sha = SHA256.Create()) challenge = B64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
                var st = Rand(12);
                l = new TcpListener(IPAddress.Loopback, Port);
                try { l.Start(); } catch { throw new Exception("Port 43821 is busy — close the other app and retry"); }
                var url = "https://accounts.spotify.com/authorize?response_type=code&client_id=" + Uri.EscapeDataString(client)
                    + "&scope=" + Uri.EscapeDataString(Scopes) + "&redirect_uri=" + Uri.EscapeDataString(Redirect)
                    + "&code_challenge_method=S256&code_challenge=" + challenge + "&state=" + st;
                GamUtil.Launch(url);
                var deadline = DateTime.UtcNow.AddMinutes(3);
                string code = null;
                while (code == null && err == null)
                {
                    if (g != connectGen) return;
                    var left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) { err = "Timed out — press Connect again"; break; }
                    var ar = l.BeginAcceptTcpClient(null, null);
                    // wake every second so Cancel is honoured
                    if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(1000, left.TotalMilliseconds)))) continue;
                    using (var cl = l.EndAcceptTcpClient(ar))
                    {
                        cl.ReceiveTimeout = 5000; cl.SendTimeout = 5000;
                        var ns = cl.GetStream();
                        var line = ReadLine(ns);                      // "GET /callback?code=..&state=.. HTTP/1.1"
                        var parts = (line ?? "").Split(' ');
                        string path = parts.Length >= 2 ? parts[1] : "";
                        if (!path.StartsWith("/callback")) { Reply(ns, 404, "Not found"); continue; }
                        var q = Query(path);
                        string v;
                        if (q.TryGetValue("error", out v)) { err = v == "access_denied" ? "Access was denied" : "Spotify: " + v; Reply(ns, 200, Page(false, err)); break; }
                        if (!q.TryGetValue("state", out v) || v != st) { Reply(ns, 400, Page(false, "State mismatch")); continue; }
                        if (!q.TryGetValue("code", out code)) { Reply(ns, 400, Page(false, "No code")); continue; }
                        Reply(ns, 200, Page(true, null));
                    }
                }
                if (code != null)
                {
                    int status; string body;
                    status = Send("POST", "https://accounts.spotify.com/api/token",
                        "grant_type=authorization_code&code=" + Uri.EscapeDataString(code) + "&redirect_uri=" + Uri.EscapeDataString(Redirect)
                        + "&client_id=" + Uri.EscapeDataString(client) + "&code_verifier=" + verifier, "application/x-www-form-urlencoded", null, out body);
                    var o = J.Parse(body);
                    var refresh = J.Str(o, "refresh_token");
                    if (status != 200 || refresh == "") err = "Token exchange failed (" + (J.Str(o, "error_description") != "" ? J.Str(o, "error_description") : "HTTP " + status) + ")";
                    else
                    {
                        lock (tokLock) { access = J.Str(o, "access_token"); expires = DateTime.UtcNow.AddSeconds(J.Num(o, "expires_in", 3600) - 60); }
                        SaveRefresh(refresh);
                    }
                }
            }
            catch (Exception e) { err = e.Message; }
            finally { try { if (l != null) l.Stop(); } catch { } }
            if (g != connectGen) return;
            Connecting = false; ConnectMsg = err;
            state = new SpfApiSnap();
            Notify();
        }

        static void SaveRefresh(string tok)
        {
            var cfg = AppConfig.Load();
            Accounts.Set(cfg, "spotify", "refresh", tok);
            cfg.Save();
            // make it effective immediately in this process too (the bar also live-reloads the file)
            Accounts.Set(AppConfig.Current, "spotify", "refresh", tok);
        }

        static string ReadLine(Stream s)
        {
            var sb = new StringBuilder(); int b;
            while ((b = s.ReadByte()) >= 0 && b != '\n') { if (b != '\r') sb.Append((char)b); if (sb.Length > 8192) break; }
            return sb.ToString();
        }
        static Dictionary<string, string> Query(string path)
        {
            var d = new Dictionary<string, string>();
            int i = path.IndexOf('?'); if (i < 0) return d;
            foreach (var kv in path.Substring(i + 1).Split('&'))
            {
                int e = kv.IndexOf('='); if (e <= 0) continue;
                try { d[Uri.UnescapeDataString(kv.Substring(0, e))] = Uri.UnescapeDataString(kv.Substring(e + 1).Replace('+', ' ')); } catch { }
            }
            return d;
        }
        static void Reply(Stream s, int code, string html)
        {
            var body = Encoding.UTF8.GetBytes(html);
            var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + (code == 200 ? " OK" : " Error") + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            try { s.Write(head, 0, head.Length); s.Write(body, 0, body.Length); s.Flush(); } catch { }
        }
        static string Page(bool ok, string msg)
        {
            return "<!doctype html><meta charset=utf-8><title>NovaBar · Spotify</title><body style=\"font-family:Segoe UI,sans-serif;background:#121212;color:#fff;display:grid;place-items:center;height:90vh\">"
                + "<div style=\"text-align:center\"><div style=\"font-size:48px;color:" + (ok ? "#1DB954\">&#10003;" : "#f87171\">&#10007;") + "</div><h2>"
                + (ok ? "Spotify is connected to NovaBar" : "Couldn't connect Spotify") + "</h2><p style=\"color:#aaa\">"
                + (ok ? "You can close this tab." : WebUtilityEncode(msg)) + "</p></div></body>";
        }
        static string WebUtilityEncode(string s) { return System.Net.WebUtility.HtmlEncode(s ?? ""); }

        public static void Disconnect()
        {
            var cfg = AppConfig.Load(); Accounts.Set(cfg, "spotify", "refresh", null); cfg.Save();
            Accounts.Set(AppConfig.Current, "spotify", "refresh", null);
            lock (tokLock) { access = null; expires = DateTime.MinValue; }
            Set(new SpfApiSnap());
        }

        // ---------------- tokens + HTTP ----------------
        static readonly object tokLock = new object();
        static string access; static DateTime expires = DateTime.MinValue;

        static string Token(bool force)
        {
            lock (tokLock)
            {
                if (!force && access != null && DateTime.UtcNow < expires) return access;
                var client = Accounts.Get("spotify", "client"); var refresh = Accounts.Get("spotify", "refresh");
                if (string.IsNullOrEmpty(client) || string.IsNullOrEmpty(refresh)) return null;
                string body;
                int st = Send("POST", "https://accounts.spotify.com/api/token", "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refresh) + "&client_id=" + Uri.EscapeDataString(client), "application/x-www-form-urlencoded", null, out body);
                var o = J.Parse(body);
                if (st != 200) { access = null; if (J.Str(o, "error") == "invalid_grant") throw new Exception("Spotify sign-in expired — press Connect again"); throw new Exception(st == 0 ? "Offline" : "Spotify sign-in failed (" + st + ")"); }
                access = J.Str(o, "access_token"); expires = DateTime.UtcNow.AddSeconds(J.Num(o, "expires_in", 3600) - 60);
                var rot = J.Str(o, "refresh_token"); if (rot != "" && rot != refresh) SaveRefresh(rot);
                return access;
            }
        }

        static int Send(string method, string url, string body, string ctype, string bearer, out string resp)
        {
            resp = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method; req.Timeout = 12000; req.ReadWriteTimeout = 12000; req.UserAgent = "NovaBar/1.0"; req.Accept = "application/json";
                req.AutomaticDecompression = DecompressionMethods.GZip;
                if (bearer != null) req.Headers["Authorization"] = "Bearer " + bearer;
                if (body != null || method != "GET")
                {
                    var bytes = Encoding.UTF8.GetBytes(body ?? ""); req.ContentType = ctype ?? "application/json"; req.ContentLength = bytes.Length;
                    if (bytes.Length > 0) using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }
                using (var r = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) { resp = sr.ReadToEnd(); return (int)r.StatusCode; }
            }
            catch (WebException we)
            {
                var hr = we.Response as HttpWebResponse; if (hr == null) return 0;
                try { using (var sr = new StreamReader(hr.GetResponseStream(), Encoding.UTF8)) resp = sr.ReadToEnd(); } catch { }
                int c = (int)hr.StatusCode; try { hr.Close(); } catch { } return c;
            }
            catch { return 0; }
        }

        /// <summary>Web API call with one transparent token refresh on 401. Throws with a friendly message on failure.</summary>
        static string Api(string method, string path, string json)
        {
            var tok = Token(false); if (tok == null) throw new Exception("Not connected");
            string body; int st = Send(method, "https://api.spotify.com/v1" + path, json, "application/json", tok, out body);
            if (st == 401) { tok = Token(true); st = Send(method, "https://api.spotify.com/v1" + path, json, "application/json", tok, out body); }
            if (st >= 200 && st < 300) return body ?? "";
            if (st == 0) throw new Exception("Offline");
            if (st == 403) throw new Exception(method == "GET" ? "Access denied (reconnect Spotify)" : "Needs Spotify Premium");
            if (st == 404) throw new Exception("No active Spotify device");
            if (st == 429) throw new Exception("Rate limited, try again soon");
            throw new Exception("Spotify error " + st);
        }

        static void Run(Action a)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { a(); }
                catch (Exception e) { var s = state.Clone(); s.Error = e.Message; Set(s); }
            });
        }

        // ---------------- state ----------------
        static volatile string wantKey = "";
        static int busy;
        /// <summary>Called while the panel is visible: fetches player/like state once per track (no polling).</summary>
        public static void Ensure(string trackKey, bool playlists)
        {
            if (!Connected) return;
            bool needPl = playlists && (state.Playlists == null || DateTime.UtcNow - plAt > TimeSpan.FromMinutes(10)) && !plBusy;
            if (trackKey == wantKey && !needPl) return;
            if (DemoApi) { DemoState(trackKey); return; }
            if (trackKey != wantKey)
            {
                wantKey = trackKey;
                if (Interlocked.Exchange(ref busy, 1) == 0)
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(700); // the Web API lags the local session by a moment after a skip
                        try { FetchPlayer(); } catch (Exception e) { var s = state.Clone(); s.Error = e.Message; Set(s); }
                        finally { Interlocked.Exchange(ref busy, 0); }
                        if (wantKey != state.TrackKey && wantKey != "") { var k = wantKey; wantKey = ""; Ensure(k, false); }
                    });
            }
            if (needPl) { plBusy = true; Run(() => { try { FetchPlaylists(); } finally { plBusy = false; } }); }
        }
        public static void Invalidate() { wantKey = ""; plAt = DateTime.MinValue; }

        static void FetchPlayer()
        {
            var key = wantKey;
            var body = Api("GET", "/me/player", null);
            var n = state.Clone(); n.Error = null; n.TrackKey = key; n.Known = true;
            if (string.IsNullOrEmpty(body)) { n.TrackId = ""; n.Device = ""; Set(n); return; }   // 204: nothing active
            var o = J.Parse(body);
            n.Shuffle = J.Bool(o, "shuffle_state"); n.Repeat = J.Str(o, "repeat_state", "off"); n.Device = J.Str(o, "device.name");
            n.TrackId = J.Str(o, "item.id");
            n.Liked = false;
            if (n.TrackId != "" && J.Str(o, "item.type") == "track")
            {
                var c = J.Parse(Api("GET", "/me/tracks/contains?ids=" + n.TrackId, null)) as System.Collections.IList;
                n.Liked = c != null && c.Count > 0 && J.AsBool(c[0]);
            }
            Set(n);
        }

        static DateTime plAt = DateTime.MinValue; static volatile bool plBusy;
        static void FetchPlaylists()
        {
            var o = J.Parse(Api("GET", "/me/playlists?limit=30", null));
            var l = new List<SpfPlaylist>();
            foreach (var p in J.Arr(o, "items") ?? new object[0])
            {
                if (p == null) continue;
                l.Add(new SpfPlaylist { Name = J.Str(p, "name"), Uri = J.Str(p, "uri"), Image = J.Str(p, "images.0.url"), Owner = J.Str(p, "owner.display_name"), Tracks = (int)J.Num(p, "tracks.total", J.Num(p, "items.total")) });
            }
            plAt = DateTime.UtcNow;
            var n = state.Clone(); n.Playlists = l; n.Error = null; Set(n);
        }

        // ---------------- actions ----------------
        public static void ToggleLike()
        {
            var s = state; if (!Connected) return;
            if (DemoApi) { var d = s.Clone(); d.Liked = !d.Liked; Set(d); return; }
            Run(() =>
            {
                var cur = state;
                if (cur.TrackId == "" || cur.TrackKey != SpfSession.Snap.Key) { wantKey = SpfSession.Snap.Key; FetchPlayer(); cur = state; }
                if (cur.TrackId == "") throw new Exception("No track to like");
                bool like = !cur.Liked;
                var n = cur.Clone(); n.Liked = like; n.Error = null; Set(n);     // optimistic
                try { Api(like ? "PUT" : "DELETE", "/me/tracks?ids=" + cur.TrackId, null); }
                catch { var r = state.Clone(); r.Liked = !like; Set(r); throw; }
            });
        }
        public static void ToggleShuffle()
        {
            var s = state.Clone(); s.Shuffle = !s.Shuffle; s.Error = null; Set(s);
            if (DemoApi) return;
            bool v = s.Shuffle;
            Run(() => { try { Api("PUT", "/me/player/shuffle?state=" + (v ? "true" : "false"), null); } catch { var r = state.Clone(); r.Shuffle = !v; Set(r); throw; } });
        }
        public static void CycleRepeat()
        {
            var s = state.Clone(); var prev = s.Repeat;
            s.Repeat = prev == "off" ? "context" : prev == "context" ? "track" : "off"; s.Error = null; Set(s);
            if (DemoApi) return;
            var v = s.Repeat;
            Run(() => { try { Api("PUT", "/me/player/repeat?state=" + v, null); } catch { var r = state.Clone(); r.Repeat = prev; Set(r); throw; } });
        }
        public static void PlayContext(SpfPlaylist p)
        {
            if (DemoApi) return;
            Run(() =>
            {
                try { Api("PUT", "/me/player/play", "{\"context_uri\":" + J.Stringify(p.Uri) + "}"); }
                catch (Exception e)
                {
                    // no active device / no Premium: at least open the playlist in the app
                    GamUtil.Launch(p.Uri);
                    if (e.Message.StartsWith("Needs")) throw;
                }
                wantKey = "";
            });
        }

        static void DemoState(string key)
        {
            wantKey = key;
            var n = new SpfApiSnap { Known = true, TrackKey = key, TrackId = "demo", Liked = true, Shuffle = true, Repeat = "context", Device = "DESKTOP-PC" };
            n.Playlists = new List<SpfPlaylist> {
                new SpfPlaylist { Name = "Liked Songs", Tracks = 1284, Uri = "spotify:collection" },
                new SpfPlaylist { Name = "Late Night Drive", Tracks = 64, Uri = "spotify:playlist:demo1" },
                new SpfPlaylist { Name = "Deep Focus", Tracks = 212, Uri = "spotify:playlist:demo2" },
                new SpfPlaylist { Name = "Gym Hits 2026", Tracks = 48, Uri = "spotify:playlist:demo3" },
                new SpfPlaylist { Name = "Discover Weekly", Tracks = 30, Uri = "spotify:playlist:demo4" } };
            plAt = DateTime.UtcNow;
            state = n;
        }
    }
}
