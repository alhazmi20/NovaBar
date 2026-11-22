// Start Menu index and Commands (launcher over the Start Menu + built-in actions).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    // ------------------------------------------------------------------ Start Menu index (shared)
    public static class CtlStartIndex
    {
        public class Entry { public string Name, Path, Lower; }
        static List<Entry> items = new List<Entry>();
        static DateTime built = DateTime.MinValue; static int building;
        public static List<Entry> Items { get { return items; } }
        public static bool Ready { get { return built != DateTime.MinValue; } }

        /// <summary>Rebuild in the background if older than maxAgeMin (or never built).</summary>
        public static void EnsureFresh(double maxAgeMin, Action done)
        {
            if ((DateTime.Now - built).TotalMinutes < maxAgeMin) return;
            if (System.Threading.Interlocked.Exchange(ref building, 1) == 1) return;
            Ctl.Async(() =>
            {
                try
                {
                    var l = new List<Entry>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
                    {
                        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                        IEnumerable<string> files; try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList(); } catch { continue; }
                        foreach (var f in files)
                        {
                            var n = System.IO.Path.GetFileNameWithoutExtension(f);
                            var low = n.ToLowerInvariant();
                            if (low.Contains("uninstall") || low.Contains("readme") || low.EndsWith(" help") || low.Contains("documentation") || low.Contains("release notes")) continue;
                            if (!seen.Add(n)) continue;
                            l.Add(new Entry { Name = n, Path = f, Lower = low });
                        }
                    }
                    items = l.OrderBy(e => e.Name).ToList(); built = DateTime.Now;
                }
                finally { System.Threading.Interlocked.Exchange(ref building, 0); }
                if (done != null) done();
            });
        }

        /// <summary>Fuzzy score: higher is better, 0 = no match.</summary>
        public static int Score(string lowerName, string q)
        {
            if (q.Length == 0) return 1;
            if (lowerName == q) return 1000;
            if (lowerName.StartsWith(q)) return 900 - lowerName.Length;
            int i = lowerName.IndexOf(" " + q, StringComparison.Ordinal);
            if (i >= 0) return 700 - i;
            // initials ("vsc" -> Visual Studio Code)
            var initials = new string(lowerName.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]).ToArray());
            if (initials.StartsWith(q)) return 650;
            i = lowerName.IndexOf(q, StringComparison.Ordinal);
            if (i >= 0) return 500 - i;
            int pos = 0, gaps = 0;
            foreach (var ch in q) { int j = lowerName.IndexOf(ch, pos); if (j < 0) return 0; gaps += j - pos; pos = j + 1; }
            return Math.Max(1, 300 - gaps * 5 - lowerName.Length);
        }
    }

    // ------------------------------------------------------------------ Commands
    [Widget("commands", "Commands", "Apps & Files", "Type a word to launch an app or an action.", RefName = "Commands", Data = DataKind.Live)]
    public class CtlCommandsWidget : Widget
    {
        class Item { public string Name, Kind, Glyph, Path; public Action Run; public bool Confirm; public int Score; }
        string query = ""; Item confirm; int gen;
        List<Item> results = new List<Item>();
        protected override string StateKey { get { return Meta.Id; } }

        static List<Item> Actions()
        {
            return new List<Item>
            {
                new Item { Name = "Lock", Kind = "Action", Glyph = Icons.Lock, Run = () => CtlNative.LockWorkStation() },
                new Item { Name = "Sleep", Kind = "Action", Glyph = Icons.Moon, Run = () => Ctl.Later(300, () => CtlNative.SetSuspendState(false, false, false)) },
                new Item { Name = "Hibernate", Kind = "Action", Glyph = Icons.Power, Run = () => Ctl.Later(300, () => Hidden("shutdown.exe", "/h")) },
                new Item { Name = "Shut down", Kind = "Action", Glyph = Icons.Power, Confirm = true, Run = () => Hidden("shutdown.exe", "/s /t 0") },
                new Item { Name = "Restart", Kind = "Action", Glyph = Icons.Refresh, Confirm = true, Run = () => Hidden("shutdown.exe", "/r /t 0") },
                new Item { Name = "Sign out", Kind = "Action", Glyph = Icons.Person, Confirm = true, Run = () => Hidden("shutdown.exe", "/l") },
                new Item { Name = "Settings", Kind = "Action", Glyph = Icons.Settings, Run = () => Ctl.Open("ms-settings:") },
                new Item { Name = "Task Manager", Kind = "Action", Glyph = Icons.Chart, Run = () => Ctl.Open("taskmgr.exe") },
                new Item { Name = "File Explorer", Kind = "Action", Glyph = Icons.Folder, Run = () => Ctl.Open("explorer.exe") },
                new Item { Name = "Empty Recycle Bin", Kind = "Action", Glyph = Icons.RecycleBin, Confirm = true, Run = () => CtlBin.Empty() },
                new Item { Name = "Show desktop", Kind = "Action", Glyph = Icons.Desktop, Run = () => Native.KeyChord(Native.VK_LWIN, 0x44) },
                new Item { Name = "Screenshot region", Kind = "Action", Glyph = Icons.Screenshot, Run = () => Ctl.Open("ms-screenclip:") },
                new Item { Name = "Control Panel", Kind = "Action", Glyph = Icons.Settings, Run = () => Ctl.Open("control.exe") },
                new Item { Name = "Terminal", Kind = "Action", Glyph = Icons.Code, Run = () => Ctl.Open("wt.exe") },
                new Item { Name = "Notification Center", Kind = "Action", Glyph = Icons.Notification, Run = () => Native.KeyChord(Native.VK_LWIN, 0x4E) },
            };
        }
        static void Hidden(string exe, string args)
        {
            Ctl.Async(() => Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true }));
        }

        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Choice("hotkey", "Keyboard shortcut", "Alt+Space", CtlCmdHotkey.Choices), SettingDef.Toggle("winkey", "Windows key opens Commands (Start: Ctrl+Esc)", true),
            SettingDef.Choice("look", "Button look", "Windows logo", "Windows logo", "Run\u2026 box") }; } }
        bool Logo { get { return SStr("look") != "Run\u2026 box"; } }
        public override void OnStart() { CtlCmdHotkey.Attach(this, SStr("hotkey")); CtlWinKey.Set(this, SBool("winkey")); }
        public override void OnStop() { CtlCmdHotkey.Detach(this); CtlWinKey.Set(this, false); }
        public override void AddMenuItems(List<KeyValuePair<string, Action>> items)
        {
            items.Add(new KeyValuePair<string, Action>("Open Commands  (" + SStr("hotkey") + ")", () => Host.OpenPanel(this)));
        }
        public override void OnPanelOpen() { query = ""; confirm = null; gen++; Recompute(); CtlStartIndex.EnsureFresh(10, () => { if (Host != null) Host.Post(() => { Recompute(); Invalidate(); }); }); }
        void Recompute()
        {
            var q = query.Trim().ToLowerInvariant();
            var all = new List<Item>();
            if (q.Length == 0)
            {
                object o; var recent = State.TryGetValue("recent", out o) && o is System.Collections.IList ? ((System.Collections.IList)o).Cast<object>().Select(x => J.AsStr(x)).ToList() : new List<string>();
                foreach (var name in recent)
                {
                    var e = CtlStartIndex.Items.FirstOrDefault(z => z.Name == name);
                    if (e != null) all.Add(AppItem(e));
                    else { var a = Actions().FirstOrDefault(z => z.Name == name); if (a != null) all.Add(a); }
                }
                foreach (var a in Actions()) if (!all.Any(z => z.Name == a.Name)) all.Add(a);
                results = all.Take(7).ToList(); return;
            }
            foreach (var e in CtlStartIndex.Items) { int s = CtlStartIndex.Score(e.Lower, q); if (s > 0) { var it = AppItem(e); it.Score = s + 5; all.Add(it); } }
            foreach (var a in Actions()) { int s = CtlStartIndex.Score(a.Name.ToLowerInvariant(), q); if (s > 0) { a.Score = s; all.Add(a); } }
            results = all.OrderByDescending(z => z.Score).ThenBy(z => z.Name.Length).Take(7).ToList();
        }
        static Item AppItem(CtlStartIndex.Entry e) { var p = e.Path; return new Item { Name = e.Name, Kind = "App", Path = p, Run = () => Ctl.Open(p) }; }
        bool closing;
        /// <summary>Enter: run the best match for exactly what was typed. Empty box → nothing. Lock/Sleep/power actions
        /// only run from Enter on a strong match (prefix/word), never from a loose fuzzy match.</summary>
        void Submit(string typed)
        {
            var q = (typed ?? "").Trim();
            if (q.Length == 0) return;
            query = q; Recompute();
            var top = results.FirstOrDefault();
            if (top == null) return;
            if (top.Kind == "Action" && top.Score < 600) return;     // e.g. "stremio" must never fall through to Lock
            closing = true;
            try { Launch(top); } finally { closing = false; }
        }
        void Launch(Item it)
        {
            if (it == null) return;
            if (it.Confirm && confirm != it) { confirm = it; Invalidate(); return; }
            confirm = null;
            object o; var recent = State.TryGetValue("recent", out o) && o is System.Collections.IList ? ((System.Collections.IList)o).Cast<object>().Select(x => J.AsStr(x)).ToList() : new List<string>();
            recent.Remove(it.Name); recent.Insert(0, it.Name); State["recent"] = recent.Take(4).Cast<object>().ToArray(); SaveState();
            Host.ClosePanel();
            try { it.Run(); } catch { }
        }

        public override int Measure(DrawCtx c) { return Logo ? c.Si(40) : c.Si(112); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds;
            if (Logo)
            {   // Windows logo: four panes, Windows 11 blue
                float sz = c.S(17), gap = c.S(1.6f), half = (sz - gap) / 2;
                float x0 = b.X + (b.Width - sz) / 2, y0 = b.Y + (b.Height - sz) / 2;
                var top = Color.FromArgb(0x4C, 0xC2, 0xFF); var bot = Color.FromArgb(0x00, 0x78, 0xD4);
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(x0, y0 - 1, sz, sz + 2), top, bot, 90f))
                    for (int i = 0; i < 4; i++)
                        using (var pth = Ui.Round(new RectangleF(x0 + (i % 2) * (half + gap), y0 + (i / 2) * (half + gap), half, half), c.S(1.2f))) c.G.FillPath(br, pth);
                return;
            }
            var ir = new RectangleF(b.X + c.S(10), b.Y, c.S(16), b.Height);
            Ui.Icon(c.G, "\uE756", ir, c.T.Accent, c.S(13));
            Ui.Text(c.G, "Run…", c.F(12.5f), c.T.TextDim, new RectangleF(ir.Right + c.S(6), b.Y, c.S(60), b.Height));
            var kr = new RectangleF(b.Right - c.S(34), b.Y + (b.Height - c.S(18)) / 2, c.S(24), c.S(18));
            Ui.StrokeRound(c.G, kr, c.S(5), c.T.TextFaint);
            Ui.Text(c.G, "↵", c.F(10), c.T.TextDim, kr, 0);
        }

        public override Size PanelSize(float s) { return new Size((int)(380 * s), (int)(392 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds;
            var ir = new RectangleF(b.X + c.S(12), b.Y + c.S(12), b.Width - c.S(24), c.S(40));
            Ui.FillRound(c.G, ir, c.S(12), c.T.Input);
            Ui.Icon(c.G, Icons.Search, new RectangleF(ir.X + c.S(8), ir.Y, c.S(22), ir.Height), c.T.TextDim, c.S(14));
            c.Input("q" + gen, Rectangle.Round(new RectangleF(ir.X + c.S(36), ir.Y + c.S(9), ir.Width - c.S(46), ir.Height - c.S(18))), "Type an app or action…",
                s => Submit(s), s => { if (closing) return; query = s ?? ""; confirm = null; Recompute(); Invalidate(); });
            float y = ir.Bottom + c.S(10);
            Ui.Text(c.G, query.Trim().Length == 0 ? "SUGGESTED" : "RESULTS", c.F(10.5f, true), c.T.TextFaint, new RectangleF(b.X + c.S(18), y, c.S(160), c.S(16)));
            if (!CtlStartIndex.Ready) Ui.Text(c.G, "indexing…", c.F(10.5f), c.T.TextFaint, new RectangleF(b.X, y, b.Width - c.S(18), c.S(16)), 1);
            y += c.S(20);
            if (results.Count == 0) Ui.Text(c.G, "No matches", c.F(12.5f), c.T.TextDim, new RectangleF(b.X + c.S(18), y, b.Width, c.S(40)));
            int ipx = c.Si(22);
            for (int i = 0; i < results.Count; i++)
            {
                var it = results[i];
                var r = new RectangleF(b.X + c.S(8), y, b.Width - c.S(16), c.S(38));
                bool hov = Ctl.Row(c, r, () => Launch(it));
                if (i == 0 && !hov && query.Trim().Length > 0) Ui.FillRound(c.G, r, c.S(8), c.T.AccentA(30));
                var icr = new RectangleF(r.X + c.S(10), r.Y + (r.Height - ipx) / 2, ipx, ipx);
                if (it.Path != null)
                {
                    var bmp = CtlIcons.PeekFile(it.Path, ipx, this);
                    if (bmp != null) Ctl.DrawImg(c.G, bmp, icr); else Ui.FillRound(c.G, icr, c.S(5), c.T.Track);
                }
                else { Ui.FillRound(c.G, icr, c.S(6), c.T.AccentA(40)); Ui.Icon(c.G, it.Glyph, icr, c.T.Accent, c.S(12)); }
                float tx = icr.Right + c.S(12);
                Ui.Text(c.G, it.Name, c.FS(13), c.T.Text, new RectangleF(tx, r.Y, r.Right - tx - c.S(70), r.Height));
                Ui.Text(c.G, i == 0 && query.Trim().Length > 0 ? "Enter ↵" : it.Kind, c.F(10.5f), c.T.TextFaint, new RectangleF(r.Right - c.S(80), r.Y, c.S(70), r.Height), 1);
                y += c.S(40);
            }
            if (confirm != null)
            {
                var cr = new RectangleF(b.X + c.S(12), b.Bottom - c.S(56), b.Width - c.S(24), c.S(44));
                Ui.FillRound(c.G, cr, c.S(12), Ui.WithA(c.T.Bad, 46));
                Ui.Text(c.G, confirm.Name + "?", c.FS(13), c.T.Text, new RectangleF(cr.X + c.S(14), cr.Y, c.S(160), cr.Height));
                var cf = confirm;
                c.Button(new RectangleF(cr.Right - c.S(178), cr.Y + c.S(8), c.S(76), c.S(28)), "Cancel", () => { confirm = null; Invalidate(); });
                var yes = new RectangleF(cr.Right - c.S(94), cr.Y + c.S(8), c.S(86), c.S(28));
                Ui.FillRound(c.G, yes, c.S(10), c.IsHover(yes) ? Ui.Mix(c.T.Bad, Color.White, 0.15) : c.T.Bad);
                Ui.Text(c.G, "Confirm", c.FS(12.5f), Color.White, yes, 0);
                c.Hit(yes, () => Launch(cf));
            }
        }
    }
}


