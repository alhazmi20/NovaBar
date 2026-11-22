// Settings page: Accounts (API keys / tokens / links declared by widgets via Accounts.Define). Values are DPAPI-encrypted.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace NovaBar
{
    class SetAccountsPage : SetPage
    {
        public override string Title { get { return "Accounts"; } }
        public override string Subtitle { get { return sel == null ? "Services your widgets can read your own data from. Click one to connect it." : "Connect this service to its widgets"; } }
        readonly Dictionary<string, string> pending = new Dictionary<string, string>(); // "acct|field" -> edited value
        readonly HashSet<string> reveal = new HashSet<string>();
        string sel, filter = "all", search = "";
        static readonly string[] CatOrder = { "AI", "Finance", "Stocks", "Analytics", "Developer", "Online & Mail", "Social", "Productivity", "Media & Audio", "Games", "Weather", "System", "Other" };

        public override float Paint(SetG g, float x, float y, float w)
        {
            var P = W.P;
            var defs = Accounts.All.ToList();
            if (defs.Count == 0)
            {
                var r = new RectangleF(x, y, w, g.S(120)); g.Card(r);
                Ui.Icon(g.G, Icons.Key, new RectangleF(x, y + g.S(22), w, g.S(30)), P.Faint, g.S(24));
                Ui.Text(g.G, "No widget in this build needs an account.", g.F(13.5f), P.Dim, new RectangleF(x, y + g.S(62), w, g.S(24)), 0);
                return y + r.Height;
            }
            var d = sel == null ? null : defs.FirstOrDefault(z => z.Id == sel);
            return d != null ? PaintDetail(g, d, x, y, w) : PaintGrid(g, defs, x, y, w);
        }

        // ---------------- grid of boxes ----------------
        float PaintGrid(SetG g, List<Accounts.Def> defs, float x, float y, float w)
        {
            var P = W.P; var T = W.T;
            int conn = defs.Count(z => Accounts.Connected(z.Id));
            // summary + filter + search
            g.Segmented(new RectangleF(x, y, g.S(420), g.S(34)), new[] { "All  " + defs.Count, "Connected  " + conn, "Not connected  " + (defs.Count - conn) },
                new[] { "all", "on", "off" }, filter, v => { filter = v; Scroll = 0; }, true, 34);
            float sw = Math.Min(g.S(280), w - g.S(440));
            if (sw > g.S(140)) g.Input("accsearch", new RectangleF(x + w - sw, y, sw, g.S(34)), search, "Search accounts\u2026", v => { search = v; Scroll = 0; }, null, false, Icons.Search);
            y += g.S(50);

            var q = (search ?? "").Trim();
            var shown = defs.Where(z => (filter == "all" || (filter == "on") == Accounts.Connected(z.Id))
                && (q.Length == 0 || z.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || Users(z).Any(m => m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))).ToList();
            if (shown.Count == 0)
            {
                var r = new RectangleF(x, y, w, g.S(100)); g.Card(r);
                Ui.Text(g.G, filter == "on" ? "No accounts connected yet \u2014 pick one under All to set it up." : "Nothing matches.", g.F(13.5f), P.Dim, r, 0);
                return r.Bottom + g.S(10);
            }
            int cols = Math.Max(1, (int)((w + g.S(12)) / (g.S(220) + g.S(12))));
            float cw = (w - g.S(12) * (cols - 1)) / cols, ch = g.S(128);
            foreach (var grp in shown.GroupBy(Cat).OrderBy(gr => { int i = Array.IndexOf(CatOrder, gr.Key); return i < 0 ? 99 : i; }))
            {
                var items = grp.ToList();
                Ui.Text(g.G, grp.Key, g.FS(13), P.Text, new RectangleF(x + g.S(2), y, w * 0.6f, g.S(22)));
                int gc = items.Count(z => Accounts.Connected(z.Id));
                Ui.Text(g.G, gc + " / " + items.Count + " connected", g.F(11.5f), gc > 0 ? T.Good : P.Faint, new RectangleF(x, y, w - g.S(2), g.S(22)), 1);
                y += g.S(30);
                for (int i = 0; i < items.Count; i++)
                {
                    var r = new RectangleF(x + (i % cols) * (cw + g.S(12)), y + (i / cols) * (ch + g.S(12)), cw, ch);
                    if (g.Visible(r.Y, r.Height)) Box(g, items[i], r);
                }
                y += ((items.Count + cols - 1) / cols) * (ch + g.S(12)) + g.S(12);
            }
            return y;
        }

        void Box(SetG g, Accounts.Def d, RectangleF r)
        {
            var P = W.P; var T = W.T;
            bool ok = Accounts.Connected(d.Id), hv = g.Hover(r);
            g.Card(r, hv);
            if (ok) Ui.FillRound(g.G, new RectangleF(r.X, r.Y + g.S(14), g.S(3), r.Height - g.S(28)), g.S(1.5f), T.Good);
            var ic = new RectangleF(r.X + g.S(16), r.Y + g.S(16), g.S(40), g.S(40));
            DrawLogo(g, d, ic, g.S(10), 14);
            // status pill (top-right)
            string st = ok ? "Connected" : "Not connected";
            var pf = g.F(11f, true); float pw = g.TextW(st, pf) + g.S(22);
            var pr = new RectangleF(r.Right - g.S(14) - pw, r.Y + g.S(16), pw, g.S(22));
            Ui.FillRound(g.G, pr, g.S(11), ok ? Ui.WithA(T.Good, T.Dark ? 45 : 35) : Ui.WithA(P.Faint, 40));
            Ui.FillCircle(g.G, new PointF(pr.X + g.S(10), pr.Y + pr.Height / 2), g.S(3), ok ? T.Good : P.Faint);
            Ui.Text(g.G, st, pf, ok ? T.Good : P.Dim, new RectangleF(pr.X + g.S(16), pr.Y, pr.Width - g.S(18), pr.Height));
            // name + users
            Ui.Text(g.G, d.Name, g.FS(14.5f), P.Text, new RectangleF(r.X + g.S(16), r.Y + g.S(66), r.Width - g.S(32), g.S(22)));
            var users = Users(d);
            string u = users.Count == 0 ? "Not used by a widget" : users.Count == 1 ? users[0].Name : users[0].Name + " +" + (users.Count - 1) + " more";
            Ui.Text(g.G, u, g.F(12), P.Dim, new RectangleF(r.X + g.S(16), r.Y + g.S(90), r.Width - g.S(48), g.S(18)));
            Ui.Icon(g.G, Icons.ChevronRight, new RectangleF(r.Right - g.S(32), r.Y + g.S(88), g.S(20), g.S(22)), hv ? T.Accent : P.Faint, g.S(11));
            var id = d.Id; g.Hit(r, () => { sel = id; Scroll = 0; W.EndEdit(); });
        }

        // ---------------- detail ----------------
        float PaintDetail(SetG g, Accounts.Def d, float x, float y, float w)
        {
            var P = W.P; var T = W.T;
            bool ok = Accounts.Connected(d.Id);
            float bw = g.ButtonWidth("All accounts", Icons.ChevronLeft);
            g.Button(new RectangleF(x, y, bw, g.S(32)), "All accounts", () => { sel = null; W.EndEdit(); }, false, Icons.ChevronLeft);
            y += g.S(46);

            // header card
            var users = Users(d);
            var helpF = g.F(13f);
            float helpH = string.IsNullOrEmpty(d.Help) ? 0 : (float)Math.Ceiling(g.G.MeasureString(d.Help, helpF, (int)(w - g.S(48))).Height) + g.S(10);
            var hc = new RectangleF(x, y, w, g.S(96) + helpH);
            g.Card(hc);
            var ic = new RectangleF(x + g.S(20), y + g.S(20), g.S(56), g.S(56));
            DrawLogo(g, d, ic, g.S(14), 19);
            Ui.Text(g.G, d.Name, g.FS(19), P.Text, new RectangleF(ic.Right + g.S(16), y + g.S(22), w * 0.6f, g.S(28)));
            float sx = ic.Right + g.S(16);
            sx += g.Badge(sx, y + g.S(64), ok ? "Connected" : "Not connected", ok ? T.Good : P.Dim) + g.S(8);
            Ui.Text(g.G, Cat(d), g.F(12), P.Faint, new RectangleF(sx, y + g.S(54), g.S(200), g.S(20)));
            if (!string.IsNullOrEmpty(d.HelpUrl))
            {
                float lw = g.TextW("How to get this", g.F(13)) + g.S(28);
                var url = d.HelpUrl;
                g.Link(new RectangleF(x + w - g.S(20) - lw, y + g.S(26), lw, g.S(24)), "How to get this", () => { try { Process.Start(url); } catch { } });
            }
            if (helpH > 0) Ui.TextWrap(g.G, d.Help, helpF, P.Dim, new RectangleF(x + g.S(24), y + g.S(92), w - g.S(48), helpH));
            y = hc.Bottom + g.S(12);

            // credentials card
            float fh = g.S(56) + d.Fields.Length * g.S(70) + g.S(56);
            var cc = new RectangleF(x, y, w, fh); g.Card(cc);
            Ui.Text(g.G, "Connection", g.FS(14), P.Text, new RectangleF(x + g.S(20), y + g.S(16), w, g.S(22)));
            Ui.Text(g.G, "Encrypted on this PC with Windows DPAPI \u2014 never sent anywhere except the service itself.", g.F(11.5f), P.Faint, new RectangleF(x + g.S(20), y + g.S(36), w - g.S(40), g.S(18)));
            float cy = y + g.S(64);
            foreach (var f in d.Fields)
            {
                string k = d.Id + "|" + f.Key;
                Ui.Text(g.G, f.Label, g.FS(12.5f), P.Text, new RectangleF(x + g.S(20), cy, w - g.S(40), g.S(18)));
                string val; if (!pending.TryGetValue(k, out val)) val = Accounts.Get(d.Id, f.Key) ?? "";
                bool secret = f.Secret && !reveal.Contains(k);
                float iw = w - g.S(40) - (f.Secret ? g.S(44) : 0);
                var ir = new RectangleF(x + g.S(20), cy + g.S(24), iw, g.S(36));
                var kk = k;
                g.Input("acc:" + k, ir, val, string.IsNullOrEmpty(f.Placeholder) ? "Paste " + f.Label.ToLowerInvariant() : f.Placeholder,
                    s => pending[kk] = s, s => { pending[kk] = s; SaveAcct(d); }, secret);
                if (f.Secret)
                {
                    var er = new RectangleF(ir.Right + g.S(8), ir.Y, g.S(36), ir.Height);
                    g.Button(er, null, () => { if (!reveal.Remove(kk)) reveal.Add(kk); }, false, reveal.Contains(k) ? "\uED1A" : Icons.Eye);
                }
                cy += g.S(70);
            }
            bool dirty = d.Fields.Any(f => { string v; return pending.TryGetValue(d.Id + "|" + f.Key, out v) && v != (Accounts.Get(d.Id, f.Key) ?? ""); });
            bool any = d.Fields.Any(f => !string.IsNullOrEmpty(Accounts.Get(d.Id, f.Key)));
            var dd = d;
            g.Button(new RectangleF(x + g.S(20), cy + g.S(4), g.S(110), g.S(36)), ok && !dirty ? "Saved" : "Connect", () => SaveAcct(dd), true, null, dirty);
            g.Button(new RectangleF(x + g.S(140), cy + g.S(4), g.S(120), g.S(36)), "Disconnect", () => ClearAcct(dd), false, null, any || dirty);
            if (dirty) Ui.Text(g.G, "Unsaved changes", g.F(12), T.Warn, new RectangleF(x + g.S(274), cy + g.S(4), g.S(200), g.S(36)));
            y = cc.Bottom + g.S(12);

            // widgets that use it
            var uc = new RectangleF(x, y, w, g.S(56) + Math.Max(1, users.Count) * g.S(40));
            g.Card(uc);
            Ui.Text(g.G, "Widgets using this account", g.FS(14), P.Text, new RectangleF(x + g.S(20), y + g.S(16), w, g.S(22)));
            float uy = y + g.S(48);
            if (users.Count == 0) Ui.Text(g.G, "No widget uses this account.", g.F(12.5f), P.Faint, new RectangleF(x + g.S(20), uy, w, g.S(32)));
            foreach (var m in users)
            {
                int onBar = Cfg.Widgets.Count(i => i.Id == m.Id);
                Ui.Text(g.G, m.Name, g.F(13.5f), P.Text, new RectangleF(x + g.S(20), uy, w * 0.5f, g.S(32)));
                Ui.Text(g.G, m.Description ?? "", g.F(12), P.Faint, new RectangleF(x + g.S(20) + w * 0.28f, uy, w * 0.5f, g.S(32)));
                if (onBar > 0) g.Badge(x + w - g.S(20), uy + g.S(16), "On your bar", T.Good, true);
                else
                {
                    var mm = m; var br = new RectangleF(x + w - g.S(20) - g.S(110), uy, g.S(110), g.S(32));
                    g.Button(br, "Add to bar", () =>
                    {
                        Cfg.Widgets.Add(new WidgetInstance { Id = mm.Id, Inst = AppConfig.NewInst(mm.Id), Zone = "center" });
                        W.Changed(); W.Toast("Added " + mm.Name + " to your bar");
                    }, false, Icons.Add);
                }
                uy += g.S(40);
            }
            return uc.Bottom + g.S(10);
        }

        static readonly Dictionary<string, Image> logos = new Dictionary<string, Image>();
        /// <summary>Embedded service logo (data/logos/&lt;id&gt;.png), or null.</summary>
        static Image Logo(string id)
        {
            Image im;
            if (logos.TryGetValue(id, out im)) return im;
            im = null;
            try
            {
                using (var s = typeof(SetAccountsPage).Assembly.GetManifestResourceStream("NovaBar.logo." + id + ".png"))
                    if (s != null) { using (var src = Image.FromStream(s)) im = new Bitmap(src); }
            }
            catch { im = null; }
            logos[id] = im; return im;
        }
        /// <summary>Logo tile: the service's logo in a rounded square, or coloured initials if none is bundled.</summary>
        void DrawLogo(SetG g, Accounts.Def d, RectangleF ic, float rad, float fontPx)
        {
            var T = W.T; var lg = Logo(d.Id);
            if (lg != null)
            {
                using (var clip = Ui.Round(ic, rad))
                {
                    var st = g.G.Save();
                    g.G.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
                    g.G.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.G.DrawImage(lg, ic);
                    g.G.Restore(st);
                }
                Ui.StrokeRound(g.G, ic, rad, Ui.WithA(T.Dark ? Color.White : Color.Black, 28), 1f);
                return;
            }
            var bc = BrandColor(d.Name);
            Ui.FillRound(g.G, ic, rad, Ui.WithA(bc, T.Dark ? 60 : 45));
            Ui.Text(g.G, Initials(d.Name), g.FS(fontPx), Ui.Mix(bc, T.Dark ? Color.White : Color.Black, 0.25), ic, 0);
        }

        static List<WidgetAttribute> Users(Accounts.Def d) { return WidgetRegistry.All.Where(m => m.Account == d.Id).ToList(); }
        static string Cat(Accounts.Def d)
        {
            var u = Users(d).Where(m => !string.Equals(m.Category, "Hidden", StringComparison.OrdinalIgnoreCase)).ToList();
            return u.Count > 0 ? u.GroupBy(m => m.Category).OrderByDescending(gr => gr.Count()).First().Key : "Other";
        }
        static Color BrandColor(string name)
        {
            var pal = new[] { Color.FromArgb(10, 132, 255), Color.FromArgb(52, 199, 89), Color.FromArgb(255, 159, 10), Color.FromArgb(191, 90, 242), Color.FromArgb(255, 55, 95), Color.FromArgb(100, 210, 255), Color.FromArgb(255, 214, 10), Color.FromArgb(94, 92, 230), Color.FromArgb(48, 209, 88), Color.FromArgb(172, 142, 104) };
            int h = 0; foreach (var ch in (name ?? "")) h = h * 31 + char.ToLowerInvariant(ch);
            return pal[Math.Abs(h % pal.Length)];
        }

        static string Initials(string n)
        {
            var parts = new string((n ?? "?").Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray()).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length));
            return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
        }

        void SaveAcct(Accounts.Def d)
        {
            W.EndEdit();
            foreach (var f in d.Fields)
            {
                string v; if (!pending.TryGetValue(d.Id + "|" + f.Key, out v)) continue;
                Accounts.Set(Cfg, d.Id, f.Key, (v ?? "").Trim());
                pending.Remove(d.Id + "|" + f.Key);
            }
            W.Changed();
            W.Toast(Accounts.Connected(d.Id) ? d.Name + " connected" : "Saved " + d.Name);
        }
        void ClearAcct(Accounts.Def d)
        {
            W.EndEdit();
            foreach (var f in d.Fields) { Accounts.Set(Cfg, d.Id, f.Key, null); pending.Remove(d.Id + "|" + f.Key); }
            Dictionary<string, string> m; if (Cfg.Accounts.TryGetValue(d.Id, out m) && m.Count == 0) Cfg.Accounts.Remove(d.Id);
            W.Changed();
            W.Toast("Cleared " + d.Name);
        }
    }
}
