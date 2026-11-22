// Todos and Notes — persisted locally in the widget State.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    [Widget("todos", "Todos", "Productivity", "Your next open task with a progress ring; tick it off from the dock.", RefName = "Todos", Data = DataKind.Live)]
    public class ProdTodosWidget : Widget
    {
        class Item { public string Id, Text; public bool Done; public long Ts, DoneTs; }
        List<Item> items; int scroll; string addText = ""; string justDone; DateTime justDoneAt;

        void Load()
        {
            if (items != null) return;
            items = new List<Item>();
            if (!State.ContainsKey("items"))
            {   // first run: a few starter tasks so the widget explains itself
                foreach (var t in new[] { "Plan the week", "Reply to Sara about the launch", "Review pull requests", "Book dentist appointment" }) items.Add(new Item { Id = Guid.NewGuid().ToString("N").Substring(0, 8), Text = t, Ts = ProdUtil.UnixNow() });
                items[3].Done = true; Persist(); return;
            }
            foreach (var d in ProdUtil.DictList(State["items"]))
                items.Add(new Item { Id = J.Str(d, "id"), Text = J.Str(d, "t"), Done = J.Bool(d, "d"), Ts = (long)J.Num(d, "ts"), DoneTs = (long)J.Num(d, "dts") });
        }
        void Persist()
        {
            State["items"] = items.Select(i => (object)new Dictionary<string, object> { { "id", i.Id }, { "t", i.Text }, { "d", i.Done }, { "ts", i.Ts }, { "dts", i.DoneTs } }).ToArray();
            SaveState(); Invalidate();
        }
        Item Next { get { return items.FirstOrDefault(i => !i.Done); } }
        void Toggle(Item i) { i.Done = !i.Done; i.DoneTs = i.Done ? ProdUtil.UnixNow() : 0; if (i.Done) { justDone = i.Text; justDoneAt = DateTime.Now; } Persist(); }
        void Add(string t)
        {
            t = (t ?? "").Trim(); if (t.Length == 0) return;
            items.Add(new Item { Id = Guid.NewGuid().ToString("N").Substring(0, 8), Text = t, Ts = ProdUtil.UnixNow() }); Persist();
        }
        void MoveUp(Item i) { int k = items.IndexOf(i); if (k > 0) { items.RemoveAt(k); items.Insert(k - 1, i); Persist(); } }

        public override void OnStart() { Load(); }
        public override int RefreshMs { get { return justDone != null && (DateTime.Now - justDoneAt).TotalSeconds < 3 ? 1000 : 0; } }
        public override void OnTick() { if (justDone != null && (DateTime.Now - justDoneAt).TotalSeconds >= 2) justDone = null; }

        public override int Measure(DrawCtx c) { return c.Si(200); }
        public override void Draw(DrawCtx c)
        {
            Load();
            var b = c.Bounds; var g = c.G;
            int done = items.Count(i => i.Done), tot = items.Count;
            var next = Next;
            // checkbox area (click = tick off next task)
            var cb = new RectangleF(b.X + c.S(6), b.Y + (b.Height - c.S(26)) / 2, c.S(26), c.S(26));
            bool hov = c.IsHover(cb) && next != null;
            if (hov) Ui.FillRound(g, cb, c.S(13), c.T.TileHover);
            var box = Ui.Inset(cb, c.S(5.5f), c.S(5.5f));
            if (justDone != null) { Ui.FillCircle(g, new PointF(box.X + box.Width / 2, box.Y + box.Height / 2), box.Width / 2, c.T.Good); Ui.Icon(g, Icons.Check, box, Color.White, c.S(9)); }
            else
            {
                using (var p = new Pen(hov ? c.T.Accent : c.T.TextDim, c.S(1.5f))) g.DrawEllipse(p, box);
                if (hov) Ui.Icon(g, Icons.Check, box, c.T.Accent, c.S(9));
            }
            if (next != null) c.Hit(cb, () => Toggle(next));
            // ring
            var ring = new RectangleF(b.Right - c.S(8) - c.S(24), b.Y + (b.Height - c.S(24)) / 2, c.S(24), c.S(24));
            Ui.Ring(g, ring, tot == 0 ? 0 : done / (double)tot, done == tot && tot > 0 ? c.T.Good : c.T.Accent, c.T.Track, c.S(2.6f));
            Ui.Text(g, (tot - done).ToString(), c.FS(9.5f), c.T.TextDim, ring, 0);
            float x = cb.Right + c.S(4), tw = ring.X - c.S(6) - x;
            if (justDone != null) { Ui.Text(g, justDone, c.FS(12.5f), c.T.TextFaint, new RectangleF(x, b.Y + c.S(3), tw, c.S(17))); Ui.Text(g, "Done!", c.F(10), c.T.Good, new RectangleF(x, b.Y + c.S(19), tw, c.S(13))); return; }
            Ui.Text(g, next != null ? next.Text : (tot == 0 ? "No tasks yet" : "All done"), c.FS(12.5f), next != null ? c.T.Text : c.T.TextDim, new RectangleF(x, b.Y + c.S(3), tw, c.S(17)));
            Ui.Text(g, done + " of " + tot + " done", c.F(10), c.T.TextDim, new RectangleF(x, b.Y + c.S(19), tw, c.S(13)));
        }

        public override Size PanelSize(float s) { return new Size((int)(340 * s), (int)(400 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            Load();
            var b = c.Bounds; var g = c.G;
            int done = items.Count(i => i.Done), tot = items.Count;
            c.PanelHeader("Todos", done + " / " + tot + " done", Icons.CheckBoxOn);
            float x = b.X + c.S(16), w = b.Width - c.S(32), y = b.Y + c.S(44);
            Ui.Progress(g, new RectangleF(x, y, w, c.S(4)), tot == 0 ? 0 : done / (double)tot, c.T.Accent, c.T.Track);
            y += c.S(12);
            var inR = new Rectangle((int)x, (int)y, (int)(w - c.S(40)), c.Si(30));
            c.Input("todo-add", inR, "Add a task and press Enter", s => { Add(s); addText = ""; }, s => addText = s);
            var addB = new RectangleF(inR.Right + c.S(8), y, c.S(32), c.S(30));
            c.Button(addB, null, () => { Add(addText); addText = ""; }, true, Icons.Add);
            y += c.S(40);
            // open first, then done
            var ordered = items.Where(i => !i.Done).Concat(items.Where(i => i.Done)).ToList();
            float rh = c.S(32); int rows = (int)((b.Bottom - c.S(46) - y) / rh);
            int maxScroll = Math.Max(0, ordered.Count - rows); scroll = Math.Max(0, Math.Min(scroll, maxScroll));
            c.Wheel(new Rectangle((int)x, (int)y, (int)w, (int)(rows * rh)), d => { scroll -= d; Invalidate(); });
            if (ordered.Count == 0) Ui.Text(g, "Nothing to do. Enjoy!", c.F(12), c.T.TextFaint, new RectangleF(x, y + c.S(10), w, c.S(20)), 0);
            for (int k = 0; k < rows && k + scroll < ordered.Count; k++)
            {
                var it = ordered[k + scroll];
                var rr = new RectangleF(x - c.S(6), y + k * rh, w + c.S(12), rh - c.S(2));
                bool hov = c.IsHover(rr);
                if (hov) Ui.FillRound(g, rr, c.S(8), c.T.Tile);
                var box = new RectangleF(rr.X + c.S(8), rr.Y + (rr.Height - c.S(16)) / 2, c.S(16), c.S(16));
                if (it.Done) { Ui.FillCircle(g, new PointF(box.X + c.S(8), box.Y + c.S(8)), c.S(8), c.T.Accent); Ui.Icon(g, Icons.Check, box, Color.White, c.S(9)); }
                else using (var p = new Pen(c.T.TextDim, c.S(1.4f))) g.DrawEllipse(p, box);
                var item = it;
                c.Hit(new RectangleF(rr.X, rr.Y, c.S(32), rr.Height), () => Toggle(item));
                float tr = rr.Right - (hov ? c.S(56) : c.S(8));
                var tRect = new RectangleF(box.Right + c.S(10), rr.Y, tr - box.Right - c.S(10), rr.Height);
                Ui.Text(g, it.Text, c.F(12.5f), it.Done ? c.T.TextFaint : c.T.Text, tRect);
                if (it.Done)
                {
                    float sw = Math.Min(tRect.Width, Ui.MeasureW(g, it.Text, c.F(12.5f)));
                    using (var p = new Pen(c.T.TextFaint, 1f)) g.DrawLine(p, tRect.X, tRect.Y + tRect.Height / 2, tRect.X + sw, tRect.Y + tRect.Height / 2);
                }
                if (hov)
                {
                    var del = new RectangleF(rr.Right - c.S(28), rr.Y + (rr.Height - c.S(24)) / 2, c.S(24), c.S(24));
                    c.IconButton(del, Icons.Delete, () => { items.Remove(item); Persist(); }, false, 11);
                    if (!it.Done) { var up = new RectangleF(del.X - c.S(26), del.Y, c.S(24), c.S(24)); c.IconButton(up, Icons.ChevronUp, () => MoveUp(item), false, 10); }
                }
            }
            // footer
            float fy = b.Bottom - c.S(38);
            using (var p = new Pen(c.T.Track, 1f)) g.DrawLine(p, x, fy, x + w, fy);
            Ui.Text(g, (tot - done) + " open", c.F(11.5f), c.T.TextDim, new RectangleF(x, fy + c.S(6), w, c.S(26)));
            if (done > 0)
            {
                var cl = new RectangleF(x + w - c.S(120), fy + c.S(7), c.S(120), c.S(24));
                c.Button(cl, "Clear completed", () => { items.RemoveAll(i => i.Done); Persist(); });
            }
        }
    }

    [Widget("notes", "Notes", "Productivity", "Your latest note, one click from editing.", RefName = "Notes", Data = DataKind.Live)]
    public class ProdNotesWidget : Widget
    {
        class Note { public string Id, Text; public long Ts; }
        List<Note> notes; string sel; System.Threading.Timer saveTimer; int listScroll;

        void Load()
        {
            if (notes != null) return;
            notes = new List<Note>();
            if (!State.ContainsKey("notes"))
            {
                notes.Add(new Note { Id = NewId(), Text = "Ideas\nDock themes per monitor\nWeekly review on Fridays", Ts = ProdUtil.UnixNow() - 86400 });
                notes.Add(new Note { Id = NewId(), Text = "Groceries: oat milk, coffee beans, dates, lemons", Ts = ProdUtil.UnixNow() });
                Persist(true);
            }
            else foreach (var d in ProdUtil.DictList(State["notes"])) notes.Add(new Note { Id = J.Str(d, "id"), Text = J.Str(d, "t"), Ts = (long)J.Num(d, "ts") });
            sel = J.Str(State, "sel", null);
        }
        static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 8); }
        void Persist(bool now)
        {
            State["notes"] = notes.Select(n => (object)new Dictionary<string, object> { { "id", n.Id }, { "t", n.Text }, { "ts", n.Ts } }).ToArray();
            State["sel"] = sel ?? "";
            if (now) { SaveState(); return; }
            // debounce autosave while typing
            if (saveTimer == null) saveTimer = new System.Threading.Timer(_ => { try { SaveState(); } catch { } }, null, 700, System.Threading.Timeout.Infinite);
            else saveTimer.Change(700, System.Threading.Timeout.Infinite);
        }
        List<Note> Sorted { get { return notes.OrderByDescending(n => n.Ts).ToList(); } }
        Note Selected { get { var n = notes.FirstOrDefault(x => x.Id == sel); if (n == null && notes.Count > 0) { n = Sorted[0]; sel = n.Id; } return n; } }
        static string Title(Note n)
        {
            var t = (n.Text ?? "").Trim(); if (t.Length == 0) return "New note";
            int i = t.IndexOf('\n'); return (i >= 0 ? t.Substring(0, i) : t).Trim();
        }
        static string Body(Note n)
        {
            var t = (n.Text ?? "").Trim(); int i = t.IndexOf('\n');
            return i >= 0 ? t.Substring(i + 1).Replace("\r", "").Replace('\n', ' ').Trim() : "";
        }
        static string When(long ts)
        {
            var d = ProdUtil.FromUnix(ts);
            if (d.Date == DateTime.Today) return Ui.Clock(d);
            if (d.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
            return d.ToString("MMM d", ProdUtil.Inv);
        }

        public override void OnStart() { Load(); }
        public override void OnStop() { if (saveTimer != null) { saveTimer.Dispose(); saveTimer = null; SaveState(); } }

        public override int Measure(DrawCtx c) { return c.Si(190); }
        public override void Draw(DrawCtx c)
        {
            Load();
            var b = c.Bounds; var g = c.G;
            Ui.Icon(g, Icons.Note, new RectangleF(b.X + c.S(9), b.Y, c.S(18), b.Height), c.T.Warn, c.S(15));
            float x = b.X + c.S(34), w = b.Right - c.S(10) - x;
            var latest = notes.Count > 0 ? Sorted[0] : null;
            if (latest == null) { Ui.Text(g, "New note", c.FS(12.5f), c.T.TextDim, new RectangleF(x, b.Y, w, b.Height)); return; }
            Ui.Text(g, Title(latest), c.FS(12.5f), c.T.Text, new RectangleF(x, b.Y + c.S(3), w, c.S(17)));
            var body = Body(latest);
            Ui.Text(g, body.Length > 0 ? body : When(latest.Ts), c.F(10), c.T.TextDim, new RectangleF(x, b.Y + c.S(19), w, c.S(13)));
        }
        public override void OnClick(MouseButtons btn)
        {
            if (btn == MouseButtons.Left && notes != null && notes.Count > 0) sel = Sorted[0].Id;
            base.OnClick(btn);
        }

        public override Size PanelSize(float s) { return new Size((int)(440 * s), (int)(340 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            Load();
            var b = c.Bounds; var g = c.G;
            c.PanelHeader("Notes", notes.Count + (notes.Count == 1 ? " note" : " notes"), Icons.Note);
            float lx = b.X + c.S(10), ly = b.Y + c.S(44), lw = c.S(150), lh = b.Bottom - c.S(10) - ly;
            // list
            var list = Sorted; float rh = c.S(46);
            int rows = (int)(lh / rh); int maxScroll = Math.Max(0, list.Count - rows); listScroll = Math.Max(0, Math.Min(listScroll, maxScroll));
            c.Wheel(new Rectangle((int)lx, (int)ly, (int)lw, (int)lh), d => { listScroll -= d; Invalidate(); });
            var cur = Selected;
            for (int k = 0; k < rows && k + listScroll < list.Count; k++)
            {
                var n = list[k + listScroll];
                var rr = new RectangleF(lx, ly + k * rh, lw, rh - c.S(4));
                bool s = cur != null && n.Id == cur.Id;
                if (s) Ui.FillRound(g, rr, c.S(8), c.T.AccentA(c.T.Dark ? 55 : 40)); else if (c.IsHover(rr)) Ui.FillRound(g, rr, c.S(8), c.T.Tile);
                Ui.Text(g, Title(n), c.FS(12), c.T.Text, new RectangleF(rr.X + c.S(10), rr.Y + c.S(5), rr.Width - c.S(16), c.S(16)));
                Ui.Text(g, When(n.Ts), c.F(10), c.T.TextDim, new RectangleF(rr.X + c.S(10), rr.Y + c.S(22), rr.Width - c.S(16), c.S(14)));
                var id = n.Id; c.Hit(rr, () => { sel = id; Persist(true); Invalidate(); });
            }
            // editor
            float ex = lx + lw + c.S(10), ew = b.Right - c.S(14) - ex;
            using (var p = new Pen(c.T.Track, 1f)) g.DrawLine(p, ex - c.S(5), ly, ex - c.S(5), b.Bottom - c.S(12));
            // toolbar (new / delete)
            var newB = new RectangleF(b.Right - c.S(14) - c.S(60), b.Y + c.S(11), c.S(60), c.S(24));
            c.Button(newB, "New", () => { var n = new Note { Id = NewId(), Text = "", Ts = ProdUtil.UnixNow() }; notes.Add(n); sel = n.Id; listScroll = 0; Persist(true); Invalidate(); }, true, Icons.Add);
            if (cur != null)
            {
                var delB = new RectangleF(newB.X - c.S(30), newB.Y, c.S(24), c.S(24));
                c.IconButton(delB, Icons.Delete, () => { notes.Remove(cur); sel = null; Persist(true); Invalidate(); }, false, 11);
                var edR = new Rectangle((int)ex, (int)ly, (int)ew, (int)(b.Bottom - c.S(30) - ly));
                var note = cur;
                c.Input("note-" + cur.Id, edR, "Start typing… the first line is the title", s => { }, s =>
                {
                    if (s == note.Text) return;
                    note.Text = s; note.Ts = ProdUtil.UnixNow(); Persist(false); Invalidate();
                }, true, cur.Text);
                int words = (cur.Text ?? "").Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                Ui.Text(g, words + (words == 1 ? " word" : " words") + " · saved automatically", c.F(10), c.T.TextFaint, new RectangleF(ex, b.Bottom - c.S(28), ew, c.S(18)));
            }
            else Ui.Text(g, "No notes — click New", c.F(12), c.T.TextFaint, new RectangleF(ex, ly, ew, c.S(30)), 0);
        }
    }
}
