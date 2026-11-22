// Settings app main window: custom-painted sidebar + page area. One control, no child controls.
using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    abstract class SetPage : IDisposable
    {
        public SetWindow W; public float Scroll, ContentH;
        public AppConfig Cfg { get { return W.Cfg; } }
        public abstract string Title { get; }
        public virtual string Subtitle { get { return null; } }
        /// <summary>Paint content starting at (x,y) with width w. Returns the bottom y of the content.</summary>
        public abstract float Paint(SetG g, float x, float y, float w);
        public virtual bool Dragging { get { return false; } }
        public virtual void DragMove(Point p) { }
        public virtual bool DragEnd(Point p) { return false; }
        public virtual void Tick() { }
        public virtual void Dispose() { }
    }

    class SetWindow : Form
    {
        public AppConfig Cfg; public Theme T; public SetPal P; public float Sc = 1f;
        public SetPreview Prev;
        public bool Shot;
        SetPage page; int pageIdx = -1;
        SetDib dib;
        List<SetHit> hits = new List<SetHit>();
        Point mouse = new Point(-9999, -9999); RectangleF downRect; SetHit hoverHit;
        readonly Timer saveTimer, tick;
        volatile bool repaintWanted;
        string toast; int toastUntil;
        RectangleF viewport; float sideW;
        static readonly string[] navNames = { "General", "Appearance", "Dock", "Widgets", "Notifications", "Accounts", "Checklist", "About" };
        static readonly string[] navIcons = { "\uE713", "\uE790", "\uE7F4", "\uECA5", "\uEA8F", "\uE910", "\uE9D5", "\uE946" };

        public SetWindow(string startPage, bool shot)
        {
            Shot = shot;
            Cfg = AppConfig.Load(); AppConfig.Current = Cfg;
            try { var tmp = WidgetRegistry.All.Count(); } catch { } // runs widget static ctors (Accounts.Define)
            try { uint d = Native.GetDpiForSystem(); if (d >= 96) Sc = d / 96f; } catch { using (var g = CreateGraphics()) Sc = g.DpiX / 96f; }
            Prev = new SetPreview(this);
            ApplyTheme();
            Text = "NovaBar Settings";
            FormBorderStyle = FormBorderStyle.Sizable; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None; KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = false;
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min((int)(1140 * Sc), wa.Width - 40), Math.Min((int)(780 * Sc), wa.Height - 40));
            MinimumSize = new Size((int)(980 * Sc), (int)(680 * Sc));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            saveTimer = new Timer { Interval = 350 }; saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(); };
            tick = new Timer { Interval = 250 }; tick.Tick += OnTick; tick.Start();
            WatchConfig();
            Navigate(startPage);
        }

        public void ApplyTheme()
        {
            object st; string sth = Cfg.Extra != null && Cfg.Extra.TryGetValue("settings.theme", out st) && st != null ? J.AsStr(st) : "same";
            T = Theme.Make(sth == "same" || sth == "" ? Cfg.Theme : sth, Cfg.Accent); Theme.Current = T; P = SetPal.From(T);
            if (IsHandleCreated) ApplyDwm();
        }
        void ApplyDwm()
        {
            var h = Handle;
            Native.SetDwm(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, T.Dark ? 1 : 0);
            Native.SetDwm(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
            if (T.IsGlass) { Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, T.Backdrop); Native.ExtendFrame(h, true); }
            else { Native.ExtendFrame(h, false); Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, Native.DWMSBT_NONE); }
            Invalidate();
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!Shot) try { uint d = Native.GetDpiForWindow(Handle); if (d >= 96) Sc = d / 96f; } catch { }
            ApplyDwm();
        }

        public string PageName { get { return SetNative.Pages[Math.Max(0, pageIdx)]; } }
        public void Navigate(string name)
        {
            int idx = SetNative.PageIndex(name);
            if (idx == pageIdx && page != null) return;
            EndEdit();
            if (page != null) { page.Dispose(); page = null; }
            Prev.StopExcept(new string[0]);
            pageIdx = idx;
            switch (idx)
            {
                case 1: page = new SetAppearancePage(); break;
                case 2: page = new SetDockPage(); break;
                case 3: page = new SetWidgetsPage(); break;
                case 4: page = new SetNotifyPage(); break;
                case 5: page = new SetAccountsPage(); break;
                case 6: page = new SetChecklistPage(); break;
                case 7: page = new SetAboutPage(); break;
                default: page = new SetGeneralPage(); break;
            }
            page.W = this;
            if (!Shot) Native.TrimMemory();
            Invalidate();
        }
        public SetPage Page { get { return page; } }

        /// <summary>Config changed by a page: save (debounced) and repaint. theme=true re-applies theme/fonts.</summary>
        public void Changed(bool theme = false)
        {
            if (theme) { Ui.ResetFonts(); ApplyTheme(); }
            if (!Shot) { saveTimer.Stop(); saveTimer.Start(); }
            Invalidate();
        }
        int lastSelfSave = -100000;
        public void SaveNow() { if (!Shot) { lastSelfSave = Environment.TickCount; Cfg.Save(); } }
        FileSystemWatcher cfgWatch; Timer cfgReload;
        /// <summary>The bar also writes config.json (drag-reorder, right-click Move/Show on/Remove). Reload when that happens,
        /// so Settings never saves an outdated copy over it.</summary>
        void WatchConfig()
        {
            if (Shot) return;
            try
            {
                cfgReload = new Timer { Interval = 300 };
                cfgReload.Tick += (s, e) =>
                {
                    cfgReload.Stop();
                    if (Environment.TickCount - lastSelfSave < 1500 || saveTimer.Enabled) return;   // our own save / we have pending edits
                    var fresh = AppConfig.Load(); Cfg.FromDict(fresh.ToDict());                     // update in place: pages keep their Cfg reference
                    Invalidate();
                };
                cfgWatch = new FileSystemWatcher(Paths.Root, "config.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                FileSystemEventHandler h = (s, e) => { try { BeginInvoke((Action)(() => { cfgReload.Stop(); cfgReload.Start(); })); } catch { } };
                cfgWatch.Changed += h; cfgWatch.Created += h; cfgWatch.Renamed += (s, e) => h(s, null);
                cfgWatch.EnableRaisingEvents = true;
            }
            catch { }
        }

        public void RequestRepaint() { repaintWanted = true; }
        int lastPrevTick;
        void OnTick(object s, EventArgs e)
        {
            if (Environment.TickCount - lastPrevTick >= 500)
            {
                lastPrevTick = Environment.TickCount;
                // live previews only while the window is in front: an idle Settings window in the background costs ~nothing
                bool front = Visible && WindowState != FormWindowState.Minimized && Form.ActiveForm == this;
                if (front && Prev.Tick()) repaintWanted = true;
                if (page != null) page.Tick();
            }
            if (EditKey != null && Environment.TickCount - caretT > 530) { caretT = Environment.TickCount; caretOn = !caretOn; repaintWanted = true; }
            if (toast != null && Environment.TickCount > toastUntil) { toast = null; repaintWanted = true; }
            if (repaintWanted && Visible && WindowState != FormWindowState.Minimized) { repaintWanted = false; Invalidate(); }
        }
        public void Toast(string text) { toast = text; toastUntil = Environment.TickCount + 2600; Invalidate(); }

        // ---------------- painting ----------------
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            var cs = ClientSize; if (cs.Width < 2 || cs.Height < 2) return;
            if (dib == null || dib.W != cs.Width || dib.H != cs.Height) { if (dib != null) dib.Dispose(); dib = new SetDib(cs.Width, cs.Height); }
            using (var g = Graphics.FromImage(dib.Bmp)) Render(g, cs, false);
            var hdc = e.Graphics.GetHdc();
            try { dib.Blit(hdc, 0, 0); } finally { e.Graphics.ReleaseHdc(hdc); }
        }

        /// <summary>Paint the whole window. fakeBackdrop: draw an imitation of the DWM glass (used for screenshots).</summary>
        public void Render(Graphics g, Size cs, bool fakeBackdrop)
        {
            Ui.Hq(g);
            g.Clear(Color.FromArgb(0, 0, 0, 0));
            if (fakeBackdrop && T.IsGlass)
            {
                SetPreview.Wallpaper(g, new RectangleF(0, 0, cs.Width, cs.Height), SetPreview.WallFor(T));
                g.FillRectangle(Ui.B(T.Dark ? Color.FromArgb(70, 20, 20, 28) : Color.FromArgb(90, 255, 255, 255)), 0, 0, cs.Width, cs.Height);
            }
            g.FillRectangle(Ui.B(P.Win), 0, 0, cs.Width, cs.Height);
            var sg = new SetG { G = g, P = P, T = T, Sc = Sc, Mouse = mouse, W = this };
            sideW = S(272);
            PaintSidebar(sg, cs);

            // header
            float cx = sideW + S(8), cw = cs.Width - cx;
            float pad = S(36), maxW = S(1040);
            float innerW = Math.Min(cw - pad * 2, maxW), ix = cx + pad;
            float hy = S(26);
            Ui.Text(g, page.Title, Ui.FontSemi(S(28)), P.Text, new RectangleF(ix, hy, innerW, S(40)));
            if (page.Subtitle != null) Ui.Text(g, page.Subtitle, sg.F(13), P.Dim, new RectangleF(ix, hy + S(40), innerW, S(20)));
            float top = hy + S(page.Subtitle != null ? 74 : 56);
            viewport = new RectangleF(cx, top, cw, cs.Height - top);
            // page content (clipped)
            var st = g.Save();
            g.SetClip(viewport);
            sg.HitClip = viewport;
            float maxScroll = Math.Max(0, page.ContentH - viewport.Height + S(24));
            page.Scroll = Math.Max(0, Math.Min(page.Scroll, maxScroll));
            eseen = false;
            float bottom = page.Paint(sg, ix, top + S(4) - page.Scroll, innerW);
            page.ContentH = bottom - (top + S(4) - page.Scroll);
            g.Restore(st);
            if (EditKey != null && !eseen && !Shot) BeginInvoke(new Action(() => EndEdit()));
            // fade at the top edge of the scroll area
            if (page.Scroll > 1)
                using (var br = new LinearGradientBrush(new RectangleF(cx, top - 1, cw, S(14)), Color.FromArgb(T.Dark ? (T.IsGlass ? 50 : 90) : 22, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(br, cx, top, cw, S(12));
            // scrollbar
            maxScroll = Math.Max(0, page.ContentH - viewport.Height + S(24));
            if (maxScroll > 0)
            {
                float trackH = viewport.Height - S(12), thumbH = Math.Max(S(40), trackH * viewport.Height / (viewport.Height + maxScroll));
                float ty = viewport.Y + S(6) + (trackH - thumbH) * (page.Scroll / maxScroll);
                bool near = mouse.X > cs.Width - S(18) && viewport.Contains(mouse);
                float bw = near ? S(6) : S(3);
                Ui.FillRound(g, new RectangleF(cs.Width - S(6) - bw, ty, bw, thumbH), bw / 2, Ui.WithA(P.Text, near ? 110 : 60));
                sbThumb = new RectangleF(cs.Width - S(18), ty, S(18), thumbH); sbMax = maxScroll; sbTrack = trackH - thumbH;
                if (near) sg.Hit(new RectangleF(cs.Width - S(18), viewport.Y, S(18), viewport.Height), null, p => { sbDrag = true; sbDragY = p.Y; sbDragScroll = page.Scroll; }, false);
            }
            else sbMax = 0;
            // toast
            if (toast != null)
            {
                var f = sg.FS(13); float tw = sg.TextW(toast, f) + S(54), th = S(40);
                var tr = new RectangleF(cx + (cw - tw) / 2, cs.Height - th - S(22), tw, th);
                using (var path = Ui.Round(new RectangleF(tr.X, tr.Y + S(3), tr.Width, tr.Height), th / 2)) g.FillPath(Ui.B(Color.FromArgb(50, 0, 0, 0)), path);
                Ui.FillRound(g, tr, th / 2, T.Dark ? Color.FromArgb(255, 44, 44, 52) : Color.FromArgb(255, 30, 30, 36));
                Ui.Icon(g, Icons.Check, new RectangleF(tr.X + S(14), tr.Y, S(18), th), T.Good, S(13));
                Ui.Text(g, toast, f, Color.White, new RectangleF(tr.X + S(38), tr.Y, tw - S(46), th));
            }
            hits = sg.Hits;
            // keep hover state coherent with new hits
            hoverHit = HitAt(mouse);
            if (Shot) return;
            Cursor want = hoverHit != null && hoverHit.Hand ? Cursors.Hand : (EditKey != null && editRect.Contains(mouse) ? Cursors.IBeam : Cursors.Default);
            if (Cursor != want) Cursor = want;
        }
        RectangleF sbThumb; float sbMax, sbTrack; bool sbDrag; int sbDragY; float sbDragScroll;

        void PaintSidebar(SetG sg, Size cs)
        {
            var g = sg.G;
            if (P.Side.A > 0 && P.Side != P.Win) g.FillRectangle(Ui.B(P.Side), 0, 0, sideW, cs.Height);
            // brand
            float x = S(20), y = S(22);
            var logo = new RectangleF(x, y, S(34), S(34));
            DrawLogo(g, logo, T.Accent);
            Ui.Text(g, "NovaBar", Ui.FontSemi(S(16)), P.Text, new RectangleF(x + S(46), y - S(1), S(200), S(20)));
            Ui.Text(g, "Settings", sg.F(12), P.Dim, new RectangleF(x + S(46), y + S(18), S(200), S(18)));
            y += S(70);
            for (int i = 0; i < navNames.Length; i++)
            {
                var r = new RectangleF(S(10), y, sideW - S(20), S(40));
                bool sel = i == pageIdx, hv = sg.Hover(r);
                if (sel) Ui.FillRound(g, r, S(6), P.Sel.A > 0 ? P.Sel : P.CardHover);
                else if (hv) Ui.FillRound(g, r, S(6), Ui.WithA(P.Sel.A > 0 ? P.Sel : P.CardHover, (P.Sel.A > 0 ? P.Sel.A : 255) * 2 / 3));
                if (sel) Ui.FillRound(g, new RectangleF(r.X + S(1), r.Y + S(11), S(3), S(18)), S(1.5f), T.Accent);
                Ui.Icon(g, navIcons[i], new RectangleF(r.X + S(14), r.Y, S(20), r.Height), sel ? T.Accent : P.Text, S(15));
                Ui.Text(g, navNames[i], sel ? sg.FS(14) : sg.F(14), P.Text, new RectangleF(r.X + S(46), r.Y, r.Width - S(50), r.Height));
                var name = SetNative.Pages[i];
                sg.Hit(r, () => Navigate(name));
                y += S(44);
            }
            // footer: theme quick info
            var fr = new RectangleF(S(22), cs.Height - S(44), sideW - S(40), S(20));
            Ui.Icon(g, "", new RectangleF(fr.X, fr.Y, S(14), fr.Height), P.Faint, S(11));
            Ui.Text(g, Theme.NameOf(Cfg.Theme) + " theme  ·  v" + SettingsApp.Version, sg.F(11.5f), P.Faint, new RectangleF(fr.X + S(20), fr.Y, fr.Width - S(20), fr.Height));
        }

        public float S(float v) { return v * Sc; }

        /// <summary>App mark: accent squircle with a white bar holding three widget dots.</summary>
        public static void DrawLogo(Graphics g, RectangleF r, Color accent)
        {
            float k = r.Width / 34f;
            using (var br = new LinearGradientBrush(r, Ui.Mix(accent, Color.White, 0.22), Ui.Mix(accent, Color.Black, 0.28), 45f))
            using (var p = Ui.Round(r, 10 * k)) g.FillPath(br, p);
            var bar = new RectangleF(r.X + 5 * k, r.Y + 12.5f * k, 24 * k, 9 * k);
            Ui.FillRound(g, bar, 4.5f * k, Color.FromArgb(240, 255, 255, 255));
            var dot = Ui.Mix(accent, Color.Black, 0.15);
            for (int i = 0; i < 3; i++) Ui.FillCircle(g, new PointF(bar.X + 6 * k + i * 6 * k, bar.Y + bar.Height / 2), 1.9f * k, i == 1 ? dot : Ui.WithA(dot, 150));
        }

        // ---------------- input ----------------
        SetHit HitAt(Point p)
        {
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return hits[i];
            return null;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var old = mouse; mouse = e.Location;
            if (sbDrag && sbTrack > 0) { page.Scroll = sbDragScroll + (e.Y - sbDragY) * sbMax / sbTrack; Invalidate(); return; }
            if (page != null && page.Dragging) { page.DragMove(e.Location); Invalidate(); return; }
            if (EditKey != null && selecting) { EditMouse(e.Location, true); Invalidate(); return; }
            var h = HitAt(mouse);
            bool sbNear = (old.X > ClientSize.Width - S(18)) != (mouse.X > ClientSize.Width - S(18));
            if (h != hoverHit || sbNear) Invalidate();
            if (h == hoverHit && h != null && !Shot) { var want = h.Hand ? Cursors.Hand : Cursors.Default; if (Cursor != want) Cursor = want; }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); mouse = new Point(-9999, -9999); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var h = HitAt(e.Location);
            if (EditKey != null)
            {
                if (editRect.Contains(e.Location)) { EditMouse(e.Location, false); selecting = true; if (e.Clicks >= 2) { anchor = 0; caret = etext.Length; } Invalidate(); return; }
                EndEdit();
            }
            downRect = h != null ? h.R : RectangleF.Empty;
            if (h != null && h.Down != null) h.Down(e.Location);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            selecting = false;
            if (sbDrag) { sbDrag = false; return; }
            if (page != null && page.Dragging) { if (page.DragEnd(e.Location)) { Invalidate(); return; } }
            if (e.Button != MouseButtons.Left) return;
            var h = HitAt(e.Location);
            if (h != null && h.R == downRect && h.Click != null) { h.Click(); Invalidate(); }
            downRect = RectangleF.Empty;
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (page == null || !viewport.Contains(e.Location)) return;
            page.Scroll -= e.Delta / 120f * S(72);
            Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        // ---------------- custom text editing ----------------
        public string EditKey; string etext = ""; int caret, anchor; bool esecret, eseen, caretOn = true, selecting; int caretT;
        Action<string> echange, esubmit; float escroll; RectangleF editRect;
        public void BeginEdit(string key, string value, bool secret, Action<string> onChange, Action<string> onSubmit)
        {
            EndEdit();
            EditKey = key; etext = value ?? ""; esecret = secret; echange = onChange; esubmit = onSubmit;
            caret = etext.Length; anchor = 0; escroll = 0; caretOn = true; caretT = Environment.TickCount;
            selecting = false;
            Invalidate();
        }
        public void EndEdit()
        {
            if (EditKey == null) return;
            EditKey = null; echange = null; esubmit = null; Invalidate();
        }
        public bool Editing(string key) { return EditKey == key; }
        string Display { get { return esecret ? new string('•', etext.Length) : etext; } }
        public void PaintEditor(SetG g, RectangleF tr)
        {
            eseen = true; editRect = new RectangleF(tr.X - S(8), tr.Y, tr.Width + S(16), tr.Height);
            var f = g.F(13.5f); var disp = Display;
            Func<int, float> px = i => i <= 0 ? 0 : g.TextW(disp.Substring(0, Math.Min(i, disp.Length)), f);
            float cxp = px(caret);
            if (cxp - escroll > tr.Width - S(4)) escroll = cxp - tr.Width + S(4);
            if (cxp - escroll < 0) escroll = cxp;
            var st = g.G.Save();
            g.G.SetClip(RectangleF.Intersect(tr, g.HitClip), CombineMode.Intersect);
            if (anchor != caret)
            {
                float a = px(Math.Min(anchor, caret)), b = px(Math.Max(anchor, caret));
                g.G.FillRectangle(Ui.B(Ui.WithA(T.Accent, 110)), tr.X + a - escroll, tr.Y + tr.Height / 2 - S(10), b - a, S(20));
            }
            Ui.Text(g.G, disp, f, P.Text, new RectangleF(tr.X - escroll, tr.Y, px(disp.Length) + S(40), tr.Height));
            if (caretOn) g.G.FillRectangle(Ui.B(P.Text), tr.X + cxp - escroll, tr.Y + tr.Height / 2 - S(9), Math.Max(1f, S(1.2f)), S(18));
            g.G.Restore(st);
        }
        void EditMouse(Point p, bool extend)
        {
            using (var g = CreateGraphics())
            {
                var f = Ui.Font(S(13.5f)); var disp = Display; float x = p.X - (editRect.X + S(8)) + escroll;
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i <= disp.Length; i++)
                {
                    float w = i == 0 ? 0 : Ui.Measure(g, disp.Substring(0, i), f).Width;
                    float d = Math.Abs(w - x); if (d < bd) { bd = d; best = i; }
                }
                caret = best; if (!extend) anchor = best;
            }
            caretOn = true; caretT = Environment.TickCount;
        }
        void Replace(string ins)
        {
            int a = Math.Min(anchor, caret), b = Math.Max(anchor, caret);
            etext = etext.Substring(0, a) + ins + etext.Substring(b);
            caret = anchor = a + ins.Length;
            var ch = echange; if (ch != null) ch(etext);
        }
        protected override bool IsInputKey(Keys k)
        {
            var kc = k & Keys.KeyCode;
            if (kc == Keys.Left || kc == Keys.Right || kc == Keys.Up || kc == Keys.Down || kc == Keys.Home || kc == Keys.End || kc == Keys.Tab) return true;
            return base.IsInputKey(k);
        }
        protected override bool ProcessDialogKey(Keys k)
        {
            if (EditKey != null) return false;
            return base.ProcessDialogKey(k);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (EditKey == null)
            {
                if (e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Down) { page.Scroll += e.KeyCode == Keys.Down ? S(60) : viewport.Height * 0.9f; Invalidate(); }
                else if (e.KeyCode == Keys.PageUp || e.KeyCode == Keys.Up) { page.Scroll -= e.KeyCode == Keys.Up ? S(60) : viewport.Height * 0.9f; Invalidate(); }
                else if (e.KeyCode == Keys.Home && e.Control) { page.Scroll = 0; Invalidate(); }
                return;
            }
            bool sh = e.Shift, ctl = e.Control; int len = etext.Length;
            caretOn = true; caretT = Environment.TickCount;
            switch (e.KeyCode)
            {
                case Keys.Left: caret = ctl ? WordLeft(caret) : (anchor != caret && !sh) ? Math.Min(anchor, caret) : Math.Max(0, caret - 1); if (!sh) anchor = caret; break;
                case Keys.Right: caret = ctl ? WordRight(caret) : (anchor != caret && !sh) ? Math.Max(anchor, caret) : Math.Min(len, caret + 1); if (!sh) anchor = caret; break;
                case Keys.Home: caret = 0; if (!sh) anchor = caret; break;
                case Keys.End: caret = len; if (!sh) anchor = caret; break;
                case Keys.Back:
                    if (anchor == caret && caret > 0) anchor = ctl ? WordLeft(caret) : caret - 1;
                    if (anchor != caret) Replace(""); break;
                case Keys.Delete:
                    if (anchor == caret && caret < len) anchor = ctl ? WordRight(caret) : caret + 1;
                    if (anchor != caret) Replace(""); break;
                case Keys.A: if (ctl) { anchor = 0; caret = len; } break;
                case Keys.C: if (ctl && !esecret && anchor != caret) try { Clipboard.SetText(etext.Substring(Math.Min(anchor, caret), Math.Abs(anchor - caret))); } catch { } break;
                case Keys.X: if (ctl && !esecret && anchor != caret) { try { Clipboard.SetText(etext.Substring(Math.Min(anchor, caret), Math.Abs(anchor - caret))); } catch { } Replace(""); } break;
                case Keys.V: if (ctl) { string t = null; try { t = Clipboard.GetText(); } catch { } if (!string.IsNullOrEmpty(t)) Replace(t.Replace("\r", "").Replace("\n", " ").Trim()); } break;
                case Keys.Enter: { var s = esubmit; var v = etext; EndEdit(); if (s != null) s(v); } break;
                case Keys.Escape: case Keys.Tab: EndEdit(); break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = ctl || e.KeyCode == Keys.Back || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Tab;
            Invalidate();
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (EditKey == null || e.KeyChar < 32 || e.KeyChar == 127) return;
            if ((ModifierKeys & Keys.Control) != 0 && (ModifierKeys & Keys.Alt) == 0) return;
            Replace(e.KeyChar.ToString()); e.Handled = true; caretOn = true; caretT = Environment.TickCount; Invalidate();
        }
        int WordLeft(int i) { while (i > 0 && etext[i - 1] == ' ') i--; while (i > 0 && etext[i - 1] != ' ') i--; return i; }
        int WordRight(int i) { int n = etext.Length; while (i < n && etext[i] != ' ') i++; while (i < n && etext[i] == ' ') i++; return i; }

        // ---------------- menus ----------------
        public void ShowMenu(RectangleF r, string[] options, int selected, Action<int> pick)
        {
            if (Shot) return;
            var m = new ContextMenuStrip { Renderer = new SetMenuRenderer(P), ShowImageMargin = false, ShowCheckMargin = false, Font = Ui.Font(S(13.5f)), Padding = new Padding(0, 4, 0, 4) };
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] == "-") { m.Items.Add(new ToolStripSeparator()); continue; }
                int k = i;
                var it = new ToolStripMenuItem(options[i]) { Checked = i == selected, Padding = new Padding(8, 5, 8, 5) };
                it.Click += (s, e) => { pick(k); Invalidate(); };
                m.Items.Add(it);
            }
            m.MinimumSize = new Size((int)r.Width, 0);
            m.Closed += (s, e) => BeginInvoke(new Action(() => m.Dispose()));
            m.Show(this, new Point((int)r.X, (int)r.Bottom + (int)S(4)));
        }

        // ---------------- single instance ----------------
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED && !Shot)
            {
                int dpi = m.WParam.ToInt32() & 0xffff; if (dpi >= 96) Sc = dpi / 96f;
                var rc = (Native.RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                MinimumSize = new Size((int)(980 * Sc), (int)(680 * Sc));
                Native.SetWindowPos(Handle, IntPtr.Zero, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top, 0x14 /*NOZORDER|NOACTIVATE*/);
                Invalidate(); return;
            }
            if (m.Msg == (int)SetNative.ShowMsg && SetNative.ShowMsg != 0)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show(); Activate(); Native.SetForegroundWindow(Handle);
                int idx = m.WParam.ToInt32();
                if (idx >= 0 && idx < SetNative.Pages.Length) Navigate(SetNative.Pages[idx]);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (saveTimer.Enabled) { saveTimer.Stop(); SaveNow(); }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tick.Stop(); tick.Dispose(); saveTimer.Dispose();
                if (page != null) { page.Dispose(); page = null; }
                Prev.Dispose();
                if (dib != null) { dib.Dispose(); dib = null; }
            }
            base.Dispose(disposing);
        }

        /// <summary>Render the current page to a bitmap (screenshot mode).</summary>
        public Bitmap Snapshot(int w, int h)
        {
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Render(g, new Size(w, h), true); // first pass lays out (ContentH, scroll)
                Render(g, new Size(w, h), true);
            }
            return bmp;
        }
        public void SetMouse(Point p) { mouse = p; }
        public void TypeText(string t) { if (EditKey != null) { Replace(t); Invalidate(); } }
        public void PressAt(Point p) { var h = HitAt(p); if (h != null && h.Down != null) h.Down(p); }
        public void ClickAt(Point p)
        {
            var h = HitAt(p); if (h == null) return;
            if (h.Down != null) h.Down(p);
            if (h.Click != null) h.Click();
        }
    }
}