namespace NovaBar
{
    /// <summary>Global shortcut for the Commands launcher (RegisterHotKey). One registration per process, shared by every
    /// Commands widget instance (one per monitor); the press opens the instance on the monitor under the mouse.</summary>
    public class CtlCmdHotkey : System.Windows.Forms.NativeWindow
    {
        public static readonly string[] Choices = { "Alt+Space", "Ctrl+Alt+Space", "Win+Alt+Space", "Ctrl+Shift+Space", "Alt+`", "Off" };
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const int ID = 0x4E42;
        static CtlCmdHotkey win; static string current;
        static readonly System.Collections.Generic.List<Widget> owners = new System.Collections.Generic.List<Widget>();
        public static bool Failed { get; private set; }
        public static string Current { get { return current; } }

        public static void Attach(Widget w, string combo)
        {
            if (!owners.Contains(w)) owners.Add(w);
            if (combo == current && win != null) return;
            Register(combo);
        }
        public static void Detach(Widget w)
        {
            owners.Remove(w);
            if (owners.Count == 0) Register("Off");
        }
        static void Register(string combo)
        {
            if (win == null) { win = new CtlCmdHotkey(); win.CreateHandle(new System.Windows.Forms.CreateParams()); }
            UnregisterHotKey(win.Handle, ID);
            current = combo; Failed = false;
            uint mods, vk;
            if (combo == null || combo == "Off" || !Parse(combo, out mods, out vk)) return;
            if (!RegisterHotKey(win.Handle, ID, mods | 0x4000 /*NOREPEAT*/, vk)) Failed = true;   // taken by another app
        }
        static bool Parse(string combo, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            foreach (var part in combo.Split('+'))
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "alt": mods |= 1; break;
                    case "ctrl": mods |= 2; break;
                    case "shift": mods |= 4; break;
                    case "win": mods |= 8; break;
                    case "space": vk = 0x20; break;
                    case "`": vk = 0xC0; break;
                    default: return false;
                }
            }
            return vk != 0;
        }
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == 0x0312 /*WM_HOTKEY*/ && m.WParam.ToInt32() == ID) { Fire(); return; }
            base.WndProc(ref m);
        }
        public static void Open() { Fire(); }
        static void Fire()
        {
            if (owners.Count == 0) return;
            var cur = System.Windows.Forms.Cursor.Position;
            Widget target = null;
            foreach (var w in owners)
            {
                if (w.Host == null) continue;
                var r = w.Host.ScreenBoundsOf(w);
                if (r.IsEmpty) continue;
                if (System.Windows.Forms.Screen.FromRectangle(r).Bounds.Contains(cur)) { target = w; break; }
                if (target == null) target = w;
            }
            if (target == null || target.Host == null) return;
            if (target.Host.IsPanelOpen(target)) target.Host.ClosePanel(); else target.Host.OpenPanel(target);
        }
    }
}


