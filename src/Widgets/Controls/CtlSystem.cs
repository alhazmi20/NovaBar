// System one-click controls: language, theme toggle, do-not-disturb, show desktop, keyboard.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NovaBar
{
    /// <summary>Base for compact icon tiles (optionally with a short label and an "active" accent state).</summary>
    public abstract class CtlIconTile : Widget
    {
        protected abstract string Glyph { get; }
        protected virtual string Label { get { return null; } }
        protected virtual bool Active { get { return false; } }
        protected virtual bool ShowLabel { get { return SettingDefs.Any(d => d.Key == "label") && SBool("label"); } }
        public override int Measure(DrawCtx c)
        {
            if (ShowLabel && Label != null) return c.Si(34) + Ui.MeasureW(c.G, Label, c.FS(12)) + c.Si(4);
            return c.Si(38);
        }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var col = Active ? c.T.Accent : c.T.Text;
            if (Active) Ui.FillRound(c.G, Ui.Inset(b, c.S(3), c.S(3)), c.S(8), c.T.AccentA(c.T.Dark ? 46 : 34));
            if (ShowLabel && Label != null)
            {
                var ir = new RectangleF(b.X + c.S(10), b.Y, c.S(18), b.Height);
                Ui.Icon(c.G, Glyph, ir, col, c.S(15));
                Ui.Text(c.G, Label, c.FS(12), c.T.Text, new RectangleF(ir.Right + c.S(6), b.Y, b.Right - ir.Right - c.S(8), b.Height));
            }
            else Ui.Icon(c.G, Glyph, b, col, c.S(16));
        }
    }

    // ------------------------------------------------------------------ Language
    public class CtlLangSnap
    {
        public IntPtr Hkl; public string Code, Name;
        public override bool Equals(object o) { var x = o as CtlLangSnap; return x != null && x.Hkl == Hkl; }
        public override int GetHashCode() { return Hkl.GetHashCode(); }
    }

    [Widget("language", "Language", "System", "The current keyboard layout; click to switch.", RefName = "Language", Data = DataKind.Live)]
    public class CtlLanguageWidget : Widget
    {
        const string Key = "ctl.lang";
        static IntPtr target;  // last foreground window that is not ours
        static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        class Layout { public IntPtr Hkl; public string Code, Name, LayoutName; }
        List<Layout> layouts;

        static CtlLanguageWidget() { Hub.Define(Key, 1000, Sample); }
        static object Sample()
        {
            var fg = Native.GetForegroundWindow();
            uint pid, tid;
            if (fg != IntPtr.Zero && Native.GetWindowThreadProcessId(fg, out pid) != 0 && pid != ownPid) target = fg;
            if (target == IntPtr.Zero) return Hub.Get(Key) ?? Describe(Native.GetKeyboardLayout(0));
            uint p2; tid = Native.GetWindowThreadProcessId(target, out p2);
            return Describe(Native.GetKeyboardLayout(tid));
        }
        static string CodeOf(IntPtr hkl) { try { return new CultureInfo((int)((long)hkl & 0xFFFF)).TwoLetterISOLanguageName.ToUpperInvariant(); } catch { return "??"; } }
        static CtlLangSnap Describe(IntPtr hkl)
        {
            var s = new CtlLangSnap { Hkl = hkl, Code = CodeOf(hkl), Name = "" };
            try { s.Name = new CultureInfo((int)((long)hkl & 0xFFFF)).DisplayName; } catch { }
            return s;
        }

        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Choice("click", "Click action", "Switch layout", "Switch layout", "Show list") }; } }
        public override void OnStart() { Hub.Subscribe(Key, this); }
        public override void OnStop() { Hub.Unsubscribe(Key, this); }
        public override int Measure(DrawCtx c) { return c.Si(50); }
        public override void Draw(DrawCtx c)
        {
            var s = Hub.Get<CtlLangSnap>(Key); var b = c.Bounds;
            var code = s != null ? s.Code : "EN";
            var ir = new RectangleF(b.X + c.S(8), b.Y, c.S(14), b.Height);
            Ui.Icon(c.G, Icons.Keyboard, ir, c.T.TextDim, c.S(12));
            Ui.Text(c.G, code, c.F(12.5f, true), c.T.Text, new RectangleF(ir.Right + c.S(3), b.Y, b.Right - ir.Right - c.S(6), b.Height));
        }
        public override void OnClick(MouseButtons b)
        {
            if (b == MouseButtons.Right || SStr("click") == "Show list") { if (Host.IsPanelOpen(this)) Host.ClosePanel(); else Host.OpenPanel(this); return; }
            if (b == MouseButtons.Left) Switch(new IntPtr(1) /*HKL_NEXT*/);
        }
        void Switch(IntPtr hkl)
        {
            var t = target;
            if (t == IntPtr.Zero) return;
            Native.PostMessage(t, 0x0050 /*WM_INPUTLANGCHANGEREQUEST*/, IntPtr.Zero, hkl);
            Ctl.Later(150, () => Hub.Refresh(Key));
            Ctl.Later(500, () => Hub.Refresh(Key));
        }

        public override Size PanelSize(float s)
        {
            int n = layouts != null ? layouts.Count : 3;
            return new Size((int)(300 * s), (int)((58 + Math.Max(1, Math.Min(8, n)) * 40 + 46) * s));
        }
        public override void OnPanelOpen()
        {
            var l = new List<Layout>();
            try
            {
                foreach (InputLanguage il in InputLanguage.InstalledInputLanguages)
                    l.Add(new Layout { Hkl = il.Handle, Code = CodeOf(il.Handle), Name = il.Culture.DisplayName, LayoutName = il.LayoutName });
            }
            catch { }
            layouts = l; Hub.Refresh(Key);
        }
        public override void DrawPanel(DrawCtx c)
        {
            var s = Hub.Get<CtlLangSnap>(Key);
            float y = c.PanelHeader("Keyboard layout", s != null ? s.Code : null, Icons.Language);
            var b = c.Bounds;
            var list = layouts ?? new List<Layout>();
            if (list.Count == 0) Ui.Text(c.G, "No layouts found", c.F(12), c.T.TextDim, new RectangleF(b.X + c.S(16), y, b.Width - c.S(32), c.S(30)));
            foreach (var l in list.Take(8))
            {
                var r = new RectangleF(b.X + c.S(8), y, b.Width - c.S(16), c.S(38));
                bool cur = s != null && s.Hkl == l.Hkl;
                var lay = l; Ctl.Row(c, r, () => Switch(lay.Hkl));
                var badge = new RectangleF(r.X + c.S(8), r.Y + c.S(7), c.S(30), c.S(24));
                Ui.FillRound(c.G, badge, c.S(6), cur ? c.T.Accent : c.T.Track);
                Ui.Text(c.G, l.Code, c.F(11, true), cur ? Color.White : c.T.Text, badge, 0);
                var tx = badge.Right + c.S(10);
                Ui.Text(c.G, l.Name, c.FS(12.5f), c.T.Text, new RectangleF(tx, r.Y + c.S(3), r.Right - tx - c.S(30), c.S(18)));
                Ui.Text(c.G, l.LayoutName, c.F(11), c.T.TextDim, new RectangleF(tx, r.Y + c.S(19), r.Right - tx - c.S(30), c.S(16)));
                if (cur) Ui.Icon(c.G, Icons.Check, new RectangleF(r.Right - c.S(28), r.Y, c.S(20), r.Height), c.T.Accent, c.S(13));
                y += c.S(40);
            }
            var br = new RectangleF(b.X + c.S(16), b.Bottom - c.S(42), b.Width - c.S(32), c.S(30));
            c.Button(br, "Language settings", () => Ctl.Open("ms-settings:regionlanguage"), false, Icons.Settings);
        }
    }

    // ------------------------------------------------------------------ Theme toggle
    [Widget("themetoggle", "Theme Toggle", "System", "Switch Windows between light and dark mode.", RefName = "Theme Toggle", Data = DataKind.Live)]
    public class CtlThemeToggleWidget : CtlIconTile
    {
        const string Key = "ctl.theme";
        const string Reg = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        static CtlThemeToggleWidget() { Hub.Define(Key, 5000, () => (object)ReadLight()); }
        static bool ReadLight()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Reg)) { var v = k == null ? null : k.GetValue("AppsUseLightTheme"); return v == null || Convert.ToInt32(v) != 0; }
        }
        bool? optimistic;
        bool Light { get { if (optimistic.HasValue) return optimistic.Value; var v = Hub.Get(Key); return v is bool ? (bool)v : true; } }
        public override SettingDef[] SettingDefs
        {
            get { return new[] { SettingDef.Choice("scope", "Apply to", "Apps and Windows", "Apps and Windows", "Apps only", "Windows only"), SettingDef.Toggle("label", "Show label", false) }; }
        }
        protected override string Glyph { get { return Light ? Icons.Sun : Icons.Moon; } }
        protected override string Label { get { return Light ? "Light" : "Dark"; } }
        public override void OnStart() { Hub.Subscribe(Key, this); }
        public override void OnStop() { Hub.Unsubscribe(Key, this); }
        public override void AddMenuItems(List<KeyValuePair<string, Action>> items) { items.Add(new KeyValuePair<string, Action>("Color settings", () => Ctl.Open("ms-settings:colors"))); }
        public override void OnClick(MouseButtons b)
        {
            if (b == MouseButtons.Right) { Ctl.Open("ms-settings:colors"); return; }
            if (b != MouseButtons.Left) return;
            bool to = !Light; optimistic = to; Invalidate();
            var scope = SStr("scope");
            Ctl.Async(() =>
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Reg))
                {
                    if (scope != "Windows only") k.SetValue("AppsUseLightTheme", to ? 1 : 0, RegistryValueKind.DWord);
                    if (scope != "Apps only") k.SetValue("SystemUsesLightTheme", to ? 1 : 0, RegistryValueKind.DWord);
                }
                IntPtr r;
                CtlNative.SendMessageTimeout(new IntPtr(0xFFFF), Native.WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", CtlNative.SMTO_ABORTIFHUNG, 200, out r);
                Hub.Refresh(Key);
                System.Threading.Thread.Sleep(400); optimistic = null; Invalidate();
            });
        }
    }

    // ------------------------------------------------------------------ Do not disturb
    [Widget("dnd", "Do Not Disturb", "System", "Silence Windows notifications in one click.", RefName = "Do Not Disturb", Data = DataKind.Live)]
    public class CtlDndWidget : CtlIconTile
    {
        // Limitation: Windows has no public DND API for desktop apps. We flip the documented per-user switch
        // NOC_GLOBAL_SETTING_TOASTS_ENABLED (0 = banners off). Windows' own Focus/DND state (quiet hours) is separate.
        const string Key = "ctl.dnd";
        const string Reg = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        static CtlDndWidget() { Hub.Define(Key, 5000, () => (object)ReadOn()); }
        static bool ReadOn()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Reg)) { var v = k == null ? null : k.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED"); return v != null && Convert.ToInt32(v) == 0; }
        }
        bool? optimistic;
        bool On { get { if (optimistic.HasValue) return optimistic.Value; var v = Hub.Get(Key); return v is bool && (bool)v; } }
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Toggle("label", "Show label", false) }; } }
        protected override string Glyph { get { return On ? Icons.Quiet : Icons.Bell; } }
        protected override string Label { get { return On ? "Silenced" : "Alerts on"; } }
        protected override bool Active { get { return On; } }
        public override void OnStart() { Hub.Subscribe(Key, this); }
        public override void OnStop() { Hub.Unsubscribe(Key, this); }
        public override void AddMenuItems(List<KeyValuePair<string, Action>> items) { items.Add(new KeyValuePair<string, Action>("Notification settings", () => Ctl.Open("ms-settings:notifications"))); }
        public override void OnClick(MouseButtons b)
        {
            if (b == MouseButtons.Right) { Ctl.Open("ms-settings:notifications"); return; }
            if (b != MouseButtons.Left) return;
            bool to = !On; optimistic = to; Invalidate();
            Ctl.Async(() =>
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Reg)) k.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", to ? 0 : 1, RegistryValueKind.DWord);
                IntPtr r;
                CtlNative.SendMessageTimeout(new IntPtr(0xFFFF), Native.WM_SETTINGCHANGE, IntPtr.Zero, "Notifications", CtlNative.SMTO_ABORTIFHUNG, 200, out r);
                Hub.Refresh(Key); System.Threading.Thread.Sleep(300); optimistic = null; Invalidate();
            });
        }
    }

    // ------------------------------------------------------------------ Show desktop
    [Widget("showdesktop", "Show Desktop", "System", "Hide every window, and bring them back.", RefName = "Show Desktop", Data = DataKind.Live)]
    public class CtlShowDesktopWidget : CtlIconTile
    {
        protected override string Glyph { get { return Icons.Desktop; } }
        protected override string Label { get { return "Desktop"; } }
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Toggle("label", "Show label", false) }; } }
        public override void OnClick(MouseButtons b)
        {
            if (b != MouseButtons.Left) return;
            Ctl.Async(() =>
            {
                bool ok = Ctl.Sta(() =>
                {
                    var t = Type.GetTypeFromProgID("Shell.Application"); if (t == null) return false;
                    object o = Activator.CreateInstance(t);
                    try { ((dynamic)o).ToggleDesktop(); return true; } finally { Marshal.FinalReleaseComObject(o); }
                }, 3000);
                if (!ok) Native.KeyChord(Native.VK_LWIN, 0x44 /*D*/);
            });
        }
    }

    // ------------------------------------------------------------------ On-screen keyboard
    [ComImport, Guid("37c994e7-432b-4834-a2f7-dce1f13b834b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface CtlITipInvocation { void Toggle(IntPtr hwnd); }

    [Widget("keyboard", "Keyboard", "System", "The on-screen keyboard, one click away.", RefName = "Keyboard", Data = DataKind.Live)]
    public class CtlKeyboardWidget : CtlIconTile
    {
        protected override string Glyph { get { return Icons.Keyboard; } }
        protected override string Label { get { return "Keyboard"; } }
        public override SettingDef[] SettingDefs { get { return new[] { SettingDef.Choice("kind", "Keyboard", "Touch keyboard", "Touch keyboard", "Classic (osk)"), SettingDef.Toggle("label", "Show label", false) }; } }
        public override void AddMenuItems(List<KeyValuePair<string, Action>> items) { items.Add(new KeyValuePair<string, Action>("Classic on-screen keyboard", () => Ctl.Open("osk.exe"))); }
        public override void OnClick(MouseButtons b)
        {
            if (b == MouseButtons.Right) { Ctl.Open("osk.exe"); return; }
            if (b != MouseButtons.Left) return;
            bool touch = SStr("kind") != "Classic (osk)";
            Ctl.Async(() => { if (!touch || !ToggleTouch()) Ctl.Open("osk.exe"); });
        }
        static bool TryToggle()
        {
            return Ctl.Sta(() =>
            {
                var t = Type.GetTypeFromCLSID(new Guid("4ce576fa-83dc-4F88-951c-9d0782b4e376")); if (t == null) return false;
                object o = Activator.CreateInstance(t);
                try { ((CtlITipInvocation)o).Toggle(CtlNative.GetDesktopWindow()); return true; } finally { Marshal.ReleaseComObject(o); }
            }, 3000);
        }
        static bool ToggleTouch()
        {
            if (TryToggle()) return true;
            var tip = @"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe";
            if (!File.Exists(tip)) return false;
            try { Process.Start(new ProcessStartInfo(tip) { UseShellExecute = true }); } catch { return false; }
            for (int i = 0; i < 6; i++) { System.Threading.Thread.Sleep(250); if (TryToggle()) return true; }
            return false;
        }
    }
}
