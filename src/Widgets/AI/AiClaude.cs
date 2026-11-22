// Claude Code usage from its local JSONL transcripts (~/.claude/projects/**/*.jsonl, also inside WSL).
// Session blocks follow ccusage: a block starts at the first message after a ≥5h gap (or after the previous block ended),
// rounded down to the hour, and lasts 5 hours.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NovaBar
{
    public class AiClaudeSnap : AiSnap
    {
        public bool Found;                 // any transcript folder exists
        public int Roots; public string RootList = "";
        public DateTime BlockStart, BlockEnd, LastAt;   // BlockEnd = MinValue when no active block
        public double BlockTok, BlockCache; public int BlockMsgs;
        public double WeekTok; public int WeekMsgs;
        public double[] Daily = new double[7];
        public List<AiRow> Models = new List<AiRow>();
        public List<double> BlockHist = new List<double>(); // tokens per past blocks (last 8), for context
        // exact numbers if a rate-limit snapshot file exists (statusline tee); -1 = none
        public double LimFive = -1, LimWeek = -1; public DateTime LimFiveReset, LimWeekReset;
    }

    public static class AiClaudeSrc
    {
        public const string Key = "ai.claude";
        class Ev { public DateTime T; public string Model; public long In, Out, CW, CR; }
        static readonly Dictionary<string, AiCursor> curs = new Dictionary<string, AiCursor>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Ev> evs = new Dictionary<string, Ev>();
        static readonly TimeSpan Five = TimeSpan.FromHours(5);

        public static void Define() { Hub.Define(Key, 60000, Sample); }

        static object Sample()
        {
            var now = DateTime.UtcNow; var cutoff = now.AddDays(-8);
            var roots = AiPaths.Find(new[] { ".claude", @".config\claude" }, "CLAUDE_CONFIG_DIR", AiCfg.ClaudeExtra);
            var snap = new AiClaudeSnap { RootList = string.Join("; ", roots) };
            foreach (var root in roots)
            {
                var proj = Path.Combine(root, "projects");
                if (!Directory.Exists(proj)) continue;
                snap.Found = true; snap.Roots++;
                foreach (var fi in AiPaths.Recent(proj, "*.jsonl", cutoff))
                {
                    AiCursor c; if (!curs.TryGetValue(fi.FullName, out c)) curs[fi.FullName] = c = new AiCursor();
                    AiIo.ReadNew(fi, c, Line);   // duplicates (resumed sessions) are removed by message id
                }
                ReadLimits(Path.Combine(root, "novabar-limits.json"), snap, now);
            }
            // prune
            foreach (var k in evs.Where(kv => kv.Value.T < cutoff).Select(kv => kv.Key).ToList()) evs.Remove(k);

            var list = evs.Values.OrderBy(e => e.T).ToList();
            // ---- 5-hour blocks ----
            DateTime bs = DateTime.MinValue, be = DateTime.MinValue, last = DateTime.MinValue; double bt = 0, bc = 0; int bm = 0;
            foreach (var e in list)
            {
                if (bs == DateTime.MinValue || e.T >= be || e.T - last >= Five)
                {
                    if (bs != DateTime.MinValue) { snap.BlockHist.Add(bt); }
                    bs = new DateTime(e.T.Year, e.T.Month, e.T.Day, e.T.Hour, 0, 0, DateTimeKind.Utc); be = bs + Five; bt = 0; bc = 0; bm = 0;
                }
                bt += e.In + e.Out + e.CW; bc += e.CR; bm++; last = e.T;
            }
            if (snap.BlockHist.Count > 8) snap.BlockHist.RemoveRange(0, snap.BlockHist.Count - 8);
            snap.LastAt = last;
            if (bs != DateTime.MinValue && now < be && now - last < Five) { snap.BlockStart = bs; snap.BlockEnd = be; snap.BlockTok = bt; snap.BlockCache = bc; snap.BlockMsgs = bm; }
            else if (bs != DateTime.MinValue) snap.BlockHist.Add(bt);
            // ---- week / daily / models ----
            var wk = now.AddDays(-7); var models = new Dictionary<string, AiRow>();
            foreach (var e in list)
            {
                double t = e.In + e.Out + e.CW;
                int di = AiDays.Index(e.T); if (di >= 0) snap.Daily[di] += t;
                if (e.T < wk) continue;
                snap.WeekTok += t; snap.WeekMsgs++;
                var name = AiFmt.Model(e.Model); AiRow r; if (!models.TryGetValue(name, out r)) models[name] = r = new AiRow { Name = name };
                r.Tokens += t; r.Count++;
            }
            snap.Models = models.Values.OrderByDescending(r => r.Tokens).ToList();
            snap.Sig = string.Join("|", snap.Found, snap.Roots, snap.BlockEnd.Ticks, snap.BlockTok, snap.WeekTok, snap.LastAt.Ticks, AiFmt.N(snap.LimFive), AiFmt.N(snap.LimWeek), snap.LimFiveReset.Ticks, snap.LimWeekReset.Ticks, string.Join(",", snap.Daily));
            return snap;
        }

        static void Line(string s)
        {
            int u = s.IndexOf("\"usage\":{", StringComparison.Ordinal);
            if (u < 0 || s.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) return;
            var model = AiIo.StrAfter(s, "model");
            if (model == null || model.StartsWith("<")) return;          // <synthetic> error messages
            var ts = AiIo.LastStr(s, "timestamp"); var t = AiFmt.ParseUtc(ts); if (t == DateTime.MinValue) return;
            var id = AiIo.StrAfter(s, "id"); var req = AiIo.StrAfter(s, "requestId");
            var key = (id ?? ts) + ":" + (req ?? "");
            int until = s.IndexOf("\"iterations\"", u, StringComparison.Ordinal);
            var e = new Ev
            {
                T = t, Model = model,
                In = AiIo.NumAfter(s, "input_tokens", u, until), Out = AiIo.NumAfter(s, "output_tokens", u, until),
                CW = AiIo.NumAfter(s, "cache_creation_input_tokens", u, until), CR = AiIo.NumAfter(s, "cache_read_input_tokens", u, until)
            };
            evs[key] = e;   // later copies of a streamed message carry the final usage
        }

        /// <summary>Optional exact limits: a JSON file holding Claude Code's status-line "rate_limits" payload
        /// ({"five_hour":{"used_percentage":..,"resets_at":epoch},"seven_day":{..}}), e.g. written by a status-line script.</summary>
        static void ReadLimits(string path, AiClaudeSnap snap, DateTime now)
        {
            try
            {
                if (!File.Exists(path)) return;
                var d = J.Parse(AiIo.ReadAll(path)); if (d == null) return;
                if (J.Get(d, "rate_limits") != null) d = J.Get(d, "rate_limits");
                Action<string, Action<double, DateTime>> one = (k, set) =>
                {
                    var w = J.Get(d, k); if (w == null) return;
                    double p = J.Num(w, "used_percentage", J.Num(w, "utilization", -1)); if (p < 0) return;
                    double ra = J.Num(w, "resets_at", 0); DateTime r = ra > 0 ? AiFmt.FromEpoch(ra) : AiFmt.ParseUtc(J.Str(w, "resets_at"));
                    if (r != DateTime.MinValue && r < now) p = 0;        // window rolled over since the snapshot
                    set(p / 100.0, r);
                };
                one("five_hour", (p, r) => { snap.LimFive = p; snap.LimFiveReset = r; });
                one("seven_day", (p, r) => { snap.LimWeek = p; snap.LimWeekReset = r; });
            }
            catch { }
        }
    }
}
