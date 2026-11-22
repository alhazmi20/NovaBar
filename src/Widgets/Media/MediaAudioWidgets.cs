// Audio Output + Microphone widgets (Core Audio, event-driven — see MediaCoreAudio.cs).
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    [Widget("audio", "Audio Output", "Media & Audio", "Volume for your current speakers or headphones.", RefName = "Audio Output", Data = DataKind.Live)]
    public class MediaAudioOutputWidget : Widget
    {
        const int MaxRows = 6;
        public override void OnStart() { MediaAudio.Start(this); }
        public override void OnStop() { MediaAudio.Stop(this); }
        public override void OnPanelOpen() { MediaAudio.RefreshDevices(); }

        public static string VolIcon(MediaEndpointSnap s)
        {
            if (!s.Ok || s.Muted) return Icons.Mute;
            return s.Volume < 0.01f ? Icons.Volume0 : s.Volume < 0.34f ? Icons.Volume1 : s.Volume < 0.67f ? Icons.Volume2 : Icons.Volume3;
        }
        static string ShortName(string n)
        {
            if (string.IsNullOrEmpty(n)) return "No output device";
            // "Speakers (Realtek(R) Audio)" -> keep as is; the tile ellipsizes
            return n;
        }

        public override int Measure(DrawCtx c) { return c.Si(158); }
        public override void Draw(DrawCtx c)
        {
            var s = MediaAudio.Output; var b = c.Bounds; var g = c.G; var T = c.T;
            float d = c.S(26);
            var ir = new RectangleF(b.X + c.S(4), b.Y + (b.Height - d) / 2, d, d);
            bool ih = c.IsHover(ir);
            if (ih) Ui.FillCircle(g, new PointF(ir.X + d / 2, ir.Y + d / 2), d / 2, T.TileHover);
            Ui.Icon(g, VolIcon(s), ir, s.Ok && s.Muted ? T.Bad : T.Text, c.S(14));
            if (s.Ok) c.Hit(ir, MediaAudio.ToggleOutputMute);

            float x = ir.Right + c.S(6);
            string pct = s.Ok ? (s.Muted ? "Muted" : Math.Round(s.Volume * 100) + "%") : "";
            var pf = c.FS(11.5f); float pw = Math.Max(c.S(30), Ui.MeasureW(g, "100%", pf));
            var prr = new RectangleF(b.Right - c.S(10) - pw, b.Y, pw, b.Height);
            float w = prr.X - c.S(8) - x;
            Ui.Text(g, ShortName(s.Name), c.F(10.5f), T.TextDim, new RectangleF(x, b.Y + c.S(6), w, c.S(13)));
            var bar = new RectangleF(x, b.Y + c.S(22), w, c.S(4));
            Ui.Progress(g, bar, s.Ok ? s.Volume : 0, s.Muted ? T.TextFaint : T.Accent, T.Track);
            Ui.Text(g, pct, s.Muted ? c.F(10.5f) : pf, s.Muted ? T.Bad : T.Text, prr, 1);
            c.Wheel(b, delta => MediaAudio.Nudge(delta));
        }

        public override Size PanelSize(float scale)
        {
            int n = Math.Max(1, Math.Min(MaxRows, MediaAudio.Devices.Count));
            return new Size((int)(330 * scale), (int)((14 + 28 + 54 + 26 + n * 38 + 12) * scale));
        }

        public override void DrawPanel(DrawCtx c)
        {
            var s = MediaAudio.Output; var b = c.Bounds; var g = c.G; var T = c.T;
            float y = c.PanelHeader("Sound output", s.Ok ? (s.Muted ? "Muted" : Math.Round(s.Volume * 100) + "%") : "", Icons.Volume3);
            float pad = c.S(16);

            // volume row: mute button + slider
            float rowH = c.S(40);
            var row = new RectangleF(b.X + pad, y + c.S(2), b.Width - 2 * pad, rowH);
            Ui.FillRound(g, row, c.S(12), T.Tile);
            var mb = new RectangleF(row.X + c.S(5), row.Y + c.S(5), rowH - c.S(10), rowH - c.S(10));
            c.IconButton(mb, VolIcon(s), MediaAudio.ToggleOutputMute, s.Ok && s.Muted, 14);
            var track = new RectangleF(mb.Right + c.S(10), row.Y + rowH / 2 - c.S(3), row.Right - c.S(16) - mb.Right - c.S(10), c.S(6));
            float frac = s.Ok ? s.Volume : 0;
            Ui.Progress(g, track, frac, s.Muted ? T.TextFaint : T.Accent, T.Track);
            var kc = new PointF(track.X + track.Width * frac, track.Y + track.Height / 2);
            bool sh = c.IsHover(new RectangleF(track.X - c.S(6), row.Y, track.Width + c.S(12), row.Height));
            if (sh) Ui.FillCircle(g, kc, c.S(10), Ui.WithA(T.Accent, 50));
            Ui.FillCircle(g, kc, c.S(7.5f), T.Dark ? Color.White : Color.FromArgb(255, 255, 255, 255));
            using (var p = new Pen(Ui.WithA(Color.Black, T.Dark ? 60 : 40), Math.Max(1f, c.S(0.8f)))) g.DrawEllipse(p, kc.X - c.S(7.5f), kc.Y - c.S(7.5f), c.S(15), c.S(15));
            if (s.Ok)
            {
                const int N = 50; float sw = (track.Width + c.S(8)) / N;
                for (int i = 0; i <= N - 1; i++)
                {
                    float v = i / (float)(N - 1);
                    c.Hit(new RectangleF(track.X - c.S(4) + i * sw, row.Y, sw + 0.5f, row.Height), () => MediaAudio.SetVolume(v));
                }
            }
            c.Wheel(Rectangle.Round(row), delta => MediaAudio.Nudge(delta));
            y = row.Bottom + c.S(14);

            Ui.Text(g, "OUTPUT DEVICE", c.F(10.5f), T.TextFaint, new RectangleF(b.X + pad, y, b.Width - 2 * pad, c.S(14)));
            y += c.S(20);
            var devs = MediaAudio.Devices;
            if (devs.Count == 0)
            {
                Ui.Text(g, s.Ok ? s.Name : "No output devices found", c.F(12.5f), T.TextDim, new RectangleF(b.X + pad, y, b.Width - 2 * pad, c.S(34)));
                return;
            }
            foreach (var dv in devs.Take(MaxRows))
            {
                var r = new RectangleF(b.X + c.S(8), y, b.Width - c.S(16), c.S(34));
                bool cur = dv.Id == s.Id; bool h = c.IsHover(r);
                if (cur) Ui.FillRound(g, r, c.S(10), T.AccentA(T.Dark ? 40 : 30));
                else if (h) Ui.FillRound(g, r, c.S(10), T.TileHover);
                string glyph = DeviceGlyph(dv.Name);
                Ui.Icon(g, glyph, new RectangleF(r.X + c.S(6), r.Y, c.S(24), r.Height), cur ? T.Accent : T.TextDim, c.S(14));
                Ui.Text(g, dv.Name, cur ? c.FS(12.5f) : c.F(12.5f), T.Text, new RectangleF(r.X + c.S(36), r.Y, r.Width - c.S(36) - c.S(30), r.Height));
                if (cur) Ui.Icon(g, Icons.Check, new RectangleF(r.Right - c.S(30), r.Y, c.S(24), r.Height), T.Accent, c.S(12));
                else { var id = dv.Id; c.Hit(r, () => MediaAudio.SetDefaultOutput(id)); }
                y += c.S(38);
            }
        }

        static string DeviceGlyph(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            if (n.Contains("headphone") || n.Contains("headset") || n.Contains("buds") || n.Contains("airpods") || n.Contains("hands-free")) return "";
            if (n.Contains("hdmi") || n.Contains("display") || n.Contains("monitor") || n.Contains("tv")) return Icons.Desktop;
            return ""; // speakers
        }
    }

    [Widget("mic", "Microphone", "Media & Audio", "A call's two buttons for every app at once: mute your microphone, and mute the sound.", RefName = "Microphone", Data = DataKind.Live)]
    public class MediaMicWidget : Widget
    {
        public override void OnStart() { MediaAudio.Start(this); }
        public override void OnStop() { MediaAudio.Stop(this); }
        public override int Measure(DrawCtx c) { return c.Si(72); }
        public override void OnClick(MouseButtons b) { }

        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; float d = c.S(26), gap = c.S(6);
            float x0 = b.X + (b.Width - 2 * d - gap) / 2, y = b.Y + (b.Height - d) / 2;
            var mic = MediaAudio.Mic; var spk = MediaAudio.Output;
            Btn(c, new RectangleF(x0, y, d, d), mic, mic.Ok && mic.Muted ? Icons.MicOff : Icons.Mic, MediaAudio.ToggleMicMute);
            Btn(c, new RectangleF(x0 + d + gap, y, d, d), spk, spk.Ok && spk.Muted ? Icons.Mute : Icons.Volume3, MediaAudio.ToggleOutputMute);
        }

        static void Btn(DrawCtx c, RectangleF r, MediaEndpointSnap s, string glyph, Action toggle)
        {
            var T = c.T; bool h = c.IsHover(r);
            var center = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            Color bg, fg;
            if (!s.Ok) { bg = T.Track; fg = T.TextFaint; }
            else if (s.Muted) { bg = h ? Ui.Mix(T.Bad, Color.White, 0.12) : T.Bad; fg = Color.White; }
            else { bg = h ? T.TileHover : Ui.WithA(T.Text, T.Dark ? 22 : 16); fg = T.Text; }
            Ui.FillCircle(c.G, center, r.Width / 2, bg);
            Ui.Icon(c.G, glyph, r, fg, c.S(12.5f));
            if (s.Ok) c.Hit(r, toggle);
        }
    }
}
