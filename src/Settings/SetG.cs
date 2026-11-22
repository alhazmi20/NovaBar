// Settings app: immediate-mode drawing context. Pages repaint everything each frame and register hit regions,
// so the whole Settings window is ONE control (plus a transient TextBox while editing). Very light.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    class SetHit
    {
        public RectangleF R; public Action Click; public Action<Point> Down; public bool Hand = true;
    }

    /// <summary>Colors for the Settings window derived from a bar theme.</summary>
    class SetPal
    {
        public Theme T;
        public Color Win, Side, Card, CardHover, CardBorder, Text, Dim, Faint, Accent, Input, InputBorder, Sel, OnAccent, Sep;
        public static SetPal From(Theme t)
        {
            var p = new SetPal { T = t, Text = t.Text, Dim = t.TextDim, Faint = t.TextFaint, Accent = t.Accent, Input = t.Input };
            double lum = (0.2126 * t.Accent.R + 0.7152 * t.Accent.G + 0.0722 * t.Accent.B) / 255.0;
            p.OnAccent = lum > 0.62 ? Color.FromArgb(255, 16, 18, 28) : Color.White;
            switch (t.Id)
            {
                case "white":
                    p.Win = Color.FromArgb(255, 243, 243, 246); p.Side = p.Win; p.Card = Color.FromArgb(255, 255, 255, 255); p.CardHover = Color.FromArgb(255, 249, 249, 251);
                    p.CardBorder = Color.FromArgb(22, 0, 0, 0); p.InputBorder = Color.FromArgb(40, 0, 0, 0); p.Sel = Color.FromArgb(14, 0, 0, 0); p.Sep = Color.FromArgb(18, 0, 0, 0);
                    break;
                case "glass":
                    p.Win = Color.FromArgb(120, 18, 18, 26); p.Side = Color.FromArgb(0, 0, 0, 0); p.Card = Color.FromArgb(26, 255, 255, 255); p.CardHover = Color.FromArgb(40, 255, 255, 255);
                    p.CardBorder = Color.FromArgb(30, 255, 255, 255); p.InputBorder = Color.FromArgb(50, 255, 255, 255); p.Sel = Color.FromArgb(30, 255, 255, 255); p.Sep = Color.FromArgb(26, 255, 255, 255);
                    break;
                case "glasslight":
                    p.Win = Color.FromArgb(150, 246, 246, 250); p.Side = Color.FromArgb(0, 0, 0, 0); p.Card = Color.FromArgb(150, 255, 255, 255); p.CardHover = Color.FromArgb(200, 255, 255, 255);
                    p.CardBorder = Color.FromArgb(120, 255, 255, 255); p.InputBorder = Color.FromArgb(40, 0, 0, 0); p.Sel = Color.FromArgb(150, 255, 255, 255); p.Sep = Color.FromArgb(22, 0, 0, 0);
                    break;
                case "midnight":
                    p.Win = Color.FromArgb(215, 9, 13, 32); p.Side = Color.FromArgb(0, 0, 0, 0); p.Card = Color.FromArgb(34, 120, 140, 255); p.CardHover = Color.FromArgb(52, 120, 140, 255);
                    p.CardBorder = Color.FromArgb(36, 140, 160, 255); p.InputBorder = Color.FromArgb(60, 140, 160, 255); p.Sel = Color.FromArgb(44, 120, 140, 255); p.Sep = Color.FromArgb(30, 140, 160, 255);
                    break;
                default:
                    p.Win = Color.FromArgb(255, 15, 15, 18); p.Side = p.Win; p.Card = Color.FromArgb(255, 27, 27, 32); p.CardHover = Color.FromArgb(255, 33, 33, 39);
                    p.CardBorder = Color.FromArgb(14, 255, 255, 255); p.InputBorder = Color.FromArgb(34, 255, 255, 255); p.Sel = Color.FromArgb(255, 34, 34, 40); p.Sep = Color.FromArgb(16, 255, 255, 255);
                    break;
            }
            return p;
        }
    }

    class SetG
    {
        public Graphics G; public SetPal P; public Theme T; public float Sc = 1f; public Point Mouse = new Point(-9999, -9999); public SetWindow W;
        public RectangleF HitClip = new RectangleF(-1e5f, -1e5f, 2e5f, 2e5f);
        public readonly List<SetHit> Hits = new List<SetHit>();

        public float S(float v) { return v * Sc; }
        public Font F(float px, bool bold = false) { return Ui.Font(S(px), bold); }
        public Font FS(float px) { return Ui.FontSemi(S(px)); }
        public bool Hover(RectangleF r) { return r.Contains(Mouse) && HitClip.Contains(Mouse); }
        public void Hit(RectangleF r, Action click, Action<Point> down = null, bool hand = true)
        {
            var c = RectangleF.Intersect(r, HitClip); if (c.Width <= 0 || c.Height <= 0) return;
            Hits.Add(new SetHit { R = c, Click = click, Down = down, Hand = hand });
        }
        public float TextW(string s, Font f) { return Ui.Measure(G, s, f).Width; }
        public bool Visible(float y, float h) { return y + h >= HitClip.Top - S(40) && y <= HitClip.Bottom + S(40); }

        // ---------- building blocks ----------
        public void Card(RectangleF r, bool hover = false, float rad = 8)
        {
            Ui.FillRound(G, r, S(rad), hover ? P.CardHover : P.Card);
            Ui.StrokeRound(G, r, S(rad), P.CardBorder, 1f);
        }

        /// <summary>Section heading above a group of rows.</summary>
        public float Section(float x, float y, float w, string title)
        {
            Ui.Text(G, title, FS(14), P.Text, new RectangleF(x + S(2), y, w, S(22)));
            return y + S(30);
        }

        /// <summary>Windows 11 style setting row (own card). Returns the area to draw the control in (right side).</summary>
        public RectangleF Row(ref float y, float x, float w, string icon, string title, string desc, float h = 0, Action click = null)
        {
            if (h <= 0) h = desc != null ? S(68) : S(56);
            var r = new RectangleF(x, y, w, h);
            if (Visible(y, h))
            {
                bool hv = click != null && Hover(r);
                Card(r, hv);
                float tx = x + S(18);
                if (icon != null) { Ui.Icon(G, icon, new RectangleF(tx, y + (Math.Min(h, S(68)) - S(20)) / 2, S(20), S(20)), P.Text, S(16)); tx += S(38); }
                float top = desc != null ? y + Math.Min(h, S(68)) / 2 - S(19) : y + Math.Min(h, S(56)) / 2 - S(10);
                Ui.Text(G, title, F(14), P.Text, new RectangleF(tx, top, w * 0.55f, S(20)));
                if (desc != null) Ui.Text(G, desc, F(12), P.Dim, new RectangleF(tx, top + S(20), w - (tx - x) - S(260), S(18)));
                if (click != null) Hit(r, click);
            }
            y += h + S(4);
            return new RectangleF(x + S(16), r.Y, w - S(34), Math.Min(h, desc != null ? S(68) : S(56)));
        }

        public void Toggle(RectangleF area, bool on, Action<bool> set, bool label = true)
        {
            float tw = S(40), th = S(20);
            var r = new RectangleF(area.Right - tw, area.Y + (area.Height - th) / 2, tw, th);
            bool hv = Hover(RectangleF.Inflate(r, S(50), S(6)));
            if (on) Ui.FillRound(G, r, th / 2, hv ? Ui.Mix(P.Accent, Color.White, 0.12) : P.Accent);
            else { Ui.FillRound(G, r, th / 2, hv ? P.Sel : Color.FromArgb(0, 0, 0, 0)); Ui.StrokeRound(G, r, th / 2, P.Dim, S(1.2f)); }
            float k = S(hv ? 14 : 12);
            float cx = on ? r.Right - S(10) : r.X + S(10);
            Ui.FillCircle(G, new PointF(cx, r.Y + th / 2), k / 2, on ? P.OnAccent : P.Dim);
            if (label) Ui.Text(G, on ? "On" : "Off", F(13.5f), P.Text, new RectangleF(r.X - S(48), area.Y, S(40), area.Height), 1);
            Hit(new RectangleF(r.X - S(50), r.Y - S(6), tw + S(50), th + S(12)), () => set(!on));
        }

        public float SegWidth(string[] labels, float pad = 16)
        {
            float w = S(4); foreach (var l in labels) w += TextW(l, F(13)) + S(pad * 2); return w;
        }
        /// <summary>Segmented control drawn right-aligned in area (or at area.X when left=true).</summary>
        public RectangleF Segmented(RectangleF area, string[] labels, string[] values, string sel, Action<string> set, bool left = false, float h = 32)
        {
            float w = SegWidth(labels), hh = S(h);
            var r = new RectangleF(left ? area.X : area.Right - w, area.Y + (area.Height - hh) / 2, w, hh);
            Ui.FillRound(G, r, S(8), P.Sel.A < 40 && !P.T.IsGlass ? Ui.Mix(P.Win, P.T.Dark ? Color.White : Color.Black, P.T.Dark ? 0.06 : 0.05) : P.Sel);
            Ui.StrokeRound(G, r, S(8), P.CardBorder);
            float x = r.X + S(2);
            for (int i = 0; i < labels.Length; i++)
            {
                float iw = TextW(labels[i], F(13)) + S(32);
                var ir = new RectangleF(x, r.Y + S(2), iw, hh - S(4));
                bool on = string.Equals(values[i], sel, StringComparison.OrdinalIgnoreCase);
                if (on) { Ui.FillRound(G, ir, S(6.5f), P.Accent); }
                else if (Hover(ir)) Ui.FillRound(G, ir, S(6.5f), P.CardHover);
                Ui.Text(G, labels[i], on ? FS(13) : F(13), on ? P.OnAccent : P.Text, ir, 0);
                var v = values[i]; Hit(ir, () => set(v));
                x += iw;
            }
            return r;
        }

        public float ButtonWidth(string label, string icon = null)
        {
            float w = S(32) + (string.IsNullOrEmpty(label) ? 0 : TextW(label, FS(13)));
            if (icon != null) w += string.IsNullOrEmpty(label) ? S(4) : S(24);
            return w;
        }
        public void Button(RectangleF r, string label, Action click, bool primary = false, string icon = null, bool enabled = true)
        {
            bool hv = enabled && Hover(r);
            Color bg = primary ? (hv ? Ui.Mix(P.Accent, Color.White, 0.14) : P.Accent) : (hv ? P.CardHover : P.Card);
            if (!enabled) bg = Ui.WithA(bg, bg.A / 2);
            Ui.FillRound(G, r, S(6), bg);
            if (!primary) Ui.StrokeRound(G, r, S(6), P.InputBorder);
            Color fg = primary ? P.OnAccent : P.Text; if (!enabled) fg = P.Faint;
            if (icon != null && string.IsNullOrEmpty(label)) Ui.Icon(G, icon, r, fg, S(14));
            else if (icon != null)
            {
                float tw = TextW(label, FS(13)), total = S(22) + tw, x = r.X + (r.Width - total) / 2;
                Ui.Icon(G, icon, new RectangleF(x, r.Y, S(16), r.Height), fg, S(13));
                Ui.Text(G, label, FS(13), fg, new RectangleF(x + S(22), r.Y, tw + S(4), r.Height));
            }
            else Ui.Text(G, label, FS(13), fg, r, 0);
            if (enabled && click != null) Hit(r, click);
        }

        public void Link(RectangleF r, string label, Action click, string icon = Icons.Link)
        {
            bool hv = Hover(r);
            var c = hv ? Ui.Mix(P.Accent, P.Text, 0.25) : P.Accent;
            float x = r.X;
            if (icon != null) { Ui.Icon(G, icon, new RectangleF(x, r.Y, S(14), r.Height), c, S(12)); x += S(19); }
            Ui.Text(G, label, F(13), c, new RectangleF(x, r.Y, r.Right - x, r.Height));
            Hit(r, click);
        }

        /// <summary>Combo box look; opens a themed popup menu.</summary>
        public void Dropdown(RectangleF r, string display, string[] options, int selected, Action<int> pick)
        {
            bool hv = Hover(r);
            Ui.FillRound(G, r, S(6), hv ? P.CardHover : P.Card);
            Ui.StrokeRound(G, r, S(6), P.InputBorder);
            Ui.Text(G, display, F(13.5f), P.Text, new RectangleF(r.X + S(12), r.Y, r.Width - S(40), r.Height));
            Ui.Icon(G, Icons.ChevronDown, new RectangleF(r.Right - S(30), r.Y, S(20), r.Height), P.Dim, S(11));
            var rr = r;
            Hit(r, () => W.ShowMenu(rr, options, selected, pick));
        }

        /// <summary>Text field. Painted; a real TextBox is overlaid only while editing.</summary>
        public void Input(string key, RectangleF r, string value, string placeholder, Action<string> onChange, Action<string> onSubmit = null, bool secret = false, string icon = null)
        {
            bool editing = W != null && W.EditKey == key;
            bool hv = Hover(r);
            Ui.FillRound(G, r, S(6), P.Input);
            Ui.StrokeRound(G, r, S(6), editing ? Ui.WithA(P.Accent, 200) : hv ? Ui.WithA(P.Text, 70) : P.InputBorder);
            if (editing) using (var pen = new Pen(P.Accent, S(2))) G.DrawLine(pen, r.X + S(5), r.Bottom - S(1.5f), r.Right - S(5), r.Bottom - S(1.5f));
            float x = r.X + S(11);
            if (icon != null) { Ui.Icon(G, icon, new RectangleF(x, r.Y, S(16), r.Height), P.Dim, S(13)); x += S(24); }
            var tr = new RectangleF(x, r.Y, r.Right - x - S(10), r.Height);
            if (editing) W.PaintEditor(this, tr);
            else if (string.IsNullOrEmpty(value)) Ui.Text(G, placeholder, F(13.5f), P.Faint, tr);
            else Ui.Text(G, secret ? new string('•', Math.Min(24, value.Length)) : value, F(13.5f), P.Text, tr);
            if (!editing && W != null)
            {
                var k = key; var v = value ?? ""; var sec = secret;
                Hit(r, () => W.BeginEdit(k, v, sec, onChange, onSubmit), null, false);
            }
        }

        public float Badge(float x, float yMid, string text, Color c, bool right = false)
        {
            var f = FS(11); float w = TextW(text, f) + S(14), h = S(19);
            if (right) x -= w;
            var r = new RectangleF(x, yMid - h / 2, w, h);
            Ui.FillRound(G, r, h / 2, Ui.WithA(c, P.T.Dark ? 46 : 34));
            Ui.Text(G, text, f, P.T.Dark ? Ui.Mix(c, Color.White, 0.25) : Ui.Mix(c, Color.Black, 0.2), r, 0);
            return w;
        }

        public void Dot(PointF c, float r, Color col) { Ui.FillCircle(G, c, r, col); }
    }

    /// <summary>Themed renderer for popup menus (dropdowns, "Add to…").</summary>
    class SetMenuRenderer : ToolStripRenderer
    {
        readonly SetPal p;
        public SetMenuRenderer(SetPal pal) { p = pal; }
        Color Bg { get { return p.T.Dark ? Color.FromArgb(255, 36, 36, 42) : Color.FromArgb(255, 252, 252, 253); } }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Bg); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(p.T.Dark ? Color.FromArgb(255, 60, 60, 68) : Color.FromArgb(255, 210, 210, 216))) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !(e.Item is ToolStripMenuItem && ((ToolStripMenuItem)e.Item).Checked)) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            Ui.FillRound(g, r, 4, e.Item.Selected ? (p.T.Dark ? Color.FromArgb(255, 52, 52, 60) : Color.FromArgb(255, 234, 234, 238)) : (p.T.Dark ? Color.FromArgb(255, 44, 44, 52) : Color.FromArgb(255, 242, 242, 245)));
            var mi = e.Item as ToolStripMenuItem;
            if (mi != null && mi.Checked) Ui.FillRound(g, new RectangleF(5, r.Y + r.Height / 2 - 8, 3, 16), 1.5f, p.Accent);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = p.T.Dark ? Color.FromArgb(255, 240, 240, 244) : Color.FromArgb(255, 20, 20, 24);
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(p.T.Dark ? Color.FromArgb(255, 56, 56, 64) : Color.FromArgb(255, 222, 222, 228))) e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
    }
}
