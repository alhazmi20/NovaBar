// Settings page: Widgets — "Your bar" layout editor (drag & drop, per-instance settings) and the widget gallery.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    class SetWidgetsPage : SetPage
    {
        public override string Title { get { return "Widgets"; } }
        public override string Subtitle { get { return tab == "bar" ? "Drag widgets to arrange them, click one to change its settings" : "Pick widgets to add to your bar"; } }
        string tab = "bar", cat = "All", search = "", selInst;
        /// <summary>Instance to pre-select (from the bar's "Widget settings…" menu item).</summary>
        public static string PendingSelect;
        List<WidgetInstance> undo; string undoLabel;
        readonly HashSet<string> keys = new HashSet<string>();
        static readonly string[] zoneIds = { "left", "center", "right" };
        static readonly string[] zoneNames = { "Left", "Center", "Right" };

        // drag state
        class Chip { public WidgetInstance Inst; public RectangleF R; public int Zone; }
        class Sect { public RectangleF R; public string Dev; }
        List<Chip> chips = new List<Chip>(); readonly List<Sect> sects = new List<Sect>();
        WidgetInstance dragInst; Point dragStart, dragPos; bool dragActive; Point dragOffset; string dragDev;

        public SetWidgetsPage() { }
        public override bool Dragging { get { return dragInst != null; } }
        public override void DragMove(Point p) { dragPos = p; if (!dragActive && (Math.Abs(p.X - dragStart.X) > 4 || Math.Abs(p.Y - dragStart.Y) > 4)) dragActive = true; }
        public override bool DragEnd(Point p)
        {
            var inst = dragInst; bool was = dragActive; dragInst = null; dragActive = false;
            if (!was || inst == null) return false;
            int zone; int idx; if (DropTarget(p, inst, out zone, out idx)) DropInto(inst, zone, idx, dragDev);
            return true;
        }
        public override void Tick() { if (keys.Count > 0) W.Prev.StopExcept(keys.ToList()); }

        public override float Paint(SetG g, float x, float y, float w)
        {
            keys.Clear();
            if (PendingSelect != null) { selInst = PendingSelect; tab = "bar"; PendingSelect = null; }
            if (W.Cfg.Widgets.Count == 0 && tab == "bar" && selInst == null && Scroll == 0 && undo == null) { }
            g.Segmented(new RectangleF(x, y, w, g.S(36)), new[] { "Your bar  " + Cfg.Widgets.Count, "Add widgets  " + WidgetRegistry.All.Count(m => !string.Equals(m.Category, "Hidden", StringComparison.OrdinalIgnoreCase)) }, new[] { "bar", "gallery" }, tab,
                v => { tab = v; Scroll = 0; W.EndEdit(); }, true, 36);
            y += g.S(52);
            return tab == "bar" ? PaintBar(g, x, y, w) : PaintGallery(g, x, y, w);
        }

        // =============================== YOUR BAR ===============================
        float PaintBar(SetG g, float x, float y, float w)
        {
            var P = W.P; var T = W.T;
            // live preview (single display; with several displays each section has its own)
            if (SetScreens().Count < 2) {
            var pc = new RectangleF(x, y, w, g.S(92)); g.Card(pc);
            var pr = new RectangleF(x + g.S(8), y + g.S(8), w - g.S(16), g.S(76));
            var st = g.G.Save();
            using (var clip = Ui.Round(pr, g.S(6))) g.G.SetClip(clip, CombineMode.Intersect);
            SetPreview.Wallpaper(g.G, pr, SetPreview.WallFor(T));
            var zl = new[] { new List<Widget>(), new List<Widget>(), new List<Widget>() };
            foreach (var inst in Cfg.Widgets)
            {
                var wd = W.Prev.Get("wb:" + inst.Inst, inst.Id, inst, true); if (wd == null) continue;
                keys.Add("wb:" + inst.Inst); zl[ZoneIdx(inst.Zone)].Add(wd);
            }
            bool full = Cfg.Style == "full" || Cfg.Style == "shell";
            W.Prev.DrawBar(g.G, T, full ? new RectangleF(pr.X, pr.Bottom - g.S(44) * 0.9f, pr.Width, g.S(44) * 0.9f) : Ui.Inset(pr, g.S(10), 0), zl, g.Sc * 0.9f, !full, !full);
            g.G.Restore(st);
            y += pc.Height + g.S(14);
            }

            // toolbar
            float bx = x;
            float pw = g.ButtonWidth("Taskbar preset", Icons.Window);
            g.Button(new RectangleF(bx, y, pw, g.S(34)), "Taskbar preset", () => { if (Confirm("Replace all your widgets with the taskbar preset?")) TaskbarPreset(); }, true, Icons.Window); bx += pw + g.S(8);
            float rw = g.ButtonWidth("Reset to default", Icons.Refresh);
            g.Button(new RectangleF(bx, y, rw, g.S(34)), "Reset to default", () => { if (Confirm("Replace all your widgets with the default layout?")) Replace(AppConfig.DefaultWidgets(), "Reset to the default layout"); }, false, Icons.Refresh); bx += rw + g.S(8);
            float qw = g.ButtonWidth("Presets", Icons.Folder);
            var qr = new RectangleF(bx, y, qw, g.S(34));
            g.Button(qr, "Presets", () => PresetMenu(qr), false, Icons.Folder); bx += qw + g.S(8);
            if (undo != null)
            {
                float uw = g.ButtonWidth("Undo", "");
                g.Button(new RectangleF(bx, y, uw, g.S(34)), "Undo", () => { var u = undo; undo = null; Cfg.Widgets = u; selInst = null; W.Changed(); W.Toast("Layout restored"); }, false, "");
                bx += uw + g.S(10);
                Ui.Text(g.G, undoLabel, g.F(12), P.Dim, new RectangleF(bx, y, x + w - bx - g.S(140), g.S(34)));
            }
            float aw = g.ButtonWidth("Add widgets", Icons.Add);
            g.Button(new RectangleF(x + w - aw, y, aw, g.S(34)), "Add widgets", () => { tab = "gallery"; Scroll = 0; }, false, Icons.Add);
            float rlw = g.ButtonWidth("Reload", Icons.Refresh);
            g.Button(new RectangleF(x + w - aw - g.S(8) - rlw, y, rlw, g.S(34)), "Reload", () => { Relaunch.AllFromSettings("widgets"); System.Windows.Forms.Application.Exit(); }, false, Icons.Refresh);
            y += g.S(46);

            // one section per display (a single "Your bar" section with one monitor)
            chips.Clear(); sects.Clear();
            var nameF = g.FS(13);
            var screens = SetScreens();
            int nSect = screens.Count > 1 ? screens.Count : 1;
            float lx = x + g.S(16), lw = w - g.S(32);
            for (int z = 0; z < nSect; z++)
            {
                string dev = nSect > 1 ? screens[z].DeviceName : null;
                var list = Cfg.Widgets.Where(i => dev == null || i.ShowsOn(dev)).ToList();
                float hy = y + g.S(12);
                float pvH = nSect > 1 ? g.S(50) : 0;
                float cy0 = hy + g.S(26) + (pvH > 0 ? pvH + g.S(10) : 0);
                // flow layout
                float cx = lx, cy = cy0, chH = g.S(36), gap = g.S(8);
                var rects = new List<RectangleF>();
                foreach (var inst in list)
                {
                    var m = WidgetRegistry.Meta(inst.Id);
                    float cw = g.TextW(m != null ? m.Name : inst.Id, nameF) + g.S(72) + TagW(g, inst, dev);
                    if (cx + cw > lx + lw && cx > lx) { cx = lx; cy += chH + gap; }
                    rects.Add(new RectangleF(cx, cy, cw, chH)); cx += cw + gap;
                }
                float zh = Math.Max(cy0 - y + chH + g.S(12), (rects.Count > 0 ? rects[rects.Count - 1].Bottom : cy0 + chH) - y + g.S(14));
                var zr = new RectangleF(x, y, w, zh);
                sects.Add(new Sect { R = zr, Dev = dev });
                bool dropHere = dragActive && zr.Contains(dragPos);
                g.Card(zr);
                if (dropHere) Ui.StrokeRound(g.G, zr, g.S(8), T.Accent, g.S(1.5f));
                // header
                string title = dev == null ? "Your bar" : "Display " + (z + 1);
                Ui.Text(g.G, title, g.FS(13.5f), P.Text, new RectangleF(lx, hy, g.S(110), g.S(20)));
                float tx = lx + g.TextW(title, g.FS(13.5f)) + g.S(10);
                string sub = (dev != null ? screens[z].Bounds.Width + "\u00d7" + screens[z].Bounds.Height + (screens[z].Primary ? "  \u00b7  primary" : "") + "  \u00b7  " : "")
                    + list.Count + (list.Count == 1 ? " widget" : " widgets") + "  \u00b7  left \u2192 right";
                Ui.Text(g.G, sub, g.F(11.5f), P.Faint, new RectangleF(tx, hy, lw - (tx - lx), g.S(20)));
                if (dev != null && SizeTag(dev) != null)
                    Ui.Text(g.G, SizeTag(dev), g.F(11.5f), P.Faint, new RectangleF(lx, hy, lw, g.S(20)), 1);
                // mini preview of this display's bar
                if (pvH > 0)
                {
                    var mpr = new RectangleF(lx, hy + g.S(26), lw, pvH);
                    var st2 = g.G.Save();
                    using (var clip = Ui.Round(mpr, g.S(6))) g.G.SetClip(clip, CombineMode.Intersect);
                    SetPreview.Wallpaper(g.G, mpr, SetPreview.WallFor(T));
                    var mzl = new[] { new List<Widget>(), new List<Widget>(), new List<Widget>() };
                    foreach (var inst in list) { var wd = W.Prev.Get("wb:" + inst.Inst, inst.Id, inst, true); if (wd != null) { keys.Add("wb:" + inst.Inst); mzl[1].Add(wd); } }
                    float sc = g.Sc * 0.62f; int need = 0;
                    foreach (var wd in mzl[1]) need += W.Prev.Measure(g.G, T, wd, sc) + (int)(4 * sc);
                    if (need > mpr.Width - g.S(20)) sc *= (mpr.Width - g.S(20)) / Math.Max(1f, need);
                    float bh = 44 * sc;
                    W.Prev.DrawBar(g.G, T, new RectangleF(mpr.X + g.S(6), mpr.Y + (mpr.Height - bh) / 2, mpr.Width - g.S(12), bh), mzl, sc, true, true);
                    g.G.Restore(st2);
                }
                if (list.Count == 0) Ui.Text(g.G, "No widgets on this display \u2014 drag one here or add from the gallery", g.F(12.5f), P.Faint, new RectangleF(lx, cy0, lw, chH));
                for (int i = 0; i < list.Count; i++)
                {
                    chips.Add(new Chip { Inst = list[i], R = rects[i], Zone = z });
                    DrawChip(g, list[i], rects[i], dragActive && dragInst == list[i] ? 0.35f : 1f, false, dev);
                }
                // insertion caret
                int dz, di;
                if (dragActive && DropTarget(dragPos, dragInst, out dz, out di) && dz == z)
                {
                    var others = chips.Where(c => c.Zone == z && c.Inst != dragInst).ToList();
                    RectangleF cr;
                    if (others.Count == 0) cr = new RectangleF(lx, cy0, g.S(3), chH);
                    else if (di < others.Count) cr = new RectangleF(others[di].R.X - g.S(5.5f), others[di].R.Y, g.S(3), chH);
                    else cr = new RectangleF(others[others.Count - 1].R.Right + g.S(2.5f), others[others.Count - 1].R.Y, g.S(3), chH);
                    Ui.FillRound(g.G, cr, g.S(1.5f), T.Accent);
                }
                y += zh + g.S(10);
            }
            // ghost
            if (dragActive && dragInst != null)
            {
                var m = WidgetRegistry.Meta(dragInst.Id);
                float cw = g.TextW(m != null ? m.Name : dragInst.Id, nameF) + g.S(72) + TagW(g, dragInst, dragDev);
                var gr = new RectangleF(dragPos.X - dragOffset.X, dragPos.Y - dragOffset.Y, cw, g.S(36));
                using (var sh = Ui.Round(new RectangleF(gr.X + g.S(2), gr.Y + g.S(4), gr.Width, gr.Height), g.S(8))) g.G.FillPath(Ui.B(Color.FromArgb(60, 0, 0, 0)), sh);
                DrawChip(g, dragInst, gr, 1f, true, dragDev);
            }

            // selected widget settings
            var sel = Cfg.Widgets.FirstOrDefault(i => i.Inst == selInst);
            y += g.S(14);
            if (sel == null)
            {
                Ui.Text(g.G, "Tip: click a widget to change its settings, drag it to move it, or press × to remove it.", g.F(12.5f), P.Faint, new RectangleF(x + g.S(2), y, w, g.S(20)));
                return y + g.S(30);
            }
            return PaintInstance(g, sel, x, y, w);
        }

        void DrawChip(SetG g, WidgetInstance inst, RectangleF r, float alpha, bool ghost = false, string dev = null)
        {
            var P = W.P; var T = W.T;
            var m = WidgetRegistry.Meta(inst.Id);
            bool sel = inst.Inst == selInst, hv = !ghost && g.Hover(r);
            Color bg = ghost ? (T.Dark ? Color.FromArgb(255, 48, 48, 56) : Color.White) : sel ? Ui.WithA(T.Accent, T.Dark ? 60 : 40) : hv ? P.CardHover : (P.T.IsGlass ? P.Sel : Ui.Mix(P.Card, T.Dark ? Color.White : Color.Black, 0.05));
            int a = (int)(255 * alpha);
            Ui.FillRound(g.G, r, g.S(8), Ui.WithA(bg, bg.A * a / 255));
            Ui.StrokeRound(g.G, r, g.S(8), sel ? T.Accent : Ui.WithA(P.InputBorder, P.InputBorder.A * a / 255), sel ? g.S(1.5f) : 1f);
            Ui.Icon(g.G, "", new RectangleF(r.X + g.S(6), r.Y, g.S(14), r.Height), Ui.WithA(P.Faint, a), g.S(11)); // gripper
            Ui.Text(g.G, m != null ? m.Name : inst.Id, g.FS(13), Ui.WithA(m != null ? P.Text : T.Warn, a), new RectangleF(r.X + g.S(24), r.Y, r.Width - g.S(56) - TagW(g, inst, dev), r.Height));
            string tag = ChipTag(inst, dev);
            if (tag != null)
            {
                var tf = g.F(10.5f, true); float tw = g.TextW(tag, tf) + g.S(10);
                var tr = new RectangleF(r.Right - g.S(36) - tw, r.Y + (r.Height - g.S(18)) / 2, tw, g.S(18));
                Ui.FillRound(g.G, tr, g.S(9), Ui.WithA(T.Accent, (int)(70 * alpha)));
                Ui.Text(g.G, tag, tf, Ui.WithA(P.Text, (int)(255 * alpha)), tr, 0);
            }
            var xr = new RectangleF(r.Right - g.S(30), r.Y + g.S(6), g.S(24), g.S(24));
            if (!ghost && g.Hover(xr)) Ui.FillRound(g.G, xr, g.S(12), Ui.WithA(T.Bad, 50));
            Ui.Icon(g.G, "", xr, Ui.WithA(!ghost && g.Hover(xr) ? T.Bad : P.Dim, a), g.S(10));
            if (ghost) return;
            var ii = inst;
            var dd = dev;
            g.Hit(r, () => { selInst = selInst == ii.Inst ? null : ii.Inst; W.EndEdit(); }, p => { dragInst = ii; dragDev = dd; dragStart = p; dragPos = p; dragActive = false; dragOffset = new Point((int)(p.X - r.X), (int)(p.Y - r.Y)); });
            g.Hit(xr, () => { if (dd == null) Remove(ii); else RemoveFrom(ii, dd); });
        }

        float PaintInstance(SetG g, WidgetInstance inst, float x, float y, float w)
        {
            var P = W.P; var T = W.T;
            var m = WidgetRegistry.Meta(inst.Id);
            var wd = W.Prev.Get("wb:" + inst.Inst, inst.Id, inst, true);
            SettingDef[] defs = new SettingDef[0];
            if (wd != null) try { defs = wd.SettingDefs ?? new SettingDef[0]; } catch { }
            float rowH = g.S(52);
            var screens = SetScreens();
            float h = g.S(132) + Math.Max(1, defs.Length) * rowH + g.S(60) + (screens.Count > 1 ? g.S(86) : 0);
            var card = new RectangleF(x, y, w, h); g.Card(card);
            // header + live tile
            Ui.Text(g.G, m != null ? m.Name : inst.Id, g.FS(17), P.Text, new RectangleF(x + g.S(20), y + g.S(16), w * 0.55f, g.S(24)));
            Ui.Text(g.G, m != null ? m.Description : "This widget is not part of this build.", g.F(12.5f), P.Dim, new RectangleF(x + g.S(20), y + g.S(42), w * 0.55f, g.S(18)));
            float bxp = x + g.S(20);
            if (m != null) { bxp += g.Badge(bxp, y + g.S(76), m.Category, P.Dim) + g.S(6); bxp += Data(g, m, bxp, y + g.S(76)) + g.S(6); }
            Ui.Text(g.G, "Position " + (Cfg.Widgets.IndexOf(inst) + 1) + " of " + Cfg.Widgets.Count + "  ·  " + inst.Inst, g.F(11.5f), P.Faint, new RectangleF(bxp + g.S(4), y + g.S(66), g.S(300), g.S(20)));
            // preview tile on themed bar background
            var pv = new RectangleF(x + w - g.S(300), y + g.S(16), g.S(280), g.S(76));
            var st = g.G.Save();
            using (var clip = Ui.Round(pv, g.S(6))) g.G.SetClip(clip, CombineMode.Intersect);
            SetPreview.Wallpaper(g.G, pv, SetPreview.WallFor(T));
            if (wd != null)
            {
                float sc = g.Sc; int tw = W.Prev.Measure(g.G, T, wd, sc);
                if (tw > pv.Width - g.S(30)) { sc *= (pv.Width - g.S(30)) / tw; tw = W.Prev.Measure(g.G, T, wd, sc); }
                var bar = new RectangleF(pv.X + (pv.Width - tw - 10 * sc) / 2, pv.Y + (pv.Height - 44 * sc) / 2, tw + 10 * sc, 44 * sc);
                SetPreview.FillBarBg(g.G, T, bar, 8 * sc);
                W.Prev.DrawTile(g.G, T, wd, new Rectangle((int)(bar.X + 5 * sc), (int)(bar.Y + 5 * sc), tw, (int)(34 * sc)), sc);
            }
            g.G.Restore(st);
            float fy = y + g.S(108);
            g.G.FillRectangle(Ui.B(P.Sep), x + g.S(16), fy, w - g.S(32), 1);
            fy += g.S(10);
            Ui.Text(g.G, "Settings", g.FS(13), P.Text, new RectangleF(x + g.S(20), fy, w, g.S(20)));
            fy += g.S(24);
            if (defs.Length == 0) { Ui.Text(g.G, "This widget has no settings.", g.F(12.5f), P.Faint, new RectangleF(x + g.S(20), fy, w, rowH)); fy += rowH; }
            foreach (var d in defs)
            {
                var r = new RectangleF(x + g.S(20), fy, w - g.S(40), rowH);
                Ui.Text(g.G, d.Label ?? d.Key, g.F(13.5f), P.Text, new RectangleF(r.X, r.Y, r.Width * 0.45f, r.Height));
                SettingControl(g, inst, d, new RectangleF(r.X + r.Width * 0.4f, r.Y, r.Width * 0.6f, r.Height));
                fy += rowH;
            }
            // per-monitor visibility
            if (screens.Count > 1)
            {
                fy += g.S(6);
                g.G.FillRectangle(Ui.B(P.Sep), x + g.S(16), fy, w - g.S(32), 1);
                fy += g.S(10);
                Ui.Text(g.G, "Show on", g.FS(13), P.Text, new RectangleF(x + g.S(20), fy, g.S(120), g.S(20)));
                Ui.Text(g.G, "Pick which monitors' bars show this widget", g.F(11.5f), P.Faint, new RectangleF(x + g.S(90), fy, w - g.S(110), g.S(20)));
                fy += g.S(26);
                if (inst.Monitors == null) inst.Monitors = new List<string>();
                bool all = inst.Monitors.Count == 0;
                float bx = x + g.S(20);
                float bw0 = g.TextW("All monitors", g.FS(12.5f)) + g.S(28);
                g.Button(new RectangleF(bx, fy, bw0, g.S(32)), "All monitors", () => { inst.Monitors.Clear(); W.Changed(); }, all);
                bx += bw0 + g.S(8);
                for (int i = 0; i < screens.Count; i++)
                {
                    var sc = screens[i]; string dev = sc.DeviceName;
                    string label = "Display " + (i + 1) + (sc.Primary ? " · primary" : "") + "  " + sc.Bounds.Width + "×" + sc.Bounds.Height;
                    float bw = g.TextW(label, g.FS(12.5f)) + g.S(28);
                    bool on = !all && inst.Monitors.Any(mm => string.Equals(mm, dev, StringComparison.OrdinalIgnoreCase));
                    g.Button(new RectangleF(bx, fy, bw, g.S(32)), label, () =>
                    {
                        if (inst.Monitors.Count == 0) inst.Monitors.Add(dev);                 // from "all" → only this one
                        else if (on) inst.Monitors.RemoveAll(mm => string.Equals(mm, dev, StringComparison.OrdinalIgnoreCase));
                        else inst.Monitors.Add(dev);
                        if (inst.Monitors.Count == 0 || inst.Monitors.Count >= screens.Count) inst.Monitors.Clear();   // none/all selected = all
                        W.Changed();
                    }, on);
                    bx += bw + g.S(8);
                }
                fy += g.S(44);
            }
            // footer
            fy += g.S(8);
            g.Button(new RectangleF(x + g.S(20), fy, g.S(118), g.S(34)), "Remove", () => Remove(inst), false, Icons.Delete);
            var zr = new RectangleF(x + g.S(150), fy, w, g.S(34));
            int ix = Cfg.Widgets.IndexOf(inst);
            g.Button(new RectangleF(zr.X, zr.Y, g.S(120), zr.Height), "Move left", () => { W.EndEdit(); if (ix > 0) MoveTo(inst, 1, ix - 1); }, false, Icons.ChevronLeft, ix > 0);
            g.Button(new RectangleF(zr.X + g.S(128), zr.Y, g.S(126), zr.Height), "Move right", () => { W.EndEdit(); if (ix < Cfg.Widgets.Count - 1) MoveTo(inst, 1, ix + 1); }, false, Icons.ChevronRight, ix >= 0 && ix < Cfg.Widgets.Count - 1);
            if (inst.Settings != null && inst.Settings.Count > 0)
                g.Button(new RectangleF(x + w - g.S(160), fy, g.S(140), g.S(34)), "Reset settings", () => { inst.Settings.Clear(); W.Changed(); }, false, Icons.Refresh);
            return y + h + g.S(10);
        }

        static string MonTag(WidgetInstance inst)
        {
            var scr = SetScreens(); var nums = new List<string>();
            for (int i = 0; i < scr.Count; i++) if (inst.Monitors.Any(mm => string.Equals(mm, scr[i].DeviceName, StringComparison.OrdinalIgnoreCase))) nums.Add((i + 1).ToString());
            return nums.Count > 0 ? "Display " + string.Join(", ", nums) : "Disconnected display";
        }
        /// <summary>Chip tag: in a per-display section, "All" for widgets on every display, "Display 1, 3" for shared ones,
        /// nothing for widgets only on this display. In the single-section view, the monitor list (if limited).</summary>
        static string ChipTag(WidgetInstance inst, string dev)
        {
            if (inst == null) return null;
            bool all = inst.Monitors == null || inst.Monitors.Count == 0;
            if (dev == null) return all ? null : MonTag(inst);
            if (all) return "All";
            if (inst.Monitors.Count == 1) return null;
            return MonTag(inst);
        }
        static float TagW(SetG g, WidgetInstance inst, string dev = null)
        {
            var t = ChipTag(inst, dev); if (t == null) return 0;
            return g.TextW(t, g.F(10.5f, true)) + g.S(18);
        }
        string SizeTag(string dev)
        {
            object v; if (Cfg.Extra == null || !Cfg.Extra.TryGetValue("size." + dev, out v)) return null;
            var sz = J.AsStr(v); return sz == "S" ? "Small" : sz == "M" ? "Medium" : sz == "L" ? "Large" : sz == "XL" ? "Extra large" : sz == "XXL" ? "Huge" : null;
        }
        /// <summary>Monitors in the same order the bar uses: primary first.</summary>
        static List<Screen> SetScreens()
        {
            var l = new List<Screen> { Screen.PrimaryScreen };
            l.AddRange(Screen.AllScreens.Where(sc => !sc.Primary));
            return l;
        }
        object Val(WidgetInstance inst, SettingDef d) { object v; return inst.Settings != null && inst.Settings.TryGetValue(d.Key, out v) && v != null ? v : d.Default; }
        void Set(WidgetInstance inst, string key, object v) { if (inst.Settings == null) inst.Settings = new Dictionary<string, object>(); inst.Settings[key] = v; W.Changed(); }

        void SettingControl(SetG g, WidgetInstance inst, SettingDef d, RectangleF area)
        {
            var v = Val(inst, d); string k = "ws:" + inst.Inst + ":" + d.Key;
            switch (d.Type)
            {
                case SettingType.Toggle:
                    g.Toggle(area, J.AsBool(v), b => Set(inst, d.Key, b)); break;
                case SettingType.Text:
                    g.Input(k, new RectangleF(area.Right - g.S(320), area.Y + (area.Height - g.S(34)) / 2, g.S(320), g.S(34)), J.AsStr(v), "", s => Set(inst, d.Key, s)); break;
                case SettingType.Number:
                    {
                        double n = J.AsNum(v);
                        var ir = new RectangleF(area.Right - g.S(196), area.Y + (area.Height - g.S(34)) / 2, g.S(120), g.S(34));
                        g.Input(k, ir, Fmt(n), "", s =>
                        {
                            double p; if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out p) || double.TryParse(s.Trim(), out p))
                                Set(inst, d.Key, Math.Max(d.Min, Math.Min(d.Max, p)));
                        });
                        double step = d.Max - d.Min <= 2 ? 0.1 : d.Max - d.Min <= 50 ? 1 : d.Max - d.Min <= 1000 ? 5 : 10;
                        g.Button(new RectangleF(ir.Right + g.S(6), ir.Y, g.S(34), g.S(34)), null, () => { W.EndEdit(); Set(inst, d.Key, Math.Max(d.Min, Math.Round(n - step, 4))); }, false, "");
                        g.Button(new RectangleF(ir.Right + g.S(42), ir.Y, g.S(34), g.S(34)), null, () => { W.EndEdit(); Set(inst, d.Key, Math.Min(d.Max, Math.Round(n + step, 4))); }, false, Icons.Add);
                    }
                    break;
                case SettingType.Choice:
                    {
                        var opts = d.Options ?? new string[0]; var cur = J.AsStr(v);
                        if (opts.Length <= 4 && g.SegWidth(opts) <= area.Width)
                            g.Segmented(area, opts, opts, cur, s => Set(inst, d.Key, s));
                        else
                        {
                            int i = Array.IndexOf(opts, cur);
                            g.Dropdown(new RectangleF(area.Right - g.S(260), area.Y + (area.Height - g.S(34)) / 2, g.S(260), g.S(34)), i >= 0 ? opts[i] : cur, opts, i, j => Set(inst, d.Key, opts[j]));
                        }
                    }
                    break;
            }
        }
        static string Fmt(double n) { return n.ToString(Math.Abs(n - Math.Round(n)) < 1e-9 ? "0" : "0.###", CultureInfo.InvariantCulture); }

        // ---- layout operations ----
        static int ZoneIdx(string z) { return z == "left" ? 0 : z == "center" ? 1 : 2; }
        static string ZoneId(string z) { return zoneIds[ZoneIdx(z)]; }
        static string ZoneName(string z) { return zoneNames[ZoneIdx(z)]; }

        bool DropTarget(Point p, WidgetInstance drag, out int zone, out int idx)
        {
            zone = -1; idx = 0;
            float best = float.MaxValue;
            for (int z = 0; z < sects.Count; z++)
            {
                var r = sects[z].R;
                float d = p.Y < r.Top ? r.Top - p.Y : p.Y > r.Bottom ? p.Y - r.Bottom : 0;
                if (d < best) { best = d; zone = z; }
            }
            if (zone < 0 || best > W.S(80)) return false;
            int zz = zone; var others = chips.Where(c => c.Zone == zz && c.Inst != drag).ToList();
            idx = others.Count;
            for (int i = 0; i < others.Count; i++)
            {
                var r = others[i].R;
                if (p.Y < r.Top - W.S(4)) { idx = i; break; }
                if (p.Y <= r.Bottom + W.S(4) && p.X < r.X + r.Width / 2) { idx = i; break; }
            }
            return true;
        }
        /// <summary>Drop a chip into section z at position idx (among that display's widgets). Dragging a widget
        /// from one display's section to another moves it to that display.</summary>
        void DropInto(WidgetInstance inst, int z, int idx, string fromDev)
        {
            if (z < 0 || z >= sects.Count) return;
            string dev = sects[z].Dev;
            var list = Cfg.Widgets;
            var inSect = list.Where(i => i != inst && (dev == null || i.ShowsOn(dev))).ToList();
            if (dev != null && !inst.ShowsOn(dev))
            {   // move between displays
                if (inst.Monitors == null) inst.Monitors = new List<string>();
                if (fromDev != null) inst.Monitors.RemoveAll(mm => string.Equals(mm, fromDev, StringComparison.OrdinalIgnoreCase));
                inst.Monitors.Add(dev);
                if (inst.Monitors.Count >= SetScreens().Count) inst.Monitors.Clear();
                W.Toast("Moved to Display " + (z + 1));
            }
            list.Remove(inst); inst.Zone = "center";
            if (idx < inSect.Count) list.Insert(list.IndexOf(inSect[idx]), inst);
            else if (inSect.Count > 0) list.Insert(list.IndexOf(inSect[inSect.Count - 1]) + 1, inst);
            else list.Add(inst);
            W.Changed();
        }
        /// <summary>Global order move (Move left/right buttons).</summary>
        void MoveTo(WidgetInstance inst, int zone, int idx)
        {
            var list = Cfg.Widgets; list.Remove(inst);
            inst.Zone = "center";
            list.Insert(Math.Max(0, Math.Min(list.Count, idx)), inst);
            W.Changed();
        }
        /// <summary>× in a display's section: take the widget off that display only (removes it entirely if it was only there).</summary>
        void RemoveFrom(WidgetInstance inst, string dev)
        {
            var scr = SetScreens();
            if (inst.Monitors == null) inst.Monitors = new List<string>();
            bool all = inst.Monitors.Count == 0;
            if (!all && inst.Monitors.Count == 1) { Remove(inst); return; }
            undo = Cfg.Widgets.Select(i => i.Clone()).ToList(); var m = WidgetRegistry.Meta(inst.Id);
            int k = scr.FindIndex(sc => sc.DeviceName == dev);
            undoLabel = "Removed " + (m != null ? m.Name : inst.Id) + " from Display " + (k + 1);
            if (all) inst.Monitors.AddRange(scr.Select(sc => sc.DeviceName).Where(d => !string.Equals(d, dev, StringComparison.OrdinalIgnoreCase)));
            else inst.Monitors.RemoveAll(mm => string.Equals(mm, dev, StringComparison.OrdinalIgnoreCase));
            if (inst.Monitors.Count == 0) { Remove(inst); return; }
            W.EndEdit(); W.Changed(); W.Toast(undoLabel);
        }
        void Remove(WidgetInstance inst)
        {
            undo = Cfg.Widgets.ToList(); var m = WidgetRegistry.Meta(inst.Id);
            undoLabel = "Removed " + (m != null ? m.Name : inst.Id);
            Cfg.Widgets.Remove(inst); W.Prev.Forget("wb:" + inst.Inst);
            if (selInst == inst.Inst) selInst = null;
            W.EndEdit(); W.Changed();
        }
        /// <summary>Presets menu: save the current layout to presets\<name>.json, load one, or open the folder.</summary>
        void PresetMenu(RectangleF r)
        {
            var names = Presets.Names();
            var opts = new List<string> { "Save current layout as preset…" };
            opts.AddRange(names.Select(n => "Load “" + n + "”"));
            if (names.Count > 0) opts.Add("Delete a preset…");
            opts.Add("Open presets folder");
            W.ShowMenu(r, opts.ToArray(), -1, i =>
            {
                if (i == 0)
                {
                    var name = AskName("Preset name", names.Count == 0 ? "My layout" : "Layout " + (names.Count + 1));
                    if (string.IsNullOrWhiteSpace(name)) return;
                    W.SaveNow();
                    try { Presets.Save(name, Cfg); W.Toast("Saved preset “" + name.Trim() + "”"); }
                    catch (Exception ex) { W.Toast("Couldn't save: " + ex.Message); }
                }
                else if (i <= names.Count)
                {
                    var n = names[i - 1];
                    if (!Confirm("Load the preset “" + n + "”?")) return;
                    var before = Cfg.Widgets.ToList();
                    try { System.IO.File.Copy(Paths.ConfigFile, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Paths.ConfigFile), "config.backup.json"), true); } catch { }
                    if (!Presets.Apply(n, Cfg)) { W.Toast("Couldn't read that preset"); return; }
                    undo = before; undoLabel = "Loaded preset “" + n + "”";
                    selInst = null; W.EndEdit(); W.Changed(true); W.Toast(undoLabel);
                }
                else if (names.Count > 0 && i == names.Count + 1)
                {
                    W.ShowMenu(r, names.ToArray(), -1, j =>
                    {
                        if (MessageBox.Show("Delete the preset “" + names[j] + "”?", "NovaBar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                        { Presets.Delete(names[j]); W.Toast("Deleted “" + names[j] + "”"); }
                    });
                }
                else { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Paths.PresetsDir + "\""); } catch { } }
            });
        }
        static string AskName(string title, string def)
        {
            using (var f = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(360, 96), ShowInTaskbar = false, TopMost = true })
            {
                var tb = new TextBox { Text = def, Left = 12, Top = 14, Width = 336 };
                var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 192, Top = 54, Width = 74 };
                var no = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 274, Top = 54, Width = 74 };
                f.Controls.AddRange(new Control[] { tb, ok, no }); f.AcceptButton = ok; f.CancelButton = no;
                f.Shown += (s, e) => { tb.SelectAll(); tb.Focus(); };
                return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
            }
        }
        static bool Confirm(string q)
        {
            return MessageBox.Show(q + "\n\nYour current layout (widgets, monitors and their settings) will be replaced. A backup is saved to config.backup.json.",
                "NovaBar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        void Replace(List<WidgetInstance> l, string label)
        {
            undo = Cfg.Widgets.ToList(); undoLabel = label;
            try { if (System.IO.File.Exists(Paths.ConfigFile)) System.IO.File.Copy(Paths.ConfigFile, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Paths.ConfigFile), "config.backup.json"), true); } catch { }
            Cfg.Widgets = l.Where(i => WidgetRegistry.Meta(i.Id) != null).ToList();
            selInst = null; W.EndEdit(); W.Changed(); W.Toast(label);
        }
        void TaskbarPreset()
        {
            var l = new List<WidgetInstance>();
            Action<string, string> add = (id, z) => { if (WidgetRegistry.Meta(id) != null) l.Add(new WidgetInstance { Id = id, Inst = AppConfig.NewInst(id), Zone = z }); };
            add("apps", "left");
            foreach (var id in new[] { "tray", "language", "network", "audio", "battery", "notifcenter", "clock", "showdesktop" }) add(id, "right");
            Replace(AppConfig.Normalize(l), "Applied the taskbar preset");
            if (Cfg.Position != "bottom") { Cfg.Position = "bottom"; W.Changed(); }
        }
        /// <summary>Add at the end of the bar, on every monitor (dev == null) or only on the given display.</summary>
        void AddOn(WidgetAttribute m, string dev)
        {
            var inst = new WidgetInstance { Id = m.Id, Inst = AppConfig.NewInst(m.Id), Zone = "center" };
            if (dev != null) inst.Monitors.Add(dev);
            Cfg.Widgets.Add(inst);
            W.Changed();
            string where = "all monitors";
            if (dev != null) { var scr = SetScreens(); int k = scr.FindIndex(x => x.DeviceName == dev); where = "Display " + (k + 1); }
            W.Toast("Added " + m.Name + " to " + where);
        }
        void Add(WidgetAttribute m, string zone)
        {
            var inst = new WidgetInstance { Id = m.Id, Inst = AppConfig.NewInst(m.Id), Zone = "center" };
            if (zone == "start") Cfg.Widgets.Insert(0, inst); else Cfg.Widgets.Add(inst);
            W.Changed();
            W.Toast("Added " + m.Name + (zone == "start" ? " at the start of your bar" : " at the end of your bar"));
        }

        // =============================== GALLERY ===============================
        static string CatIcon(string c)
        {
            switch (c)
            {
                case "AI": return Icons.Sparkle; case "Productivity": return Icons.CheckBoxOn; case "Finance": return Icons.Money; case "System": return Icons.Cpu;
                case "Media & Audio": return Icons.Music; case "Analytics": return Icons.Chart; case "Developer": return Icons.Code; case "Online & Mail": return Icons.Mail;
                case "Social": return Icons.People; case "Apps & Files": return Icons.Folder; case "Weather": return Icons.PartlyCloudy; case "Stocks": return "";
                case "Clocks": return Icons.Clock; case "All": return Icons.Apps; default: return Icons.Apps;
            }
        }
        float Data(SetG g, WidgetAttribute m, float x, float ym)
        {
            var T = W.T;
            if (m.Data == DataKind.Live) return g.Badge(x, ym, "Live", T.Good);
            if (m.Data == DataKind.NeedsAccount)
            {
                bool ok = !string.IsNullOrEmpty(m.Account) && Accounts.Connected(m.Account);
                return g.Badge(x, ym, ok ? "Connected" : "Needs account", ok ? T.Good : T.Warn);
            }
            return g.Badge(x, ym, "Sample", W.P.Dim);
        }

        float PaintGallery(SetG g, float x, float y, float w)
        {
            var P = W.P; var T = W.T;
            var all = WidgetRegistry.All.Where(m => !string.Equals(m.Category, "Hidden", StringComparison.OrdinalIgnoreCase)).ToList();
            var cats = new List<string> { "All" }; cats.AddRange(all.Select(m => m.Category).Distinct().OrderBy(c => c));
            // category list
            float lw = g.S(196), ly = y;
            foreach (var c in cats)
            {
                var r = new RectangleF(x, ly, lw, g.S(36));
                bool sel = c == cat, hv = g.Hover(r);
                if (sel) Ui.FillRound(g.G, r, g.S(6), P.Sel.A > 0 ? P.Sel : P.CardHover);
                else if (hv) Ui.FillRound(g.G, r, g.S(6), P.CardHover);
                if (sel) Ui.FillRound(g.G, new RectangleF(r.X + g.S(1), r.Y + g.S(10), g.S(3), g.S(16)), g.S(1.5f), T.Accent);
                Ui.Icon(g.G, CatIcon(c), new RectangleF(r.X + g.S(12), r.Y, g.S(18), r.Height), sel ? T.Accent : P.Dim, g.S(13));
                Ui.Text(g.G, c, sel ? g.FS(13.5f) : g.F(13.5f), P.Text, new RectangleF(r.X + g.S(40), r.Y, r.Width - g.S(80), r.Height));
                int n = c == "All" ? all.Count : all.Count(m => m.Category == c);
                Ui.Text(g.G, n.ToString(), g.F(12), P.Faint, new RectangleF(r.X, r.Y, r.Width - g.S(12), r.Height), 1);
                var cc = c; g.Hit(r, () => { cat = cc; Scroll = 0; });
                ly += g.S(38);
            }
            // search + grid
            float gx = x + lw + g.S(20), gw = w - lw - g.S(20);
            g.Input("wsearch", new RectangleF(gx, y, gw, g.S(36)), search, "Search " + all.Count + " widgets…", s => { search = s; Scroll = 0; }, null, false, Icons.Search);
            float gy = y + g.S(50);
            var q = (search ?? "").Trim();
            var items = all.Where(m => (cat == "All" || m.Category == cat) &&
                (q.Length == 0 || m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (m.Description ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || m.Category.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || m.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (items.Count == 0)
            {
                var r = new RectangleF(gx, gy, gw, g.S(120)); g.Card(r);
                Ui.Text(g.G, all.Count == 0 ? "No widgets are part of this build yet." : "No widgets match “" + q + "”.", g.F(13.5f), P.Dim, r, 0);
                return Math.Max(ly, r.Bottom) + g.S(10);
            }
            int cols = Math.Max(1, (int)((gw + g.S(12)) / (g.S(232) + g.S(12))));
            float cw = (gw - g.S(12) * (cols - 1)) / cols, ch = g.S(206);
            for (int i = 0; i < items.Count; i++)
            {
                var r = new RectangleF(gx + (i % cols) * (cw + g.S(12)), gy + (i / cols) * (ch + g.S(12)), cw, ch);
                if (g.Visible(r.Y, r.Height)) GalleryCard(g, items[i], r);
            }
            float end = gy + ((items.Count + cols - 1) / cols) * (ch + g.S(12));
            return Math.Max(ly, end) + g.S(4);
        }

        void GalleryCard(SetG g, WidgetAttribute m, RectangleF r)
        {
            var P = W.P; var T = W.T;
            bool hv = g.Hover(r);
            g.Card(r, hv);
            // preview strip
            var pv = new RectangleF(r.X + g.S(6), r.Y + g.S(6), r.Width - g.S(12), g.S(78));
            var st = g.G.Save();
            using (var clip = Ui.Round(pv, g.S(6))) g.G.SetClip(clip, CombineMode.Intersect);
            if (T.IsGlass) SetPreview.Wallpaper(g.G, pv, SetPreview.WallFor(T));
            else g.G.FillRectangle(Ui.B(T.Dark ? Color.FromArgb(255, 10, 10, 13) : Color.FromArgb(255, 236, 236, 240)), pv);
            SetPreview.FillBarBg(g.G, T, pv, 0);
            string key = "g:" + m.Id; keys.Add(key);
            var wd = W.Prev.Get(key, m.Id, null, true);
            if (wd != null)
            {
                float sc = g.Sc; int tw = W.Prev.Measure(g.G, T, wd, sc);
                float maxW = pv.Width - g.S(16);
                if (tw > maxW) { sc *= maxW / tw; tw = W.Prev.Measure(g.G, T, wd, sc); }
                int th = (int)Math.Round(34 * sc);
                W.Prev.DrawTile(g.G, T, wd, new Rectangle((int)(pv.X + (pv.Width - tw) / 2), (int)(pv.Y + (pv.Height - th) / 2), tw, th), sc);
            }
            g.G.Restore(st);
            float ty = pv.Bottom + g.S(10);
            Ui.Text(g.G, m.Name, g.FS(14), P.Text, new RectangleF(r.X + g.S(14), ty, r.Width - g.S(28), g.S(20)));
            Ui.TextWrap(g.G, m.Description, g.F(12), P.Dim, new RectangleF(r.X + g.S(14), ty + g.S(22), r.Width - g.S(28), g.S(34)));
            float by = r.Bottom - g.S(26);
            Data(g, m, r.X + g.S(14), by);
            int count = Cfg.Widgets.Count(i => i.Id == m.Id);
            if (count > 0) Ui.Text(g.G, "On bar" + (count > 1 ? " ×" + count : ""), g.F(11.5f), P.Faint, new RectangleF(r.X + g.S(14), by - g.S(10), r.Width - g.S(110), g.S(20)), 1);
            var ar = new RectangleF(r.Right - g.S(84), by - g.S(16), g.S(72), g.S(32));
            var mm = m;
            g.Button(ar, "Add", () =>
            {
                var scr = SetScreens();
                if (scr.Count < 2) { AddOn(mm, null); return; }
                var opts = new List<string> { "All monitors" };
                for (int k = 0; k < scr.Count; k++) opts.Add("Display " + (k + 1) + (scr[k].Primary ? " · primary" : "") + "  " + scr[k].Bounds.Width + "×" + scr[k].Bounds.Height);
                W.ShowMenu(ar, opts.ToArray(), -1, i => AddOn(mm, i == 0 ? null : scr[i - 1].DeviceName));
            }, true, Icons.Add);
        }
    }
}
