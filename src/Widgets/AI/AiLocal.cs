// Local, credential-free samplers: Codex CLI (ChatGPT plan limits), Gemini CLI, OpenCode.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NovaBar
{
    // =====================================================================================  Codex
    public class AiCodexSnap : AiSnap
    {
        public bool Found, HasLimits;
        public double PrimPct = -1, SecPct = -1; public int PrimMin = 300, SecMin = 10080;
        public DateTime PrimReset, SecReset, LimAt, LastAt;
        public string Plan = "";
        public double SessionTok, WeekTok; public int WeekTurns;
        public double[] Daily = new double[7];
        public List<AiRow> Models = new List<AiRow>();
    }

    /// <summary>Codex CLI writes ~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl; token_count events carry "rate_limits".</summary>
    public static class AiCodexSrc
    {
        public const string Key = "ai.codex";
        class Ev { public DateTime T; public string Model; public double Tok; }
        class FileData { public List<Ev> Evs = new List<Ev>(); public string Model; }
        static readonly Dictionary<string, AiCursor> curs = new Dictionary<string, AiCursor>(StringComparer.OrdinalIgnoreCase);
        static DateTime limAt = DateTime.MinValue; static object lastLim; static string lastPlan = "";

        public static void Define() { Hub.Define(Key, 60000, Sample); }

        static object Sample()
        {
            var now = DateTime.UtcNow; var cutoff = now.AddDays(-8);
            var roots = AiPaths.Find(new[] { ".codex" }, "CODEX_HOME", AiCfg.CodexExtra);
            var snap = new AiCodexSnap();
            string newest = null; DateTime newestT = DateTime.MinValue;
            foreach (var root in roots)
            {
                var dir = Path.Combine(root, "sessions"); if (!Directory.Exists(dir)) continue;
                snap.Found = true;
                foreach (var fi in AiPaths.Recent(dir, "*.jsonl", cutoff))
                {
                    AiCursor c; if (!curs.TryGetValue(fi.FullName, out c)) curs[fi.FullName] = c = new AiCursor { Data = new FileData() };
                    var fd = (FileData)c.Data;
                    if (fi.Length < c.Offset) fd.Evs.Clear();
                    AiIo.ReadNew(fi, c, s => Line(s, fd));
                    if (fi.LastWriteTimeUtc > newestT) { newestT = fi.LastWriteTimeUtc; newest = fi.FullName; }
                }
            }
            foreach (var k in curs.Where(kv => kv.Value.Mtime < cutoff).Select(kv => kv.Key).ToList()) curs.Remove(k);
            // limits (latest event wins)
            if (lastLim != null)
            {
                snap.HasLimits = true; snap.LimAt = limAt; snap.Plan = lastPlan;
                Win(J.Get(lastLim, "primary"), limAt, now, out snap.PrimPct, out snap.PrimReset, ref snap.PrimMin);
                Win(J.Get(lastLim, "secondary"), limAt, now, out snap.SecPct, out snap.SecReset, ref snap.SecMin);
            }
            var wk = now.AddDays(-7); var models = new Dictionary<string, AiRow>();
            foreach (var kv in curs)
            {
                var fd = (FileData)kv.Value.Data;
                foreach (var e in fd.Evs)
                {
                    if (e.T > snap.LastAt) snap.LastAt = e.T;
                    int di = AiDays.Index(e.T); if (di >= 0) snap.Daily[di] += e.Tok;
                    if (e.T < wk) continue;
                    snap.WeekTok += e.Tok; snap.WeekTurns++;
                    var n = AiFmt.Model(e.Model ?? "gpt-5-codex"); AiRow r; if (!models.TryGetValue(n, out r)) models[n] = r = new AiRow { Name = n };
                    r.Tokens += e.Tok; r.Count++;
                }
                if (kv.Key == newest) snap.SessionTok = fd.Evs.Sum(e => e.Tok);
            }
            snap.Models = models.Values.OrderByDescending(r => r.Tokens).ToList();
            snap.Sig = string.Join("|", snap.Found, snap.HasLimits, AiFmt.N(snap.PrimPct), AiFmt.N(snap.SecPct), snap.PrimReset.Ticks, snap.SecReset.Ticks, snap.WeekTok, snap.SessionTok, string.Join(",", snap.Daily));
            return snap;
        }

        static void Win(object w, DateTime at, DateTime now, out double pct, out DateTime reset, ref int minutes)
        {
            pct = -1; reset = DateTime.MinValue;
            if (w == null) return;
            pct = J.Num(w, "used_percent", -1) / 100.0;
            var wm = J.Num(w, "window_minutes", 0); if (wm > 0) minutes = (int)wm;
            var ra = J.Num(w, "resets_at", 0); var rin = J.Num(w, "resets_in_seconds", -1);
            if (ra > 0) reset = AiFmt.FromEpoch(ra); else if (rin >= 0) reset = at.AddSeconds(rin);
            if (reset != DateTime.MinValue && reset < now) { pct = 0; reset = DateTime.MinValue; }   // window rolled over
        }

        static void Line(string s, FileData fd)
        {
            if (s.IndexOf("\"turn_context\"", StringComparison.Ordinal) >= 0 || s.IndexOf("\"session_meta\"", StringComparison.Ordinal) >= 0)
            {
                var m = AiIo.StrAfter(s, "model"); if (!string.IsNullOrEmpty(m)) fd.Model = m;
                return;
            }
            if (s.IndexOf("\"token_count\"", StringComparison.Ordinal) < 0) return;
            var d = J.Parse(s); if (d == null) return;
            var t = AiFmt.ParseUtc(J.Str(d, "timestamp")); if (t == DateTime.MinValue) return;
            var p = J.Get(d, "payload") ?? d;
            var last = J.Get(p, "info.last_token_usage");
            if (last != null)
            {
                double tok = J.Num(last, "input_tokens") - J.Num(last, "cached_input_tokens") + J.Num(last, "output_tokens");
                if (tok > 0) fd.Evs.Add(new Ev { T = t, Model = fd.Model, Tok = tok });
            }
            var rl = J.Get(p, "rate_limits");
            if (rl != null && J.Get(rl, "primary") != null && t >= limAt)
            {
                limAt = t; lastLim = rl;
                var plan = J.Str(rl, "plan_type", ""); if (plan != "") lastPlan = plan;
            }
        }
    }

    // =====================================================================================  Gemini CLI
    public class AiGeminiSnap : AiSnap
    {
        public bool Found;
        public int ProToday, FlashToday; public double TokToday, WeekTok;
        public DateTime LastAt, ResetAt;
        public double[] Daily = new double[7];
        public List<AiRow> Models = new List<AiRow>();
    }

    /// <summary>Gemini CLI keeps chats under ~/.gemini/tmp/&lt;project&gt;/chats/session-*.json (messages with model + tokens).
    /// Daily quota (resets midnight Pacific) is estimated by counting model replies against plan limits.</summary>
    public static class AiGeminiSrc
    {
        public const string Key = "ai.gemini";
        class Ev { public DateTime T; public string Model; public double Tok; }
        static readonly Dictionary<string, AiCursor> curs = new Dictionary<string, AiCursor>(StringComparer.OrdinalIgnoreCase);

        public static void Define() { Hub.Define(Key, 60000, Sample); }

        public static DateTime PacificMidnightUtc(out DateTime dayStartUtc)
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); } catch { tz = TimeZoneInfo.Utc; }
            var pt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(pt.Date, DateTimeKind.Unspecified), tz);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(pt.Date.AddDays(1), DateTimeKind.Unspecified), tz);
        }

        static object Sample()
        {
            var now = DateTime.UtcNow; var cutoff = now.AddDays(-8);
            var roots = AiPaths.Find(new[] { ".gemini" }, "GEMINI_CLI_HOME", AiCfg.GeminiExtra);
            var snap = new AiGeminiSnap();
            foreach (var root in roots)
            {
                var tmp = Path.Combine(root, "tmp"); if (!Directory.Exists(tmp)) { snap.Found |= File.Exists(Path.Combine(root, "settings.json")); continue; }
                snap.Found = true;
                foreach (var fi in AiPaths.Recent(tmp, "session-*.json", cutoff))
                {
                    AiCursor c; if (!curs.TryGetValue(fi.FullName, out c)) curs[fi.FullName] = c = new AiCursor();
                    if (c.Len == fi.Length && c.Mtime == fi.LastWriteTimeUtc) continue;     // chats are rewritten whole: re-read on change only
                    c.Len = fi.Length; c.Mtime = fi.LastWriteTimeUtc;
                    var evs = new List<Ev>();
                    var d = J.Parse(AiIo.ReadAll(fi.FullName));
                    var msgs = J.Arr(d, "messages");
                    if (msgs != null) foreach (var m in msgs)
                        {
                            if (J.Str(m, "type") != "gemini") continue;
                            var t = AiFmt.ParseUtc(J.Str(m, "timestamp")); if (t == DateTime.MinValue) continue;
                            evs.Add(new Ev { T = t, Model = J.Str(m, "model", "gemini-2.5-pro"), Tok = J.Num(m, "tokens.total", J.Num(m, "tokens.input") + J.Num(m, "tokens.output")) });
                        }
                    c.Data = evs;
                }
            }
            foreach (var k in curs.Where(kv => kv.Value.Mtime < cutoff).Select(kv => kv.Key).ToList()) curs.Remove(k);
            DateTime dayStart; snap.ResetAt = PacificMidnightUtc(out dayStart);
            var wk = now.AddDays(-7); var models = new Dictionary<string, AiRow>();
            foreach (var c in curs.Values)
            {
                var evs = c.Data as List<Ev>; if (evs == null) continue;
                foreach (var e in evs)
                {
                    if (e.T > snap.LastAt) snap.LastAt = e.T;
                    int di = AiDays.Index(e.T); if (di >= 0) snap.Daily[di] += e.Tok;
                    if (e.T >= dayStart) { if (e.Model.IndexOf("pro", StringComparison.OrdinalIgnoreCase) >= 0) snap.ProToday++; else snap.FlashToday++; snap.TokToday += e.Tok; }
                    if (e.T < wk) continue;
                    snap.WeekTok += e.Tok;
                    var n = AiFmt.Model(e.Model); AiRow r; if (!models.TryGetValue(n, out r)) models[n] = r = new AiRow { Name = n };
                    r.Tokens += e.Tok; r.Count++;
                }
            }
            snap.Models = models.Values.OrderByDescending(r => r.Tokens).ToList();
            snap.Sig = string.Join("|", snap.Found, snap.ProToday, snap.FlashToday, snap.WeekTok, snap.ResetAt.Ticks, string.Join(",", snap.Daily));
            return snap;
        }
    }

    // =====================================================================================  OpenCode
    public class AiOpenCodeSnap : AiSnap
    {
        public bool Found;
        public string SessionId = "", SessionTitle = "";
        public double SesTok, SesCache, SesCost; public int SesMsgs; public DateTime SesStart, SesLast;
        public double WeekTok, WeekCost; public int WeekMsgs;
        public double[] Daily = new double[7];
        public List<AiRow> Models = new List<AiRow>();
    }

    /// <summary>OpenCode stores one JSON per message: ~/.local/share/opencode/storage/message/&lt;session&gt;/msg_*.json
    /// (role, modelID, time.created ms, tokens{input,output,reasoning,cache{read,write}}, cost).</summary>
    public static class AiOpenCodeSrc
    {
        public const string Key = "ai.opencode";
        class Ev { public DateTime T; public string Model, Ses; public double Tok, Cache, Cost; }
        static readonly Dictionary<string, DateTime> dirSeen = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, KeyValuePair<DateTime, Ev>> files = new Dictionary<string, KeyValuePair<DateTime, Ev>>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> titles = new Dictionary<string, string>();

        public static void Define() { Hub.Define(Key, 60000, Sample); }

        static object Sample()
        {
            var now = DateTime.UtcNow; var cutoff = now.AddDays(-8);
            var lad = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var roots = AiPaths.Find(new[] { @".local\share\opencode" }, null, AiCfg.OpenCodeExtra + (string.IsNullOrEmpty(lad) ? "" : ";" + Path.Combine(lad, "opencode")));
            var snap = new AiOpenCodeSnap();
            string sesRoot = null;
            foreach (var root in roots)
            {
                var md = Path.Combine(root, @"storage\message"); if (!Directory.Exists(md)) { snap.Found |= Directory.Exists(Path.Combine(root, "storage")); continue; }
                snap.Found = true; sesRoot = Path.Combine(root, @"storage\session");
                try
                {
                    foreach (var di in new DirectoryInfo(md).EnumerateDirectories())
                    {
                        if (di.LastWriteTimeUtc < cutoff) continue;
                        DateTime seen; if (dirSeen.TryGetValue(di.FullName, out seen) && seen == di.LastWriteTimeUtc && (now - seen).TotalMinutes > 10) continue;
                        dirSeen[di.FullName] = di.LastWriteTimeUtc;
                        foreach (var fi in di.EnumerateFiles("*.json"))
                        {
                            KeyValuePair<DateTime, Ev> old;
                            if (files.TryGetValue(fi.FullName, out old) && old.Key == fi.LastWriteTimeUtc) continue;
                            Ev e = null;
                            var d = J.Parse(AiIo.ReadAll(fi.FullName));
                            if (d != null && J.Str(d, "role") == "assistant")
                            {
                                e = new Ev
                                {
                                    T = AiFmt.FromEpochMs(J.Num(d, "time.created")), Model = J.Str(d, "modelID", "unknown"), Ses = J.Str(d, "sessionID", di.Name),
                                    Tok = J.Num(d, "tokens.input") + J.Num(d, "tokens.output") + J.Num(d, "tokens.reasoning") + J.Num(d, "tokens.cache.write"),
                                    Cache = J.Num(d, "tokens.cache.read"), Cost = J.Num(d, "cost")
                                };
                            }
                            files[fi.FullName] = new KeyValuePair<DateTime, Ev>(fi.LastWriteTimeUtc, e);
                        }
                    }
                }
                catch { }
            }
            var all = files.Values.Select(v => v.Value).Where(e => e != null && e.T >= cutoff).ToList();
            foreach (var k in files.Where(kv => kv.Value.Value == null ? kv.Value.Key < cutoff : kv.Value.Value.T < cutoff).Select(kv => kv.Key).ToList()) files.Remove(k);
            var latest = all.OrderByDescending(e => e.T).FirstOrDefault();
            var wk = now.AddDays(-7); var models = new Dictionary<string, AiRow>();
            foreach (var e in all)
            {
                int di = AiDays.Index(e.T); if (di >= 0) snap.Daily[di] += e.Tok;
                if (latest != null && e.Ses == latest.Ses)
                {
                    snap.SesTok += e.Tok; snap.SesCache += e.Cache; snap.SesCost += e.Cost; snap.SesMsgs++;
                    if (snap.SesStart == DateTime.MinValue || e.T < snap.SesStart) snap.SesStart = e.T;
                    if (e.T > snap.SesLast) snap.SesLast = e.T;
                }
                if (e.T < wk) continue;
                snap.WeekTok += e.Tok; snap.WeekCost += e.Cost; snap.WeekMsgs++;
                var n = AiFmt.Model(e.Model); AiRow r; if (!models.TryGetValue(n, out r)) models[n] = r = new AiRow { Name = n };
                r.Tokens += e.Tok; r.Count++; r.Cost += e.Cost;
            }
            if (latest != null) { snap.SessionId = latest.Ses; snap.SessionTitle = Title(sesRoot, latest.Ses); }
            snap.Models = models.Values.OrderByDescending(r => r.Tokens).ToList();
            snap.Sig = string.Join("|", snap.Found, snap.SessionId, snap.SesTok, snap.SesCost, snap.WeekTok, snap.SessionTitle, string.Join(",", snap.Daily));
            return snap;
        }

        static string Title(string sesRoot, string id)
        {
            string t; if (titles.TryGetValue(id, out t)) return t;
            t = "";
            try
            {
                if (sesRoot != null && Directory.Exists(sesRoot))
                    foreach (var f in Directory.EnumerateFiles(sesRoot, id + ".json", SearchOption.AllDirectories)) { t = J.Str(J.Parse(AiIo.ReadAll(f)), "title"); break; }
            }
            catch { }
            titles[id] = t; return t;
        }
    }
}
