// Parity checklist: reference widget list vs. widgets in this build. Also: online check for new reference widgets.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace NovaBar
{
    class SetRefItem { public string Category, Name, Description; }

    /// <summary>Reference list loading + matching against WidgetRegistry.</summary>
    static class SetRef
    {
        public const string GalleryUrl = "https://lumabar.xyz/widgets";
        public static string UserFile { get { return Path.Combine(Paths.Root, "reference-widgets.json"); } }
        public static string Source = "";

        /// <summary>%APPDATA%\NovaBar\reference-widgets.json > embedded resource > data/reference-widgets.json near the exe or in the source tree.</summary>
        public static List<SetRefItem> Load()
        {
            string json = null;
            try { if (File.Exists(UserFile)) { json = File.ReadAllText(UserFile, Encoding.UTF8); Source = "user copy"; } } catch { }
            if (json == null)
                try
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("NovaBar.reference-widgets.json"))
                        if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) { json = r.ReadToEnd(); Source = "built-in"; }
                }
                catch { }
            if (json == null)
            {
                var dir = Paths.ExeDir;
                for (int i = 0; i < 5 && dir != null && json == null; i++)
                {
                    var f = Path.Combine(dir, "data", "reference-widgets.json");
                    try { if (File.Exists(f)) { json = File.ReadAllText(f, Encoding.UTF8); Source = f; } } catch { }
                    dir = Path.GetDirectoryName(dir.TrimEnd('\\', '/'));
                }
            }
            return Parse(json);
        }
        public static List<SetRefItem> Parse(string json)
        {
            var l = new List<SetRefItem>();
            var o = J.Parse(json);
            var arr = J.Arr(o, "widgets") ?? (o as System.Collections.IList);
            if (arr != null) foreach (var w in arr)
                {
                    var n = J.Str(w, "name"); if (string.IsNullOrEmpty(n)) continue;
                    l.Add(new SetRefItem { Category = J.Str(w, "category", "Other"), Name = n, Description = J.Str(w, "description") });
                }
            return l;
        }
        public static void SaveUser(List<SetRefItem> items)
        {
            var d = new Dictionary<string, object>();
            d["source"] = GalleryUrl; d["fetched"] = DateTime.Now.ToString("yyyy-MM-dd"); d["count"] = items.Count;
            d["widgets"] = items.Select(i => (object)new Dictionary<string, object> { { "category", i.Category }, { "name", i.Name }, { "description", i.Description ?? "" } }).ToArray();
            File.WriteAllText(UserFile, J.Stringify(d, true), new UTF8Encoding(false));
        }

        public class Row { public SetRefItem Ref; public List<WidgetAttribute> Ours = new List<WidgetAttribute>(); public int Status; } // 0 missing, 1 partial, 2 live
        public class Result { public List<Row> Rows = new List<Row>(); public List<WidgetAttribute> Extras = new List<WidgetAttribute>(); public int Live, Partial, Missing; }

        public static Result Match(List<SetRefItem> refs)
        {
            var res = new Result();
            var all = WidgetRegistry.All.ToList();
            var used = new HashSet<string>(); // ids (Attribute overrides Equals/GetHashCode, so don't hash attributes)
            foreach (var r in refs)
            {
                var row = new Row { Ref = r };
                foreach (var m in all) if (string.Equals((m.RefName ?? m.Name).Trim(), r.Name.Trim(), StringComparison.OrdinalIgnoreCase)) { row.Ours.Add(m); used.Add(m.Id); }
                row.Status = row.Ours.Count == 0 ? 0 : row.Ours.Any(m => m.Data == DataKind.Live) ? 2 : 1;
                if (row.Status == 0) res.Missing++; else if (row.Status == 1) res.Partial++; else res.Live++;
                res.Rows.Add(row);
            }
            res.Extras = all.Where(m => !used.Contains(m.Id)).ToList();
            return res;
        }

        public static string Kind(WidgetAttribute m)
        {
            return m.Data == DataKind.Live ? "Live" : m.Data == DataKind.NeedsAccount ? "Needs account" : "Sample";
        }

        // ---- online gallery parsing ----
        /// <summary>Fetches the gallery page and returns its widgets (blocking; call on a worker thread). null on failure.</summary>
        public static List<SetRefItem> FetchGallery(out string error)
        {
            error = null;
            string html = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
                var req = (HttpWebRequest)WebRequest.Create(GalleryUrl);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NovaBar/1.0"; req.Accept = "text/html";
                req.Timeout = 20000; req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (var resp = (HttpWebResponse)req.GetResponse()) using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) html = r.ReadToEnd();
            }
            catch (Exception e) { error = e.Message; return null; }
            var l = ParseGallery(html);
            if (l.Count == 0) error = "The page loaded but no widgets were recognised.";
            return l;
        }

        static string Clean(string s)
        {
            s = Regex.Replace(s ?? "", "<span\\b.*?</span>", "", RegexOptions.Singleline);
            s = Regex.Replace(s, "<[^>]+>", "");
            s = WebUtility.HtmlDecode(s).Replace('\u00a0', ' ');
            return Regex.Replace(s, "\\s+", " ").Trim();
        }
        public static List<SetRefItem> ParseGallery(string html)
        {
            var l = new List<SetRefItem>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(html)) return l;
            var secs = Regex.Matches(html, "<section\\b[^>]*\\bid=\"([^\"]*)\"[^>]*>");
            for (int i = 0; i < secs.Count; i++)
            {
                int start = secs[i].Index + secs[i].Length, end = i + 1 < secs.Count ? secs[i + 1].Index : html.Length;
                var block = html.Substring(start, end - start);
                var h2 = Regex.Match(block, "<h2\\b[^>]*>(.*?)</h2>", RegexOptions.Singleline);
                if (!h2.Success) continue;
                var cat = Clean(h2.Groups[1].Value);
                if (secs[i].Groups[1].Value.Equals("highlights", StringComparison.OrdinalIgnoreCase) || cat.Equals("Highlights", StringComparison.OrdinalIgnoreCase)) continue;
                var cards = Regex.Matches(block, "<h3\\b[^>]*class=\"[^\"]*wcard-name[^\"]*\"[^>]*>(.*?)</h3>\\s*(?:<p\\b[^>]*>(.*?)</p>)?", RegexOptions.Singleline);
                if (cards.Count == 0) cards = Regex.Matches(block, "<h3\\b[^>]*>(.*?)</h3>\\s*(?:<p\\b[^>]*>(.*?)</p>)?", RegexOptions.Singleline);
                foreach (Match c in cards)
                {
                    var n = Clean(c.Groups[1].Value);
                    if (n.Length == 0 || !seen.Add(n)) continue;
                    l.Add(new SetRefItem { Category = cat, Name = n, Description = Clean(c.Groups[2].Value) });
                }
            }
            if (l.Count == 0) // fallback: image alt texts "<Name> widget"
                foreach (Match m in Regex.Matches(html, "alt=\"([^\"]+?) widget\""))
                {
                    var n = Clean(m.Groups[1].Value);
                    if (n.Length > 0 && seen.Add(n)) l.Add(new SetRefItem { Category = "Other", Name = n, Description = "" });
                }
            return l;
        }
    }

    /// <summary>Public entry used by `NovaBar.exe --checklist [path]`.</summary>
    public static class Checklist
    {
        public static void Export(string path)
        {
            var refs = SetRef.Load();
            var res = SetRef.Match(refs);
            File.WriteAllText(path, Markdown(refs, res), new UTF8Encoding(false));
            try { Console.WriteLine("Checklist: " + (res.Live + res.Partial) + "/" + refs.Count + " built (" + res.Live + " live, " + res.Partial + " account/sample), " + res.Missing + " missing -> " + Path.GetFullPath(path)); } catch { }
        }

        internal static string Markdown(List<SetRefItem> refs, SetRef.Result res)
        {
            var sb = new StringBuilder();
            Func<string, string> esc = s => (s ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
            sb.AppendLine("# NovaBar widget parity checklist");
            sb.AppendLine();
            sb.AppendLine("Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " by `NovaBar.exe --checklist` (reference list: " + SetRef.Source + ", " + refs.Count + " widgets).");
            sb.AppendLine();
            int built = res.Live + res.Partial;
            sb.AppendLine("**" + built + " / " + refs.Count + " built** \u2014 \u2705 " + res.Live + " live \u00b7 \U0001F7E1 " + res.Partial + " needs account / sample data \u00b7 \u274C " + res.Missing + " missing \u00b7 \u2795 " + res.Extras.Count + " extra");
            sb.AppendLine();
            sb.AppendLine("Legend: \u2705 Built (live data) \u00b7 \U0001F7E1 Built (needs an account or shows sample data) \u00b7 \u274C Missing");
            sb.AppendLine();
            foreach (var grp in res.Rows.GroupBy(r => r.Ref.Category))
            {
                int b = grp.Count(r => r.Status > 0);
                sb.AppendLine("## " + grp.Key + " (" + b + "/" + grp.Count() + ")");
                sb.AppendLine();
                sb.AppendLine("| Status | Reference widget | Description | NovaBar widget id | Data |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var r in grp)
                {
                    string st = r.Status == 2 ? "\u2705" : r.Status == 1 ? "\U0001F7E1" : "\u274C";
                    string ids = r.Ours.Count == 0 ? "\u2014" : string.Join(", ", r.Ours.Select(m => "`" + m.Id + "`"));
                    string data = r.Ours.Count == 0 ? "" : string.Join(", ", r.Ours.Select(m => SetRef.Kind(m) + (string.IsNullOrEmpty(m.Account) ? "" : " (" + m.Account + ")")).Distinct());
                    sb.AppendLine("| " + st + " | " + esc(r.Ref.Name) + " | " + esc(r.Ref.Description) + " | " + ids + " | " + data + " |");
                }
                sb.AppendLine();
            }
            if (res.Extras.Count > 0)
            {
                sb.AppendLine("## Extra widgets (not in the reference list)");
                sb.AppendLine();
                sb.AppendLine("| Widget id | Name | Category | RefName | Data |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var m in res.Extras) sb.AppendLine("| `" + m.Id + "` | " + esc(m.Name) + " | " + esc(m.Category) + " | " + esc(m.RefName) + " | " + SetRef.Kind(m) + " |");
                sb.AppendLine();
            }
            var missing = res.Rows.Where(r => r.Status == 0).ToList();
            if (missing.Count > 0)
            {
                sb.AppendLine("## Missing (" + missing.Count + ")");
                sb.AppendLine();
                foreach (var r in missing) sb.AppendLine("- [ ] **" + esc(r.Ref.Name) + "** (" + esc(r.Ref.Category) + ") \u2014 " + esc(r.Ref.Description));
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    // =====================================================================================
    class SetChecklistPage : SetPage
    {
        public override string Title { get { return "Checklist"; } }
        public override string Subtitle { get { return "Widget parity with the reference gallery"; } }
        List<SetRefItem> refs; SetRef.Result res; string filter = "all";
        volatile bool checking; string checkMsg; List<SetRefItem> fresh; // NEW in the online gallery
        public SetChecklistPage() { Reload(); }
        void Reload() { refs = SetRef.Load(); res = SetRef.Match(refs); }

        public override float Paint(SetG g, float x, float y, float w)
        {
            var P = W.P; var T = W.T;
            int total = refs.Count, built = res.Live + res.Partial;
            // summary cards
            float gap = g.S(12), cw = (w - gap * 3) / 4, chh = g.S(88);
            SumCard(g, new RectangleF(x, y, cw, chh), built + " / " + total, "Built", T.Accent, total > 0 ? built / (double)total : 0);
            SumCard(g, new RectangleF(x + (cw + gap), y, cw, chh), res.Live.ToString(), "Live data", T.Good, -1);
            SumCard(g, new RectangleF(x + (cw + gap) * 2, y, cw, chh), res.Partial.ToString(), "Account / sample", T.Warn, -1);
            SumCard(g, new RectangleF(x + (cw + gap) * 3, y, cw, chh), res.Missing.ToString(), "Missing", T.Bad, -1);
            y += chh + g.S(16);

            // toolbar
            var bar = new RectangleF(x, y, w, g.S(36));
            g.Segmented(bar, new[] { "All " + total, "Missing " + res.Missing, "Built " + built }, new[] { "all", "missing", "built" }, filter, v => { filter = v; Scroll = 0; }, true, 34);
            float bx = x + w;
            float ew = g.ButtonWidth("Export markdown", Icons.Download); bx -= ew;
            g.Button(new RectangleF(bx, y, ew, g.S(34)), "Export markdown", Export, false, Icons.Download);
            string cl = checking ? "Checking\u2026" : "Check online gallery for new widgets";
            float kw = g.ButtonWidth(cl, Icons.Refresh); bx -= kw + g.S(8);
            g.Button(new RectangleF(bx, y, kw, g.S(34)), cl, Check, true, Icons.Refresh, !checking);
            y += g.S(48);
            if (checkMsg != null)
            {
                var mr = new RectangleF(x, y, w, g.S(38)); Ui.FillRound(g.G, mr, g.S(6), Ui.WithA(T.Accent, T.Dark ? 34 : 24));
                Ui.Icon(g.G, Icons.Info, new RectangleF(x + g.S(12), y, g.S(18), mr.Height), T.Accent, g.S(13));
                Ui.Text(g.G, checkMsg, g.F(12.5f), P.Text, new RectangleF(x + g.S(38), y, w - g.S(50), mr.Height));
                y += mr.Height + g.S(10);
            }

            // NEW in gallery
            if (fresh != null && fresh.Count > 0)
            {
                y = g.Section(x, y, w, "New in the online gallery (" + fresh.Count + ")");
                foreach (var n in fresh.ToList())
                {
                    var r = new RectangleF(x, y, w, g.S(54));
                    if (g.Visible(y, r.Height))
                    {
                        g.Card(r);
                        g.Badge(x + g.S(16), y + r.Height / 2, "NEW", T.Accent);
                        Ui.Text(g.G, n.Name, g.FS(13.5f), P.Text, new RectangleF(x + g.S(70), y + g.S(8), w * 0.5f, g.S(20)));
                        Ui.Text(g.G, n.Category + "  \u00b7  " + n.Description, g.F(12), P.Dim, new RectangleF(x + g.S(70), y + g.S(28), w - g.S(270), g.S(18)));
                        var item = n;
                        g.Button(new RectangleF(x + w - g.S(186), y + g.S(10), g.S(172), g.S(34)), "Add to reference list", () => AddRef(item), false, Icons.Add);
                    }
                    y += r.Height + g.S(4);
                }
                y += g.S(14);
            }

            // table grouped by category
            var rows = res.Rows.Where(r => filter == "all" || (filter == "missing" ? r.Status == 0 : r.Status > 0)).ToList();
            foreach (var grp in rows.GroupBy(r => r.Ref.Category))
            {
                var allInCat = res.Rows.Where(r => r.Ref.Category == grp.Key).ToList();
                int b = allInCat.Count(r => r.Status > 0);
                if (g.Visible(y, g.S(30)))
                {
                    Ui.Text(g.G, grp.Key, g.FS(14), P.Text, new RectangleF(x + g.S(2), y, w * 0.5f, g.S(22)));
                    Ui.Text(g.G, b + " / " + allInCat.Count, g.F(12.5f), b == allInCat.Count ? T.Good : P.Dim, new RectangleF(x, y, w - g.S(4), g.S(22)), 1);
                }
                y += g.S(30);
                var list = grp.ToList();
                float rh = g.S(56), boxH = list.Count * rh;
                var box = new RectangleF(x, y, w, boxH);
                if (g.Visible(y, boxH)) g.Card(box);
                for (int i = 0; i < list.Count; i++)
                {
                    float ry = y + i * rh;
                    if (!g.Visible(ry, rh)) continue;
                    var r = list[i];
                    if (i > 0) g.G.FillRectangle(Ui.B(P.Sep), x + g.S(16), ry, w - g.S(32), 1);
                    float col1 = w * 0.52f;
                    Ui.Text(g.G, r.Ref.Name, g.FS(13.5f), P.Text, new RectangleF(x + g.S(18), ry + g.S(9), col1 - g.S(24), g.S(20)));
                    Ui.Text(g.G, r.Ref.Description, g.F(12), P.Dim, new RectangleF(x + g.S(18), ry + g.S(29), col1 - g.S(24), g.S(18)));
                    string ids = r.Ours.Count == 0 ? "\u2014" : string.Join(", ", r.Ours.Select(m => m.Id));
                    Ui.Text(g.G, ids, Ui.Mono(g.S(12)), r.Ours.Count == 0 ? P.Faint : P.Dim, new RectangleF(x + col1, ry, w - col1 - g.S(200), rh));
                    string st; Color sc; string ic;
                    if (r.Status == 2) { st = "Built \u00b7 Live"; sc = T.Good; ic = Icons.Check; }
                    else if (r.Status == 1) { st = r.Ours.Any(m => m.Data == DataKind.NeedsAccount) ? "Built \u00b7 Needs account" : "Built \u00b7 Sample data"; sc = T.Warn; ic = "\uE7BA"; }
                    else { st = "Missing"; sc = T.Bad; ic = "\uE711"; }
                    StatusPill(g, x + w - g.S(16), ry + rh / 2, st, sc, ic);
                }
                y += boxH + g.S(20);
            }
            if (rows.Count == 0)
            {
                var r = new RectangleF(x, y, w, g.S(80)); g.Card(r);
                Ui.Text(g.G, filter == "missing" ? "Nothing missing \u2014 every reference widget is built." : "No widgets match this filter yet.", g.F(13.5f), P.Dim, r, 0);
                y += r.Height + g.S(20);
            }

            // extras
            if (filter != "missing" && res.Extras.Count > 0)
            {
                y = g.Section(x, y, w, "Extra widgets (" + res.Extras.Count + ")");
                Ui.Text(g.G, "In NovaBar but not in the reference list", g.F(12), P.Dim, new RectangleF(x + g.S(2), y - g.S(8), w, g.S(18)));
                y += g.S(16);
                float rh = g.S(48), boxH = res.Extras.Count * rh;
                if (g.Visible(y, boxH)) g.Card(new RectangleF(x, y, w, boxH));
                for (int i = 0; i < res.Extras.Count; i++)
                {
                    float ry = y + i * rh; if (!g.Visible(ry, rh)) continue;
                    var m = res.Extras[i];
                    if (i > 0) g.G.FillRectangle(Ui.B(P.Sep), x + g.S(16), ry, w - g.S(32), 1);
                    Ui.Text(g.G, m.Name, g.FS(13.5f), P.Text, new RectangleF(x + g.S(18), ry, w * 0.4f, rh));
                    Ui.Text(g.G, m.Category, g.F(12.5f), P.Dim, new RectangleF(x + w * 0.4f, ry, w * 0.2f, rh));
                    Ui.Text(g.G, m.Id, Ui.Mono(g.S(12)), P.Dim, new RectangleF(x + w * 0.6f, ry, w * 0.25f, rh));
                    g.Badge(x + w - g.S(16), ry + rh / 2, SetRef.Kind(m), m.Data == DataKind.Live ? T.Good : T.Warn, true);
                }
                y += boxH + g.S(16);
            }
            Ui.Text(g.G, "Reference list: " + SetRef.Source + "  \u00b7  " + refs.Count + " widgets", g.F(11.5f), P.Faint, new RectangleF(x + g.S(2), y, w, g.S(18)));
            return y + g.S(28);
        }

        void SumCard(SetG g, RectangleF r, string big, string label, Color c, double frac)
        {
            g.Card(r);
            Ui.Text(g.G, big, Ui.FontSemi(g.S(26)), W.P.Text, new RectangleF(r.X + g.S(18), r.Y + g.S(12), r.Width - g.S(30), g.S(36)));
            Ui.FillCircle(g.G, new PointF(r.X + g.S(22), r.Y + g.S(64)), g.S(4), c);
            Ui.Text(g.G, label, g.F(12.5f), W.P.Dim, new RectangleF(r.X + g.S(32), r.Y + g.S(54), r.Width - g.S(40), g.S(20)));
            if (frac >= 0) Ui.Ring(g.G, new RectangleF(r.Right - g.S(56), r.Y + g.S(22), g.S(42), g.S(42)), frac, c, W.T.Track, g.S(5));
        }
        void StatusPill(SetG g, float right, float ym, string text, Color c, string icon)
        {
            var f = g.FS(12); float w = g.TextW(text, f) + g.S(40), h = g.S(26);
            var r = new RectangleF(right - w, ym - h / 2, w, h);
            Ui.FillRound(g.G, r, h / 2, Ui.WithA(c, W.T.Dark ? 40 : 30));
            Ui.Icon(g.G, icon, new RectangleF(r.X + g.S(9), r.Y, g.S(14), h), c, g.S(11));
            Ui.Text(g.G, text, f, W.T.Dark ? Ui.Mix(c, Color.White, 0.3) : Ui.Mix(c, Color.Black, 0.25), new RectangleF(r.X + g.S(28), r.Y, w - g.S(32), h));
        }

        void Export()
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var path = Path.Combine(Directory.Exists(docs) ? docs : Paths.Root, "NovaBar widget checklist.md");
                Checklist.Export(path);
                W.Toast("Saved to " + path);
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
            }
            catch (Exception e) { checkMsg = "Export failed: " + e.Message; }
        }
        void Check()
        {
            if (checking) return;
            checking = true; checkMsg = null; W.Invalidate();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err; var live = SetRef.FetchGallery(out err);
                Action done = () =>
                {
                    checking = false;
                    if (live == null || live.Count == 0) { checkMsg = "Couldn't read the online gallery: " + err; fresh = null; }
                    else
                    {
                        var known = new HashSet<string>(refs.Select(r => r.Name.Trim()), StringComparer.OrdinalIgnoreCase);
                        fresh = live.Where(l => !known.Contains(l.Name)).ToList();
                        var liveSet = new HashSet<string>(live.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
                        int gone = refs.Count(r => !liveSet.Contains(r.Name.Trim()));
                        checkMsg = "Online gallery lists " + live.Count + " widgets. " + (fresh.Count == 0 ? "No new widgets \u2014 the reference list is up to date." : fresh.Count + " new widget" + (fresh.Count == 1 ? "" : "s") + " not in the reference list.")
                            + (gone > 0 ? "  (" + gone + " reference widget" + (gone == 1 ? " is" : "s are") + " no longer listed online.)" : "");
                    }
                    W.Invalidate();
                };
                try { W.BeginInvoke(done); } catch { }
            });
        }
        void AddRef(SetRefItem item)
        {
            try
            {
                var list = SetRef.Load();
                if (!list.Any(r => string.Equals(r.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    int idx = list.FindLastIndex(r => r.Category == item.Category);
                    if (idx >= 0) list.Insert(idx + 1, item); else list.Add(item);
                    SetRef.SaveUser(list);
                }
                fresh.Remove(item); Reload();
                W.Toast("Added \u201c" + item.Name + "\u201d to the reference list");
            }
            catch (Exception e) { checkMsg = "Couldn't save the reference list: " + e.Message; }
        }
    }
}
