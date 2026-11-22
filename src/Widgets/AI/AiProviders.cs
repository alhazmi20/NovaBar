// Builds the display model (AiProv) for each provider from hub snapshots + per-widget options, with sample fallbacks.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NovaBar
{
    public class AiOpts
    {
        public double ClaudeSession = 10e6, ClaudeWeek = 100e6; public string ClaudePlan = "Max 5x";
        public double GemPro = 100, GemFlash = 1000;
    }

    public static class AiProviders
    {
        public static readonly string[] Keys = { "claude", "chatgpt", "gemini", "grok", "deepseek", "glm", "opencode", "cursor" };
        public static readonly string[] Names = { "Claude", "ChatGPT", "Gemini", "Grok", "DeepSeek", "GLM", "OpenCode", "Cursor" };
        public static string KeyOf(string name) { int i = Array.IndexOf(Names, name); return i < 0 ? "claude" : Keys[i]; }
        public static string NameOf(string key) { int i = Array.IndexOf(Keys, key); return i < 0 ? key : Names[i]; }

        /// <summary>Hub key that feeds a provider (null = no sampler, sample only).</summary>
        public static string HubKey(string key)
        {
            switch (key)
            {
                case "claude": return AiClaudeSrc.Key; case "chatgpt": return AiCodexSrc.Key; case "gemini": return AiGeminiSrc.Key;
                case "opencode": return AiOpenCodeSrc.Key; case "deepseek": return AiRemoteSrc.DeepSeekKey; case "glm": return AiRemoteSrc.GlmKey;
                case "cursor": return AiRemoteSrc.CursorKey; default: return null;
            }
        }
        public static string AccountOf(string key) { return key == "deepseek" || key == "glm" || key == "cursor" ? key : null; }

        static bool defined;
        public static void EnsureDefined()
        {
            if (defined) return; defined = true;
            AiClaudeSrc.Define(); AiCodexSrc.Define(); AiGeminiSrc.Define(); AiOpenCodeSrc.Define(); AiRemoteSrc.Define();
        }

        /// <summary>Claude budgets (input + output + cache-write tokens) used to estimate plan % when no exact snapshot exists.</summary>
        public static void ClaudeBudgets(string plan, out double session, out double week)
        {
            switch (plan)
            {
                case "Pro": session = 2e6; week = 20e6; break;
                case "Max 20x": session = 40e6; week = 400e6; break;
                default: session = 10e6; week = 100e6; break;
            }
        }

        static DateTime NextBoundary(int hours, int offsetMin)
        {
            var n = DateTime.UtcNow; var b = n.Date.AddHours(((int)n.TimeOfDay.TotalHours / hours + 1) * hours).AddMinutes(offsetMin);
            while (b <= n) b = b.AddHours(hours); return b;
        }
        static DateTime NextWeekday(DayOfWeek d, int hourUtc)
        {
            var n = DateTime.UtcNow; var b = n.Date.AddHours(hourUtc);
            while (b.DayOfWeek != d || b <= n) b = b.AddDays(1); return b;
        }
        static List<AiRow> Rows(params object[] kv)
        {
            var l = new List<AiRow>(); for (int i = 0; i + 2 < kv.Length; i += 3) l.Add(new AiRow { Name = (string)kv[i], Tokens = Convert.ToDouble(kv[i + 1]), Count = Convert.ToInt32(kv[i + 2]) }); return l;
        }

        public static AiProv Build(string key, AiOpts o)
        {
            if (o == null) o = new AiOpts();
            var hk = HubKey(key); var snap = hk == null ? null : Hub.Get(hk);
            AiProv p;
            switch (key)
            {
                case "claude": p = Claude(snap as AiClaudeSnap, o); break;
                case "chatgpt": p = Codex(snap as AiCodexSnap); break;
                case "gemini": p = Gemini(snap as AiGeminiSnap, o); break;
                case "grok": p = Grok(); break;
                case "deepseek": p = DeepSeek(Accounts.Connected("deepseek") ? snap as AiDeepSeekSnap : null); break;
                case "glm": p = Glm(Accounts.Connected("glm") ? snap as AiGlmSnap : null); break;
                case "opencode": p = OpenCode(snap as AiOpenCodeSnap); break;
                default: p = Cursor(Accounts.Connected("cursor") ? snap as AiCursorSnap : null); break;
            }
            p.Key = key; if (p.Name == null) p.Name = NameOf(key);
            return p;
        }

        // ------------------------------------------------------------------ Claude
        static AiProv Claude(AiClaudeSnap s, AiOpts o)
        {
            var p = new AiProv { Name = "Claude", Source = "Claude Code logs" };
            if (s == null || !s.Found || (s.WeekMsgs == 0 && s.LimFive < 0))
            {
                p.IsSample = true; p.Source = s == null ? "Reading Claude Code logs…" : "No Claude Code logs found";
                var r1 = NextBoundary(5, 0);
                p.Wins.Add(new AiWin("Session · 5-hour", 0.62, r1, "6.2M / 10M tokens"));
                p.Wins.Add(new AiWin("Weekly", 0.31, NextWeekday(DayOfWeek.Thursday, 17), "31M / 100M tokens"));
                p.Week = new double[] { 4.1e6, 8.8e6, 2.2e6, 11.5e6, 7.4e6, 3.3e6, 6.2e6 };
                p.Models = Rows("Opus 5.5", 28.4e6, 1032, "Sonnet 5", 9.7e6, 412, "Haiku 5.5", 1.2e6, 96);
                p.Stats.Add(new AiStat("Messages · 5h", "184")); p.Stats.Add(new AiStat("Cache reads", "48M"));
                return p;
            }
            bool exact5 = s.LimFive >= 0, exactW = s.LimWeek >= 0;
            double f5 = exact5 ? s.LimFive : s.BlockTok / Math.Max(1, o.ClaudeSession);
            DateTime r5 = exact5 && s.LimFiveReset != DateTime.MinValue ? s.LimFiveReset : s.BlockEnd;
            string d5 = s.BlockEnd == DateTime.MinValue ? "No active session" : AiFmt.Tok(s.BlockTok) + (exact5 ? " tokens" : " / " + AiFmt.Tok(o.ClaudeSession) + " · est.");
            if (s.BlockEnd == DateTime.MinValue && !exact5) f5 = 0;
            p.Wins.Add(new AiWin("Session · 5-hour", f5, r5, d5));
            double fw = exactW ? s.LimWeek : s.WeekTok / Math.Max(1, o.ClaudeWeek);
            p.Wins.Add(new AiWin("Weekly", fw, exactW ? s.LimWeekReset : DateTime.MinValue, AiFmt.Tok(s.WeekTok) + (exactW ? " tokens" : " / " + AiFmt.Tok(o.ClaudeWeek) + " · est.")));
            p.Week = s.Daily; p.Models = s.Models;
            p.Stats.Add(new AiStat("Messages · 5h", s.BlockMsgs.ToString()));
            p.Stats.Add(new AiStat("Cache reads · 5h", AiFmt.Tok(s.BlockCache)));
            p.Stats.Add(new AiStat("Last activity", AiFmt.Ago(s.LastAt)));
            p.Source = (exact5 ? "Exact limits" : o.ClaudePlan + " estimate") + " · " + (s.Roots > 1 ? s.Roots + " folders" : "Claude Code logs");
            return p;
        }

        // ------------------------------------------------------------------ ChatGPT / Codex
        static AiProv Codex(AiCodexSnap s)
        {
            var p = new AiProv { Name = "ChatGPT", Source = "Codex CLI logs", ModelsLabel = "Codex models · 7 days" };
            if (s == null || !s.Found || (!s.HasLimits && s.WeekTok == 0))
            {
                p.IsSample = true; p.Source = s == null ? "Reading Codex logs…" : "No Codex CLI sessions found";
                p.Wins.Add(new AiWin("5-hour", 0.44, NextBoundary(5, 37), "Codex · Plus"));
                p.Wins.Add(new AiWin("Weekly", 0.18, NextWeekday(DayOfWeek.Monday, 9), ""));
                p.Week = new double[] { 0.6e6, 1.4e6, 0.2e6, 2.1e6, 1.1e6, 0.9e6, 1.6e6 };
                p.Models = Rows("GPT-5 Codex", 6.1e6, 214, "GPT-5", 1.8e6, 63);
                p.Stats.Add(new AiStat("This session", "312K")); p.Stats.Add(new AiStat("Turns · 7d", "277"));
                return p;
            }
            Func<int, string> wl = m => m == 300 ? "5-hour" : m == 10080 ? "Weekly" : m % 1440 == 0 ? (m / 1440) + "-day" : m % 60 == 0 ? (m / 60) + "-hour" : m + "-min";
            p.Wins.Add(new AiWin(wl(s.PrimMin), s.PrimPct, s.PrimReset, s.HasLimits ? "" : "No limit data yet"));
            p.Wins.Add(new AiWin(wl(s.SecMin), s.SecPct, s.SecReset, ""));
            if (s.Plan != "") p.Wins[0].Detail = "Plan · " + char.ToUpper(s.Plan[0]) + s.Plan.Substring(1);
            p.Week = s.Daily; p.Models = s.Models;
            p.Stats.Add(new AiStat("This session", AiFmt.Tok(s.SessionTok)));
            p.Stats.Add(new AiStat("Turns · 7d", s.WeekTurns.ToString()));
            p.Stats.Add(new AiStat("Limits as of", AiFmt.Ago(s.LimAt)));
            return p;
        }

        // ------------------------------------------------------------------ Gemini CLI
        static AiProv Gemini(AiGeminiSnap s, AiOpts o)
        {
            var p = new AiProv { Name = "Gemini", Source = "Gemini CLI logs · est." };
            if (s == null || !s.Found || (s.WeekTok == 0 && s.ProToday + s.FlashToday == 0))
            {
                p.IsSample = true; p.Source = s == null ? "Reading Gemini CLI logs…" : "No Gemini CLI chats found";
                DateTime ds; var r = AiGeminiSrc.PacificMidnightUtc(out ds);
                p.Wins.Add(new AiWin("Pro · daily", 0.37, r, "37 / 100 requests"));
                p.Wins.Add(new AiWin("Flash · daily", 0.12, r, "120 / 1000 requests"));
                p.Week = new double[] { 1.2e6, 0.4e6, 2.6e6, 1.9e6, 0.8e6, 1.1e6, 1.5e6 };
                p.Models = Rows("Gemini 2.5 Pro", 5.2e6, 241, "Gemini 2.5 Flash", 2.9e6, 610);
                p.Stats.Add(new AiStat("Tokens today", "1.5M"));
                return p;
            }
            p.Wins.Add(new AiWin("Pro · daily", s.ProToday / Math.Max(1, o.GemPro), s.ResetAt, s.ProToday + " / " + o.GemPro + " requests"));
            p.Wins.Add(new AiWin("Flash · daily", s.FlashToday / Math.Max(1, o.GemFlash), s.ResetAt, s.FlashToday + " / " + o.GemFlash + " requests"));
            p.Week = s.Daily; p.Models = s.Models;
            p.Stats.Add(new AiStat("Tokens today", AiFmt.Tok(s.TokToday)));
            p.Stats.Add(new AiStat("Last activity", AiFmt.Ago(s.LastAt)));
            return p;
        }

        // ------------------------------------------------------------------ Grok (no public API)
        static AiProv Grok()
        {
            var p = new AiProv { Name = "Grok", Source = "SuperGrok · sample", IsSample = true, Note = "xAI has no public SuperGrok usage API yet." };
            p.Wins.Add(new AiWin("Credits · 2-hour", 0.28, NextBoundary(2, 12), "Grok 4 · 28 of 100 credits"));
            p.Wins.Add(new AiWin("Heavy · daily", 0.10, DateTime.UtcNow.Date.AddDays(1), "2 of 20 queries"));
            p.Week = new double[] { 22, 41, 12, 60, 35, 18, 28 }; p.WeekLabel = "Credits per day";
            p.Models = Rows("Grok 4", 160, 160, "Grok 4 Heavy", 24, 24, "Grok 3", 32, 32); p.ModelsLabel = "Credits by model · 7 days";
            return p;
        }

        // ------------------------------------------------------------------ DeepSeek
        static AiProv DeepSeek(AiDeepSeekSnap s)
        {
            var p = new AiProv { Name = "DeepSeek", Source = "DeepSeek API", WeekMoney = true, WeekLabel = "Spend per day", NeedsAccount = s == null };
            if (s == null || s.Error)
            {
                p.IsSample = true; p.Source = s == null ? "Sample · connect DeepSeek" : "Couldn't reach DeepSeek — check the key";
                p.Big = "$18.42"; p.Small = "-$0.86 today";
                p.Stats.Add(new AiStat("Balance", "$18.42")); p.Stats.Add(new AiStat("Today", "$0.86")); p.Stats.Add(new AiStat("This week", "$4.71"));
                p.Week = new double[] { 0.52, 0.91, 0.33, 1.20, 0.47, 0.42, 0.86 };
                return p;
            }
            p.Cur = s.Cur; p.Big = AiFmt.Money(s.Balance, s.Cur); p.Small = s.Today > 0 ? "-" + AiFmt.Money(s.Today, s.Cur) + " today" : "no spend today";
            p.Stats.Add(new AiStat("Balance", AiFmt.Money(s.Balance, s.Cur)));
            p.Stats.Add(new AiStat("Today", AiFmt.Money(s.Today, s.Cur)));
            p.Stats.Add(new AiStat("This week", AiFmt.Money(s.Week, s.Cur)));
            if (s.Granted > 0) p.Stats.Add(new AiStat("Granted", AiFmt.Money(s.Granted, s.Cur)));
            p.Week = s.Daily;
            if (!s.Available) p.Note = "Balance is too low for API calls.";
            else p.Note = "Spend is measured from balance changes while NovaBar runs.";
            return p;
        }

        // ------------------------------------------------------------------ GLM
        static AiProv Glm(AiGlmSnap s)
        {
            var p = new AiProv { Name = "GLM", Source = "Z.ai Coding Plan", NeedsAccount = s == null };
            if (s == null || s.Error || s.Wins.Count == 0)
            {
                p.IsSample = true; p.Source = s == null ? "Sample · connect Z.ai" : "Couldn't read GLM quota — check key/region";
                p.Wins.Add(new AiWin("Prompts · 5-hour", 0.53, NextBoundary(5, 21), "64 / 120 prompts · Lite"));
                p.Wins.Add(new AiWin("Web tools · month", 0.08, DateTime.UtcNow.Date.AddDays(17), "8 / 100 calls"));
                return p;
            }
            p.Wins = s.Wins; if (s.Plan != "") p.Source = "Z.ai · " + s.Plan;
            p.Stats.Add(new AiStat("Updated", AiFmt.Ago(s.At)));
            return p;
        }

        // ------------------------------------------------------------------ OpenCode
        static AiProv OpenCode(AiOpenCodeSnap s)
        {
            var p = new AiProv { Name = "OpenCode", Source = "OpenCode local history" };
            if (s == null || !s.Found || s.WeekMsgs == 0 && s.SesMsgs == 0)
            {
                p.IsSample = true; p.Source = s == null ? "Reading OpenCode history…" : "No OpenCode history found";
                p.Big = "184K"; p.Small = "session · 42m";
                p.Stats.Add(new AiStat("Session", "184K tok")); p.Stats.Add(new AiStat("Messages", "38")); p.Stats.Add(new AiStat("Cost", "$0.62")); p.Stats.Add(new AiStat("This week", "2.4M"));
                p.Week = new double[] { 0.2e6, 0.5e6, 0.1e6, 0.6e6, 0.4e6, 0.3e6, 0.3e6 };
                p.Models = Rows("Claude Sonnet 4.5", 1.3e6, 210, "GPT-5", 0.7e6, 96, "Kimi K2", 0.4e6, 51);
                p.Note = "Fix login flow"; return p;
            }
            p.Big = AiFmt.Tok(s.SesTok); p.Small = "session · " + AiFmt.Left(DateTime.UtcNow + (s.SesLast - s.SesStart)).Replace("now", "0m");
            p.Stats.Add(new AiStat("Session", AiFmt.Tok(s.SesTok) + " tok"));
            p.Stats.Add(new AiStat("Messages", s.SesMsgs.ToString()));
            p.Stats.Add(new AiStat("Cost", AiFmt.Money(s.SesCost, "$")));
            p.Stats.Add(new AiStat("This week", AiFmt.Tok(s.WeekTok)));
            p.Week = s.Daily; p.Models = s.Models;
            p.Note = string.IsNullOrEmpty(s.SessionTitle) ? null : s.SessionTitle;
            return p;
        }

        // ------------------------------------------------------------------ Cursor
        static AiProv Cursor(AiCursorSnap s)
        {
            var p = new AiProv { Name = "Cursor", Source = "Cursor dashboard", NeedsAccount = s == null, ModelsLabel = "Models · this cycle" };
            if (s == null || s.Error)
            {
                p.IsSample = true; p.Source = s == null ? "Sample · connect Cursor" : "Couldn't reach Cursor — refresh the cookie";
                p.Wins.Add(new AiWin("Included usage", 0.47, DateTime.UtcNow.Date.AddDays(12), "$9.40 / $20.00"));
                p.Big = "$9.40"; p.Small = "4.8M tok";
                p.Stats.Add(new AiStat("Spend", "$9.40")); p.Stats.Add(new AiStat("On-demand", "$0.00")); p.Stats.Add(new AiStat("Tokens", "4.8M"));
                p.Models = Rows("claude-4.5-sonnet", 3.1e6, 0, "gpt-5", 1.2e6, 0, "auto", 0.5e6, 0);
                return p;
            }
            double used = s.UsedCents / 100.0, lim = s.LimitCents / 100.0;
            p.Wins.Add(new AiWin("Included usage", lim > 0 ? used / lim : -1, s.CycleEnd, AiFmt.Money(used, "$") + (lim > 0 ? " / " + AiFmt.Money(lim, "$") : "")));
            p.Big = AiFmt.Money(used + s.OnDemandCents / 100.0, "$"); p.Small = s.Tokens > 0 ? AiFmt.Tok(s.Tokens) + " tok" : "this cycle";
            p.Stats.Add(new AiStat("Spend", AiFmt.Money(used, "$")));
            p.Stats.Add(new AiStat("On-demand", AiFmt.Money(s.OnDemandCents / 100.0, "$")));
            p.Stats.Add(new AiStat("Tokens", AiFmt.Tok(s.Tokens)));
            if (s.Plan != "") p.Source = "Cursor · " + char.ToUpper(s.Plan[0]) + s.Plan.Substring(1);
            p.Models = s.Models;
            return p;
        }
    }
}
