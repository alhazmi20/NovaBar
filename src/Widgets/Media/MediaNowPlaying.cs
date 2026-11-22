// Now Playing family: full tile, compact, mini (round button + progress ring), bar. All share one panel.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NovaBar
{
    /// <summary>Shared base: session subscription, 1 s progress tick only while playing, shared big panel.</summary>
    public abstract class MediaNowPlayingBase : Widget
    {
        protected virtual bool ShowsProgress { get { return true; } }
        public override void OnStart() { MediaSession.Start(this); }
        public override void OnStop() { MediaSession.Stop(this); }
        public override int RefreshMs
        {
            get
            {
                var s = MediaSession.Snap;
                bool need = s.Playing && s.HasTimeline && (ShowsProgress || Host == null || Host.IsPanelOpen(this));
                return need ? 1000 : 0;
            }
        }
        public override Size PanelSize(float scale) { return new Size((int)(340 * scale), (int)(250 * scale)); }
        public override void DrawPanel(DrawCtx c) { MediaDraw.Panel(c); }
    }

    public static class MediaDraw
    {
        public static Color Glow(DrawCtx c, MediaSnap s) { return s.ArtColor.IsEmpty ? c.T.Accent : s.ArtColor; }

        /// <summary>Album art (cached, rounded) or an elegant placeholder.</summary>
        public static void Art(DrawCtx c, RectangleF r, float radius, MediaSnap s)
        {
            var bmp = s.HasArt ? MediaSession.Art((int)Math.Round(r.Width), radius) : null;
            if (bmp != null) { c.G.DrawImage(bmp, new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y), bmp.Width, bmp.Height)); return; }
            var acc = c.T.Accent;
            using (var path = Ui.Round(r, radius))
            using (var br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Ui.WithA(acc, s.Has ? 120 : 70), Ui.WithA(Ui.Mix(acc, Color.FromArgb(255, 236, 72, 153), 0.55), s.Has ? 90 : 40), 45f))
                c.G.FillPath(br, path);
            Ui.Icon(c.G, Icons.Music, r, s.Has ? Color.White : c.T.TextDim, r.Width * 0.42f);
        }

        /// <summary>Soft colored halo behind the art (a few stacked translucent rounded rects — cheap, no blur).</summary>
        public static void Halo(DrawCtx c, RectangleF r, float radius, Color col, int strength)
        {
            for (int i = 10; i >= 1; i--)
            {
                float d = c.S(1.6f) * i;
                var rr = new RectangleF(r.X - d, r.Y - d + c.S(3), r.Width + 2 * d, r.Height + 2 * d);
                Ui.FillRound(c.G, rr, radius + d, Ui.WithA(col, strength));
            }
        }

        public static void PlayCircle(DrawCtx c, RectangleF r, MediaSnap s, float iconPx)
        {
            bool h = c.IsHover(r);
            var bg = s.Has ? (h ? Ui.Mix(c.T.Accent, Color.White, 0.15) : c.T.Accent) : (h ? c.T.TileHover : c.T.Track);
            Ui.FillCircle(c.G, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, bg);
            var ic = s.Playing ? Icons.Pause : Icons.Play;
            // play glyph looks optically left-heavy; nudge it right a hair
            var ir = s.Playing ? r : new RectangleF(r.X + r.Width * 0.04f, r.Y, r.Width, r.Height);
            Ui.Icon(c.G, ic, ir, s.Has ? Color.White : c.T.TextDim, c.S(iconPx));
            c.Hit(r, MediaSession.TogglePlay);
        }

        /// <summary>Two text lines (title bold, subtitle dim) vertically centered in r.</summary>
        public static void TwoLines(DrawCtx c, RectangleF r, string a, string b, float pa, float pb)
        {
            float ha = c.S(pa + 3), hb = c.S(pb + 3), gap = c.S(0.5f);
            float y = r.Y + (r.Height - ha - hb - gap) / 2;
            Ui.Text(c.G, a, c.FS(pa), c.T.Text, new RectangleF(r.X, y, r.Width, ha));
            Ui.Text(c.G, b, c.F(pb), c.T.TextDim, new RectangleF(r.X, y + ha + gap, r.Width, hb));
        }

        public static string Sub(MediaSnap s)
        {
            if (!s.Has) return "Start music in any app";
            if (s.Artist != "") return s.Artist;
            return s.App != "" ? s.App : "Unknown artist";
        }
        public static string Title(MediaSnap s) { return !s.Has ? "Nothing playing" : (s.Title != "" ? s.Title : "Unknown track"); }

        // ---------------- shared panel ----------------
        public static void Panel(DrawCtx c)
        {
            var s = MediaSession.Snap; var b = c.Bounds; var T = c.T; var g = c.G;
            float pad = c.S(18);
            if (!s.Has) { Idle(c); return; }

            // art + halo
            float a = c.S(116);
            var ar = new RectangleF(b.X + pad, b.Y + pad, a, a);
            Halo(c, ar, c.S(14), Glow(c, s), T.Dark ? 3 : 5);
            Art(c, ar, c.S(12), s);

            // meta column
            float x = ar.Right + c.S(16), w = b.Right - pad - x, y = ar.Y + c.S(2);
            if (s.App != "")
            {
                var f = c.F(10.5f); float tw = Ui.MeasureW(g, s.App.ToUpperInvariant(), f) + c.S(22);
                var chip = new RectangleF(x, y, Math.Min(w, tw), c.S(19));
                Ui.FillRound(g, chip, chip.Height / 2, Ui.WithA(Glow(c, s), T.Dark ? 46 : 38));
                var dot = new PointF(chip.X + c.S(8), chip.Y + chip.Height / 2);
                Ui.FillCircle(g, dot, c.S(2.6f), s.Playing ? T.Good : T.TextFaint);
                Ui.Text(g, s.App.ToUpperInvariant(), f, T.TextDim, new RectangleF(chip.X + c.S(14), chip.Y, chip.Width - c.S(17), chip.Height));
                y = chip.Bottom + c.S(9);
            }
            var tf = c.FS(16); var title = Title(s);
            float lh = tf.GetHeight(g);
            var tsz = g.MeasureString(title, tf, (int)w, StringFormat.GenericTypographic);
            float th = (tsz.Height > lh * 1.4f ? 2 : 1) * lh + c.S(2);
            Ui.TextWrap(g, title, tf, T.Text, new RectangleF(x, y, w, th));
            y += th + c.S(3);
            Ui.Text(g, Sub(s), c.F(12.5f), T.TextDim, new RectangleF(x, y, w, c.S(17))); y += c.S(18);
            if (s.Album != "" && y + c.S(16) <= ar.Bottom + c.S(2)) Ui.Text(g, s.Album, c.F(11.5f), T.TextFaint, new RectangleF(x, y, w, c.S(16)));

            // timeline
            float ty = ar.Bottom + c.S(20);
            var track = new RectangleF(b.X + pad, ty, b.Width - 2 * pad, c.S(4));
            if (s.HasTimeline)
            {
                double frac = s.Frac;
                bool hov = c.IsHover(new RectangleF(track.X, track.Y - c.S(8), track.Width, track.Height + c.S(16)));
                Ui.Progress(g, track, frac, T.Accent, T.Track);
                if (hov || s.CanSeek)
                {
                    var kc = new PointF(track.X + (float)(track.Width * frac), track.Y + track.Height / 2);
                    if (hov) Ui.FillCircle(g, kc, c.S(6.5f), Ui.WithA(T.Accent, 60));
                    Ui.FillCircle(g, kc, c.S(hov ? 5.5f : 4.5f), T.Dark ? Color.White : T.Accent);
                }
                if (s.CanSeek)
                {
                    const int N = 60; float sw = track.Width / N;
                    for (int i = 0; i < N; i++) { double f = (i + 0.5) / N; c.Hit(new RectangleF(track.X + i * sw, track.Y - c.S(8), sw + 0.5f, track.Height + c.S(16)), () => MediaSession.Seek(f)); }
                }
                var mf = c.F(11);
                Ui.Text(g, MediaSnap.Time(s.EstPos()), mf, T.TextFaint, new RectangleF(track.X, track.Bottom + c.S(4), c.S(80), c.S(15)));
                Ui.Text(g, "-" + MediaSnap.Time(s.Dur - s.EstPos()), mf, T.TextFaint, new RectangleF(track.Right - c.S(80), track.Bottom + c.S(4), c.S(80), c.S(15)), 1);
            }
            else
            {
                Ui.FillRound(g, track, track.Height / 2, T.Track);
                Ui.Text(g, s.Playing ? "Live" : "Paused", c.F(11), T.TextFaint, new RectangleF(track.X, track.Bottom + c.S(4), track.Width, c.S(15)), 0);
            }

            // controls
            float cy = b.Bottom - pad - c.S(22), cx = b.X + b.Width / 2f;
            var play = new RectangleF(cx - c.S(23), cy - c.S(23), c.S(46), c.S(46));
            PlayCircle(c, play, s, 18);
            var prev = new RectangleF(play.X - c.S(62), cy - c.S(19), c.S(38), c.S(38));
            var next = new RectangleF(play.Right + c.S(24), cy - c.S(19), c.S(38), c.S(38));
            Ctl(c, prev, Icons.Prev, s.CanPrev, MediaSession.Prev);
            Ctl(c, next, Icons.Next, s.CanNext, MediaSession.Next);
        }

        static void Ctl(DrawCtx c, RectangleF r, string glyph, bool enabled, Action a)
        {
            bool h = enabled && c.IsHover(r);
            if (h) Ui.FillCircle(c.G, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, c.T.TileHover);
            Ui.Icon(c.G, glyph, r, enabled ? c.T.Text : c.T.TextFaint, c.S(16));
            if (enabled) c.Hit(r, a);
        }

        static void Idle(DrawCtx c)
        {
            var b = c.Bounds; var T = c.T; var g = c.G;
            float cx = b.X + b.Width / 2f, cy = b.Y + b.Height * 0.40f;
            float rad = c.S(34);
            for (int i = 4; i >= 1; i--) Ui.FillCircle(g, new PointF(cx, cy), rad + c.S(5) * i, T.AccentA(T.Dark ? 9 : 8));
            using (var br = new LinearGradientBrush(new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2), T.AccentA(150), Ui.WithA(Ui.Mix(T.Accent, Color.FromArgb(255, 236, 72, 153), 0.55), 120), 45f))
                g.FillEllipse(br, cx - rad, cy - rad, rad * 2, rad * 2);
            Ui.Icon(g, Icons.Music, new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2), Color.White, c.S(26));
            Ui.Text(g, "Nothing playing", c.FS(16), T.Text, new RectangleF(b.X, cy + rad + c.S(16), b.Width, c.S(22)), 0);
            Ui.Text(g, MediaSession.Unsupported ? "Media controls are not available on this Windows version" : "Play something in Spotify, a browser or any media app",
                c.F(12), T.TextDim, new RectangleF(b.X + c.S(16), cy + rad + c.S(40), b.Width - c.S(32), c.S(18)), 0);
            // media keys still work for apps that don't publish a session
            float by = b.Bottom - c.S(44), bw = c.S(34);
            var pr = new RectangleF(cx - bw * 1.5f - c.S(8), by, bw, bw);
            var pl = new RectangleF(cx - bw / 2, by, bw, bw);
            var nx = new RectangleF(cx + bw / 2 + c.S(8), by, bw, bw);
            c.IconButton(pr, Icons.Prev, MediaSession.Prev, false, 13);
            c.IconButton(pl, Icons.Play, MediaSession.TogglePlay, false, 14);
            c.IconButton(nx, Icons.Next, MediaSession.Next, false, 13);
        }
    }

    [Widget("nowplaying", "Now Playing", "Media & Audio", "Cover art, title and full controls for the current track.", RefName = "Now Playing", Data = DataKind.Live)]
    public class MediaNowPlayingWidget : MediaNowPlayingBase
    {
        public override int Measure(DrawCtx c) { return c.Si(250); }
        public override void Draw(DrawCtx c)
        {
            var s = MediaSession.Snap; var b = c.Bounds;
            float a = b.Height - c.S(8);
            var ar = new RectangleF(b.X + c.S(4), b.Y + (b.Height - a) / 2, a, a);
            MediaDraw.Art(c, ar, c.S(7), s);

            float bw = c.S(24), right = b.Right - c.S(5), by = b.Y + (b.Height - bw) / 2;
            var rn = new RectangleF(right - bw, by, bw, bw);
            var rp = new RectangleF(rn.X - bw, by, bw, bw);
            var rv = new RectangleF(rp.X - bw, by, bw, bw);
            if (s.Has)
            {
                c.IconButton(rv, Icons.Prev, MediaSession.Prev, false, 11);
                c.IconButton(rp, s.Playing ? Icons.Pause : Icons.Play, MediaSession.TogglePlay, false, 13);
                c.IconButton(rn, Icons.Next, MediaSession.Next, false, 11);
            }
            else c.IconButton(rp, Icons.Play, MediaSession.TogglePlay, false, 13);

            float x = ar.Right + c.S(9), tw = (s.Has ? rv.X : rp.X) - c.S(4) - x;
            var tr = new RectangleF(x, b.Y, tw, b.Height - (s.HasTimeline ? c.S(4) : 0));
            MediaDraw.TwoLines(c, tr, MediaDraw.Title(s), s.Has ? MediaDraw.Sub(s) : "Music idle", 12, 10.5f);
            if (s.HasTimeline)
                Ui.Progress(c.G, new RectangleF(x, b.Bottom - c.S(6), tw, c.S(2)), s.Frac, c.T.Accent, c.T.Track);
        }
    }

    [Widget("nowplaying-compact", "Now Playing (compact)", "Media & Audio", "Title, artist and play/pause in a smaller card.", RefName = "Now Playing (compact)", Data = DataKind.Live)]
    public class MediaNowPlayingCompactWidget : MediaNowPlayingBase
    {
        protected override bool ShowsProgress { get { return false; } }
        public override int Measure(DrawCtx c) { return c.Si(168); }
        public override void Draw(DrawCtx c)
        {
            var s = MediaSession.Snap; var b = c.Bounds;
            float d = c.S(24);
            var pr = new RectangleF(b.X + c.S(5), b.Y + (b.Height - d) / 2, d, d);
            MediaDraw.PlayCircle(c, pr, s, 10);
            var tr = new RectangleF(pr.Right + c.S(8), b.Y, b.Right - c.S(10) - pr.Right - c.S(8), b.Height);
            MediaDraw.TwoLines(c, tr, MediaDraw.Title(s), s.Has ? MediaDraw.Sub(s) : "Music idle", 11.5f, 10f);
        }
    }

    [Widget("nowplaying-mini", "Now Playing Mini", "Media & Audio", "One round play button with the track's progress.", RefName = "Now Playing Mini", Data = DataKind.Live)]
    public class MediaNowPlayingMiniWidget : MediaNowPlayingBase
    {
        public override int Measure(DrawCtx c) { return c.Si(34); }
        public override void Draw(DrawCtx c)
        {
            var s = MediaSession.Snap; var b = c.Bounds;
            float d = Math.Min(b.Width, b.Height) - c.S(6);
            var r = new RectangleF(b.X + (b.Width - d) / 2, b.Y + (b.Height - d) / 2, d, d);
            float th = c.S(2.4f);
            if (s.Has && !s.ArtColor.IsEmpty && s.Playing) Ui.FillCircle(c.G, new PointF(r.X + d / 2, r.Y + d / 2), d / 2, Ui.WithA(s.ArtColor, c.T.Dark ? 34 : 28));
            Ui.Ring(c.G, r, s.HasTimeline ? s.Frac : 0, s.Has ? c.T.Accent : c.T.TextFaint, c.T.Track, th);
            var inner = Ui.Inset(r, th + c.S(2), th + c.S(2));
            bool h = c.Hover;
            if (h) Ui.FillCircle(c.G, new PointF(r.X + d / 2, r.Y + d / 2), inner.Width / 2, c.T.TileHover);
            string ic = !s.Has ? Icons.Music : s.Playing ? Icons.Pause : Icons.Play;
            var ir = (!s.Playing && s.Has) ? new RectangleF(inner.X + c.S(0.8f), inner.Y, inner.Width, inner.Height) : inner;
            Ui.Icon(c.G, ic, ir, s.Has ? c.T.Text : c.T.TextDim, c.S(s.Has ? 10.5f : 11));
            c.Hit(r, MediaSession.TogglePlay);
        }
        // tile click outside the button (or right-click) opens the shared panel
        public override void OnClick(MouseButtons btn) { if (btn == MouseButtons.Right || btn == MouseButtons.Left) { if (Host.IsPanelOpen(this)) Host.ClosePanel(); else Host.OpenPanel(this); } }
    }

    [Widget("nowplaying-bar", "Now Playing Bar", "Media & Audio", "The track with a progress bar and skip.", RefName = "Now Playing Bar", Data = DataKind.Live)]
    public class MediaNowPlayingBarWidget : MediaNowPlayingBase
    {
        public override int Measure(DrawCtx c) { return c.Si(220); }
        public override void Draw(DrawCtx c)
        {
            var s = MediaSession.Snap; var b = c.Bounds; var g = c.G;
            float a = b.Height - c.S(12);
            var ar = new RectangleF(b.X + c.S(7), b.Y + (b.Height - a) / 2, a, a);
            MediaDraw.Art(c, ar, c.S(5), s);
            float bw = c.S(24);
            var rn = new RectangleF(b.Right - c.S(5) - bw, b.Y + (b.Height - bw) / 2, bw, bw);
            if (s.Has && s.CanNext) c.IconButton(rn, Icons.Next, MediaSession.Next, false, 11);
            else Ui.Icon(g, Icons.Next, rn, c.T.TextFaint, c.S(11));
            float x = ar.Right + c.S(8), w = rn.X - c.S(4) - x;
            float top = b.Y + c.S(8);
            string line = !s.Has ? "Nothing playing" : (s.Artist != "" ? MediaDraw.Title(s) + "  ·  " + s.Artist : MediaDraw.Title(s));
            Ui.Text(g, line, c.FS(11.5f), s.Has ? c.T.Text : c.T.TextDim, new RectangleF(x, top, w, c.S(14)));
            var pr = new RectangleF(x, b.Bottom - c.S(11), w, c.S(3));
            Ui.Progress(g, pr, s.HasTimeline ? s.Frac : 0, s.Playing ? c.T.Accent : c.T.TextFaint, c.T.Track);
        }
    }
}
