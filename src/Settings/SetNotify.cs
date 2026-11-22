// Settings page: Notifications (NovaBar's own toasts + notification center replace the Windows ones).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace NovaBar
{
    class SetNotifyPage : SetPage
    {
        public override string Title { get { return "Notifications"; } }
        public override string Subtitle { get { return "NovaBar shows its own banners and notification center instead of Windows'"; } }

        bool B(string k, bool def) { object v; return Cfg.Extra.TryGetValue(k, out v) && v != null ? J.AsBool(v, def) : def; }
        double N(string k, double def) { object v; return Cfg.Extra.TryGetValue(k, out v) && v != null ? J.AsNum(v, def) : def; }
        void Set(string k, object v) { Cfg.Extra[k] = v; W.Changed(); }

        public override float Paint(SetG g, float x, float y, float w)
        {
            bool replace = B("notify.replace", true);
            y = g.Section(x, y, w, "Notifications");
            var a = g.Row(ref y, x, w, "", "Replace Windows notifications", "Show notifications as NovaBar banners and keep them in the bar's notification center");
            g.Toggle(a, replace, v => Set("notify.replace", v));
            a = g.Row(ref y, x, w, Icons.Quiet, "Do not disturb", "Collect notifications silently without showing banners");
            g.Toggle(a, B("notify.dnd", false), v => Set("notify.dnd", v));
            a = g.Row(ref y, x, w, Icons.Volume, "Play sound", "Play a short sound when a banner appears");
            g.Toggle(a, B("notify.sound", true), v => Set("notify.sound", v));
            a = g.Row(ref y, x, w, "", "Hide Windows' own banners while active", "Prevents seeing every notification twice");
            g.Toggle(a, B("notify.suppressBanners", true), v => Set("notify.suppressBanners", v));

            y += g.S(18);
            y = g.Section(x, y, w, "Banners");
            a = g.Row(ref y, x, w, Icons.Timer, "Banner duration", "Seconds a banner stays on screen");
            double d = N("notify.duration", 6);
            var ir = new RectangleF(a.Right - g.S(196), a.Y + (a.Height - g.S(34)) / 2, g.S(120), g.S(34));
            g.Input("notify.duration", ir, d.ToString("0", CultureInfo.InvariantCulture), "6", s =>
            {
                double p; if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out p)) Set("notify.duration", Math.Max(2, Math.Min(60, Math.Round(p))));
            });
            g.Button(new RectangleF(ir.Right + g.S(6), ir.Y, g.S(34), g.S(34)), null, () => { W.EndEdit(); Set("notify.duration", Math.Max(2, d - 1)); }, false, "");
            g.Button(new RectangleF(ir.Right + g.S(42), ir.Y, g.S(34), g.S(34)), null, () => { W.EndEdit(); Set("notify.duration", Math.Min(60, d + 1)); }, false, Icons.Add);
            a = g.Row(ref y, x, w, "", "Banner position", "Follows the bar \u2014 " + (Cfg.Position == "top" ? "top-right corner" : Cfg.Position == "right" ? "bottom-left corner" : "bottom-right corner") + ", next to it", 0, () => W.Navigate("dock"));
            Ui.Icon(g.G, Icons.ChevronRight, new RectangleF(a.Right - g.S(20), a.Y, g.S(20), a.Height), W.P.Dim, g.S(12));
            a = g.Row(ref y, x, w, Icons.Bell, "Test", replace ? "Shows a sample banner from the running bar" : "Turn on “Replace Windows notifications” first");
            float bw = g.ButtonWidth("Send test notification", Icons.Bell);
            g.Button(new RectangleF(a.Right - bw, a.Y + (a.Height - g.S(34)) / 2, bw, g.S(34)), "Send test notification", SendTest, true, Icons.Bell, replace);
            return y;
        }

        void SendTest()
        {
            try
            {
                W.SaveNow();
                File.WriteAllText(Path.Combine(Paths.Root, "notify-test.flag"), DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                W.Toast("Test notification sent to the bar");
            }
            catch (Exception e) { W.Toast("Couldn't send: " + e.Message); }
        }
    }
}
