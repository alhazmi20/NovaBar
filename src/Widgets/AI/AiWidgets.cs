// The 11 AI usage widgets. Data comes from the shared samplers (AiClaude / AiLocal / AiRemote) via the Hub.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>Shared plumbing: subscribe to the providers' hub keys, settings → sampler config, panel.</summary>
    public abstract class AiWidgetBase : Widget
    {
        protected abstract string MainKey { get; }
        protected virtual IEnumerable<string> ProvKeys { get { return new[] { MainKey }; } }
        protected virtual AiPanelOpts PanelOpts { get { return new AiPanelOpts(); } }

        protected static SettingDef[] ClaudeDefs()
        {
            return new[] {
                SettingDef.Choice("plan", "Claude plan (for % estimate)", "Max 5x", "Pro", "Max 5x", "Max 20x", "Custom"),
                SettingDef.Number("sessionM", "Custom 5-hour budget (M tokens)", 10, 0.1, 1000),
                SettingDef.Number("weekM", "Custom weekly budget (M tokens)", 100, 1, 10000),
                SettingDef.Text("paths", "Extra .claude folders (; separated)", ""),
                SettingDef.Toggle("wsl", "Also read Claude Code inside WSL", true) };
        }
        protected static SettingDef[] PathDefs(string tool, string folder)
        {
            return new[] { SettingDef.Text("paths", "Extra " + folder + " folders (; separated)", ""), SettingDef.Toggle("wsl", "Also read " + tool + " inside WSL", true) };
        }

        protected bool Has(string key) { return SettingDefs.Any(d => d.Key == key); }
        protected AiOpts Opts()
        {
            var o = new AiOpts();
            if (Has("plan"))
            {
                o.ClaudePlan = SStr("plan");
                if (o.ClaudePlan == "Custom") { o.ClaudeSession = Math.Max(0.1, SNum("sessionM")) * 1e6; o.ClaudeWeek = Math.Max(1, SNum("weekM")) * 1e6; }
                else AiProviders.ClaudeBudgets(o.ClaudePlan, out o.ClaudeSession, out o.ClaudeWeek);
            }
            if (Has("proLimit")) { o.GemPro = Math.Max(1, SNum("proLimit")); o.GemFlash = Math.Max(1, SNum("flashLimit")); }
            return o;
        }
        protected AiProv Prov(string key) { return AiProviders.Build(key, Opts()); }

        protected virtual void ApplyCfg()
        {
            if (Has("wsl")) AiCfg.ScanWsl = SBool("wsl");
            if (Has("paths"))
            {
                var p = SStr("paths");
                switch (MainKey)
                {
                    case "claude": AiCfg.ClaudeExtra = p; break; case "chatgpt": AiCfg.CodexExtra = p; break;
                    case "gemini": AiCfg.GeminiExtra = p; break; case "opencode": AiCfg.OpenCodeExtra = p; break;
                }
            }
        }

        public override void OnStart()
        {
            AiProviders.EnsureDefined(); ApplyCfg();
            foreach (var k in ProvKeys.Distinct()) { var hk = AiProviders.HubKey(k); if (hk != null) Hub.Subscribe(hk, this); }
        }
        public override void OnStop() { Hub.UnsubscribeAll(this); }
        public override int RefreshMs { get { return 30000; } }   // reset countdowns tick by the minute

        public override Size PanelSize(float scale) { return new Size((int)(340 * scale), (int)(AiUi.PanelHeight(PanelProv(), PanelOpts) * scale)); }
        protected virtual AiProv PanelProv() { return Prov(MainKey); }
        public override void DrawPanel(DrawCtx c) { AiUi.Panel(c, PanelProv(), PanelOpts, () => Host.OpenSettings("accounts")); }
        public override void OnPanelOpen() { foreach (var k in ProvKeys.Distinct()) { var hk = AiProviders.HubKey(k); if (hk != null) Hub.Refresh(hk); } }
    }

    /// <summary>Ring tile: one ring (or concentric pair) + "62% · resets 2h 14m".</summary>
    public abstract class AiRingWidget : AiWidgetBase
    {
        protected virtual bool Dual { get { return false; } }
        protected virtual bool Label0 { get { return false; } }
        protected virtual string Extra(AiProv p) { return null; }
        public override int Measure(DrawCtx c)
        {
            var p = Prov(MainKey);
            return (int)Math.Ceiling(c.S(10) + AiUi.RingBlock(c, 0, p, Dual, false, false, Extra(p), Label0) + c.S(11));
        }
        public override void Draw(DrawCtx c)
        {
            var p = Prov(MainKey);
            AiUi.RingBlock(c, c.Bounds.X + c.S(10), p, Dual, false, true, Extra(p), Label0);
            if (p.IsSample) AiUi.SampleDot(c);
        }
    }

    /// <summary>Value tile: brand chip + big value + small caption.</summary>
    public abstract class AiValueWidget : AiWidgetBase
    {
        protected abstract void Values(AiProv p, out string big, out string small);
        public override int Measure(DrawCtx c)
        {
            var p = Prov(MainKey); string a, b; Values(p, out a, out b);
            return (int)Math.Ceiling(c.S(10) + AiUi.ValueBlock(c, 0, p, a, b, false) + c.S(11));
        }
        public override void Draw(DrawCtx c)
        {
            var p = Prov(MainKey); string a, b; Values(p, out a, out b);
            AiUi.ValueBlock(c, c.Bounds.X + c.S(10), p, a, b, true);
            if (p.IsSample) AiUi.SampleDot(c);
        }
    }

    // =====================================================================================  AI Limits
    [Widget("ailimits", "AI Limits", "AI", "Two AI plans on one tile, with when each limit resets.", RefName = "AI Limits", Data = DataKind.Live)]
    public class AiLimitsWidget : AiWidgetBase
    {
        static AiLimitsWidget() { AiRemoteSrc.DefineAccounts(); }
        public override SettingDef[] SettingDefs
        {
            get
            {
                return new[] {
                    SettingDef.Choice("provA", "Provider A", "Claude", AiProviders.Names),
                    SettingDef.Choice("provB", "Provider B", "ChatGPT", AiProviders.Names),
                    SettingDef.Choice("plan", "Claude plan (for % estimate)", "Max 5x", "Pro", "Max 5x", "Max 20x", "Custom"),
                    SettingDef.Number("sessionM", "Custom Claude 5-hour budget (M tokens)", 10, 0.1, 1000),
                    SettingDef.Number("weekM", "Custom Claude weekly budget (M tokens)", 100, 1, 10000),
                    SettingDef.Number("proLimit", "Gemini Pro requests / day", 100, 1, 100000),
                    SettingDef.Number("flashLimit", "Gemini Flash requests / day", 1000, 1, 100000),
                    SettingDef.Toggle("wsl", "Also read CLI logs inside WSL", true) };
            }
        }
        string A { get { return AiProviders.KeyOf(SStr("provA")); } }
        string B { get { var b = AiProviders.KeyOf(SStr("provB")); return b == A ? (A == "claude" ? "chatgpt" : "claude") : b; } }
        protected override string MainKey { get { return A; } }
        protected override IEnumerable<string> ProvKeys { get { return new[] { A, B }; } }

        public override int Measure(DrawCtx c)
        {
            var pa = Prov(A); var pb = Prov(B);
            return (int)Math.Ceiling(c.S(10) + AiUi.RingBlock(c, 0, pa, false, true, false) + c.S(19) + AiUi.RingBlock(c, 0, pb, false, true, false) + c.S(11));
        }
        public override void Draw(DrawCtx c)
        {
            var pa = Prov(A); var pb = Prov(B);
            float x = c.Bounds.X + c.S(10);
            x += AiUi.RingBlock(c, x, pa, false, true, true) + c.S(9);
            using (var pen = new Pen(Color.FromArgb(c.T.Dark ? 34 : 40, c.T.Text), 1f)) c.G.DrawLine(pen, x, c.Bounds.Y + c.S(9), x, c.Bounds.Bottom - c.S(9));
            x += c.S(10);
            AiUi.RingBlock(c, x, pb, false, true, true);
            if (pa.IsSample || pb.IsSample) AiUi.SampleDot(c);
        }

        static readonly AiPanelOpts sub = new AiPanelOpts { MaxWins = 2, Stats = false, Chart = false, Models = false };
        public override Size PanelSize(float scale)
        {
            int h = AiUi.PanelHeight(Prov(A), sub) + AiUi.PanelHeight(Prov(B), sub) + 2;
            return new Size((int)(340 * scale), (int)(h * scale));
        }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var pa = Prov(A); var pb = Prov(B);
            int ha = c.Si(AiUi.PanelHeight(pa, sub));
            c.Bounds = new Rectangle(b.X, b.Y, b.Width, ha);
            AiUi.Panel(c, pa, sub, () => Host.OpenSettings("accounts"));
            using (var pen = new Pen(Color.FromArgb(c.T.Dark ? 26 : 34, c.T.Text), 1f)) c.G.DrawLine(pen, b.X + c.S(16), b.Y + ha, b.Right - c.S(16), b.Y + ha);
            c.Bounds = new Rectangle(b.X, b.Y + ha + c.Si(2), b.Width, b.Height - ha - c.Si(2));
            AiUi.Panel(c, pb, sub, () => Host.OpenSettings("accounts"));
            c.Bounds = b;
        }
    }

    // =====================================================================================  Claude
    [Widget("claude", "Claude Usage", "AI", "Claude session (5-hour window) limit used and when it resets, from Claude Code's local logs.", RefName = "Claude Usage", Data = DataKind.Live)]
    public class AiClaudeWidget : AiRingWidget
    {
        protected override string MainKey { get { return "claude"; } }
        public override SettingDef[] SettingDefs { get { return ClaudeDefs(); } }
        protected override AiPanelOpts PanelOpts { get { return new AiPanelOpts { MaxWins = 1, Models = false }; } }
    }

    [Widget("claude-plan", "Claude Plan Usage", "AI", "Session and weekly Claude limits, plus your most-used model.", RefName = "Claude Plan Usage", Data = DataKind.Live)]
    public class AiClaudePlanWidget : AiRingWidget
    {
        protected override string MainKey { get { return "claude"; } }
        protected override bool Dual { get { return true; } }
        public override SettingDef[] SettingDefs { get { return ClaudeDefs(); } }
        protected override AiPanelOpts PanelOpts { get { return new AiPanelOpts { MaxWins = 2, Stats = false }; } }
        protected override string Extra(AiProv p) { return p.Models.Count > 0 ? p.Models[0].Name : null; }
    }

    // =====================================================================================  ChatGPT (Codex)
    [Widget("chatgpt", "ChatGPT Usage", "AI", "Your ChatGPT plan's Codex limits (5-hour and weekly), from Codex CLI's local session logs.", RefName = "ChatGPT Usage", Data = DataKind.Live)]
    public class AiChatGptWidget : AiRingWidget
    {
        protected override string MainKey { get { return "chatgpt"; } }
        protected override bool Dual { get { return true; } }
        public override SettingDef[] SettingDefs { get { return PathDefs("Codex CLI", ".codex"); } }
    }

    // =====================================================================================  Gemini
    [Widget("gemini", "Gemini Usage", "AI", "Gemini CLI daily quota for Pro and Flash, estimated from its local chat logs.", RefName = "Gemini Usage", Data = DataKind.Live)]
    public class AiGeminiWidget : AiRingWidget
    {
        protected override string MainKey { get { return "gemini"; } }
        protected override bool Dual { get { return true; } }
        protected override bool Label0 { get { return true; } }
        public override SettingDef[] SettingDefs
        {
            get
            {
                return new[] {
                    SettingDef.Number("proLimit", "Pro requests per day", 100, 1, 100000),
                    SettingDef.Number("flashLimit", "Flash requests per day", 1000, 1, 100000),
                    SettingDef.Text("paths", "Extra .gemini folders (; separated)", ""),
                    SettingDef.Toggle("wsl", "Also read Gemini CLI inside WSL", true) };
            }
        }
    }

    // =====================================================================================  Grok
    [Widget("grok", "Grok Usage", "AI", "SuperGrok credit use. xAI has no public usage API yet, so this shows sample data.", RefName = "Grok Usage", Data = DataKind.Sample)]
    public class AiGrokWidget : AiRingWidget
    {
        protected override string MainKey { get { return "grok"; } }
        protected override AiPanelOpts PanelOpts { get { return new AiPanelOpts { MaxWins = 2, Stats = false }; } }
    }

    // =====================================================================================  DeepSeek
    [Widget("deepseek", "DeepSeek Balance", "AI", "Your DeepSeek balance, with today's and this week's spend.", RefName = "DeepSeek Balance", Account = "deepseek", Data = DataKind.NeedsAccount)]
    public class AiDeepSeekWidget : AiValueWidget
    {
        static AiDeepSeekWidget() { AiRemoteSrc.DefineAccounts(); }
        protected override string MainKey { get { return "deepseek"; } }
        protected override AiPanelOpts PanelOpts { get { return new AiPanelOpts { Models = false, BigLabel = "Account balance" }; } }
        protected override void Values(AiProv p, out string big, out string small) { big = p.Big; small = p.Small; }
        protected override AiProv PanelProv()
        {
            var p = Prov(MainKey); if (p.Stats.Count > 0 && p.Stats[0].Label == "Balance") p.Stats.RemoveAt(0); return p;
        }
    }

    // =====================================================================================  GLM
    [Widget("glm", "GLM Usage", "AI", "Z.ai / Zhipu GLM Coding Plan limits and when they reset.", RefName = "GLM Usage", Account = "glm", Data = DataKind.NeedsAccount)]
    public class AiGlmWidget : AiRingWidget
    {
        static AiGlmWidget() { AiRemoteSrc.DefineAccounts(); }
        protected override string MainKey { get { return "glm"; } }
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Choice("region", "Region", "Global (z.ai)", "Global (z.ai)", "China (bigmodel.cn)") }; } }
        protected override void ApplyCfg() { base.ApplyCfg(); AiCfg.GlmRegion = SStr("region"); }
        protected override AiPanelOpts PanelOpts { get { return new AiPanelOpts { MaxWins = 3, Chart = false, Models = false }; } }
    }

    // =====================================================================================  OpenCode
    [Widget("opencode", "OpenCode Usage", "AI", "OpenCode usage for the current session, from its local history.", RefName = "OpenCode Usage", Data = DataKind.Live)]
    public class AiOpenCodeWidget : AiValueWidget
    {
        protected override string MainKey { get { return "opencode"; } }
        public override SettingDef[] SettingDefs { get { return PathDefs("OpenCode", "opencode data"); } }
        protected override void Values(AiProv p, out string big, out string small) { big = p.Big + " tok"; small = p.Small; }
        protected override AiProv PanelProv()
        {
            var p = Prov(MainKey); p.Models.Clear();
            p.Big = p.Big + " tokens"; p.Stats.RemoveAll(st => st.Label == "Session");
            p.Small = string.IsNullOrEmpty(p.Note) ? "Current session" : "\u201C" + p.Note + "\u201D";
            p.Note = null; return p;
        }
        protected override AiPanelOpts PanelOpts { get { return new AiPanelOpts { Chart = true, Models = false }; } }
    }

    [Widget("opencode-plan", "OpenCode Plan Usage", "AI", "OpenCode tokens this session and this week, and your top model.", RefName = "OpenCode Plan Usage", Data = DataKind.Live)]
    public class AiOpenCodePlanWidget : AiValueWidget
    {
        protected override string MainKey { get { return "opencode"; } }
        public override SettingDef[] SettingDefs { get { return PathDefs("OpenCode", "opencode data"); } }
        protected override void Values(AiProv p, out string big, out string small)
        {
            var wk = p.Stats.FirstOrDefault(s => s.Label == "This week");
            big = (wk != null ? wk.Value : "0") + " / wk";
            small = (p.Big ?? "0") + " session" + (p.Models.Count > 0 ? " · " + p.Models[0].Name : "");
        }
        protected override AiProv PanelProv() { var p = Prov(MainKey); p.Note = null; return p; }
        protected override AiPanelOpts PanelOpts { get { return new AiPanelOpts { Big = false }; } }
    }

    // =====================================================================================  Cursor
    [Widget("cursor", "Cursor Usage", "AI", "Cursor spend and tokens for the current billing cycle.", RefName = "Cursor Usage", Account = "cursor", Data = DataKind.NeedsAccount)]
    public class AiCursorWidget : AiValueWidget
    {
        static AiCursorWidget() { AiRemoteSrc.DefineAccounts(); }
        protected override string MainKey { get { return "cursor"; } }
        protected override void Values(AiProv p, out string big, out string small) { big = p.Big; small = p.Small; }
        protected override AiPanelOpts PanelOpts { get { return new AiPanelOpts { MaxWins = 1, Chart = false }; } }
    }
}
