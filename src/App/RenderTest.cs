// Offline render harness: draws bar strips (+ optional panels) for each theme into a PNG.
// Uses exactly the same BarRenderer code path as the live bar.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NovaBar
{
    public static class RenderTest
    {
        class Host : IWidgetHost
        {
            readonly Queue<Action> posted = new Queue<Action>();
            public List<BarTile> Tiles;
            public void Invalidate(Widget w) { }
            public void Post(Action a) { lock (posted) posted.Enqueue(a); }
            public void OpenPanel(Widget w) { }
            public void ClosePanel() { }
            public bool IsPanelOpen(Widget w) { return false; }
            public void OpenSettings(string page) { }
            public Rectangle ScreenBoundsOf(Widget w)
            {
                if (Tiles != null) foreach (var t in Tiles) if (t.W == w) return t.R;
                return Rectangle.Empty;
            }
            public void RunPosted()
            {
                while (true)
                {
                    Action a; lock (posted) { if (posted.Count == 0) return; a = posted.Dequeue(); }
                    try { a(); } catch (Exception ex) { Log("Post callback", ex); }
                }
            }
        }

        static StringBuilder log = new StringBuilder();
        static int errors;
        static void Log(string where, Exception ex)
        {
            lock (log) { errors++; log.AppendLine("=== " + where + " ==="); log.AppendLine(ex.ToString()); log.AppendLine(); }
        }

        public static string Arg(string[] a, string name)
        {
            for (int i = 0; i < a.Length - 1; i++) if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return null;
        }
        public static bool Flag(string[] a, string name) { return a.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)); }

        class Section
        {
            public Theme T; public List<List<BarTile>> Rows = new List<List<BarTile>>(); public List<int> RowW = new List<int>();
            public List<KeyValuePair<BarTile, Rectangle>> Panels = new List<KeyValuePair<BarTile, Rectangle>>();
            public int Height, Width;
        }

        public static int Run(string[] args)
        {
            string outPng = Arg(args, "--render");
            var cfg = AppConfig.Current;
            string size = Arg(args, "--size") ?? cfg.Size;
            int wait = 3000; int.TryParse(Arg(args, "--wait") ?? "3000", out wait);
            bool panels = Flag(args, "--panels");
            var themes = (Arg(args, "--themes") ?? string.Join(",", Theme.Ids)).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            BarRenderer.OnError = Log;

            // ---- pick widget instances ----
            var insts = new List<WidgetInstance>();
            string ids = Arg(args, "--widgets"), cat = Arg(args, "--category");
            bool grouped = false;
            if (ids != null)
                foreach (var id in ids.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
                {
                    var ci = cfg.Widgets.FirstOrDefault(w => w.Id == id);
                    insts.Add(ci != null ? ci : new WidgetInstance { Id = id, Inst = id, Zone = "left" });
                }
            else if (cat != null)
                foreach (var m in WidgetRegistry.All.Where(m => string.Equals(m.Category, cat, StringComparison.OrdinalIgnoreCase))) insts.Add(new WidgetInstance { Id = m.Id, Inst = m.Id, Zone = "left" });
            else if (Flag(args, "--all"))
                foreach (var m in WidgetRegistry.All) insts.Add(new WidgetInstance { Id = m.Id, Inst = m.Id, Zone = "left" });
            else { insts.AddRange(cfg.Widgets); grouped = true; }

            var host = new Host();
            var tiles = new List<BarTile>();
            foreach (var wi in insts)
            {
                var t = new BarTile { Inst = wi, Zone = grouped ? wi.Zone : "left" };
                try
                {
                    t.W = WidgetRegistry.Create(wi.Id, wi);
                    if (t.W == null) { t.Error = "unknown widget id"; lock (log) log.AppendLine("unknown widget id: " + wi.Id); errors++; }
                    else t.W.Host = host;
                }
                catch (Exception ex) { t.W = null; t.Error = "ctor: " + ex.GetType().Name + " " + ex.Message; Log(wi.Id + " [ctor]", ex); }
                tiles.Add(t);
            }
            host.Tiles = tiles;

            // ---- start + pump ----
            foreach (var t in tiles) if (t.W != null) { try { t.W.OnStart(); } catch (Exception ex) { t.Error = "OnStart: " + ex.GetType().Name + " " + ex.Message; Log(t.Id + " [OnStart]", ex); } }
            if (panels) foreach (var t in tiles) if (t.W != null && t.Error == null) { try { t.W.OnPanelOpen(); } catch (Exception ex) { Log(t.Id + " [OnPanelOpen]", ex); } }
            var sw = Stopwatch.StartNew();
            var lastTick = new Dictionary<BarTile, long>();
            do
            {
                Application.DoEvents();
                host.RunPosted();
                foreach (var t in tiles)
                {
                    if (t.W == null || t.Error != null) continue;
                    int rm = 0; try { rm = t.W.RefreshMs; } catch { }
                    if (rm <= 0) continue;
                    long lt; if (lastTick.TryGetValue(t, out lt) && sw.ElapsedMilliseconds - lt < rm) continue;
                    lastTick[t] = sw.ElapsedMilliseconds;
                    try { t.W.OnTick(); } catch (Exception ex) { t.Error = "OnTick: " + ex.GetType().Name + " " + ex.Message; Log(t.Id + " [OnTick]", ex); }
                }
                Thread.Sleep(25);
            } while (sw.ElapsedMilliseconds < wait);
            host.RunPosted();

            // ---- plan ----
            var m0 = BarMetrics.For(size, 1f);
            float S = m0.Scale;
            int margin = 24, labelH = 34, inner = 20, maxW = 1800;
            // config-based render in full style: lay the strip out like the real full-width bar
            bool fullStyle = grouped && (string.Equals(cfg.Style, "full", StringComparison.OrdinalIgnoreCase) || cfg.IsShell);
            int fullW = 1920; int.TryParse(Arg(args, "--width") ?? "1920", out fullW);
            var sections = new List<Section>();
            using (var scratch = new Bitmap(4, 4, PixelFormat.Format32bppPArgb))
            using (var sg = Graphics.FromImage(scratch))
            {
                Ui.Hq(sg);
                foreach (var tid in themes)
                {
                    var th = Theme.Make(tid, cfg.Accent); Theme.Current = th;
                    var sec = new Section { T = th };
                    // rows
                    if (grouped) sec.Rows.Add(tiles.ToList());
                    else
                    {
                        var row = new List<BarTile>(); int rw = m0.Pad * 2;
                        foreach (var t in tiles)
                        {
                            int w = BarRenderer.MeasureTile(sg, th, m0, t);
                            if (row.Count > 0 && rw + w + m0.Gap > maxW) { sec.Rows.Add(row); row = new List<BarTile>(); rw = m0.Pad * 2; }
                            row.Add(t); rw += w + m0.Gap;
                        }
                        if (row.Count > 0) sec.Rows.Add(row);
                    }
                    int y = labelH + inner, contentW = 0;
                    foreach (var row in sec.Rows)
                    {
                        int w = fullStyle ? BarRenderer.Layout(sg, th, m0, row, fullW, false) : BarRenderer.Layout(sg, th, m0, row, 0, true);
                        sec.RowW.Add(w); contentW = Math.Max(contentW, w);
                        y += m0.Height + inner;
                    }
                    if (panels)
                    {
                        int px = 0, rowH = 0, pw = Math.Max(contentW, 1100), lblH = 22;
                        foreach (var t in tiles)
                        {
                            if (t.W == null) continue;
                            var ps = BarRenderer.SafePanelSize(t.W, S);
                            if (ps.Width <= 0 || ps.Height <= 0) continue;
                            if (px > 0 && px + ps.Width > pw) { px = 0; y += rowH + inner; rowH = 0; }
                            sec.Panels.Add(new KeyValuePair<BarTile, Rectangle>(t, new Rectangle(px, y + lblH, ps.Width, ps.Height)));
                            px += ps.Width + inner; rowH = Math.Max(rowH, ps.Height + lblH);
                            contentW = Math.Max(contentW, px - inner);
                        }
                        if (rowH > 0) y += rowH + inner;
                    }
                    sec.Width = contentW + 2 * inner; sec.Height = y - labelH;
                    sections.Add(sec);
                }
            }
            int W = Math.Max(600, sections.Count == 0 ? 600 : sections.Max(s => s.Width) + 2 * margin);
            int H = margin + sections.Sum(s => labelH + s.Height + margin);

            // ---- draw ----
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                Ui.Hq(g);
                g.Clear(Color.FromArgb(255, 30, 30, 34));
                int y = margin;
                foreach (var sec in sections)
                {
                    var th = sec.T; Theme.Current = th;
                    Ui.Text(g, th.Name + "  (" + th.Id + ")   size " + size.ToUpperInvariant() + (fullStyle ? "   full " + fullW + "px" : "") + (th.IsGlass ? "   · glass backdrop simulated" : ""), Ui.Font(15, true), Color.FromArgb(235, 235, 240), new RectangleF(margin, y, W - 2 * margin, labelH - 6));
                    var area = new Rectangle(margin, y + labelH, W - 2 * margin, sec.Height);
                    PaintBackdrop(g, th, area);
                    int ry = area.Y + inner;
                    for (int i = 0; i < sec.Rows.Count; i++)
                    {
                        var row = sec.Rows[i];
                        if (fullStyle) BarRenderer.Layout(g, th, m0, row, fullW, false); else BarRenderer.Layout(g, th, m0, row, 0, true);
                        var barRect = new Rectangle(area.X + inner, ry, sec.RowW[i], m0.Height);
                        DrawBarStrip(g, th, m0, row, barRect, area, fullStyle ? 0 : m0.WindowRadius);
                        ry += m0.Height + inner;
                    }
                    foreach (var kv in sec.Panels)
                    {
                        var r = kv.Value; r.Offset(area.X + inner, area.Y - labelH);
                        var lblColor = th.IsGlass ? Color.White : (th.Dark ? Color.FromArgb(200, 200, 205) : Color.FromArgb(60, 60, 66));
                        Ui.Text(g, kv.Key.Id + "  (" + r.Width + "×" + r.Height + ")", Ui.Font(11.5f), lblColor, new RectangleF(r.X + 2, r.Y - 21, r.Width, 18));
                        if (th.IsGlass) Ui.FillRound(g, r, m0.PanelRadius, AcrylicTint(th));
                        BarRenderer.PaintCard(g, th, r, m0.PanelRadius);
                        var c = new DrawCtx();
                        BarRenderer.PaintPanel(g, th, m0, kv.Key.W, c, r, false, new Point(-1, -1), spec => FakeInput(g, th, S, spec));
                    }
                    y += labelH + sec.Height + margin;
                }
                foreach (var t in tiles) if (t.W != null)
                    {
                        if (panels) try { t.W.OnPanelClose(); } catch { }
                        try { t.W.OnStop(); } catch (Exception ex) { Log(t.Id + " [OnStop]", ex); }
                    }
                var full = Path.GetFullPath(outPng);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                bmp.Save(full, ImageFormat.Png);
                var head = "NovaBar render " + DateTime.Now.ToString("s") + "  widgets=" + tiles.Count + "  errors=" + errors + Environment.NewLine + Environment.NewLine;
                try { File.WriteAllText(full + ".log", head + log.ToString(), Encoding.UTF8); } catch { }
                Console.WriteLine("rendered " + full + " (" + W + "x" + H + "), errors=" + errors);
            }
            return errors == 0 ? 0 : 2;
        }

        static void DrawBarStrip(Graphics g, Theme th, BarMetrics m, List<BarTile> row, Rectangle barRect, Rectangle area, int radius)
        {
            if (th.IsGlass) { if (radius > 0) Ui.FillRound(g, barRect, radius, AcrylicTint(th)); else g.FillRectangle(Ui.B(AcrylicTint(th)), barRect); }
            BarRenderer.PaintBar(g, th, barRect, radius);
            foreach (var t in row)
            {
                var orig = t.R;
                t.R = new Rectangle(orig.X + barRect.X, orig.Y + barRect.Y, orig.Width, orig.Height);
                BarRenderer.PaintTile(g, th, m, t, false, new Point(-1, -1));
                t.R = orig;
            }
        }

        /// <summary>Approximation of the DWM acrylic layer (blur + luminosity tint) under the theme's BarBg.</summary>
        static Color AcrylicTint(Theme th) { return th.Dark ? Color.FromArgb(150, 32, 32, 36) : Color.FromArgb(150, 238, 238, 240); }

        static void PaintBackdrop(Graphics g, Theme th, Rectangle r)
        {
            if (!th.IsGlass)
            {
                Ui.FillRound(g, r, 10, th.Dark ? Color.FromArgb(255, 72, 74, 80) : Color.FromArgb(255, 205, 207, 212));
                return;
            }
            // synthetic colourful "wallpaper", soft blobs = already blurred look
            using (var path = Ui.Round(r, 10))
            {
                var oldClip = g.Clip;
                g.SetClip(path);
                using (var lg = new LinearGradientBrush(r, Color.FromArgb(255, 40, 70, 160), Color.FromArgb(255, 200, 70, 120), 20f))
                    g.FillRectangle(lg, r);
                var blobs = new[] {
                    new { X = 0.12f, Y = 0.2f, R = 0.45f, C = Color.FromArgb(255, 255, 170, 60) },
                    new { X = 0.55f, Y = 0.1f, R = 0.40f, C = Color.FromArgb(255, 60, 220, 200) },
                    new { X = 0.85f, Y = 0.6f, R = 0.50f, C = Color.FromArgb(255, 130, 80, 255) },
                    new { X = 0.35f, Y = 0.85f, R = 0.45f, C = Color.FromArgb(255, 255, 90, 90) },
                    new { X = 0.7f, Y = 0.95f, R = 0.35f, C = Color.FromArgb(255, 250, 240, 120) },
                };
                float ext = Math.Max(r.Width, r.Height * 2f);
                foreach (var b in blobs)
                {
                    float rad = b.R * ext * 0.6f;
                    var cr = new RectangleF(r.X + b.X * r.Width - rad, r.Y + b.Y * r.Height - rad, rad * 2, rad * 2);
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(cr);
                        using (var pb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb(210, b.C), SurroundColors = new[] { Color.FromArgb(0, b.C) } })
                            g.FillPath(pb, gp);
                    }
                }
                g.Clip = oldClip; oldClip.Dispose();
            }
        }

        static void FakeInput(Graphics g, Theme th, float s, InputSpec spec)
        {
            var r = spec.Rect;
            Ui.FillRound(g, r, Math.Min(r.Height / 2f, 8 * s), th.Input);
            var txt = !string.IsNullOrEmpty(spec.Initial) ? spec.Initial : spec.Placeholder;
            var col = !string.IsNullOrEmpty(spec.Initial) ? th.Text : th.TextFaint;
            var tr = new RectangleF(r.X + 8 * s, r.Y, r.Width - 16 * s, r.Height);
            if (spec.Multiline) Ui.TextWrap(g, txt, Ui.Font(12.5f * s), col, new RectangleF(tr.X, r.Y + 6 * s, tr.Width, r.Height - 12 * s));
            else Ui.Text(g, txt, Ui.Font(12.5f * s), col, tr);
        }

        // ---------------- --list ----------------
        public static int List(string outJson)
        {
            var arr = new List<object>();
            foreach (var m in WidgetRegistry.All)
            {
                var d = new Dictionary<string, object>();
                d["id"] = m.Id; d["name"] = m.Name; d["category"] = m.Category; d["description"] = m.Description; d["refName"] = m.RefName;
                d["data"] = m.Data.ToString(); d["account"] = m.Account;
                try
                {
                    var w = WidgetRegistry.Create(m.Id, null);
                    d["settings"] = w.SettingDefs.Select(s => (object)s.Key).ToArray();
                    var ps = w.PanelSize(1f); d["panel"] = ps.Width > 0 ? ps.Width + "x" + ps.Height : null;
                    d["refreshMs"] = w.RefreshMs;
                }
                catch (Exception ex) { d["error"] = ex.GetType().Name + ": " + ex.Message; }
                arr.Add(d);
            }
            var full = Path.GetFullPath(outJson);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, J.Stringify(arr.ToArray(), true), Encoding.UTF8);
            Console.WriteLine("listed " + arr.Count + " widgets -> " + full);
            return 0;
        }
    }
}
