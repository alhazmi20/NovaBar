// Apps = the task list (NovaBar replaces the Windows taskbar): Start + pinned apps + open windows.
// Event-driven: SetWinEventHook (out-of-context) + RegisterShellHookWindow (flash / create / destroy), debounced rescans.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NovaBar
{
    public class CtlWin { public IntPtr Hwnd; public string Title, Exe; public Bitmap Icon; public bool Min; }
    public class CtlWinSnap
    {
        public List<CtlWin> Wins = new List<CtlWin>();
        public override bool Equals(object o)
        {
            var x = o as CtlWinSnap; if (x == null || x.Wins.Count != Wins.Count) return false;
            for (int i = 0; i < Wins.Count; i++)
            {
                var a = x.Wins[i]; var b = Wins[i];
                if (a.Hwnd != b.Hwnd || a.Title != b.Title || a.Icon != b.Icon || a.Min != b.Min) return false;
            }
            return true;
        }
        public override int GetHashCode() { return Wins.Count; }
    }

    /// <summary>Hidden window that receives WM_SHELLHOOK notifications (flashing windows etc.).</summary>
    public class CtlShellHookWnd : NativeWindow
    {
        [DllImport("user32.dll")] static extern bool RegisterShellHookWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool DeregisterShellHookWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
        readonly uint msg;
        public CtlShellHookWnd()
        {
            CreateHandle(new CreateParams { Caption = "NovaBarShellHook", ExStyle = Native.WS_EX_TOOLWINDOW });
            msg = RegisterWindowMessage("SHELLHOOK");
            RegisterShellHookWindow(Handle);
        }
        public void Close() { try { DeregisterShellHookWindow(Handle); DestroyHandle(); } catch { } }
        protected override void WndProc(ref Message m)
        {
            if (msg != 0 && m.Msg == (int)msg)
            {
                try
                {
                    int code = (int)(long)m.WParam; var h = m.LParam;
                    switch (code)
                    {
                        case 0x8006: CtlTasks.OnFlash(h); break;                     // HSHELL_FLASH
                        case 1: case 2: case 6: CtlTasks.Kick(); break;             // created / destroyed / redraw
                        case 4: case 0x8004: CtlTasks.SetFg(h); break;              // activated
                    }
                }
                catch { }
            }
            base.WndProc(ref m);
        }
    }

    public static class CtlTasks
    {
        public const string Key = "ctl.tasks";
        public static volatile int IconPx = 20;
        static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;

        delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

        static IntPtr fg;
        public static IntPtr Fg { get { return fg; } }
        static readonly HashSet<IntPtr> flashing = new HashSet<IntPtr>();
        public static bool IsFlashing(IntPtr h) { lock (flashing) return flashing.Contains(h); }
        static volatile HashSet<IntPtr> known = new HashSet<IntPtr>();
        static readonly List<IntPtr> order = new List<IntPtr>();
        static readonly Dictionary<IntPtr, Bitmap> hwndIcons = new Dictionary<IntPtr, Bitmap>();
        static readonly Dictionary<uint, string> pidExe = new Dictionary<uint, string>();
        static readonly HashSet<Widget> listeners = new HashSet<Widget>();
        static readonly List<IntPtr> hooks = new List<IntPtr>();
        static WinEventProc proc;
        static System.Threading.Timer debounce;
        static CtlShellHookWnd shellWnd;

        static CtlTasks() { Hub.Define(Key, 20000 /*safety net only; real updates come from the shell hook*/, Scan); }

        // ---------------- lifecycle (UI thread) ----------------
        public static void Attach(Widget w)
        {
            listeners.Add(w);
            if (listeners.Count == 1) Install();
            Hub.Subscribe(Key, w);
        }
        public static void Detach(Widget w)
        {
            Hub.Unsubscribe(Key, w);
            if (listeners.Remove(w) && listeners.Count == 0) Uninstall();
        }
        static void Install()
        {
            try
            {
                proc = OnEvent;
                debounce = new System.Threading.Timer(_ => Hub.Refresh(Key), null, Timeout.Infinite, Timeout.Infinite);
                // Low-traffic events only. Window create/destroy/show/hide and title changes come from the shell hook
                // (HSHELL_WINDOWCREATED/DESTROYED/REDRAW) — the global DESTROY/SHOW/HIDE/NAMECHANGE WinEvents fire
                // thousands of times per minute (tooltips, accessibility updates from browsers) and cost real CPU.
                uint[][] ranges = { new uint[] { 0x0003, 0x0003 } /*FOREGROUND*/, new uint[] { 0x0016, 0x0017 } /*MINIMIZESTART/END*/,
                                    new uint[] { 0x8017, 0x8018 } /*CLOAKED/UNCLOAKED*/ };
                foreach (var r in ranges)
                {
                    var h = SetWinEventHook(r[0], r[1], IntPtr.Zero, proc, 0, 0, 0 /*OUTOFCONTEXT*/ | 2 /*SKIPOWNPROCESS*/);
                    if (h != IntPtr.Zero) hooks.Add(h);
                }
                try { shellWnd = new CtlShellHookWnd(); } catch { shellWnd = null; }
                SetFg(Native.GetForegroundWindow());
            }
            catch { }
        }
        static void Uninstall()
        {
            foreach (var h in hooks) { try { UnhookWinEvent(h); } catch { } }
            hooks.Clear();
            if (shellWnd != null) { shellWnd.Close(); shellWnd = null; }
            if (debounce != null) { debounce.Dispose(); debounce = null; }
        }

        // ---------------- events (UI thread) ----------------
        static void OnEvent(IntPtr hook, uint evt, IntPtr h, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (h == IntPtr.Zero) return;
                if (evt == 0x0003) { SetFg(h); return; }
                if (idObject != 0 || idChild != 0) return;   // OBJID_WINDOW / CHILDID_SELF only
                bool isKnown = known.Contains(h);
                switch (evt)
                {
                    case 0x8001: case 0x8003: case 0x8017: case 0x0016: case 0x0017:
                        if (isKnown) Kick(); break;
                    case 0x8002: case 0x8018: case 0x800C:
                        if (isKnown || IsTaskWindow(h)) Kick(); break;
                }
            }
            catch { }
        }
        public static void Kick() { var d = debounce; if (d != null) d.Change(90, Timeout.Infinite); }
        public static void SetFg(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            uint pid; Native.GetWindowThreadProcessId(h, out pid);
            if (pid == ownPid) return;                // keep the last real app as "active" while our panels/menus have focus
            fg = h;
            lock (flashing) flashing.Remove(h);
            InvalidateAll();
            if (!known.Contains(h) && IsTaskWindow(h)) Kick();
        }
        public static void OnFlash(IntPtr h)
        {
            if (h == fg) return;
            lock (flashing) flashing.Add(h);
            InvalidateAll();
        }
        static void InvalidateAll() { foreach (var w in listeners.ToArray()) w.Invalidate(); }

        // ---------------- scanning (thread pool) ----------------
        public static bool IsTaskWindow(IntPtr h)
        {
            if (!Native.IsWindowVisible(h)) return false;
            if (CtlNative.GetAncestor(h, 1 /*GA_PARENT*/) != CtlNative.GetDesktopWindow()) return false;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            bool appWin = (ex & 0x40000) != 0;
            if (!appWin)
            {
                if (CtlNative.GetWindow(h, CtlNative.GW_OWNER) != IntPtr.Zero) return false;
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0) return false;
            }
            if (Native.GetWindowTextLength(h) == 0) return false;
            if (CtlNative.IsCloaked(h)) return false;
            uint pid; Native.GetWindowThreadProcessId(h, out pid);
            if (pid == ownPid) return false;
            return true;
        }

        static object Scan()
        {
            var hs = new List<IntPtr>();
            CtlNative.EnumWindows((h, l) => { try { if (IsTaskWindow(h)) hs.Add(h); } catch { } return true; }, IntPtr.Zero);
            var set = new HashSet<IntPtr>(hs);
            int px = IconPx;
            var wins = new List<CtlWin>(); var pids = new HashSet<uint>();
            foreach (var h in hs)
            {
                try
                {
                    var cls = Native.WindowClass(h);
                    if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") { set.Remove(h); continue; }
                    uint pid; Native.GetWindowThreadProcessId(h, out pid); pids.Add(pid);
                    string exe; lock (pidExe) { if (!pidExe.TryGetValue(pid, out exe)) { exe = CtlNative.ProcessPath(pid); pidExe[pid] = exe; } }
                    bool uwp = false;
                    if (exe != null && exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        // UWP frame: the real app lives in a child CoreWindow of another process
                        IntPtr child = IntPtr.Zero;
                        while ((child = Native.FindWindowEx(h, child, null, null)) != IntPtr.Zero)
                        {
                            uint p2; Native.GetWindowThreadProcessId(child, out p2);
                            if (p2 != pid) { var e2 = CtlNative.ProcessPath(p2); if (e2 != null) { exe = e2; uwp = true; } break; }
                        }
                    }
                    Bitmap icon; bool have;
                    lock (hwndIcons) have = hwndIcons.TryGetValue(h, out icon);
                    if (!have || icon == null)
                    {
                        icon = uwp ? CtlIcons.Get("f:" + exe, px, () => CtlIcons.FromFile(exe, px)) : WindowIcon(h, exe, px);
                        lock (hwndIcons) hwndIcons[h] = icon;
                    }
                    wins.Add(new CtlWin { Hwnd = h, Title = Native.WindowTitle(h), Exe = exe, Icon = icon, Min = Native.IsIconic(h) });
                }
                catch { }
            }
            lock (order)
            {
                order.RemoveAll(h => !set.Contains(h));
                // new windows go to the end (stable like a real taskbar); first scan uses bottom-up z-order ≈ age
                var fresh = hs.Where(h => set.Contains(h) && !order.Contains(h)).ToList();
                if (order.Count == 0) fresh.Reverse();
                order.AddRange(fresh);
                var idx = new Dictionary<IntPtr, int>(); for (int i = 0; i < order.Count; i++) idx[order[i]] = i;
                wins = wins.Where(w => idx.ContainsKey(w.Hwnd)).OrderBy(w => idx[w.Hwnd]).ToList();
            }
            known = set;
            lock (hwndIcons) foreach (var k in hwndIcons.Keys.Where(k => !set.Contains(k)).ToList()) hwndIcons.Remove(k);
            lock (pidExe) foreach (var k in pidExe.Keys.Where(k => !pids.Contains(k)).ToList()) pidExe.Remove(k);
            lock (flashing) flashing.RemoveWhere(h => !set.Contains(h));
            return new CtlWinSnap { Wins = wins };
        }
        static Bitmap WindowIcon(IntPtr h, string exe, int px)
        {
            IntPtr r;
            foreach (var k in px > 20 ? new[] { 1 /*ICON_BIG*/, 2 /*ICON_SMALL2*/, 0 } : new[] { 2, 1, 0 })
            {
                if (CtlNative.SendMessageTimeoutI(h, CtlNative.WM_GETICON, new IntPtr(k), IntPtr.Zero, CtlNative.SMTO_ABORTIFHUNG, 50, out r) != IntPtr.Zero && r != IntPtr.Zero)
                { var b = CtlIcons.FromHIcon(r, px); if (b != null) return b; }
            }
            var hc = CtlNative.GetClassLongPtr(h, CtlNative.GCLP_HICON);
            if (hc == IntPtr.Zero) hc = CtlNative.GetClassLongPtr(h, CtlNative.GCLP_HICONSM);
            var cb = CtlIcons.FromHIcon(hc, px); if (cb != null) return cb;
            return exe == null ? null : CtlIcons.Get("f:" + exe, px, () => CtlIcons.FromFile(exe, px));
        }

        // ---------------- actions (UI thread: we hold the foreground right from the user's click) ----------------
        public static void Click(IntPtr h)
        {
            try
            {
                if (h == fg && !Native.IsIconic(h) && Native.GetForegroundWindow() == h) Native.ShowWindow(h, 6 /*SW_MINIMIZE*/);
                else { CtlNative.Activate(h); SetFg(h); }
            }
            catch { }
            Kick();
        }
        /// <summary>Bring a window to the front (never minimizes; used by the hover previews).</summary>
        public static void Activate(IntPtr h) { try { CtlNative.Activate(h); SetFg(h); } catch { } Kick(); }
        public static void Close(IntPtr h) { Native.PostMessage(h, 0x0112 /*WM_SYSCOMMAND*/, new IntPtr(0xF060 /*SC_CLOSE*/), IntPtr.Zero); Kick(); }

        // ---------------- pin target resolution (.lnk -> exe) ----------------
        static readonly Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static int TargetsVer;
        public static string Target(string path, Widget w)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return path;
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
            string t; lock (targets) { if (targets.TryGetValue(path, out t)) return t; targets[path] = null; }
            Ctl.Async(() =>
            {
                var r = Ctl.Sta(() =>
                {
                    var ty = Type.GetTypeFromProgID("WScript.Shell"); if (ty == null) return "";
                    dynamic sh = Activator.CreateInstance(ty);
                    try { dynamic lk = sh.CreateShortcut(path); string tp = lk.TargetPath; Marshal.FinalReleaseComObject(lk); return tp ?? ""; }
                    finally { Marshal.FinalReleaseComObject(sh); }
                }, 5000);
                lock (targets) targets[path] = string.IsNullOrEmpty(r) ? "" : r;
                Interlocked.Increment(ref TargetsVer);
                if (w != null) w.Invalidate();
            });
            return null;
        }
    }

    // ------------------------------------------------------------------ Apps widget
    [Widget("apps", "Apps", "Apps & Files", "Start, your pinned apps and every open window.", RefName = "Apps", Data = DataKind.Live)]
    public class CtlAppsWidget : Widget
    {
        class Group { public string Pin, Exe; public List<CtlWin> Wins = new List<CtlWin>(); public RectangleF R; }
        bool editing; string addMsg; int pinsVer;
        List<Group> groups = new List<Group>(); object gSnap; int gPins = -1, gTargets = -1; string gOpts;
        Group hoverGroup;

        public override SettingDef[] SettingDefs
        {
            get
            {
                return new[] { SettingDef.Toggle("start", "Show Start button", true), SettingDef.Toggle("windows", "Show open windows", true),
                    SettingDef.Toggle("group", "Group windows of the same app", true), SettingDef.Toggle("labels", "Show window titles", false),
                    SettingDef.Toggle("dimMin", "Fade icons of minimized apps", false),
                    SettingDef.Choice("badgeColor", "Group badge color", "Gray (accent when active)", "Gray (accent when active)", "Accent", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink", "Gray", "White"),
                    SettingDef.Text("badgeHex", "Custom badge color (#RRGGBB, overrides the choice)", ""),
                    SettingDef.Choice("badgeSize", "Group badge size", "Medium", "Small", "Medium", "Large"),
                    SettingDef.Choice("badgeText", "Badge number color", "Automatic", "Automatic", "White", "Black", "Accent", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink"),
                    SettingDef.Text("badgeTextHex", "Custom number color (#RRGGBB, overrides the choice)", ""),
                    SettingDef.Choice("badgeTextActive", "Badge number color (active app)", "Same as above", "Same as above", "Automatic", "White", "Black", "Accent", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink"),
                    SettingDef.Text("badgeTextActiveHex", "Custom number color for the active app (#RRGGBB)", ""),
                    SettingDef.Number("max", "Max window icons", 40, 0, 80), SettingDef.Toggle("previews", "Window previews on hover", true) };
            }
        }
        protected override string StateKey { get { return Meta.Id; } }
        List<string> Pinned
        {
            get
            {
                object o;
                if (!State.TryGetValue("pinned", out o) || !(o is System.Collections.IList))
                {
                    var d = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") };
                    foreach (var e in new[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe" })
                        if (File.Exists(e)) { d.Add(e); break; }
                    return d;
                }
                return ((System.Collections.IList)o).Cast<object>().Select(x => J.AsStr(x)).Where(x => x.Length > 0).ToList();
            }
        }
        void SetPinned(List<string> l) { State["pinned"] = l.Cast<object>().ToArray(); SaveState(); pinsVer++; Invalidate(); }
        bool IsPinnedExe(string exe) { return exe != null && Pinned.Any(p => string.Equals(CtlTasks.Target(p, this), exe, StringComparison.OrdinalIgnoreCase)); }

        public override void OnStart() { CtlTasks.Attach(this); }
        public override void OnStop() { CtlThumbs.Stop(this); CtlTasks.Detach(this); }
        public override void OnPanelOpen() { CtlStartIndex.EnsureFresh(10, Invalidate); Hub.Refresh(CtlTasks.Key); }
        public override void OnPanelClose() { editing = false; addMsg = null; }
        CtlWinSnap Snap { get { return Hub.Get<CtlWinSnap>(CtlTasks.Key) ?? new CtlWinSnap(); } }

        /// <summary>Does a pinned/grouped target belong to the same app as a window's process? Tolerates path spelling
        /// differences (case, short names, WINDOWS vs Windows) and launcher stubs that start a differently named process.</summary>
        static bool SameApp(string targetExe, string pin, string winExe)
        {
            if (string.IsNullOrEmpty(winExe)) return false;
            foreach (var cand in new[] { targetExe, pin })
            {
                if (string.IsNullOrEmpty(cand)) continue;
                if (string.Equals(cand, winExe, StringComparison.OrdinalIgnoreCase)) return true;
                string a = SafeName(cand), b = SafeName(winExe);
                if (a == null || b == null) continue;
                if (a == b) return true;
                string alias;
                if (Aliases.TryGetValue(a, out alias) && alias == b) return true;            // launcher -> real process
                // Squirrel-style apps: <app>\Update.exe launches <app>\app-x.y\<App>.exe
                var da = SafeDir(cand); var db = SafeDir(winExe);
                if (a == "update" && da != null && db != null && db.StartsWith(da, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "whatsapp", "whatsapp.root" }, { "msedge_proxy", "msedge" }, { "chrome_proxy", "chrome" }, { "teams", "ms-teams" },
            { "spotifylauncher", "spotify" }, { "outlook", "olk" }, { "wt", "windowsterminal" },
        };
        static string SafeName(string p) { try { return Path.GetFileNameWithoutExtension(p).ToLowerInvariant(); } catch { return null; } }
        static string SafeDir(string p) { try { return Path.GetDirectoryName(Path.GetFullPath(p)); } catch { return null; } }
        List<Group> Groups()
        {
            var snap = Snap; string opts = SBool("windows") + "|" + SBool("group") + "|" + SNum("max");
            if (snap == gSnap && gPins == pinsVer && gTargets == CtlTasks.TargetsVer && opts == gOpts) return groups;
            var l = Pinned.Select(p => new Group { Pin = p, Exe = CtlTasks.Target(p, this) }).ToList();
            if (SBool("windows"))
            {
                bool grp = SBool("group"); int max = (int)SNum("max"), n = 0;
                foreach (var w in snap.Wins)
                {
                    var g = w.Exe == null ? null : l.FirstOrDefault(x => x.Pin != null && SameApp(x.Exe, x.Pin, w.Exe));          // pinned app -> its own icon
                    if (g == null && grp && w.Exe != null) g = l.FirstOrDefault(x => x.Pin == null && !string.IsNullOrEmpty(x.Exe) && SameApp(x.Exe, null, w.Exe));
                    if (g == null) { if (n >= max) continue; g = new Group { Exe = w.Exe }; l.Add(g); n++; }
                    g.Wins.Add(w);
                }
            }
            // user's drag order for unpinned apps (pinned ones follow the pin list)
            object wo; var order = State.TryGetValue("winOrder", out wo) && wo is System.Collections.IList ? ((System.Collections.IList)wo).Cast<object>().Select(x => J.AsStr(x)).ToList() : null;
            if (order != null && order.Count > 0)
            {
                var pinnedPart = l.Where(x => x.Pin != null).ToList();
                var rest = l.Where(x => x.Pin == null).Select((x, i) => new { x, i }).ToList();
                var sorted = rest.OrderBy(a => { int k = order.IndexOf(OrderKey(a.x)); return k < 0 ? 100000 + a.i : k; }).Select(a => a.x);
                l = pinnedPart.Concat(sorted).ToList();
            }
            groups = l; gSnap = snap; gPins = pinsVer; gTargets = CtlTasks.TargetsVer; gOpts = opts;
            return l;
        }
        float ItemW(DrawCtx c, Group g)
        {
            if (!SBool("labels") || g.Wins.Count == 0) return c.S(34);
            return c.S(150);
        }

        public override int Measure(DrawCtx c)
        {
            float w = c.S(6) + (SBool("start") ? c.S(34) + c.S(9) : 0);
            foreach (var g in Groups()) w += ItemW(c, g);
            return (int)Math.Ceiling(w + c.S(6));
        }
        public override void Draw(DrawCtx c)
        {
            CtlTasks.IconPx = c.Si(20);
            vertical = c.Vertical;
            if (c.Vertical) { DrawVertical(c); return; }
            var b = c.Bounds; float x = b.X + c.S(6);
            IntPtr fg = CtlTasks.Fg; Group hov = null;
            if (SBool("start"))
            {
                var r = new RectangleF(x, b.Y + c.S(2), c.S(34), b.Height - c.S(4));
                if (c.IsHover(r)) Ui.FillRound(c.G, r, c.S(7), c.T.TileHover);
                Ui.Icon(c.G, Icons.Start, r, c.T.Accent, c.S(17));
                c.Hit(r, () => Native.KeyChord(Native.VK_LWIN)); x += c.S(34);
                using (var p = new Pen(Ui.WithA(c.T.TextFaint, 120), 1)) c.G.DrawLine(p, x + c.S(4), b.Y + c.S(10), x + c.S(4), b.Bottom - c.S(10));
                x += c.S(9);
            }
            foreach (var g in Groups())
            {
                float iw = ItemW(c, g);
                var r = new RectangleF(x, b.Y + c.S(2), iw, b.Height - c.S(4)); g.R = r;
                bool labels = SBool("labels") && g.Wins.Count > 0;
                if (DrawGroupItem(c, g, r, fg, labels ? g.Wins[0].Title : null)) hov = g;
                x += iw;
            }
            if (dragKey != null) DrawDragFeedback(c);
            hoverGroup = hov;
            Previews(c, hov);
        }
        /// <summary>One app group cell (any layout). label != null → icon on the left + text. Returns true when hovered.</summary>
        bool DrawGroupItem(DrawCtx c, Group g, RectangleF r, IntPtr fg, string label) { return DrawGroupItem(c, g, r, fg, label, false); }
        /// <summary>rowMode = vertical bar with labels: the running indicator becomes a pill on the left edge of the row.</summary>
        bool DrawGroupItem(DrawCtx c, Group g, RectangleF r, IntPtr fg, string label, bool rowMode)
        {
            int px = c.Si(20); float iw = r.Width;
            bool active = g.Wins.Any(w => w.Hwnd == fg), flash = g.Wins.Any(w => CtlTasks.IsFlashing(w.Hwnd)), hover = c.IsHover(r);
            if (flash) Ui.FillRound(c.G, r, c.S(7), Ui.WithA(c.T.Warn, c.T.Dark ? 70 : 90));
            else if (active || hover) Ui.FillRound(c.G, r, c.S(7), active && hover ? Ui.Mix(c.T.TileHover, c.T.Accent, 0.12) : c.T.TileHover);
            var bmp = g.Pin != null ? (CtlIcons.PeekFile(g.Pin, px, this) ?? (g.Wins.Count > 0 ? g.Wins[0].Icon : null)) : (g.Wins.Count > 0 ? g.Wins[0].Icon : null);
            bool labels = label != null;
            var ir = labels ? new RectangleF(r.X + c.S(8), r.Y + (r.Height - px) / 2 - c.S(1), px, px) : new RectangleF(r.X + (iw - px) / 2, r.Y + (r.Height - px) / 2 - c.S(1), px, px);
            DrawAppIcon(c, bmp, ir, g.Pin ?? g.Exe ?? (g.Wins.Count > 0 ? g.Wins[0].Title : "?"), SBool("dimMin") && g.Wins.Count > 0 && g.Wins.All(w => w.Min));
            if (g.Wins.Count > 1)
            {
                // window-count badge on grouped apps
                string cnt = g.Wins.Count > 9 ? "9+" : g.Wins.Count.ToString();
                string bsz = SStr("badgeSize");
                float fpx = bsz == "Small" ? 8.5f : bsz == "Large" ? 12f : 10.5f, hpx = bsz == "Small" ? 13f : bsz == "Large" ? 18f : 15.5f;
                var bf = c.F(fpx, true); float bh = c.S(hpx), bw = Math.Max(bh, Ui.Measure(c.G, cnt, bf).Width + c.S(7));
                var br = new RectangleF(ir.Right - bw / 2 + c.S(2), ir.Y - bh * 0.35f, bw, bh);
                var bcol = BadgeColor(c, active);
                Ui.FillRound(c.G, br, bh / 2, bcol);
                Ui.Text(c.G, cnt, bf, BadgeTextColor(bcol, active), br, 0);
            }
            if (labels)
            {
                if (!rowMode) Ui.Text(c.G, label, c.F(11.5f), c.T.Text, new RectangleF(ir.Right + c.S(7), r.Y, r.Right - ir.Right - c.S(12), r.Height - c.S(2)));
                else
                {
                    float tx = ir.Right + c.S(g.Wins.Count > 1 ? 13 : 7);   // rows: clear the count badge
                    Ui.Text(c.G, label, c.F(11.5f), c.T.Text, new RectangleF(tx, r.Y, r.Right - tx - c.S(5), r.Height - c.S(2)));
                }
            }
            if (g.Wins.Count > 0 && rowMode)
            {
                float dh = active ? c.S(14) : c.S(6);
                var col = flash ? c.T.Warn : active ? c.T.Accent : c.T.TextFaint;
                Ui.FillRound(c.G, new RectangleF(r.X + c.S(1.5f), r.Y + (r.Height - dh) / 2, c.S(2.5f), dh), c.S(1.25f), col);
            }
            else if (g.Wins.Count > 0)
            {
                float dw = active ? c.S(14) : c.S(5), dy = r.Bottom - c.S(3.5f);
                var col = flash ? c.T.Warn : active ? c.T.Accent : c.T.TextFaint;
                if (g.Wins.Count > 1 && !active)
                {
                    Ui.FillRound(c.G, new RectangleF(r.X + iw / 2 - c.S(6), dy, c.S(4), c.S(2.5f)), c.S(1.25f), col);
                    Ui.FillRound(c.G, new RectangleF(r.X + iw / 2 + c.S(2), dy, c.S(4), c.S(2.5f)), c.S(1.25f), col);
                }
                else Ui.FillRound(c.G, new RectangleF(r.X + (iw - dw) / 2, dy, dw, c.S(2.5f)), c.S(1.25f), col);
            }
            var gg = g; c.Hit(r, () => ClickGroup(gg));
            return hover;
        }

        // ---------------- vertical bar (docked left/right): grid that wraps by the sidebar width ----------------
        bool vertical;          // last Draw was in a vertical bar (drag maths uses 2D cells then)
        bool vRows;             // vertical + labels: one item per row (icon + title)
        /// <summary>Vertical layout: cell i of n (i = 0 is Start when shown). Rows top→bottom, row-major.</summary>
        void VGeom(DrawCtx c, out float pad, out float gap, out int cols, out float cw, out float ch, out bool rowsMode)
        {
            pad = c.S(4); gap = c.S(2);
            float avail = Math.Max(c.S(34), c.Bounds.Width - 2 * pad);
            rowsMode = SBool("labels");
            if (rowsMode) { cols = 1; cw = avail; ch = c.S(32); return; }
            cols = Math.Max(1, (int)((avail + gap) / (c.S(34) + gap)));
            cw = (avail - (cols - 1) * gap) / cols; ch = c.S(34);
        }
        RectangleF VCell(DrawCtx c, int i)
        {
            float pad, gap, cw, ch; int cols; bool rm; VGeom(c, out pad, out gap, out cols, out cw, out ch, out rm);
            return new RectangleF(c.Bounds.X + pad + (i % cols) * (cw + gap), c.Bounds.Y + pad + (i / cols) * (ch + gap), cw, ch);
        }
        public override int MeasureHeight(DrawCtx c)
        {
            if (!c.Vertical) return 0;
            int n = (SBool("start") ? 1 : 0) + Groups().Count;
            if (n == 0) return c.Si(34);
            float pad, gap, cw, ch; int cols; bool rm; VGeom(c, out pad, out gap, out cols, out cw, out ch, out rm);
            int rows = (n + cols - 1) / cols;
            return (int)Math.Ceiling(2 * pad + rows * ch + (rows - 1) * gap);
        }
        void DrawVertical(DrawCtx c)
        {
            float pad, gap, cw, ch; int cols; bool rm; VGeom(c, out pad, out gap, out cols, out cw, out ch, out rm);
            vRows = rm;
            IntPtr fg = CtlTasks.Fg; Group hov = null; int i = 0;
            if (SBool("start"))
            {
                var r = VCell(c, i++);
                if (c.IsHover(r)) Ui.FillRound(c.G, r, c.S(7), c.T.TileHover);
                if (rm)
                {
                    var ir = new RectangleF(r.X + c.S(6), r.Y, c.S(24), r.Height);
                    Ui.Icon(c.G, Icons.Start, ir, c.T.Accent, c.S(17));
                    Ui.Text(c.G, "Start", c.FS(11.5f), c.T.Text, new RectangleF(ir.Right + c.S(5), r.Y, r.Right - ir.Right - c.S(10), r.Height - c.S(2)));
                }
                else Ui.Icon(c.G, Icons.Start, r, c.T.Accent, c.S(17));
                c.Hit(r, () => Native.KeyChord(Native.VK_LWIN));
            }
            foreach (var g in Groups())
            {
                var r = VCell(c, i++); g.R = r;
                string label = null;
                if (rm) label = g.Wins.Count > 0 ? g.Wins[0].Title : SafeTitle(g.Pin ?? g.Exe);
                if (DrawGroupItem(c, g, r, fg, label, rm)) hov = g;
            }
            if (dragKey != null) DrawDragFeedback(c);
            hoverGroup = hov;
            Previews(c, hov);
        }
        static string SafeTitle(string path) { try { return string.IsNullOrEmpty(path) ? "" : Path.GetFileNameWithoutExtension(path); } catch { return path ?? ""; } }
        string GroupKey(Group g)
        {
            if (g.Pin != null) return "p:" + g.Pin;
            if (SBool("group") && g.Exe != null) return "e:" + g.Exe;
            return g.Wins.Count > 0 ? "h:" + g.Wins[0].Hwnd.ToInt64() : null;
        }
        /// <summary>Hover previews: report the hovered group (screen coords) to CtlThumbs, which owns the delay/close timer.</summary>
        void Previews(DrawCtx c, Group hov)
        {
            var test = CtlThumbs.TestExe;
            if (test != null)
            {
                var tg = Groups().FirstOrDefault(g => g.Wins.Count > 0 && g.Exe != null && string.Equals(Path.GetFileNameWithoutExtension(g.Exe), test, StringComparison.OrdinalIgnoreCase));
                if (tg != null) CtlThumbs.RunTest(this, c.T, c.Scale, tg.Wins);
                return;
            }
            if (!SBool("previews") || Host == null) return;
            var key = hov != null && hov.Wins.Count > 0 ? GroupKey(hov) : null;
            if (key == null) { CtlThumbs.NoHover(this); return; }
            var scr = Host.ScreenBoundsOf(this);
            if (scr.IsEmpty) return;
            int dx = scr.X - c.Bounds.X, dy = scr.Y - c.Bounds.Y;
            var ir = Rectangle.Round(hov.R); ir.Offset(dx, dy);
            CtlThumbs.Hover(this, key, ir, scr, c.Scale, () =>
            {
                var g = Groups().FirstOrDefault(x => x.Wins.Count > 0 && GroupKey(x) == key);
                return g != null ? g.Wins : null;
            });
        }
        Color BadgeTextColor(Color bg, bool active)
        {
            string hexKey = "badgeTextHex", choiceKey = "badgeText";
            if (active)
            {
                var ah = SStr("badgeTextActiveHex");
                if (!string.IsNullOrEmpty(ah) && Theme.Hex(ah, Color.Empty) != Color.Empty) return Theme.Hex(ah, Color.Empty);
                if (SStr("badgeTextActive") != "Same as above") { hexKey = null; choiceKey = "badgeTextActive"; }
            }
            var hex = hexKey != null ? SStr(hexKey) : null;
            if (!string.IsNullOrEmpty(hex)) { var hc = Theme.Hex(hex, Color.Empty); if (hc != Color.Empty) return hc; }
            switch (SStr(choiceKey))
            {
                case "White": return Color.White;
                case "Black": return Color.FromArgb(20, 20, 24);
                case "Accent": return Theme.Current.Accent;
                case "Red": return Color.FromArgb(239, 68, 68);
                case "Orange": return Color.FromArgb(249, 115, 22);
                case "Yellow": return Color.FromArgb(234, 179, 8);
                case "Green": return Color.FromArgb(34, 197, 94);
                case "Blue": return Color.FromArgb(59, 130, 246);
                case "Purple": return Color.FromArgb(168, 85, 247);
                case "Pink": return Color.FromArgb(236, 72, 153);
                default: return (bg.R * 299 + bg.G * 587 + bg.B * 114) / 1000 > 170 ? Color.FromArgb(20, 20, 24) : Color.White;   // readable on the badge
            }
        }
        Color BadgeColor(DrawCtx c, bool active)
        {
            var hex = SStr("badgeHex");
            if (!string.IsNullOrEmpty(hex)) { var hc = Theme.Hex(hex, Color.Empty); if (hc != Color.Empty) return hc; }
            Color gray = c.T.Dark ? Color.FromArgb(255, 70, 70, 82) : Color.FromArgb(255, 110, 110, 122);
            switch (SStr("badgeColor"))
            {
                case "Accent": return c.T.Accent;
                case "Red": return Color.FromArgb(239, 68, 68);
                case "Orange": return Color.FromArgb(249, 115, 22);
                case "Yellow": return Color.FromArgb(234, 179, 8);
                case "Green": return Color.FromArgb(34, 197, 94);
                case "Blue": return Color.FromArgb(59, 130, 246);
                case "Purple": return Color.FromArgb(168, 85, 247);
                case "Pink": return Color.FromArgb(236, 72, 153);
                case "Gray": return gray;
                case "White": return Color.FromArgb(245, 245, 247);
                default: return active ? c.T.Accent : gray;
            }
        }
        void DrawAppIcon(DrawCtx c, Bitmap bmp, RectangleF r, string name, bool dim = false)
        {
            if (bmp != null)
            {
                if (!dim) { Ctl.DrawImg(c.G, bmp, r); return; }
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    var cm = new System.Drawing.Imaging.ColorMatrix(); cm.Matrix33 = 0.6f; ia.SetColorMatrix(cm);
                    c.G.DrawImage(bmp, Rectangle.Round(r), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                }
                return;
            }
            Ui.FillRound(c.G, r, c.S(5), c.T.Track);
            var n = string.IsNullOrEmpty(name) ? "?" : Path.GetFileNameWithoutExtension(name);
            Ui.Text(c.G, n.Length > 0 ? n.Substring(0, 1).ToUpperInvariant() : "?", c.F(10, true), c.T.TextDim, r, 0);
        }

        // ---------------- clicks ----------------
        void ClickGroup(Group g)
        {
            if (g.Wins.Count > 1 && SBool("previews") && CtlThumbs.ToggleNow(this, GroupKey(g))) return;   // several windows → pick from the preview
            CtlThumbs.Dismiss(this);
            if (g.Wins.Count == 0) { Launch(g); return; }
            if (g.Wins.Count == 1) { CtlTasks.Click(g.Wins[0].Hwnd); return; }
            int i = g.Wins.FindIndex(w => w.Hwnd == CtlTasks.Fg);
            CtlTasks.Click(i < 0 ? g.Wins[0].Hwnd : g.Wins[(i + 1) % g.Wins.Count].Hwnd);
        }
        // ---------------- drag to reorder icons ----------------
        static string OrderKey(Group g) { return g.Exe ?? (g.Wins.Count > 0 ? "h:" + g.Wins[0].Hwnd.ToInt64() : "?"); }
        string dragKey; float dragX, dragY;
        public override bool BeginDrag(Point p)
        {
            var g = Groups().FirstOrDefault(x => x.R.Contains(p));
            if (g == null) return false;                       // Start button / empty space → the bar moves the whole widget
            dragKey = g.Pin != null ? "p:" + g.Pin : OrderKey(g); dragX = p.X; dragY = p.Y;
            CtlThumbs.Dismiss(this);
            return true;
        }
        public override void DragTo(Point p) { dragX = p.X; dragY = p.Y; Invalidate(); }
        Group DragGroup() { return dragKey == null ? null : Groups().FirstOrDefault(x => (x.Pin != null ? "p:" + x.Pin : OrderKey(x)) == dragKey); }
        int DropIndex(Group g, float x, float y, out List<Group> others)
        {
            others = Groups().Where(o => o != g).ToList();
            if (!vertical) return others.Count(o => o.R.X + o.R.Width / 2 < x);
            // vertical grid: row-major order — every cell in an earlier row, plus the cells of the pointer's row left of it
            bool rm = vRows;
            return others.Count(o => o.R.Bottom <= y || (o.R.Top <= y && y < o.R.Bottom && (rm ? o.R.Y + o.R.Height / 2 < y : o.R.X + o.R.Width / 2 < x)));
        }
        public override void EndDrag(Point p, bool cancelled)
        {
            var g = DragGroup(); dragKey = null;
            if (cancelled || g == null) { Invalidate(); return; }
            List<Group> others; int idx = DropIndex(g, p.X, p.Y, out others);
            int pinnedCount = others.Count(o => o.Pin != null);
            var pins = Pinned;
            if (g.Pin != null)
            {
                pins.RemoveAll(x => string.Equals(x, g.Pin, StringComparison.OrdinalIgnoreCase));
                pins.Insert(Math.Min(idx, pinnedCount), g.Pin); SetPinned(pins);
            }
            else if (idx < pinnedCount && g.Exe != null)
            {   // dropped among the pinned apps → pin it there (like the Windows taskbar)
                pins.Insert(idx, g.Exe); SetPinned(pins);
            }
            else
            {
                var unp = others.Where(o => o.Pin == null).Select(o => OrderKey(o)).ToList();
                unp.Insert(Math.Max(0, Math.Min(unp.Count, idx - pinnedCount)), OrderKey(g));
                State["winOrder"] = unp.Cast<object>().ToArray(); SaveState(); pinsVer++;
            }
            Invalidate();
        }
        /// <summary>While dragging: dim the dragged icon and show where it will land.</summary>
        void DrawDragFeedback(DrawCtx c)
        {
            var g = DragGroup(); if (g == null) return;
            Ui.FillRound(c.G, g.R, c.S(7), Ui.WithA(c.T.Tile, 170));
            List<Group> others; int idx = DropIndex(g, dragX, dragY, out others);
            if (vertical) { DrawDragFeedbackV(c, g, others, idx); return; }
            float lx = idx < others.Count ? others[idx].R.X - c.S(1.5f) : (others.Count > 0 ? others[others.Count - 1].R.Right + c.S(0.5f) : g.R.X);
            Ui.FillRound(c.G, new RectangleF(lx - c.S(1.5f), c.Bounds.Y + c.S(6), c.S(3), c.Bounds.Height - c.S(12)), c.S(1.5f), c.T.Accent);
            // the icon follows the mouse
            int px = c.Si(20); var bmp = g.Pin != null ? (CtlIcons.PeekFile(g.Pin, px, this) ?? (g.Wins.Count > 0 ? g.Wins[0].Icon : null)) : (g.Wins.Count > 0 ? g.Wins[0].Icon : null);
            var ir = new RectangleF(dragX - px / 2f, c.Bounds.Y + (c.Bounds.Height - px) / 2f - c.S(2), px, px);
            if (bmp != null) Ctl.DrawImg(c.G, bmp, ir);
        }
        void DrawDragFeedbackV(DrawCtx c, Group g, List<Group> others, int idx)
        {
            float t = c.S(3);
            if (vRows)
            {   // one item per row: horizontal insertion line above the target row (or below the last one)
                float ly = idx < others.Count ? others[idx].R.Y - c.S(1) : (others.Count > 0 ? others[others.Count - 1].R.Bottom + c.S(1) : g.R.Y);
                Ui.FillRound(c.G, new RectangleF(c.Bounds.X + c.S(6), ly - t / 2, c.Bounds.Width - c.S(12), t), t / 2, c.T.Accent);
            }
            else
            {   // grid: vertical marker at the left edge of the target cell (or right of the last cell)
                RectangleF tr = idx < others.Count ? others[idx].R : (others.Count > 0 ? others[others.Count - 1].R : g.R);
                float lx = idx < others.Count ? tr.X - c.S(1) : tr.Right + c.S(1);
                Ui.FillRound(c.G, new RectangleF(lx - t / 2, tr.Y + c.S(4), t, tr.Height - c.S(8)), t / 2, c.T.Accent);
            }
            int px = c.Si(20); var bmp = g.Pin != null ? (CtlIcons.PeekFile(g.Pin, px, this) ?? (g.Wins.Count > 0 ? g.Wins[0].Icon : null)) : (g.Wins.Count > 0 ? g.Wins[0].Icon : null);
            if (bmp != null) Ctl.DrawImg(c.G, bmp, new RectangleF(dragX - px / 2f, dragY - px / 2f, px, px));
        }
        public override bool HandlesRightClick() { return hoverGroup != null; }
        void Launch(Group g) { var t = g.Pin ?? g.Exe; if (t != null) Ctl.Open(t); }
        public override void OnClick(MouseButtons b)
        {
            var g = hoverGroup;
            CtlThumbs.Dismiss(this);
            if (b == MouseButtons.Middle) { if (g != null) Launch(g); return; }
            if (b == MouseButtons.Right) { if (g != null) ShowMenu(g); else new CtlMenu().Item("Taskbar settings", () => Ctl.Open("ms-settings:taskbar")).Item("Task Manager", () => Ctl.Open("taskmgr.exe")).Show(); return; }
            base.OnClick(b);
        }
        void ShowMenu(Group g)
        {
            var m = new CtlMenu();
            var exe = g.Exe ?? CtlTasks.Target(g.Pin, this);
            string name = g.Wins.Count == 1 ? g.Wins[0].Title : Path.GetFileNameWithoutExtension(g.Pin ?? g.Exe ?? "App");
            m.Header(name);
            if (g.Wins.Count > 1) { m.Sep(); foreach (var w in g.Wins.Take(12)) { var h = w.Hwnd; m.Item(w.Title, () => CtlTasks.Click(h)); } }
            m.Sep();
            if (g.Pin != null || exe != null) m.Item("New window", () => Launch(g));
            if (g.Pin != null) m.Item("Unpin from taskbar", () => { var l = Pinned; l.RemoveAll(p => string.Equals(p, g.Pin, StringComparison.OrdinalIgnoreCase)); SetPinned(l); });
            else if (exe != null) m.Item("Pin to taskbar", () => { var l = Pinned; if (!l.Contains(exe, StringComparer.OrdinalIgnoreCase)) l.Add(exe); SetPinned(l); });
            var loc = g.Pin ?? exe;
            if (loc != null) m.Item("Open file location", () => Ctl.Open("explorer.exe", "/select,\"" + loc + "\""));
            if (g.Wins.Count > 0)
            {
                m.Sep();
                if (g.Wins.Count == 1) { var h = g.Wins[0].Hwnd; m.Item("Close window", () => CtlTasks.Close(h)); }
                else { var hs = g.Wins.Select(w => w.Hwnd).ToList(); m.Item("Close all windows", () => { foreach (var h in hs) CtlTasks.Close(h); }); }
            }
            m.Show();
        }

        // ---------------- panel ----------------
        public override Size PanelSize(float s)
        {
            int n = Math.Max(1, Math.Min(9, Snap.Wins.Count));
            return new Size((int)(380 * s), (int)((48 + 30 + 52 + 34 + 22 + n * 40 + 10) * s));
        }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var snap = Snap; int px = c.Si(24);
            float y = c.PanelHeader("Apps", snap.Wins.Count + " open", Icons.Apps);
            Ui.Text(c.G, "PINNED", c.F(10.5f, true), c.T.TextFaint, new RectangleF(b.X + c.S(16), y, c.S(120), c.S(18)));
            c.Button(new RectangleF(b.Right - c.S(76), y - c.S(2), c.S(60), c.S(22)), editing ? "Done" : "Edit", () => { editing = !editing; Invalidate(); }, editing);
            y += c.S(24);
            float x = b.X + c.S(12); var pins = Pinned;
            for (int i = 0; i < pins.Count && x < b.Right - c.S(50); i++)
            {
                var r = new RectangleF(x, y, c.S(42), c.S(42)); var p = pins[i]; int idx = i;
                Ui.FillRound(c.G, r, c.S(10), c.IsHover(r) ? c.T.TileHover : c.T.Tile);
                DrawAppIcon(c, CtlIcons.PeekFile(p, px, this), new RectangleF(r.X + (r.Width - px) / 2, r.Y + (r.Height - px) / 2, px, px), p);
                if (editing)
                {
                    var xr = new RectangleF(r.Right - c.S(14), r.Y - c.S(4), c.S(18), c.S(18));
                    Ui.FillCircle(c.G, new PointF(xr.X + xr.Width / 2, xr.Y + xr.Height / 2), c.S(8), c.T.Bad);
                    Ui.Icon(c.G, "\uE711", xr, Color.White, c.S(8));
                    c.Hit(r, () => { var l = Pinned; if (idx < l.Count) { l.RemoveAt(idx); SetPinned(l); } });
                    if (i > 0) c.Hit(new RectangleF(r.X - c.S(4), r.Bottom - c.S(14), c.S(16), c.S(16)), () => { var l = Pinned; if (idx < l.Count && idx > 0) { var t = l[idx]; l.RemoveAt(idx); l.Insert(idx - 1, t); SetPinned(l); } });
                }
                else c.Hit(r, () => { Ctl.Open(p); Host.ClosePanel(); });
                x += c.S(48);
            }
            if (pins.Count == 0) Ui.Text(c.G, "Nothing pinned yet", c.F(12), c.T.TextDim, new RectangleF(x + c.S(4), y, c.S(200), c.S(42)));
            y += c.S(50);
            var ir = new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(26));
            Ui.FillRound(c.G, ir, c.S(8), c.T.Input);
            c.Input("add", Rectangle.Round(Ui.Inset(ir, c.S(8), c.S(3))), addMsg ?? "Pin an app: type its name or a path, Enter", AddPin);
            y += c.S(34);
            Ui.Text(c.G, "OPEN WINDOWS", c.F(10.5f, true), c.T.TextFaint, new RectangleF(b.X + c.S(16), y, c.S(160), c.S(18)));
            y += c.S(22);
            if (snap.Wins.Count == 0) Ui.Text(c.G, "No open windows", c.F(12), c.T.TextDim, new RectangleF(b.X + c.S(16), y, b.Width, c.S(36)));
            var fg = CtlTasks.Fg;
            foreach (var w in snap.Wins.Take(9))
            {
                var r = new RectangleF(b.X + c.S(8), y, b.Width - c.S(16), c.S(38));
                var hw = w.Hwnd; bool isFg = hw == fg;
                bool hov = Ctl.Row(c, r, () => CtlTasks.Click(hw));
                if (isFg && !hov) Ui.FillRound(c.G, r, c.S(8), c.T.AccentA(28));
                if (CtlTasks.IsFlashing(hw)) Ui.FillRound(c.G, new RectangleF(r.X, r.Y + c.S(8), c.S(3), r.Height - c.S(16)), c.S(1.5f), c.T.Warn);
                int ipx = c.Si(20);
                DrawAppIcon(c, w.Icon, new RectangleF(r.X + c.S(10), r.Y + (r.Height - ipx) / 2, ipx, ipx), w.Title, w.Min);
                float tx = r.X + c.S(40);
                Ui.Text(c.G, w.Title, c.FS(12.5f), w.Min ? c.T.TextDim : c.T.Text, new RectangleF(tx, r.Y + c.S(3), r.Right - tx - c.S(70), c.S(18)));
                Ui.Text(c.G, (w.Exe != null ? Path.GetFileNameWithoutExtension(w.Exe) : "") + (w.Min ? " · minimized" : ""), c.F(10.5f), c.T.TextDim, new RectangleF(tx, r.Y + c.S(20), r.Right - tx - c.S(70), c.S(14)));
                if (hov)
                {
                    var exe = w.Exe;
                    if (exe != null)
                    {
                        bool pinned = IsPinnedExe(exe);
                        c.IconButton(new RectangleF(r.Right - c.S(62), r.Y + c.S(5), c.S(28), c.S(28)), pinned ? "\uE77A" : "\uE718", () =>
                        {
                            var l = Pinned;
                            if (pinned) l.RemoveAll(p => string.Equals(CtlTasks.Target(p, this), exe, StringComparison.OrdinalIgnoreCase)); else l.Add(exe);
                            SetPinned(l);
                        }, false, 12);
                    }
                    c.IconButton(new RectangleF(r.Right - c.S(32), r.Y + c.S(5), c.S(28), c.S(28)), "\uE711", () => CtlTasks.Close(hw), false, 11);
                }
                y += c.S(40);
            }
        }
        void AddPin(string text)
        {
            text = (text ?? "").Trim().Trim('"'); if (text.Length == 0) return;
            string path = null;
            var exp = Environment.ExpandEnvironmentVariables(text);
            if (File.Exists(exp) || Directory.Exists(exp)) path = exp;
            else
            {
                var q = text.ToLowerInvariant();
                var best = CtlStartIndex.Items.Select(e => new { e, s = CtlStartIndex.Score(e.Lower, q) }).Where(z => z.s > 0).OrderByDescending(z => z.s).FirstOrDefault();
                if (best != null) path = best.e.Path;
            }
            if (path == null) { addMsg = "Not found: " + text; Invalidate(); return; }
            var l = Pinned; if (!l.Contains(path, StringComparer.OrdinalIgnoreCase)) l.Add(path);
            addMsg = "Pinned " + Path.GetFileNameWithoutExtension(path); SetPinned(l);
        }
    }
}
