// Spotify widgets: "spotify" (art + title + controls, rich panel) and "spotify-mini" (green play button with progress ring).
// Local now-playing works without any account; the optional Web API account adds like / shuffle / repeat / playlists.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace NovaBar
{
    public static class SpfDraw
    {
        public static readonly Color Brand = Color.FromArgb(255, 30, 215, 96);   // #1ED760 bright
        public static readonly Color BrandDeep = Color.FromArgb(255, 29, 185, 84); // #1DB954
        public static readonly Color Ink = Color.FromArgb(255, 18, 18, 18);
        /// <summary>Spotify green tuned per theme: classic on dark, deeper on light surfaces for contrast.</summary>
        public static Color Green(Theme T) { return T.Dark ? BrandDeep : Color.FromArgb(255, 22, 158, 70); }
        public static Color GreenHover(Theme T) { return T.Dark ? Brand : Color.FromArgb(255, 26, 176, 78); }

        /// <summary>Vector Spotify mark: green disc with three arcs.</summary>
        public static void Logo(Graphics g, RectangleF r, Color? disc = null, Color? ink = null)
        {
            var sq = Ui.Square(r); float d = sq.Width;
            g.FillEllipse(Ui.B(disc ?? BrandDeep), sq);
            var ic = ink ?? Ink;
            float[] yy = { 0.34f, 0.51f, 0.66f }, ww = { 0.62f, 0.54f, 0.44f }, th = { 0.095f, 0.08f, 0.068f };
            for (int i = 0; i < 3; i++)
            {
                float y = sq.Y + d * yy[i], w = d * ww[i], x = sq.X + d * 0.5f - w / 2 + d * 0.02f * i;
                using (var p = Ui.P(ic, d * th[i]))
                    g.DrawBezier(p, new PointF(x, y), new PointF(x + w * 0.35f, y - d * 0.075f), new PointF(x + w * 0.7f, y - d * 0.06f), new PointF(x + w, y + d * 0.055f));
            }
        }

        public static void Art(DrawCtx c, RectangleF r, float radius, SpfSnap s)
        {
            var bmp = s.HasArt ? SpfSession.Art((int)Math.Round(r.Width), radius) : null;
            if (bmp != null) { c.G.DrawImage(bmp, (int)Math.Round(r.X), (int)Math.Round(r.Y), bmp.Width, bmp.Height); return; }
            using (var path = Ui.Round(r, radius))
            using (var br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Color.FromArgb(255, 40, 40, 44), Color.FromArgb(255, 18, 18, 20), 60f))
                c.G.FillPath(br, path);
            var lr = Ui.Inset(r, r.Width * 0.24f, r.Height * 0.24f);
            Logo(c.G, lr);
        }

        public static void PlayButton(DrawCtx c, RectangleF r, SpfSnap s, float iconPx)
        {
            bool h = c.IsHover(r);
            Ui.FillCircle(c.G, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, h ? GreenHover(c.T) : Green(c.T));
            var ic = s.Playing ? "\uF8AE" : "\uF5B0";
            var ir = s.Playing ? r : new RectangleF(r.X + r.Width * 0.04f, r.Y, r.Width, r.Height);
            Ui.Icon(c.G, ic, ir, c.T.Dark ? Ink : Color.White, c.S(iconPx));
            c.Hit(r, SpfSession.TogglePlay);
        }

        public static string Title(SpfSnap s) { return !s.Has ? "Spotify" : s.Title != "" ? s.Title : "Spotify"; }
        public static string Sub(SpfSnap s) { return !s.Has ? "Not running" : s.Artist != "" ? s.Artist : (s.Title == "" ? "Ready to play" : "Unknown artist"); }

        static void Ctl(DrawCtx c, RectangleF r, string glyph, bool enabled, Action a, float px)
        {
            bool h = enabled && c.IsHover(r);
            if (h) Ui.FillCircle(c.G, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, c.T.TileHover);
            Ui.Icon(c.G, glyph, r, enabled ? c.T.Text : c.T.TextFaint, c.S(px));
            if (enabled) c.Hit(r, a);
        }
        static void Toggle(DrawCtx c, RectangleF r, string glyph, bool on, bool enabled, Action a, string badge = null)
        {
            bool h = enabled && c.IsHover(r);
            if (h) Ui.FillCircle(c.G, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, c.T.TileHover);
            var col = !enabled ? c.T.TextFaint : on ? Green(c.T) : c.T.TextDim;
            Ui.Icon(c.G, glyph, r, col, c.S(14));
            if (on && enabled) Ui.FillCircle(c.G, new PointF(r.X + r.Width / 2, r.Bottom - c.S(3)), c.S(1.8f), col);
            if (badge != null && on) Ui.Text(c.G, badge, c.F(8, true), col, new RectangleF(r.Right - c.S(11), r.Y + c.S(3), c.S(10), c.S(10)), 0);
            if (enabled) c.Hit(r, a);
        }

        // ---------------- panel ----------------
        public const float ConnectH = 74, PlaylistRows = 4;
        public static int PanelHeight()
        {
            float h = 46 + 104 + 18 + 30 + 56 + 12;
            if (SpfApi.Connected) h += 24 + PlaylistRows * 40; else h += ConnectH + 8;
            return (int)h;
        }

        public static void Panel(DrawCtx c, Widget w, ref int plScroll)
        {
            var s = SpfSession.Snap; var b = c.Bounds; var g = c.G; var T = c.T;
            bool conn = SpfApi.Connected;
            if (conn) SpfApi.Ensure(s.Key, true);
            var api = SpfApi.State;
            float pad = c.S(16);
            // header
            var lr = new RectangleF(b.X + pad, b.Y + c.S(13), c.S(22), c.S(22));
            Logo(g, lr, Green(T), T.Dark ? Ink : Color.White);
            Ui.Text(g, "Spotify", c.FS(14), T.Text, new RectangleF(lr.Right + c.S(9), lr.Y, c.S(120), lr.Height));
            string right = s.Has ? (s.Playing ? "Playing" : "Paused") : "Not running";
            if (conn && api.Device != "" && s.Has) right = api.Device;
            var hb = new RectangleF(b.Right - pad - c.S(28), lr.Y - c.S(3), c.S(28), c.S(28));
            float rightEdge = b.Right - pad;
            if (conn && s.Has && s.Title != "")
            {
                bool liked = api.Liked && api.TrackKey == s.Key;
                bool h = c.IsHover(hb);
                if (h) Ui.FillCircle(g, new PointF(hb.X + hb.Width / 2, hb.Y + hb.Height / 2), hb.Width / 2, T.TileHover);
                Ui.Icon(g, liked ? "" : Icons.Heart, hb, liked ? Green(T) : T.TextDim, c.S(15));
                c.Hit(hb, SpfApi.ToggleLike);
                rightEdge = hb.X - c.S(6);
            }
            Ui.Text(g, right, c.F(11.5f), T.TextDim, new RectangleF(lr.Right + c.S(80), lr.Y, rightEdge - lr.Right - c.S(80), lr.Height), 1);
            float y = b.Y + c.S(46);

            // art + meta
            float a = c.S(104);
            var ar = new RectangleF(b.X + pad, y, a, a);
            if (s.Has && s.HasArt) for (int i = 6; i >= 1; i--) { float d = c.S(1.6f) * i; Ui.FillRound(g, new RectangleF(ar.X - d, ar.Y - d + c.S(3), ar.Width + 2 * d, ar.Height + 2 * d), c.S(12) + d, Ui.WithA(Green(T), T.Dark ? 2 : 4)); }
            Art(c, ar, c.S(10), s);
            float x = ar.Right + c.S(14), tw = b.Right - pad - x, ty = ar.Y + c.S(4);
            if (!s.Has)
            {
                Ui.Text(g, "Spotify isn't running", c.FS(15), T.Text, new RectangleF(x, ty + c.S(8), tw, c.S(20)));
                Ui.Text(g, "Open it to see what's playing", c.F(12), T.TextDim, new RectangleF(x, ty + c.S(30), tw, c.S(17)));
                var ob = new RectangleF(x, ty + c.S(58), c.S(130), c.S(30));
                Ui.FillRound(g, ob, ob.Height / 2, c.IsHover(ob) ? GreenHover(T) : Green(T));
                Ui.Text(g, "Open Spotify", c.FS(12.5f), T.Dark ? Ink : Color.White, ob, 0);
                c.Hit(ob, SpfSession.OpenApp);
            }
            else
            {
                var tf = c.FS(16); var title = Title(s); float lh = tf.GetHeight(g);
                var tsz = g.MeasureString(title, tf, (int)tw, StringFormat.GenericTypographic);
                float th = (tsz.Height > lh * 1.4f ? 2 : 1) * lh + c.S(2);
                Ui.TextWrap(g, title, tf, T.Text, new RectangleF(x, ty, tw, th)); ty += th + c.S(3);
                Ui.Text(g, Sub(s), c.F(12.5f), T.TextDim, new RectangleF(x, ty, tw, c.S(17))); ty += c.S(18);
                if (s.Album != "" && ty + c.S(16) <= ar.Bottom) Ui.Text(g, s.Album, c.F(11.5f), T.TextFaint, new RectangleF(x, ty, tw, c.S(16)));
            }
            y = ar.Bottom + c.S(18);

            // timeline
            var track = new RectangleF(b.X + pad, y, b.Width - 2 * pad, c.S(4));
            if (s.HasTimeline)
            {
                double frac = s.Frac;
                bool hov = c.IsHover(new RectangleF(track.X, track.Y - c.S(8), track.Width, track.Height + c.S(16)));
                Ui.Progress(g, track, frac, hov ? Green(T) : (T.Dark ? Color.FromArgb(255, 230, 230, 230) : Green(T)), T.Track);
                var kc = new PointF(track.X + (float)(track.Width * frac), track.Y + track.Height / 2);
                Ui.FillCircle(g, kc, c.S(hov ? 6 : 4.5f), T.Dark ? Color.White : Green(T));
                if (s.CanSeek)
                {
                    const int N = 60; float sw = track.Width / N;
                    for (int i = 0; i < N; i++) { double f = (i + 0.5) / N; c.Hit(new RectangleF(track.X + i * sw, track.Y - c.S(8), sw + 0.5f, track.Height + c.S(16)), () => SpfSession.Seek(f)); }
                }
                var mf = c.F(11);
                Ui.Text(g, SpfSnap.Time(s.EstPos()), mf, T.TextFaint, new RectangleF(track.X, track.Bottom + c.S(5), c.S(80), c.S(15)));
                Ui.Text(g, SpfSnap.Time(s.Dur), mf, T.TextFaint, new RectangleF(track.Right - c.S(80), track.Bottom + c.S(5), c.S(80), c.S(15)), 1);
            }
            else Ui.FillRound(g, track, track.Height / 2, T.Track);
            y = track.Bottom + c.S(26);

            // controls: [shuffle] prev PLAY next [repeat]
            float cx = b.X + b.Width / 2f, cy = y + c.S(24);
            var play = new RectangleF(cx - c.S(22), cy - c.S(22), c.S(44), c.S(44));
            if (s.Has) PlayButton(c, play, s, 17);
            else { c.IconButton(play, Icons.Play, SpfSession.OpenApp, false, 17); }
            var prev = new RectangleF(play.X - c.S(56), cy - c.S(18), c.S(36), c.S(36));
            var next = new RectangleF(play.Right + c.S(20), cy - c.S(18), c.S(36), c.S(36));
            Ctl(c, prev, Icons.Prev, s.Has && s.CanPrev, SpfSession.Prev, 15);
            Ctl(c, next, Icons.Next, s.Has && s.CanNext, SpfSession.Next, 15);
            var shf = new RectangleF(prev.X - c.S(52), cy - c.S(16), c.S(32), c.S(32));
            var rep = new RectangleF(next.Right + c.S(20), cy - c.S(16), c.S(32), c.S(32));
            bool apiOk = conn && s.Has;
            Toggle(c, shf, "", apiOk && api.Shuffle, apiOk, SpfApi.ToggleShuffle);
            Toggle(c, rep, api.Repeat == "track" && apiOk ? "" : "", apiOk && api.Repeat != "off", apiOk, SpfApi.CycleRepeat);
            y = cy + c.S(22) + c.S(12);

            if (!conn) { ConnectCard(c, new RectangleF(b.X + pad, y, b.Width - 2 * pad, c.S(ConnectH)), w); return; }

            // playlists / error
            var pf = c.F(10, true);
            string head = "YOUR PLAYLISTS";
            Ui.Text(g, head, pf, T.TextFaint, new RectangleF(b.X + pad, y, c.S(150), c.S(14)));
            if (api.Error != null) Ui.Text(g, api.Error, c.F(11), T.Bad, new RectangleF(b.X + pad + c.S(110), y, b.Width - 2 * pad - c.S(110), c.S(14)), 1);
            y += c.S(22);
            var pls = api.Playlists;
            var area = new RectangleF(b.X + pad - c.S(6), y - c.S(2), b.Width - 2 * pad + c.S(12), c.S(PlaylistRows * 40));
            if (pls == null) { Ui.Text(g, "Loading playlists…", c.F(12), T.TextDim, new RectangleF(b.X, y + c.S(30), b.Width, c.S(18)), 0); return; }
            if (pls.Count == 0) { Ui.Text(g, "No playlists yet", c.F(12), T.TextDim, new RectangleF(b.X, y + c.S(30), b.Width, c.S(18)), 0); return; }
            int maxScroll = Math.Max(0, pls.Count - (int)PlaylistRows);
            plScroll = Math.Max(0, Math.Min(maxScroll, plScroll));
            int sc = plScroll;
            for (int i = sc; i < pls.Count && i < sc + PlaylistRows; i++)
            {
                var p = pls[i];
                var row = new RectangleF(area.X, y - c.S(2), area.Width, c.S(38));
                bool h = c.IsHover(row);
                if (h) Ui.FillRound(g, row, c.S(8), T.TileHover);
                var ir = new RectangleF(b.X + pad, y + c.S(1), c.S(32), c.S(32));
                if (p.Uri == "spotify:collection")
                {
                    using (var path = Ui.Round(ir, c.S(5)))
                    using (var br = new LinearGradientBrush(ir, Color.FromArgb(255, 69, 10, 245), Color.FromArgb(255, 196, 239, 217), 45f)) g.FillPath(br, path);
                    Ui.Icon(g, "", ir, Color.White, c.S(13));
                }
                else GamImg.Draw(c, p.Image, ir, c.S(5), p.Name, w);
                float px = ir.Right + c.S(10), pw = row.Right - c.S(36) - px;
                Ui.Text(g, p.Name, c.FS(12.5f), T.Text, new RectangleF(px, y, pw, c.S(17)));
                Ui.Text(g, (p.Owner != "" ? p.Owner + " · " : "Playlist · ") + p.Tracks + " songs", c.F(11), T.TextDim, new RectangleF(px, y + c.S(17), pw, c.S(15)));
                var pb = new RectangleF(row.Right - c.S(32), y + c.S(3), c.S(28), c.S(28));
                if (h) { Ui.FillCircle(g, new PointF(pb.X + pb.Width / 2, pb.Y + pb.Height / 2), pb.Width / 2, Green(T)); Ui.Icon(g, Icons.Play, new RectangleF(pb.X + c.S(1), pb.Y, pb.Width, pb.Height), T.Dark ? Ink : Color.White, c.S(11)); }
                var pp = p; c.Hit(row, () => SpfApi.PlayContext(pp));
                y += c.S(40);
            }
            if (maxScroll > 0)
            {
                // slim scroll indicator
                float trackH = area.Height - c.S(8), thumbH = Math.Max(c.S(16), trackH * PlaylistRows / pls.Count);
                float ty2 = area.Y + c.S(4) + (trackH - thumbH) * sc / maxScroll;
                Ui.FillRound(g, new RectangleF(area.Right + c.S(1), ty2, c.S(3), thumbH), c.S(1.5f), T.TextFaint);
            }
        }

        static void ConnectCard(DrawCtx c, RectangleF r, Widget w)
        {
            var g = c.G; var T = c.T;
            Ui.FillRound(g, r, c.S(12), T.Tile);
            float x = r.X + c.S(12), bw = c.S(124);
            var btn = new RectangleF(r.Right - c.S(12) - bw, r.Y + (r.Height - c.S(30)) / 2, bw, c.S(30));
            float tw = btn.X - c.S(10) - x;
            if (SpfApi.Connecting)
            {
                Ui.Text(g, "Connecting…", c.FS(12.5f), T.Text, new RectangleF(x, r.Y + c.S(14), tw, c.S(17)));
                Ui.TextWrap(g, "Approve NovaBar in the browser tab that just opened.", c.F(11), T.TextDim, new RectangleF(x, r.Y + c.S(33), tw, c.S(32)));
                c.Button(btn, "Cancel", SpfApi.CancelConnect);
                return;
            }
            var msg = SpfApi.ConnectMsg;
            Ui.Text(g, msg != null ? "Couldn't connect" : "Likes, shuffle & playlists", c.FS(12.5f), msg != null ? T.Bad : T.Text, new RectangleF(x, r.Y + c.S(14), tw, c.S(17)));
            string sub = msg ?? (SpfApi.HasClient ? "Connect your Spotify account (optional)." : "Add your free Spotify app's Client ID first.");
            Ui.TextWrap(g, sub, c.F(11), T.TextDim, new RectangleF(x, r.Y + c.S(33), tw, c.S(32)));
            if (SpfApi.HasClient)
            {
                bool h = c.IsHover(btn);
                Ui.FillRound(g, btn, btn.Height / 2, h ? GreenHover(T) : Green(T));
                Ui.Text(g, "Connect Spotify", c.FS(12.5f), T.Dark ? Ink : Color.White, btn, 0);
                c.Hit(btn, SpfApi.Connect);
            }
            else
            {
                bool h = c.IsHover(btn);
                Ui.FillRound(g, btn, btn.Height / 2, h ? Ui.Mix(T.Track, T.Text, 0.12) : T.Track);
                Ui.Text(g, "Set up in Settings", c.FS(12), T.Text, btn, 0);
                c.Hit(btn, () => w.Host.OpenSettings("accounts"));
            }
        }

        public static void Menu(List<KeyValuePair<string, Action>> items)
        {
            items.Add(new KeyValuePair<string, Action>("Open Spotify", SpfSession.OpenApp));
            var s = SpfSession.Snap;
            if (SpfApi.Connected && s.Has && s.Title != "")
            {
                var api = SpfApi.State;
                bool liked = api.Liked && api.TrackKey == s.Key;
                items.Add(new KeyValuePair<string, Action>(liked ? "Remove from Liked Songs" : "Like song", SpfApi.ToggleLike));
            }
        }
    }

    public abstract class SpfBase : Widget
    {
        protected int plScroll;
        static SpfBase() { SpfApi.Init(); }
        protected virtual bool TileShowsProgress { get { return true; } }
        public override void OnStart() { SpfApi.Init(); SpfSession.Start(this); SpfApi.Watch(this, true); }
        public override void OnStop() { SpfSession.Stop(this); SpfApi.Watch(this, false); }
        public override void OnPanelOpen() { SpfApi.Invalidate(); plScroll = 0; }
        public override int RefreshMs
        {
            get
            {
                var s = SpfSession.Snap;
                return s.Playing && s.HasTimeline && (TileShowsProgress || (Host != null && Host.IsPanelOpen(this))) ? 1000 : 0;
            }
        }
        public override Size PanelSize(float scale) { return new Size((int)(340 * scale), (int)(SpfDraw.PanelHeight() * scale)); }
        public override void DrawPanel(DrawCtx c)
        {
            SpfDraw.Panel(c, this, ref plScroll);
            var b = c.Bounds;
            c.Wheel(new Rectangle(b.X, b.Bottom - c.Si(SpfDraw.PlaylistRows * 40 + 10), b.Width, c.Si(SpfDraw.PlaylistRows * 40)), d => { plScroll -= d; Invalidate(); });
        }
        public override void AddMenuItems(List<KeyValuePair<string, Action>> items) { SpfDraw.Menu(items); }
    }

    [Widget("spotify", "Spotify", "Media & Audio", "Spotify's current track with album art and controls. Optional account: like songs, shuffle/repeat and play your playlists.", Account = "spotify", Data = DataKind.Live)]
    public class SpfSpotifyWidget : SpfBase
    {
        static SpfSpotifyWidget() { SpfApi.Init(); }
        public override int Measure(DrawCtx c) { return SpfSession.Snap.Has ? c.Si(236) : c.Si(166); }
        public override void Draw(DrawCtx c)
        {
            var s = SpfSession.Snap; var b = c.Bounds; var g = c.G; var T = c.T;
            if (!s.Has)
            {
                float d = c.S(22);
                var lr = new RectangleF(b.X + c.S(10), b.Y + (b.Height - d) / 2, d, d);
                Logo(c, lr);
                var ob = new RectangleF(b.Right - c.S(6) - c.S(50), b.Y + (b.Height - c.S(24)) / 2, c.S(50), c.S(24));
                bool h = c.IsHover(ob);
                Ui.FillRound(g, ob, ob.Height / 2, h ? SpfDraw.GreenHover(T) : SpfDraw.Green(T));
                Ui.Text(g, "Open", c.FS(11.5f), T.Dark ? SpfDraw.Ink : Color.White, ob, 0);
                c.Hit(ob, SpfSession.OpenApp);
                float x0 = lr.Right + c.S(9);
                TwoLines(c, new RectangleF(x0, b.Y, ob.X - c.S(6) - x0, b.Height), "Spotify", "Not running");
                return;
            }
            float a = b.Height - c.S(8);
            var ar = new RectangleF(b.X + c.S(4), b.Y + (b.Height - a) / 2, a, a);
            SpfDraw.Art(c, ar, c.S(7), s);
            // tiny brand badge on the art
            var badge = new RectangleF(ar.Right - c.S(9), ar.Bottom - c.S(9), c.S(11), c.S(11));
            Ui.FillCircle(g, new PointF(badge.X + badge.Width / 2, badge.Y + badge.Height / 2), c.S(6.5f), T.Dark ? Color.FromArgb(255, 20, 20, 22) : Color.White);
            SpfDraw.Logo(g, badge, SpfDraw.Green(T), T.Dark ? SpfDraw.Ink : Color.White);

            float bw = c.S(24), right = b.Right - c.S(5), by = b.Y + (b.Height - bw) / 2;
            var rn = new RectangleF(right - bw, by, bw, bw);
            var rp = new RectangleF(rn.X - bw - c.S(3), by, bw, bw);
            SpfDraw.PlayButton(c, rp, s, 10);
            if (s.CanNext) c.IconButton(rn, Icons.Next, SpfSession.Next, false, 11); else Ui.Icon(g, Icons.Next, rn, T.TextFaint, c.S(11));
            float x = ar.Right + c.S(9), tw = rp.X - c.S(6) - x;
            TwoLines(c, new RectangleF(x, b.Y, tw, b.Height - (s.HasTimeline ? c.S(4) : 0)), SpfDraw.Title(s), SpfDraw.Sub(s));
            if (s.HasTimeline) Ui.Progress(g, new RectangleF(x, b.Bottom - c.S(6), tw, c.S(2)), s.Frac, s.Playing ? SpfDraw.Green(T) : T.TextFaint, T.Track);
        }
        static void Logo(DrawCtx c, RectangleF r) { SpfDraw.Logo(c.G, r, SpfDraw.Green(c.T), c.T.Dark ? SpfDraw.Ink : Color.White); }
        static void TwoLines(DrawCtx c, RectangleF r, string a, string b)
        {
            float ha = c.S(15), hb = c.S(13.5f), y = r.Y + (r.Height - ha - hb) / 2;
            Ui.Text(c.G, a, c.FS(12), c.T.Text, new RectangleF(r.X, y, r.Width, ha));
            Ui.Text(c.G, b, c.F(10.5f), c.T.TextDim, new RectangleF(r.X, y + ha, r.Width, hb));
        }
    }

    [Widget("spotify-mini", "Spotify Mini", "Media & Audio", "A round Spotify-green play button with a progress ring and tiny album art.", Account = "spotify", Data = DataKind.Live)]
    public class SpfSpotifyMiniWidget : SpfBase
    {
        static SpfSpotifyMiniWidget() { SpfApi.Init(); }
        public override int Measure(DrawCtx c) { return SpfSession.Snap.Has ? c.Si(62) : c.Si(36); }
        public override void Draw(DrawCtx c)
        {
            var s = SpfSession.Snap; var b = c.Bounds; var g = c.G; var T = c.T;
            float d = b.Height - c.S(6);
            if (!s.Has)
            {
                var r0 = new RectangleF(b.X + (b.Width - d) / 2, b.Y + c.S(3), d, d);
                if (c.Hover) Ui.FillCircle(g, new PointF(r0.X + d / 2, r0.Y + d / 2), d / 2, T.TileHover);
                SpfDraw.Logo(g, Ui.Inset(r0, c.S(4), c.S(4)), SpfDraw.Green(T), T.Dark ? SpfDraw.Ink : Color.White);
                c.Hit(r0, SpfSession.OpenApp);
                return;
            }
            var art = new RectangleF(b.X + c.S(6), b.Y + (b.Height - c.S(20)) / 2, c.S(20), c.S(20));
            SpfDraw.Art(c, art, c.S(5), s);
            var r = new RectangleF(b.Right - c.S(4) - d, b.Y + c.S(3), d, d);
            float th = c.S(2.4f);
            Ui.Ring(g, r, s.HasTimeline ? s.Frac : 0, SpfDraw.Green(T), T.Track, th);
            var inner = Ui.Inset(r, th + c.S(2), th + c.S(2));
            SpfDraw.PlayButton(c, inner, s, 9.5f);
        }
        public override void OnClick(MouseButtons btn) { if (btn == MouseButtons.Left) { if (Host.IsPanelOpen(this)) Host.ClosePanel(); else Host.OpenPanel(this); } }
    }
}
