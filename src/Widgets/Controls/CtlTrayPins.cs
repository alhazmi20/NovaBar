// Tray pin/hide state + the small native hook the tray widget puts on its own panel window.
//   * CtlTrayPins: which tray icons are pinned inline on the bar tile (the rest live in the overflow panel), and the
//     order of the pinned ones. Persisted in StateStore "ctl.tray.pins", shared by every tray instance/monitor.
//     Identity is stable across app restarts: "g:<guid>" for NIF_GUID icons, else "<exe file name>|<uID>".
//     New icons follow Explorer's own choice (HKCU\Control Panel\NotifyIconSettings\<id>\IsPromoted) when known.
//   * CtlTrayPanelHook: NativeWindow subclass of the tray's PanelForm. The host panel only reports left clicks, so the
//     hook adds right-click (= the app's own context menu), drag-to-reorder in Customize mode, and keeps the panel
//     open while an app's context menu (opened from the panel) is on screen, closing it once the menu goes away.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NovaBar
{
    public static class CtlTrayPins
    {
        const string Store = "ctl.tray.pins";
        static Dictionary<string, bool> pins;          // key -> pinned (only icons the user or a default decided)
        static List<string> order;                      // pinned keys in display order
        public static int Version;                      // bumps on every change (cheap cache key for the widget)

        public static string KeyOf(CtlTrayIcon ic)
        {
            if (ic.HasGuid) return "g:" + ic.Guid.ToString("D").ToLowerInvariant();
            var exe = !string.IsNullOrEmpty(ic.Path) ? System.IO.Path.GetFileName(ic.Path) : (ic.Exe ?? "") + ".exe";
            return exe.ToLowerInvariant() + "|" + (ic.Fallback ? "*" : ic.UID.ToString());
        }

        static void Load()
        {
            if (pins != null) return;
            pins = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); order = new List<string>();
            try
            {
                var d = StateStore.Load(Store);
                var p = J.Obj(d, "pins");
                if (p != null) foreach (var kv in p) pins[kv.Key] = J.AsBool(kv.Value);
                var o = J.Arr(d, "order");
                if (o != null) foreach (var x in o) { var s = J.AsStr(x); if (s.Length > 0 && !order.Contains(s)) order.Add(s); }
            }
            catch { }
        }
        static void Save()
        {
            Version++;
            var p = new Dictionary<string, object>();
            foreach (var kv in pins) p[kv.Key] = kv.Value;
            var d = new Dictionary<string, object> { { "pins", p }, { "order", order.Cast<object>().ToList() } };
            StateStore.Save(Store, d);
        }

        /// <summary>Pinned state of an icon; decides (and remembers) a default the first time an icon is seen.</summary>
        public static bool IsPinned(CtlTrayIcon ic)
        {
            Load();
            if (ic.PinKey == null) ic.PinKey = KeyOf(ic);
            bool v;
            if (pins.TryGetValue(ic.PinKey, out v)) return v;
            v = DefaultPinned(ic);
            pins[ic.PinKey] = v;
            if (v && !order.Contains(ic.PinKey)) order.Add(ic.PinKey);
            Save();
            return v;
        }
        public static void SetPinned(CtlTrayIcon ic, bool on)
        {
            Load();
            if (ic.PinKey == null) ic.PinKey = KeyOf(ic);
            pins[ic.PinKey] = on;
            order.Remove(ic.PinKey);
            if (on) order.Add(ic.PinKey);
            Save();
        }
        public static int OrderOf(CtlTrayIcon ic)
        {
            Load();
            if (ic.PinKey == null) ic.PinKey = KeyOf(ic);
            int i = order.IndexOf(ic.PinKey); return i < 0 ? int.MaxValue : i;
        }
        /// <summary>Move a pinned icon so it sits before <paramref name="before"/> (null = at the end).</summary>
        public static void MoveBefore(CtlTrayIcon ic, CtlTrayIcon before)
        {
            Load();
            if (ic.PinKey == null) ic.PinKey = KeyOf(ic);
            order.Remove(ic.PinKey);
            int i = before != null ? order.IndexOf(before.PinKey ?? KeyOf(before)) : -1;
            if (i < 0) order.Add(ic.PinKey); else order.Insert(i, ic.PinKey);
            pins[ic.PinKey] = true;
            Save();
        }

        // ---------------- defaults: Explorer's own promotion state ----------------
        static Dictionary<string, int> promo;           // key (same format as KeyOf) -> IsPromoted (0/1)
        static bool DefaultPinned(CtlTrayIcon ic)
        {
            if (ic.Fallback) return ic.Promoted;
            if (promo == null) LoadPromo();
            int p;
            if (promo.TryGetValue(ic.PinKey, out p)) return p != 0;
            // Explorer has no opinion yet: keep Windows' own system icons (Bluetooth, safely remove, …) visible
            return string.Equals(ic.Exe, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        static void LoadPromo()
        {
            promo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
                {
                    if (root == null) return;
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = root.OpenSubKey(sub))
                            {
                                var ip = k.GetValue("IsPromoted"); if (ip == null) continue;
                                int v = Convert.ToInt32(ip);
                                var g = k.GetValue("IconGuid") as string;
                                if (!string.IsNullOrEmpty(g)) { Guid gg; if (Guid.TryParse(g, out gg)) promo["g:" + gg.ToString("D").ToLowerInvariant()] = v; continue; }
                                var ep = k.GetValue("ExecutablePath") as string; if (string.IsNullOrEmpty(ep)) continue;
                                var uid = k.GetValue("UID");
                                promo[System.IO.Path.GetFileName(ep).ToLowerInvariant() + "|" + (uid != null ? unchecked((uint)Convert.ToInt32(uid)).ToString() : "0")] = v;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>Subclass of the tray widget's panel window (see file header).</summary>
    public class CtlTrayPanelHook : NativeWindow
    {
        [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr h);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        const int WM_ACTIVATE = 0x06, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x7B;

        public Func<Point, bool> RightClick;            // client point; true = handled
        public Func<Point, bool> DragStart;             // true = a drag may start here
        public Action<Point> DragMove;
        public Action<Point, bool> DragEnd;             // (point, moved)
        public Action CloseRequest;

        Point downPt; bool armed, moved;
        uint holdPid; int holdStart; bool holding, menuSeen; Timer holdTimer;

        public Form Form { get { return Control.FromHandle(Handle) as Form; } }
        public bool Holding { get { return holding; } }

        /// <summary>Keep the panel open while process <paramref name="pid"/> shows a context menu; close it afterwards.</summary>
        public void HoldFor(uint pid)
        {
            holdPid = pid; holdStart = Environment.TickCount; holding = true; menuSeen = false;
            if (holdTimer == null) { holdTimer = new Timer { Interval = 120 }; holdTimer.Tick += (o, e) => HoldTick(); }
            holdTimer.Start();
        }
        void StopHold() { holding = false; if (holdTimer != null) holdTimer.Stop(); }

        void HoldTick()
        {
            if (Handle == IntPtr.Zero) { StopHold(); return; }
            var fg = Native.GetForegroundWindow();
            if (fg == Handle) { StopHold(); return; }                         // user came back to the panel
            bool up = MenuVisible(holdPid);
            if (!up && fg != IntPtr.Zero)
            {
                uint fp; Native.GetWindowThreadProcessId(fg, out fp);
                // apps with custom (non-#32768) popup menus: a visible topmost/tool window of the same process
                if (fp == holdPid && Native.IsWindowVisible(fg) && (Native.GetWindowLong(fg, -20) & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST)) != 0) up = true;
            }
            int el = Environment.TickCount - holdStart;
            if (up) { menuSeen = true; if (el < 120000) return; }
            else if (!menuSeen && el < 1500) return;                         // the menu may take a moment to appear
            StopHold();
            if (CloseRequest != null) CloseRequest();
        }
        static bool MenuVisible(uint pid)
        {
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, "#32768", null)) != IntPtr.Zero)
            {
                if (!Native.IsWindowVisible(h)) continue;
                uint p; Native.GetWindowThreadProcessId(h, out p);
                if (p == pid) return true;
            }
            return false;
        }

        static Point Pt(IntPtr l) { int v = unchecked((int)(long)l); return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF)); }

        protected override void WndProc(ref Message m)
        {
            try
            {
                switch (m.Msg)
                {
                    case WM_ACTIVATE:
                        // swallow the deactivation (which would close the panel) while the app's menu is up
                        if (holding && ((long)m.WParam & 0xFFFF) == 0) { m.Result = IntPtr.Zero; return; }
                        break;
                    case WM_RBUTTONDOWN: return;
                    case WM_RBUTTONUP:
                        if (RightClick != null && RightClick(Pt(m.LParam))) { m.Result = IntPtr.Zero; return; }
                        break;
                    case WM_CONTEXTMENU: return;
                    case WM_LBUTTONDOWN:
                        armed = DragStart != null && DragStart(Pt(m.LParam)); moved = false; downPt = Pt(m.LParam);
                        break;
                    case WM_MOUSEMOVE:
                        if (armed && ((long)m.WParam & 1) != 0)
                        {
                            var p = Pt(m.LParam);
                            if (!moved && (Math.Abs(p.X - downPt.X) > 4 || Math.Abs(p.Y - downPt.Y) > 4)) { moved = true; SetCapture(Handle); }
                            if (moved) { if (DragMove != null) DragMove(p); return; }
                        }
                        break;
                    case WM_LBUTTONUP:
                        if (armed)
                        {
                            armed = false;
                            if (moved) { ReleaseCapture(); if (DragEnd != null) DragEnd(Pt(m.LParam), true); m.Result = IntPtr.Zero; return; }   // a drag is not a click
                            if (DragEnd != null) DragEnd(Pt(m.LParam), false);
                        }
                        break;
                }
            }
            catch { }
            base.WndProc(ref m);
        }

        protected override void OnHandleChange() { base.OnHandleChange(); if (Handle == IntPtr.Zero) StopHold(); }
        public void Detach() { StopHold(); if (holdTimer != null) { holdTimer.Dispose(); holdTimer = null; } try { if (Handle != IntPtr.Zero) ReleaseHandle(); } catch { } }
    }
}
