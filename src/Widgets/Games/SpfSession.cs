// Spotify-only now-playing service (prefix Spf). Same WinRT source as the Windows media flyout
// (GlobalSystemMediaTransportControlsSessionManager) but it only follows sessions whose SourceAppUserModelId contains
// "Spotify". Event-driven: SessionsChanged (Spotify started/closed) + the Spotify session's MediaProperties/PlaybackInfo/
// Timeline events schedule one debounced re-read on the pool. No polling, no ticks while paused.
// Render-harness demo: NOVABAR_SPOTIFY_DEMO=1 (playing) | paused | idle | api (playing + fake Web API account state).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using SpfMgr = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using SpfSess = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace NovaBar
{
    public sealed class SpfSnap
    {
        public static readonly SpfSnap Empty = new SpfSnap();
        public bool Has, Playing, CanPrev = true, CanNext = true, CanSeek;
        public string Title = "", Artist = "", Album = "";
        public double Pos, Dur;
        public DateTime Stamp = DateTime.UtcNow;
        public int ArtVer; public bool HasArt;
        public string Key { get { return Title + "|" + Artist; } }
        public double EstPos()
        {
            if (Dur <= 0) return 0;
            double p = Pos + (Playing ? (DateTime.UtcNow - Stamp).TotalSeconds : 0);
            return Math.Max(0, Math.Min(Dur, p));
        }
        public double Frac { get { return Dur > 0 ? EstPos() / Dur : 0; } }
        public bool HasTimeline { get { return Dur > 1; } }
        public SpfSnap Clone() { return (SpfSnap)MemberwiseClone(); }
        public static string Time(double sec)
        {
            if (sec < 0) sec = 0; var t = TimeSpan.FromSeconds(Math.Floor(sec));
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");
        }
    }

    /// <summary>Blocking wait for a WinRT async op (pool threads only). AsTask doesn't bind against the split System32 winmds.</summary>
    public static class SpfWinRT
    {
        public static T Wait<T>(IAsyncOperation<T> op, int ms)
        {
            using (var ev = new ManualResetEvent(false))
            {
                op.Completed = (o, st) => { try { ev.Set(); } catch { } };
                if (!ev.WaitOne(ms)) { try { op.Cancel(); } catch { } throw new TimeoutException(); }
                if (op.Status != AsyncStatus.Completed) throw new InvalidOperationException("WinRT op " + op.Status);
                return op.GetResults();
            }
        }
    }

    public static class SpfSession
    {
        static readonly HashSet<Widget> subs = new HashSet<Widget>();
        static readonly object UL = new object();
        static volatile SpfSnap snap = SpfSnap.Empty;
        static SpfMgr mgr; static SpfSess active;
        static readonly List<SpfSess> hooked = new List<SpfSess>();
        static Timer debounce; static int gen;
        public static bool Unsupported;
        static volatile bool rehookPending;

        static readonly TypedEventHandler<SpfMgr, SessionsChangedEventArgs> onSessions = (s, e) => Schedule(true);
        static readonly TypedEventHandler<SpfSess, MediaPropertiesChangedEventArgs> onProps = (s, e) => Schedule(false);
        static readonly TypedEventHandler<SpfSess, PlaybackInfoChangedEventArgs> onPlay = (s, e) => Schedule(false);
        static readonly TypedEventHandler<SpfSess, TimelinePropertiesChangedEventArgs> onTl = (s, e) => Schedule(false);

        public static SpfSnap Snap { get { return snap; } }
        public static string DemoMode { get { return Environment.GetEnvironmentVariable("NOVABAR_SPOTIFY_DEMO") ?? ""; } }

        public static void Start(Widget w)
        {
            bool first; lock (subs) first = subs.Add(w) && subs.Count == 1;
            if (!first) return;
            int g = Interlocked.Increment(ref gen);
            ThreadPool.QueueUserWorkItem(_ => Init(g));
        }
        public static void Stop(Widget w)
        {
            bool last; lock (subs) last = subs.Remove(w) && subs.Count == 0;
            if (!last) return;
            int g = Interlocked.Increment(ref gen);
            ThreadPool.QueueUserWorkItem(_ => { lock (UL) { if (g == gen) Teardown(); } });
        }
        public static void Changed() { Widget[] ws; lock (subs) ws = subs.ToArray(); foreach (var w in ws) w.Invalidate(); }

        static void Init(int g)
        {
            lock (UL)
            {
                if (g != gen || mgr != null) return;
                var demo = DemoMode;
                if (demo == "1" || demo == "paused" || demo == "api") { Demo(demo == "paused"); return; }
                if (demo == "idle") { snap = SpfSnap.Empty; Changed(); return; }
                try
                {
                    mgr = SpfWinRT.Wait(SpfMgr.RequestAsync(), 5000);
                    mgr.SessionsChanged += onSessions;
                    Rehook();
                }
                catch { mgr = null; Unsupported = true; }
            }
            Update();
        }
        static void Teardown()
        {
            foreach (var s in hooked) Unhook(s);
            hooked.Clear(); active = null;
            if (mgr != null) { try { mgr.SessionsChanged -= onSessions; } catch { } mgr = null; }
            if (debounce != null) { debounce.Dispose(); debounce = null; }
        }
        static void Unhook(SpfSess h) { try { h.MediaPropertiesChanged -= onProps; h.PlaybackInfoChanged -= onPlay; h.TimelinePropertiesChanged -= onTl; } catch { } }
        static bool IsSpotify(SpfSess s)
        {
            try { var id = s.SourceAppUserModelId; return id != null && id.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0; } catch { return false; }
        }
        /// <summary>Hook only Spotify sessions: other players' events never wake us.</summary>
        static void Rehook()
        {
            foreach (var s in hooked) Unhook(s);
            hooked.Clear();
            if (mgr == null) return;
            foreach (var s in mgr.GetSessions())
            {
                if (!IsSpotify(s)) continue;
                try { s.MediaPropertiesChanged += onProps; s.PlaybackInfoChanged += onPlay; s.TimelinePropertiesChanged += onTl; hooked.Add(s); } catch { }
            }
        }
        static void Schedule(bool rehook)
        {
            if (rehook) rehookPending = true;
            lock (subs)
            {
                if (subs.Count == 0) return;
                if (debounce == null) debounce = new Timer(_ => Update(), null, 80, Timeout.Infinite);
                else debounce.Change(80, Timeout.Infinite);
            }
        }

        static string artKey = "";
        static void Update()
        {
            lock (UL)
            {
                if (mgr == null) return;
                try
                {
                    if (rehookPending) { rehookPending = false; Rehook(); }
                    var s = hooked.FirstOrDefault(x => { try { var pb = x.GetPlaybackInfo(); return pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { return false; } }) ?? hooked.FirstOrDefault();
                    active = s;
                    if (s == null) { SetArt(null, ""); Publish(SpfSnap.Empty); return; }
                    var props = SpfWinRT.Wait(s.TryGetMediaPropertiesAsync(), 3000);
                    var pbi = s.GetPlaybackInfo();
                    var tl = s.GetTimelineProperties();
                    var n = new SpfSnap { Has = true };
                    if (props != null) { n.Title = props.Title ?? ""; n.Artist = props.Artist ?? ""; if (n.Artist == "") n.Artist = props.AlbumArtist ?? ""; n.Album = props.AlbumTitle ?? ""; }
                    if (pbi != null)
                    {
                        n.Playing = pbi.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        if (pbi.Controls != null) { n.CanNext = pbi.Controls.IsNextEnabled; n.CanPrev = pbi.Controls.IsPreviousEnabled; n.CanSeek = pbi.Controls.IsPlaybackPositionEnabled; }
                    }
                    if (tl != null)
                    {
                        n.Dur = Math.Max(0, (tl.EndTime - tl.StartTime).TotalSeconds);
                        n.Pos = Math.Max(0, (tl.Position - tl.StartTime).TotalSeconds);
                        var lu = tl.LastUpdatedTime.UtcDateTime; var now = DateTime.UtcNow;
                        n.Stamp = lu.Year > 2000 && lu <= now.AddSeconds(2) && (now - lu).TotalHours < 6 ? lu : now;
                    }
                    // Spotify open but nothing loaded yet: still "running" (Has) with an empty title
                    string key = n.Title + "|" + n.Artist;
                    if (key != artKey || (!artHas && props != null && props.Thumbnail != null))
                    {
                        Bitmap bmp = null;
                        try { if (props != null && props.Thumbnail != null) bmp = LoadThumb(props.Thumbnail); } catch { bmp = null; }
                        SetArt(bmp, key);
                    }
                    n.HasArt = artHas; n.ArtVer = artVer;
                    Publish(n);
                }
                catch { }
            }
        }
        static void Publish(SpfSnap n)
        {
            var o = snap;
            // skip repaint when nothing visible changed (timeline events fire often while seeking/buffering)
            if (o.Has == n.Has && o.Playing == n.Playing && o.Key == n.Key && o.ArtVer == n.ArtVer && o.CanNext == n.CanNext && o.CanPrev == n.CanPrev
                && Math.Abs(o.Dur - n.Dur) < 0.5 && Math.Abs(o.EstPos() - n.EstPos()) < 1.5) return;
            snap = n; Changed();
        }

        // ---------------- controls ----------------
        public static void TogglePlay()
        {
            var s = snap;
            if (!s.Has) { OpenApp(); return; }
            var n = s.Clone(); n.Pos = s.EstPos(); n.Stamp = DateTime.UtcNow; n.Playing = !s.Playing; snap = n; Changed();
            Act(x => x.TryTogglePlayPauseAsync());
        }
        public static void Next() { Act(x => x.TrySkipNextAsync()); }
        public static void Prev() { Act(x => x.TrySkipPreviousAsync()); }
        public static void Seek(double frac)
        {
            var s = snap; if (!s.Has || s.Dur <= 0) return;
            double sec = Math.Max(0, Math.Min(s.Dur, frac * s.Dur));
            var n = s.Clone(); n.Pos = sec; n.Stamp = DateTime.UtcNow; snap = n; Changed();
            Act(x => x.TryChangePlaybackPositionAsync((long)(sec * 10000000)));
        }
        static void Act(Func<SpfSess, IAsyncOperation<bool>> op)
        {
            var s = active; if (s == null) return;
            ThreadPool.QueueUserWorkItem(_ => { try { SpfWinRT.Wait(op(s), 2000); } catch { } Schedule(false); });
        }
        public static void OpenApp() { GamUtil.Launch("spotify:"); }

        // ---------------- album art ----------------
        static readonly object artLock = new object();
        static Bitmap artOrig; static bool artHas; static int artVer;
        static readonly Dictionary<long, Bitmap> artCache = new Dictionary<long, Bitmap>();
        static int cacheVer = -1;

        static void SetArt(Bitmap bmp, string key)
        {
            if (bmp == null && artOrig == null) { artKey = key; return; }
            lock (artLock) { if (artOrig != null) artOrig.Dispose(); artOrig = bmp; artHas = bmp != null; artVer++; artKey = key; }
        }
        static Bitmap LoadThumb(IRandomAccessStreamReference r)
        {
            using (var ras = SpfWinRT.Wait(r.OpenReadAsync(), 3000))
            using (var reader = new DataReader(ras))
            {
                ulong size = ras.Size; if (size == 0 || size > 16 * 1024 * 1024) return null;
                uint n = SpfWinRT.Wait(reader.LoadAsync((uint)size), 3000);
                var bytes = new byte[n]; reader.ReadBytes(bytes);
                using (var ms = new MemoryStream(bytes))
                using (var img = Image.FromStream(ms))
                {
                    int side = Math.Min(img.Width, img.Height), target = Math.Min(300, side);
                    var src = new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side);
                    var bmp = new Bitmap(target, target, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(bmp)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(img, new Rectangle(0, 0, target, target), src, GraphicsUnit.Pixel); }
                    return bmp;
                }
            }
        }
        /// <summary>UI thread: art pre-scaled with rounded corners, cached per size (max 8 variants). null if none.</summary>
        public static Bitmap Art(int size, float radius)
        {
            if (size < 4) return null;
            lock (artLock)
            {
                if (cacheVer != artVer) { foreach (var x in artCache.Values) x.Dispose(); artCache.Clear(); cacheVer = artVer; }
                if (artOrig == null) return null;
                long key = size * 1000L + (long)Math.Round(radius * 4);
                Bitmap bmp; if (artCache.TryGetValue(key, out bmp)) return bmp;
                if (artCache.Count >= 8) { foreach (var x in artCache.Values) x.Dispose(); artCache.Clear(); }
                bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                using (var scaled = new Bitmap(size, size, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(scaled))
                    using (var ia = new ImageAttributes())
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        ia.SetWrapMode(WrapMode.TileFlipXY); g.DrawImage(artOrig, new Rectangle(0, 0, size, size), 0, 0, artOrig.Width, artOrig.Height, GraphicsUnit.Pixel, ia);
                    }
                    using (var g = Graphics.FromImage(bmp))
                    using (var tb = new TextureBrush(scaled))
                    using (var path = radius >= size / 2f - 0.5f ? Circle(size) : Ui.Round(new RectangleF(0, 0, size, size), radius))
                    { g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.FillPath(tb, path); }
                }
                artCache[key] = bmp; return bmp;
            }
        }
        static GraphicsPath Circle(int size) { var p = new GraphicsPath(); p.AddEllipse(0, 0, size, size); return p; }

        static void Demo(bool paused)
        {
            var bmp = new Bitmap(300, 300, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, 300, 300), Color.FromArgb(255, 18, 60, 52), Color.FromArgb(255, 30, 215, 96), 60f)) g.FillRectangle(br, 0, 0, 300, 300);
                using (var br = new SolidBrush(Color.FromArgb(90, 255, 236, 140))) g.FillEllipse(br, 140, 20, 200, 200);
                using (var br = new SolidBrush(Color.FromArgb(80, 10, 20, 40))) g.FillEllipse(br, -70, 160, 240, 240);
            }
            SetArt(bmp, "demo");
            snap = new SpfSnap { Has = true, Playing = !paused, Title = "Blinding Lights", Artist = "The Weeknd", Album = "After Hours", Pos = 74, Dur = 200, Stamp = DateTime.UtcNow, CanSeek = true, HasArt = true, ArtVer = artVer };
            Changed();
        }
    }
}
