// Notification Center widget: bell tile (unread badge, DND moon) + panel grouped by app.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    [Widget("notifcenter", "Notifications", "System", "NovaBar's own notification center: banners that match your theme, history grouped by app, Do Not Disturb.", RefName = "Notifications", Data = DataKind.Live)]
    public class NtfCenterWidget : Widget
    {
        static NtfCenterWidget() { try { NtfService.Boot(); } catch (Exception ex) { NtfNative.Log("static boot", ex); } }

        bool panelOpen; float scroll; float contentH, viewH;
        readonly HashSet<string> expanded = new HashSet<string>();

        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Toggle("badge", "Show unread count", true) }; } }
        public override int RefreshMs { get { return panelOpen ? 30000 : 0; } }   // relative times while the panel is open

        public override void OnStart()
        {
            NtfService.Boot();
            NtfService.Subscribe(this);
            NtfService.AddRef();
            NtfPreview.MaybeRender();
        }
        public override void OnStop() { NtfService.Unsubscribe(this); NtfService.Release(); }

        // ---------------- tile ----------------
        public override int Measure(DrawCtx c) { return c.Si(40); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var t = c.T;
            bool dnd = NtfService.Dnd;
            var ir = new RectangleF(b.X + (b.Width - c.S(20)) / 2f, b.Y + (b.Height - c.S(20)) / 2f, c.S(20), c.S(20));
            Ui.Icon(c.G, Icons.Bell, ir, dnd ? t.TextDim : t.Text, c.S(15));
            if (dnd)
            {
                var mr = new RectangleF(ir.Right - c.S(9), ir.Bottom - c.S(10), c.S(14), c.S(14));
                Ui.FillCircle(c.G, new PointF(mr.X + mr.Width / 2, mr.Y + mr.Height / 2), c.S(7.5f), t.IsGlass ? Ui.Mix(t.Tile, t.Dark ? Color.Black : Color.White, 0.5) : t.Tile);
                Ui.Icon(c.G, Icons.Moon, mr, t.Accent, c.S(10.5f));
            }
            int n = NtfService.Unread;
            if (n > 0 && SBool("badge"))
            {
                var txt = n > 99 ? "99+" : n.ToString();
                var f = Ui.FontSemi(c.S(9.5f));
                float w = Math.Max(c.S(15), Ui.Measure(c.G, txt, f).Width + c.S(8)), h = c.S(15);
                var br = new RectangleF(Math.Min(b.Right - w - c.S(2), ir.Right - c.S(6)), ir.Y - c.S(5), w, h);
                Ui.FillRound(c.G, Ui.Inset(br, -c.S(1.5f), -c.S(1.5f)), h / 2 + c.S(1.5f), t.Dark || t.IsGlass ? Color.FromArgb(255, 20, 20, 24) : Color.White);
                Ui.FillRound(c.G, br, h / 2, dnd ? t.TextFaint : t.Accent);
                Ui.Text(c.G, txt, f, Color.White, br, 0);
            }
        }
        public override void OnClick(MouseButtons btn)
        {
            if (btn == MouseButtons.Middle) { NtfService.SetDnd(!NtfService.Dnd); Invalidate(); return; }
            base.OnClick(btn);
        }

        // ---------------- panel ----------------
        public override Size PanelSize(float s) { return new Size((int)(372 * s), (int)(520 * s)); }
        public override void OnPanelOpen() { panelOpen = true; scroll = 0; NtfService.MarkAllRead(); }
        public override void OnPanelClose() { panelOpen = false; expanded.Clear(); NtfService.MarkAllRead(); }

        class Group { public string App; public List<NtfItem> Items; }

        public override void DrawPanel(DrawCtx c)
        {
            var g = c.G; var t = c.T; var b = c.Bounds;
            float x = b.X + c.S(16), right = b.Right - c.S(16), y = b.Y + c.S(14);
            var all = NtfService.Items;
            bool dnd = NtfService.Dnd;

            // header: title + DND + clear all
            Ui.Text(g, "Notifications", c.FS(15), t.Text, new RectangleF(x, y, c.S(200), c.S(22)));
            float bx = right;
            if (all.Length > 0)
            {
                var cf = c.FS(12);
                float cw = Ui.Measure(g, "Clear all", cf).Width + c.S(18);
                var cr = new RectangleF(bx - cw, y - c.S(2), cw, c.S(26));
                if (c.IsHover(cr)) Ui.FillRound(g, cr, cr.Height / 2, t.TileHover);
                Ui.Text(g, "Clear all", cf, t.Accent, cr, 0);
                c.Hit(cr, () => { NtfService.ClearAll(); scroll = 0; Invalidate(); });
                bx = cr.X - c.S(6);
            }
            var dr = new RectangleF(bx - c.S(26), y - c.S(2), c.S(26), c.S(26));
            c.IconButton(dr, Icons.Moon, () => { NtfService.SetDnd(!NtfService.Dnd); Invalidate(); }, dnd, 12.5f);
            y += c.S(34);

            // status strips
            if (dnd) y = Strip(c, y, Icons.Moon, "Do not disturb is on", "Banners are silenced, notifications collect here.", null, null);
            if (NtfService.WindowsToastsOff && !NtfService.Demo)
                y = Strip(c, y, Icons.Warning, "Windows notifications are off", "Turn them on so NovaBar can receive them.", "Turn on", () => Ctl("ms-settings:notifications"));
            else if (!string.IsNullOrEmpty(NtfService.Status) && !NtfService.Demo)
                y = Strip(c, y, Icons.Warning, "Can't read notifications", NtfService.Status, "Settings", () => Ctl("ms-settings:privacy-notifications"));

            // footer
            float footH = c.S(44);
            var foot = new RectangleF(b.X, b.Bottom - footH, b.Width, footH);
            using (var p = new Pen(t.TileBorder.A > 0 ? t.TileBorder : t.Track, 1f)) g.DrawLine(p, b.X + c.S(12), foot.Y, b.Right - c.S(12), foot.Y);
            var lr = new RectangleF(x - c.S(8), foot.Y + c.S(8), c.S(190), footH - c.S(16));
            bool lh = c.IsHover(lr);
            if (lh) Ui.FillRound(g, lr, lr.Height / 2, t.TileHover);
            Ui.Icon(g, Icons.Settings, new RectangleF(lr.X + c.S(8), lr.Y, c.S(16), lr.Height), t.TextDim, c.S(12.5f));
            Ui.Text(g, "Notification settings", c.F(12.5f), lh ? t.Text : t.TextDim, new RectangleF(lr.X + c.S(30), lr.Y, lr.Width - c.S(30), lr.Height));
            c.Hit(lr, () => Ctl("ms-settings:notifications"));
            if (all.Length > 0)
                Ui.Text(g, all.Length + (all.Length == 1 ? " notification" : " notifications"), c.F(11.5f), t.TextFaint, new RectangleF(right - c.S(150), foot.Y, c.S(150), footH), 1);

            // list
            var list = new RectangleF(b.X + c.S(10), y, b.Width - c.S(20), foot.Y - y - c.S(6));
            viewH = list.Height;
            if (all.Length == 0) { Empty(c, list); return; }
            var groups = all.GroupBy(i => i.App ?? "").Select(gr => new Group { App = gr.Key, Items = gr.OrderByDescending(i => i.Time).ToList() })
                .OrderByDescending(gr => gr.Items[0].Time).ToList();

            float maxScroll = Math.Max(0, contentH - viewH);
            if (scroll > maxScroll) scroll = maxScroll; if (scroll < 0) scroll = 0;
            var oldClip = g.Clip;
            g.SetClip(list);
            float cy = list.Y - scroll + c.S(2);
            float cx = list.X + c.S(6), cwid = list.Width - c.S(12);
            foreach (var gr in groups)
            {
                // group header
                var hr = new RectangleF(cx, cy, cwid, c.S(26));
                var first = gr.Items[0];
                NtfIcons.Draw(g, first, new RectangleF(hr.X + c.S(2), hr.Y + c.S(5), c.S(16), c.S(16)), t);
                Ui.Text(g, gr.App, c.FS(12.5f), t.Text, new RectangleF(hr.X + c.S(26), hr.Y, hr.Width - c.S(80), hr.Height));
                var xr = new RectangleF(hr.Right - c.S(24), hr.Y + c.S(1), c.S(24), c.S(24));
                if (Visible(list, xr))
                {
                    bool xh = c.IsHover(xr);
                    if (xh) Ui.FillRound(g, xr, xr.Width / 2, t.TileHover);
                    Ui.Icon(g, "", xr, xh ? t.Text : t.TextFaint, c.S(9.5f));
                    var app = gr.App; c.Hit(Clip(list, xr), () => { NtfService.DismissApp(app); Invalidate(); });
                }
                cy += c.S(30);
                bool exp = expanded.Contains(gr.App);
                int show = exp ? gr.Items.Count : Math.Min(3, gr.Items.Count);
                for (int i = 0; i < show; i++) cy = Card(c, list, gr.Items[i], cx, cy, cwid) + c.S(6);
                if (gr.Items.Count > 3)
                {
                    var mr = new RectangleF(cx, cy - c.S(2), cwid, c.S(22));
                    string lbl = exp ? "Show less" : "+" + (gr.Items.Count - 3) + " more from " + gr.App;
                    if (Visible(list, mr))
                    {
                        Ui.Text(g, lbl, c.F(11.5f), c.IsHover(mr) ? t.Text : t.Accent, Ui.Inset(mr, c.S(6), 0));
                        var app = gr.App; c.Hit(Clip(list, mr), () => { if (!expanded.Remove(app)) expanded.Add(app); Invalidate(); });
                    }
                    cy += c.S(22);
                }
                cy += c.S(8);
            }
            contentH = cy + scroll - list.Y;
            g.Clip = oldClip; oldClip.Dispose();
            // scrollbar
            if (contentH > viewH + 1)
            {
                float th = Math.Max(c.S(24), viewH * viewH / contentH), ty = list.Y + (viewH - th) * (scroll / Math.Max(1, contentH - viewH));
                Ui.FillRound(g, new RectangleF(b.Right - c.S(6), ty, c.S(3), th), c.S(1.5f), t.Track);
            }
            c.Wheel(Rectangle.Round(list), d => { scroll -= d * c.S(64); Invalidate(); });
        }

        float Card(DrawCtx c, RectangleF list, NtfItem it, float x, float y, float w)
        {
            var g = c.G; var t = c.T;
            float pad = c.S(12);
            var bf = c.F(12);
            float lh = bf.GetHeight(g);
            float bodyH = 0;
            if (!string.IsNullOrEmpty(it.Body))
            {
                var sz = g.MeasureString(it.Body, bf, (int)(w - 2 * pad), StringFormat.GenericTypographic);
                bodyH = Math.Max(1, Math.Min(2, (int)Math.Ceiling(sz.Height / lh - 0.05))) * lh;
            }
            float h = pad + c.S(19) + (bodyH > 0 ? c.S(2) + bodyH : 0) + pad;
            var r = new RectangleF(x, y, w, h);
            if (!Visible(list, r)) return y + h;
            bool hov = c.IsHover(r) && list.Contains(c.Mouse);
            Ui.FillRound(g, r, c.S(10), hov ? t.TileHover : t.Tile);
            if (t.TileBorder.A > 0) Ui.StrokeRound(g, r, c.S(10), t.TileBorder);
            float tx = r.X + pad;
            if (!it.Read) { Ui.FillCircle(g, new PointF(r.X + c.S(7), r.Y + pad + c.S(9.5f)), c.S(3), t.Accent); }
            var xr = new RectangleF(r.Right - c.S(30), r.Y + c.S(7), c.S(24), c.S(24));
            string time = NtfItem.Ago(it.Time);
            var tf = c.F(11);
            float tw = Ui.Measure(g, time, tf).Width + 2;
            float timeRight = hov ? xr.X : r.Right - pad;
            Ui.Text(g, time, tf, t.TextFaint, new RectangleF(timeRight - tw, r.Y + pad, tw, c.S(19)), 1);
            Ui.Text(g, it.Title, c.FS(13), t.Text, new RectangleF(tx, r.Y + pad, timeRight - tw - c.S(8) - tx, c.S(19)));
            if (bodyH > 0) Ui.TextWrap(g, it.Body, bf, t.TextDim, new RectangleF(tx, r.Y + pad + c.S(21), r.Right - pad - tx, bodyH + 1));
            var item = it;
            c.Hit(Clip(list, r), () => { NtfService.Activate(item); Invalidate(); });
            if (hov)
            {
                bool xh = c.IsHover(xr);
                if (xh) Ui.FillRound(g, xr, xr.Width / 2, t.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0));
                Ui.Icon(g, "", xr, xh ? t.Text : t.TextDim, c.S(9.5f));
                c.Hit(Clip(list, xr), () => { NtfService.Dismiss(item); Invalidate(); });
            }
            return y + h;
        }

        float Strip(DrawCtx c, float y, string icon, string title, string sub, string btn, Action onBtn)
        {
            var g = c.G; var t = c.T; var b = c.Bounds;
            var r = new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(46));
            Ui.FillRound(g, r, c.S(10), t.AccentA(t.Dark ? 36 : 26));
            Ui.Icon(g, icon, new RectangleF(r.X + c.S(10), r.Y, c.S(18), r.Height), t.Accent, c.S(14));
            float bw = 0;
            if (btn != null)
            {
                bw = Ui.Measure(g, btn, c.FS(12)).Width + c.S(22);
                c.Button(new RectangleF(r.Right - bw - c.S(8), r.Y + c.S(9), bw, c.S(28)), btn, onBtn, true);
                bw += c.S(10);
            }
            Ui.Text(g, title, c.FS(12.5f), t.Text, new RectangleF(r.X + c.S(36), r.Y + c.S(6), r.Width - c.S(44) - bw, c.S(18)));
            Ui.Text(g, sub, c.F(11), t.TextDim, new RectangleF(r.X + c.S(36), r.Y + c.S(24), r.Width - c.S(44) - bw, c.S(16)));
            return y + r.Height + c.S(10);
        }

        void Empty(DrawCtx c, RectangleF list)
        {
            var g = c.G; var t = c.T;
            float cy = list.Y + list.Height * 0.38f;
            var cr = new RectangleF(list.X + list.Width / 2 - c.S(30), cy - c.S(30), c.S(60), c.S(60));
            Ui.FillCircle(g, new PointF(cr.X + cr.Width / 2, cr.Y + cr.Height / 2), c.S(30), t.AccentA(t.Dark ? 40 : 28));
            Ui.Icon(g, Icons.Bell, cr, t.Accent, c.S(24));
            var ck = new RectangleF(cr.Right - c.S(20), cr.Bottom - c.S(20), c.S(20), c.S(20));
            Ui.FillCircle(g, new PointF(ck.X + ck.Width / 2, ck.Y + ck.Height / 2), c.S(10), t.Good);
            Ui.Icon(g, Icons.Check, ck, Color.White, c.S(10));
            Ui.Text(g, "You're all caught up", c.FS(14), t.Text, new RectangleF(list.X, cr.Bottom + c.S(14), list.Width, c.S(22)), 0);
            Ui.Text(g, "New notifications will show up here.", c.F(12), t.TextDim, new RectangleF(list.X, cr.Bottom + c.S(38), list.Width, c.S(18)), 0);
        }

        static bool Visible(RectangleF list, RectangleF r) { return r.Bottom > list.Y && r.Y < list.Bottom; }
        static Rectangle Clip(RectangleF list, RectangleF r) { return Rectangle.Round(RectangleF.Intersect(list, r)); }
        static void Ctl(string target)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch { } });
        }
    }

    /// <summary>Legacy id "notifications" (old configs / default layout) → same widget. Category "Hidden" so galleries can skip it.</summary>
    [Widget("notifications", "Notifications", "Hidden", "Alias of the Notification Center (kept so existing configs keep working).", RefName = "Notifications (alias)", Data = DataKind.Live)]
    public class NtfCenterAliasWidget : NtfCenterWidget { }
}
