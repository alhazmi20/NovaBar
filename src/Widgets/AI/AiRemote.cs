// Account-based samplers: DeepSeek (balance API), Z.ai / Zhipu GLM Coding Plan (quota API), Cursor (dashboard API, session cookie).
// Grok has no public usage API for SuperGrok credits → sample data only.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NovaBar
{
    public class AiDeepSeekSnap : AiSnap
    {
        public double Balance, Granted, ToppedUp, Today, Week; public string Cur = "$"; public bool Available = true, Error;
        public double[] Daily = new double[7]; public DateTime At;
    }
    public class AiGlmSnap : AiSnap
    {
        public List<AiWin> Wins = new List<AiWin>(); public string Plan = ""; public bool Error; public DateTime At;
    }
    public class AiCursorSnap : AiSnap
    {
        public double UsedCents, LimitCents, OnDemandCents; public DateTime CycleStart, CycleEnd; public string Plan = "";
        public double Tokens; public List<AiRow> Models = new List<AiRow>(); public bool Error; public DateTime At;
    }

    public static class AiRemoteSrc
    {
        public const string DeepSeekKey = "ai.deepseek", GlmKey = "ai.glm", CursorKey = "ai.cursor";

        public static void DefineAccounts()
        {
            Accounts.Define("deepseek", "DeepSeek", "Paste an API key from platform.deepseek.com → API keys. Only the balance endpoint is read; spend is derived from balance changes.", "https://platform.deepseek.com/api_keys", Accounts.F("key", "API key", true, "sk-..."));
            Accounts.Define("glm", "Z.ai / Zhipu GLM", "Paste your GLM Coding Plan API key (z.ai or open.bigmodel.cn). Used only to read your plan quota.", "https://z.ai/manage-apikey/apikey-list", Accounts.F("key", "API key", true, ""));
            Accounts.Define("cursor", "Cursor", "Paste the value of the WorkosCursorSessionToken cookie from cursor.com (DevTools → Application → Cookies). Used only to read your usage summary.", "https://cursor.com/dashboard", Accounts.F("session", "Session token (cookie)", true, "user_...%3A%3A..."));
        }
        public static void Define()
        {
            Hub.Define(DeepSeekKey, 10 * 60000, DeepSeek);
            Hub.Define(GlmKey, 5 * 60000, Glm);
            Hub.Define(CursorKey, 10 * 60000, Cursor);
        }

        // ------------------------------------------------------------------ DeepSeek
        static object DeepSeek()
        {
            var key = Accounts.Get("deepseek", "key"); if (string.IsNullOrEmpty(key)) return null;
            var d = J.Parse(Http.Get("https://api.deepseek.com/user/balance", "Authorization: Bearer " + key));
            var st = StateStore.Load("ai-deepseek");
            var snap = new AiDeepSeekSnap { At = DateTime.UtcNow };
            var infos = J.Arr(d, "balance_infos");
            if (infos == null || infos.Count == 0) { snap.Error = true; snap.Sig = "err"; return snap; }
            object bi = infos[0]; foreach (var o in infos) if (J.Str(o, "currency") == "USD") bi = o;
            var cur = J.Str(bi, "currency", "USD");
            snap.Cur = cur == "USD" ? "$" : cur == "CNY" ? "¥" : cur + " ";
            snap.Balance = J.Num(bi, "total_balance"); snap.Granted = J.Num(bi, "granted_balance"); snap.ToppedUp = J.Num(bi, "topped_up_balance");
            snap.Available = J.Bool(d, "is_available", true);
            // spend = sum of balance decreases between samples (top-ups are ignored), bucketed by local day
            var days = J.Obj(st, "days") ?? new Dictionary<string, object>();
            double prev = J.Num(st, "prev", -1);
            if (J.Str(st, "cur", cur) != cur) prev = -1;
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (prev >= 0 && snap.Balance < prev - 1e-9) days[today] = J.Num(days, today) + (prev - snap.Balance);
            st["prev"] = snap.Balance; st["cur"] = cur;
            foreach (var k in days.Keys.ToList()) { DateTime dt; if (!DateTime.TryParse(k, out dt) || (DateTime.Now.Date - dt).TotalDays > 30) days.Remove(k); }
            st["days"] = days; StateStore.Save("ai-deepseek", st);
            for (int i = 0; i < 7; i++) snap.Daily[i] = J.Num(days, DateTime.Now.Date.AddDays(i - 6).ToString("yyyy-MM-dd"));
            snap.Today = snap.Daily[6]; snap.Week = snap.Daily.Sum();
            snap.Sig = string.Join("|", snap.Balance, snap.Today, snap.Week, cur, snap.Available);
            return snap;
        }

        // ------------------------------------------------------------------ GLM
        static object Glm()
        {
            var key = Accounts.Get("glm", "key"); if (string.IsNullOrEmpty(key)) return null;
            var host = (AiCfg.GlmRegion ?? "").StartsWith("China") ? "https://open.bigmodel.cn" : "https://api.z.ai";
            var d = J.Parse(Http.Get(host + "/api/monitor/usage/quota/limit", "Authorization: Bearer " + key, "Accept-Language: en-US,en"));
            var snap = new AiGlmSnap { At = DateTime.UtcNow };
            var lim = J.Arr(d, "data.limits");
            if (lim == null) { snap.Error = true; snap.Sig = "err"; return snap; }
            snap.Plan = J.Str(d, "data.planName", J.Str(d, "data.level", ""));
            foreach (var l in lim)
            {
                var type = J.Str(l, "type");
                double pct = J.Num(l, "percentage", -1);
                double usage = J.Num(l, "usage", 0), curv = J.Num(l, "currentValue", 0), rem = J.Num(l, "remaining", 0);
                if (pct < 0 && usage > 0) pct = curv / usage * 100;
                var nr = J.Num(l, "nextResetTime", 0);
                var reset = nr > 1e12 ? AiFmt.FromEpochMs(nr) : nr > 0 ? AiFmt.FromEpoch(nr) : DateTime.MinValue;
                string label = type == "TOKENS_LIMIT" ? "Prompts · 5-hour" : type == "TIME_LIMIT" ? "Web tools · month" : type;
                string detail = usage > 0 ? AiFmt.Tok(curv) + " / " + AiFmt.Tok(usage) : "";
                snap.Wins.Add(new AiWin(label, pct / 100.0, reset, detail));
            }
            snap.Wins = snap.Wins.OrderBy(w => w.Label.StartsWith("Prompts") ? 0 : 1).ToList();
            snap.Sig = string.Join("|", snap.Wins.Select(w => w.Label + AiFmt.N(w.Frac) + w.Reset.Ticks)) + snap.Plan;
            return snap;
        }

        // ------------------------------------------------------------------ Cursor
        static object Cursor()
        {
            var tok = Accounts.Get("cursor", "session"); if (string.IsNullOrEmpty(tok)) return null;
            var cookie = "Cookie: WorkosCursorSessionToken=" + tok.Trim();
            var d = J.Parse(Http.Get("https://cursor.com/api/usage-summary", cookie, "Origin: https://cursor.com", "Referer: https://cursor.com/dashboard"));
            var snap = new AiCursorSnap { At = DateTime.UtcNow };
            if (d == null) { snap.Error = true; snap.Sig = "err"; return snap; }
            snap.Plan = J.Str(d, "membershipType", "");
            snap.CycleStart = AiFmt.ParseUtc(J.Str(d, "billingCycleStart")); snap.CycleEnd = AiFmt.ParseUtc(J.Str(d, "billingCycleEnd"));
            snap.UsedCents = J.Num(d, "individualUsage.plan.used"); snap.LimitCents = J.Num(d, "individualUsage.plan.limit");
            snap.OnDemandCents = J.Num(d, "individualUsage.onDemand.used");
            // tokens by model for the current cycle (best effort; endpoint used by the dashboard)
            try
            {
                var start = snap.CycleStart == DateTime.MinValue ? DateTime.UtcNow.AddDays(-30) : snap.CycleStart;
                var body = "{\"teamId\":-1,\"startDate\":\"" + (long)(start - new DateTime(1970, 1, 1)).TotalMilliseconds + "\",\"endDate\":\"" + (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds + "\"}";
                var a = J.Parse(Http.Request("POST", "https://cursor.com/api/dashboard/get-aggregated-usage-events", body, "application/json", 10000, cookie, "Origin: https://cursor.com"));
                var ag = J.Arr(a, "aggregations");
                if (ag != null) foreach (var x in ag)
                    {
                        double t = J.Num(x, "inputTokens") + J.Num(x, "outputTokens") + J.Num(x, "cacheWriteTokens");
                        snap.Models.Add(new AiRow { Name = J.Str(x, "modelIntent", "model"), Tokens = t, Cost = J.Num(x, "totalCents") / 100.0 });
                        snap.Tokens += t;
                    }
                snap.Models = snap.Models.OrderByDescending(r => r.Tokens).ToList();
            }
            catch { }
            snap.Sig = string.Join("|", snap.UsedCents, snap.LimitCents, snap.OnDemandCents, snap.Tokens, snap.CycleEnd.Ticks);
            return snap;
        }
    }
}