namespace NovaBar
{
    /// <summary>
    /// Tap the Windows key alone → open Commands instead of Start. Combos (Win+E, Win+D, Win+Shift+S…) are untouched:
    /// a low-level keyboard hook watches for Win down → Win up with no other key in between; only then it injects a
    /// harmless masking key (so Windows cancels Start) and opens Commands. Injected keys (our own Start button,
    /// other tools) are ignored. The hook lives on its own thread so a busy UI can never delay typing.
    /// </summary>
    public static class CtlWinKey
    {
        delegate IntPtr LLProc(int code, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LLProc fn, IntPtr mod, uint tid);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetMessage(out System.Windows.Forms.Message m, IntPtr h, uint min, uint max);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct KBD { public uint vk, scan, flags, time; public IntPtr extra; }

        static readonly System.Collections.Generic.HashSet<Widget> on = new System.Collections.Generic.HashSet<Widget>();
        static System.Threading.Thread thread; static uint threadId; static LLProc proc; static IntPtr hook;
        static bool winDown, other;
        static System.Windows.Forms.Control ui;   // marshal "open" to the UI thread

        static readonly bool liveBar = !Environment.GetCommandLineArgs().Any(a => a.StartsWith("--", StringComparison.Ordinal) && a != "--home");
        public static void Set(Widget w, bool enable)
        {
            if (!liveBar) return;                     // never hook the keyboard from --render / --settings / tests
            if (enable) on.Add(w); else on.Remove(w);
            if (on.Count > 0) Start(); else Stop();
        }
        static void Start()
        {
            if (thread != null) return;
            if (ui == null) { ui = new System.Windows.Forms.Control(); ui.CreateControl(); var _ = ui.Handle; }
            thread = new System.Threading.Thread(() =>
            {
                threadId = GetCurrentThreadId();
                proc = Hook;
                hook = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, proc, GetModuleHandle(null), 0);
                System.Windows.Forms.Message m;
                while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { }
                if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            });
            thread.IsBackground = true; thread.Name = "NovaBar.WinKey"; thread.Start();
        }
        static void Stop()
        {
            if (thread == null) return;
            PostThreadMessage(threadId, 0x0012 /*WM_QUIT*/, IntPtr.Zero, IntPtr.Zero);
            thread = null; winDown = false;
        }
        static IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                var k = (KBD)System.Runtime.InteropServices.Marshal.PtrToStructure(l, typeof(KBD));
                bool injected = (k.flags & 0x10) != 0, up = (k.flags & 0x80) != 0;
                bool isWin = k.vk == 0x5B || k.vk == 0x5C;
                if (!injected)
                {
                    if (isWin && !up) { if (!winDown) { winDown = true; other = false; } }
                    else if (!isWin && !up && winDown) other = true;                     // a combo: leave it alone
                    else if (isWin && up && winDown)
                    {
                        winDown = false;
                        if (!other)
                        {
                            // mask the lone Win press so Windows doesn't open Start, then open Commands on the UI thread
                            Native.keybd_event(0xE8, 0, 0, UIntPtr.Zero); Native.keybd_event(0xE8, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                            try { if (ui != null && ui.IsHandleCreated) ui.BeginInvoke((Action)CtlCmdHotkey.Open); } catch { }
                        }
                    }
                }
            }
            return CallNextHookEx(hook, code, w, l);
        }
    }
}
