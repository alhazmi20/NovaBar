// Themed right-click menu for the bar. Created on demand and disposed when closed.
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    static class BarMenu
    {
        public static void Show(BarForm bar, Point screenPt)
        {
            var cfg = AppConfig.Current; var t = bar.T;
            float s = bar.M.Dpi; // dpi only
            var cms = new ContextMenuStrip { Renderer = new BarMenuRenderer(t), ShowImageMargin = false, ShowCheckMargin = true };
            cms.Font = Ui.Font(13f * s);
            cms.Padding = new Padding(2, 4, 2, 4);

            Action<Action> apply = a => { a(); BarForm.SaveConfig(); BarApp.ApplyAll(false); };
            cms.Items.Add(Item("Widgets…", () => bar.OpenSettings("widgets")));
            cms.Items.Add(Item("Settings", () => bar.OpenSettings(null)));
            cms.Items.Add(new ToolStripSeparator());

            var theme = Sub("Theme", cms.Font);
            foreach (var id in Theme.Ids) { var tid = id; theme.DropDownItems.Add(Check(Theme.NameOf(id), cfg.Theme == id, () => apply(() => AppConfig.Current.Theme = tid))); }
            cms.Items.Add(theme);
            var pos = Sub("Position", cms.Font);
            {
                string dev = bar.Scr.DeviceName, mine = bar.Pos;
                object ov; bool own = cfg.Extra != null && cfg.Extra.TryGetValue("position." + dev, out ov) && J.AsStr(ov).Length > 0;
                pos.DropDownItems.Add(new ToolStripMenuItem("This display") { Enabled = false });
                foreach (var pv in new[] { "top", "bottom", "left", "right" })
                {
                    var v = pv; string label = v == "top" ? "Top" : v == "bottom" ? "Bottom" : v == "left" ? "Left (vertical)" : "Right (vertical)";
                    pos.DropDownItems.Add(Check(label, mine == v, () => apply(() => AppConfig.Current.SetPositionFor(dev, v))));
                }
                if (own) pos.DropDownItems.Add(Item("Use default position", () => apply(() => AppConfig.Current.SetPositionFor(dev, null))));
                pos.DropDownItems.Add(new ToolStripSeparator());
                pos.DropDownItems.Add(new ToolStripMenuItem("Default (all displays)") { Enabled = false });
                foreach (var pv in new[] { "top", "bottom", "left", "right" })
                {
                    var v = pv; string label = v == "top" ? "Top" : v == "bottom" ? "Bottom" : v == "left" ? "Left (vertical)" : "Right (vertical)";
                    pos.DropDownItems.Add(Check(label, (cfg.Position ?? "bottom") == v, () => apply(() => AppConfig.Current.Position = v)));
                }
            }
            cms.Items.Add(pos);
            var style = Sub("Style", cms.Font);
            style.DropDownItems.Add(Check("Pill (floating)", cfg.Style != "full" && cfg.Style != "shell", () => apply(() => AppConfig.Current.Style = "pill")));
            style.DropDownItems.Add(Check("Full width", cfg.Style == "full", () => apply(() => AppConfig.Current.Style = "full")));
            style.DropDownItems.Add(Check("Shell (attached, panels slide out)", cfg.Style == "shell", () => apply(() => AppConfig.Current.Style = "shell")));
            cms.Items.Add(style);
            var size = Sub("Size", cms.Font);
            {
                string dev = bar.Scr.DeviceName, mine = bar.MySize;
                object ov; bool own = cfg.Extra != null && cfg.Extra.TryGetValue("size." + dev, out ov) && J.AsStr(ov).Length > 0;
                size.DropDownItems.Add(new ToolStripMenuItem("This display") { Enabled = false });
                foreach (var sz in new[] { "S", "M", "L" }) { var v = sz; size.DropDownItems.Add(Check(sz == "S" ? "Small" : sz == "M" ? "Medium" : "Large", string.Equals(mine, sz, StringComparison.OrdinalIgnoreCase), () => apply(() => AppConfig.Current.SetSizeFor(dev, v)))); }
                if (own) size.DropDownItems.Add(Item("Use default size", () => apply(() => AppConfig.Current.SetSizeFor(dev, null))));
                size.DropDownItems.Add(new ToolStripSeparator());
                size.DropDownItems.Add(new ToolStripMenuItem("Default (all displays)") { Enabled = false });
                foreach (var sz in new[] { "S", "M", "L" }) { var v = sz; size.DropDownItems.Add(Check(sz == "S" ? "Small" : sz == "M" ? "Medium" : "Large", string.Equals(cfg.Size, sz, StringComparison.OrdinalIgnoreCase), () => apply(() => AppConfig.Current.Size = v))); }
            }
            cms.Items.Add(size);
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add(Item("Reload NovaBar", () => { Relaunch.BarFromBar(); Application.Exit(); }));
            cms.Items.Add(Item("Exit NovaBar", () => Application.Exit()));

            foreach (ToolStripItem it in cms.Items)
            {
                var dd = it as ToolStripMenuItem;
                if (dd != null && dd.HasDropDownItems)
                {
                    dd.DropDown.Renderer = cms.Renderer; dd.DropDown.Font = cms.Font; dd.DropDown.Padding = cms.Padding;
                    ((ToolStripDropDownMenu)dd.DropDown).ShowImageMargin = false; ((ToolStripDropDownMenu)dd.DropDown).ShowCheckMargin = true;
                    dd.DropDown.Opened += (o, e) => Round(((ToolStripDropDown)o).Handle);
                }
            }
            cms.Opened += (o, e) => Round(cms.Handle);
            cms.Closed += (o, e) => bar.BeginInvoke((Action)(() => { cms.Dispose(); Native.TrimMemory(); }));
            cms.Show(screenPt, bar.Vertical ? (bar.LeftEdge ? ToolStripDropDownDirection.BelowRight : ToolStripDropDownDirection.BelowLeft) : bar.IsTop ? ToolStripDropDownDirection.BelowRight : ToolStripDropDownDirection.AboveRight);
        }

        /// <summary>Standard right-click menu for a widget tile.</summary>
        public static void ShowWidget(BarForm bar, BarTile tile, Point screenPt)
        {
            var cfg = AppConfig.Current; var t = bar.T; var w = tile.W; var inst = tile.Inst;
            float s = bar.M.Dpi;
            var cms = new ContextMenuStrip { Renderer = new BarMenuRenderer(t), ShowImageMargin = false, ShowCheckMargin = true };
            cms.Font = Ui.Font(13f * s); cms.Padding = new Padding(2, 4, 2, 4);
            Action<Action> apply = a => { a(); BarForm.SaveConfig(); BarApp.ApplyAll(false); };

            var head = new ToolStripMenuItem(w != null && w.Meta != null ? w.Meta.Name : inst.Id) { Enabled = false };
            cms.Items.Add(head);
            var extra = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Action>>();
            if (w != null) { try { w.AddMenuItems(extra); } catch { } }
            foreach (var kv in extra) { var a = kv.Value; cms.Items.Add(Item(kv.Key, () => { try { a(); } catch { } })); }
            bool hasPanel = false; try { hasPanel = w != null && w.PanelSize(1f) != Size.Empty; } catch { }
            if (hasPanel) cms.Items.Add(Item("Open details", () => { try { bar.OpenPanel(w); } catch { } }));
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add(Item("Widget settings…", () => bar.OpenSettings("widgets:" + inst.Inst)));

            var move = Sub("Move", cms.Font);
            string myDev = bar.Scr.DeviceName;
            Action<int> shift = mode => apply(() =>
            {
                var l = AppConfig.Current.Widgets;
                var vis = l.Where(x => x.ShowsOn(myDev)).ToList();              // order as seen on this monitor
                int vi = vis.IndexOf(inst); if (vi < 0) return;
                vis.RemoveAt(vi);
                int nv = mode == -2 ? 0 : mode == 2 ? vis.Count : Math.Max(0, Math.Min(vis.Count, vi + mode));
                vis.Insert(nv, inst); inst.Zone = "center";
                int k = 0;                                                     // write back into the same slots
                for (int i = 0; i < l.Count; i++) if (l[i].ShowsOn(myDev)) l[i] = vis[k++];
            });
            var visNow = AppConfig.Current.Widgets.Where(x => x.ShowsOn(myDev)).ToList();
            int pos = visNow.IndexOf(inst), cnt = visNow.Count;
            var ml = Item("Move left", () => shift(-1)); ml.Enabled = pos > 0; move.DropDownItems.Add(ml);
            var mr = Item("Move right", () => shift(1)); mr.Enabled = pos >= 0 && pos < cnt - 1; move.DropDownItems.Add(mr);
            move.DropDownItems.Add(new ToolStripSeparator());
            var ms = Item("Move to start", () => shift(-2)); ms.Enabled = pos > 0; move.DropDownItems.Add(ms);
            var me = Item("Move to end", () => shift(2)); me.Enabled = pos >= 0 && pos < cnt - 1; move.DropDownItems.Add(me);
            cms.Items.Add(move);

            var scrs = new System.Collections.Generic.List<Screen> { Screen.PrimaryScreen };
            foreach (var sc in Screen.AllScreens) if (!sc.Primary) scrs.Add(sc);
            if (scrs.Count > 1)
            {
                if (inst.Monitors == null) inst.Monitors = new System.Collections.Generic.List<string>();
                var show = Sub("Show on", cms.Font);
                show.DropDownItems.Add(Check("All monitors", inst.Monitors.Count == 0, () => apply(() => inst.Monitors.Clear())));
                string mine = bar.Scr.DeviceName;
                show.DropDownItems.Add(Check("This monitor only", inst.Monitors.Count == 1 && string.Equals(inst.Monitors[0], mine, StringComparison.OrdinalIgnoreCase),
                    () => apply(() => { inst.Monitors.Clear(); inst.Monitors.Add(mine); })));
                show.DropDownItems.Add(new ToolStripSeparator());
                for (int i = 0; i < scrs.Count; i++)
                {
                    var dev = scrs[i].DeviceName;
                    bool on = inst.Monitors.Count == 0 || inst.Monitors.Exists(m => string.Equals(m, dev, StringComparison.OrdinalIgnoreCase));
                    string label = "Display " + (i + 1) + (scrs[i].Primary ? " (primary)" : "") + (dev == mine ? " · this one" : "");
                    int n = scrs.Count;
                    show.DropDownItems.Add(Check(label, on, () => apply(() =>
                    {
                        var mons = inst.Monitors;
                        if (mons.Count == 0) { foreach (var sc in scrs) mons.Add(sc.DeviceName); }   // expand "all" to explicit list
                        if (on) mons.RemoveAll(m => string.Equals(m, dev, StringComparison.OrdinalIgnoreCase)); else mons.Add(dev);
                        if (mons.Count == 0 || mons.Count >= n) mons.Clear();                          // none or all → all
                    })));
                }
                cms.Items.Add(show);
            }
            cms.Items.Add(Item("Remove from bar", () => apply(() => AppConfig.Current.Widgets.Remove(inst))));
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add(Item("Add widgets…", () => bar.OpenSettings("widgets")));
            cms.Items.Add(Item("NovaBar settings", () => bar.OpenSettings(null)));
            cms.Items.Add(Item("Reload NovaBar", () => { Relaunch.BarFromBar(); Application.Exit(); }));

            foreach (ToolStripItem it in cms.Items)
            {
                var dd = it as ToolStripMenuItem;
                if (dd != null && dd.HasDropDownItems)
                {
                    dd.DropDown.Renderer = cms.Renderer; dd.DropDown.Font = cms.Font; dd.DropDown.Padding = cms.Padding;
                    ((ToolStripDropDownMenu)dd.DropDown).ShowImageMargin = false; ((ToolStripDropDownMenu)dd.DropDown).ShowCheckMargin = true;
                    dd.DropDown.Opened += (o, e) => Round(((ToolStripDropDown)o).Handle);
                }
            }
            cms.Opened += (o, e) => Round(cms.Handle);
            cms.Closed += (o, e) => bar.BeginInvoke((Action)(() => { cms.Dispose(); Native.TrimMemory(); }));
            cms.Show(screenPt, bar.Vertical ? (bar.LeftEdge ? ToolStripDropDownDirection.BelowRight : ToolStripDropDownDirection.BelowLeft) : bar.IsTop ? ToolStripDropDownDirection.BelowRight : ToolStripDropDownDirection.AboveRight);
        }

        static void Round(IntPtr h) { Native.SetDwm(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND); }
        static ToolStripMenuItem Item(string text, Action a) { var i = new ToolStripMenuItem(text); i.Click += (o, e) => a(); return i; }
        static ToolStripMenuItem Check(string text, bool on, Action a) { var i = Item(text, a); i.Checked = on; return i; }
        static ToolStripMenuItem Sub(string text, Font f) { return new ToolStripMenuItem(text) { Font = f }; }
    }

    class BarMenuRenderer : ToolStripProfessionalRenderer
    {
        readonly Theme t; readonly Color bg, hover, border, text, dim;
        public BarMenuRenderer(Theme t)
        {
            this.t = t;
            // menus are opaque (no glass): derive solid colours from the theme
            switch (t.Id)
            {
                case "white": case "glasslight": bg = Color.FromArgb(252, 252, 253); hover = Color.FromArgb(236, 236, 241); break;
                case "midnight": bg = Color.FromArgb(14, 18, 40); hover = Color.FromArgb(32, 40, 78); break;
                default: bg = Color.FromArgb(28, 28, 33); hover = Color.FromArgb(46, 46, 54); break;
            }
            border = t.Dark ? Color.FromArgb(60, 60, 70) : Color.FromArgb(220, 220, 226);
            text = t.Text; dim = t.TextDim;
            RoundedEdges = false;
        }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(bg); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Ui.FillRound(g, new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 5, hover);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? text : dim;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = dim; base.OnRenderArrow(e); }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(border)) e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Ui.Icon(e.Graphics, Icons.Check, new RectangleF(r.X, r.Y, r.Width, r.Height), t.Accent, Math.Max(10, r.Height * 0.8f));
        }
    }
}
