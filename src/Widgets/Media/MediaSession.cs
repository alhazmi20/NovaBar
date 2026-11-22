// Now-playing service on top of WinRT GlobalSystemMediaTransportControls (the same source as the
// Windows media flyout). Event-driven: CurrentSessionChanged / SessionsChanged / MediaPropertiesChanged /
// PlaybackInfoChanged / TimelinePropertiesChanged schedule a debounced re-read on the pool. Widgets only
// read the immutable MediaSnap; the progress estimate is extrapolated from the last timeline stamp.
// Fallback if WinRT is unavailable: controls send media keys, state shows "Nothing playing".
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
using WinMgr = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using WinSess = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace NovaBar
{
    public sealed class MediaSnap
    {
        public static readonly MediaSnap Empty = new MediaSnap();
        public bool Has, Playing, CanPrev = true, CanNext = true, CanSeek;
        public string Title = "", Artist = "", Album = "", App = "";
        public double Pos, Dur;               // seconds
        public DateTime Stamp = DateTime.UtcNow; // when Pos was true
        public int ArtVer; public bool HasArt; public Color ArtColor = Color.Empty;

        public double EstPos()
        {
            if (Dur <= 0) return 0;
            double p = Pos + (Playing ? (DateTime.UtcNow - Stamp).TotalSeconds : 0);
            return Math.Max(0, Math.Min(Dur, p));
        }
        public double Frac { get { return Dur > 0 ? EstPos() / Dur : 0; } }
        public bool HasTimeline { get { return Dur > 1; } }
        public MediaSnap Clone() { return (MediaSnap)MemberwiseClone(); }
        public static string Time(double sec)
        {
            if (sec < 0) sec = 0; var t = TimeSpan.FromSeconds(Math.Floor(sec));
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");
        }
    }

    /// <summary>Blocking wait for a WinRT async op (pool threads only). System.Runtime.WindowsRuntime's AsTask can't be
    /// used with the split System32 winmds (it references the union "Windows" assembly), so wire Completed ourselves.</summary>
    public static class MediaWinRT
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

    public static class MediaSession
    {
        static readonly HashSet<Widget> subs = new HashSet<Widget>();
        static readonly object UL = new object();          // serializes WinRT reads
        static volatile MediaSnap snap = MediaSnap.Empty;
        static WinMgr mgr; static WinSess active;
        static readonly List<WinSess> hooked = new List<WinSess>();
        static Timer debounce; static int gen;
        public static bool Unsupported;

        static readonly TypedEventHandler<WinMgr, CurrentSessionChangedEventArgs> onCur = (s, e) => Schedule(true);
        static readonly TypedEventHandler<WinMgr, SessionsChangedEventArgs> onSessions = (s, e) => Schedule(true);
        static readonly TypedEventHandler<WinSess, MediaPropertiesChangedEventArgs> onProps = (s, e) => Schedule(false);
        static readonly TypedEventHandler<WinSess, PlaybackInfoChangedEventArgs> onPlay = (s, e) => Schedule(false);
        static readonly TypedEventHandler<WinSess, TimelinePropertiesChangedEventArgs> onTl = (s, e) => Schedule(false);
        static volatile bool rehookPending;

        public static MediaSnap Snap { get { return snap; } }

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
        static void Changed() { Widget[] ws; lock (subs) ws = subs.ToArray(); foreach (var w in ws) w.Invalidate(); }

        static void Init(int g)
        {
            lock (UL)
            {
                if (g != gen || mgr != null) return;
                var demo = Environment.GetEnvironmentVariable("NOVABAR_MEDIA_DEMO");
                if (demo == "1") { Demo(); return; }
                if (demo == "idle") { snap = MediaSnap.Empty; Changed(); return; }
                try
                {
                    mgr = MediaWinRT.Wait(WinMgr.RequestAsync(), 5000);
                    mgr.CurrentSessionChanged += onCur;
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
            if (mgr != null) { try { mgr.CurrentSessionChanged -= onCur; mgr.SessionsChanged -= onSessions; } catch { } mgr = null; }
            if (debounce != null) { debounce.Dispose(); debounce = null; }
        }
        static void Unhook(WinSess h)
        {
            try { h.MediaPropertiesChanged -= onProps; h.PlaybackInfoChanged -= onPlay; h.TimelinePropertiesChanged -= onTl; } catch { }
        }
        static void Rehook()
        {
            foreach (var s in hooked) Unhook(s);
            hooked.Clear();
            if (mgr == null) return;
            foreach (var s in mgr.GetSessions())
            {
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

        // ---------------- read state ----------------
        static string artKey = "";
        static void Update()
        {
            lock (UL)
            {
                if (mgr == null) return;
                try
                {
                    if (rehookPending) { rehookPending = false; Rehook(); }
                    var s = Pick();
                    active = s;
                    if (s == null) { SetArt(null, ""); snap = MediaSnap.Empty; Changed(); return; }
                    var props = MediaWinRT.Wait(s.TryGetMediaPropertiesAsync(), 3000);
                    var pb = s.GetPlaybackInfo();
                    var tl = s.GetTimelineProperties();
                    var n = new MediaSnap { Has = true };
                    n.Title = props != null ? (props.Title ?? "") : "";
                    n.Artist = props != null ? (props.Artist ?? "") : "";
                    if (n.Artist == "" && props != null) n.Artist = props.AlbumArtist ?? "";
                    n.Album = props != null ? (props.AlbumTitle ?? "") : "";
                    n.App = AppName(s.SourceAppUserModelId);
                    if (pb != null)
                    {
                        n.Playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        if (pb.Controls != null) { n.CanNext = pb.Controls.IsNextEnabled; n.CanPrev = pb.Controls.IsPreviousEnabled; n.CanSeek = pb.Controls.IsPlaybackPositionEnabled; }
                    }
                    if (tl != null)
                    {
                        n.Dur = Math.Max(0, (tl.EndTime - tl.StartTime).TotalSeconds);
                        n.Pos = Math.Max(0, (tl.Position - tl.StartTime).TotalSeconds);
                        var lu = tl.LastUpdatedTime.UtcDateTime; var now = DateTime.UtcNow;
                        n.Stamp = lu.Year > 2000 && lu <= now.AddSeconds(2) && (now - lu).TotalHours < 6 ? lu : now;
                    }
                    if (n.Title == "" && !n.Playing && n.Dur <= 0) { SetArt(null, ""); snap = MediaSnap.Empty; Changed(); return; }

                    string key = n.App + "|" + n.Title + "|" + n.Artist;
                    bool needArt = key != artKey || (!artHas && props != null && props.Thumbnail != null);
                    if (needArt)
                    {
                        Bitmap bmp = null;
                        try { if (props != null && props.Thumbnail != null) bmp = LoadThumb(props.Thumbnail); } catch { bmp = null; }
                        SetArt(bmp, key);
                    }
                    n.HasArt = artHas; n.ArtVer = artVer; n.ArtColor = artColor;
                    snap = n; Changed();
                }
                catch { }
            }
        }

        /// <summary>Current session if playing; else any playing session; else current; else first.</summary>
        static WinSess Pick()
        {
            WinSess cur = null; SessionList all;
            try { cur = mgr.GetCurrentSession(); } catch { }
            all = new SessionList(mgr);
            if (cur != null && IsPlaying(cur)) return cur;
            foreach (var s in all.Items) if (IsPlaying(s)) return s;
            if (cur != null) return cur;
            return all.Items.FirstOrDefault();
        }
        sealed class SessionList
        {
            public List<WinSess> Items = new List<WinSess>();
            public SessionList(WinMgr m) { try { foreach (var s in m.GetSessions()) Items.Add(s); } catch { } }
        }
        static bool IsPlaying(WinSess s)
        {
            try { var pb = s.GetPlaybackInfo(); return pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { return false; }
        }

        public static string AppName(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return "";
            var low = aumid.ToLowerInvariant();
            string[,] map = {
                { "spotify", "Spotify" }, { "msedge", "Edge" }, { "chrome", "Chrome" }, { "firefox", "Firefox" }, { "308046b0af4a39cb", "Firefox" },
                { "zunemusic", "Media Player" }, { "zunevideo", "Movies & TV" }, { "applemusic", "Apple Music" }, { "itunes", "iTunes" },
                { "vlc", "VLC" }, { "foobar", "foobar2000" }, { "brave", "Brave" }, { "opera", "Opera" }, { "vivaldi", "Vivaldi" },
                { "tidal", "TIDAL" }, { "deezer", "Deezer" }, { "amazonmusic", "Amazon Music" }, { "musicbee", "MusicBee" }, { "aimp", "AIMP" },
                { "youtube", "YouTube" }, { "soundcloud", "SoundCloud" }, { "plex", "Plex" }, { "discord", "Discord" }, { "teams", "Teams" } };
            for (int i = 0; i < map.GetLength(0); i++) if (low.Contains(map[i, 0])) return map[i, 1];
            var t = aumid; int bang = t.IndexOf('!'); if (bang >= 0) t = t.Substring(bang + 1);
            if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 4);
            int dot = t.LastIndexOf('.'); if (dot >= 0 && dot < t.Length - 1) t = t.Substring(dot + 1);
            int us = t.IndexOf('_'); if (us > 0) t = t.Substring(0, us);
            return t.Length > 0 ? char.ToUpperInvariant(t[0]) + t.Substring(1) : t;
        }

        // ---------------- controls ----------------
        public static void TogglePlay()
        {
            var s = snap;
            if (s.Has) { var n = s.Clone(); n.Pos = s.EstPos(); n.Stamp = DateTime.UtcNow; n.Playing = !s.Playing; snap = n; Changed(); }
            Act(x => x.TryTogglePlayPauseAsync(), Native.VK_MEDIA_PLAY_PAUSE);
        }
        public static void Next() { Act(x => x.TrySkipNextAsync(), Native.VK_MEDIA_NEXT); }
        public static void Prev() { Act(x => x.TrySkipPreviousAsync(), Native.VK_MEDIA_PREV); }
        public static void Seek(double frac)
        {
            var s = snap; if (!s.Has || s.Dur <= 0) return;
            double sec = Math.Max(0, Math.Min(s.Dur, frac * s.Dur));
            var n = s.Clone(); n.Pos = sec; n.Stamp = DateTime.UtcNow; snap = n; Changed();
            Act(x => x.TryChangePlaybackPositionAsync((long)(sec * 10000000)), 0);
        }
        static void Act(Func<WinSess, IAsyncOperation<bool>> op, byte vk)
        {
            var s = active;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                if (s != null) { try { ok = MediaWinRT.Wait(op(s), 2000); } catch { ok = false; } }
                if (!ok && vk != 0) Native.KeyChord(vk);
                Schedule(false);
            });
        }

        // ---------------- album art ----------------
        static readonly object artLock = new object();
        static Bitmap artOrig; static bool artHas; static int artVer; static Color artColor = Color.Empty;
        static readonly Dictionary<long, Bitmap> artCache = new Dictionary<long, Bitmap>();
        static int cacheVer = -1;

        static void SetArt(Bitmap bmp, string key)
        {
            if (bmp == null && artOrig == null) { artKey = key; return; }
            var col = bmp != null ? AvgColor(bmp) : Color.Empty;
            lock (artLock)
            {
                if (artOrig != null) artOrig.Dispose();
                artOrig = bmp; artHas = bmp != null; artColor = col; artVer++; artKey = key;
            }
        }

        static Bitmap LoadThumb(IRandomAccessStreamReference r)
        {
            using (var ras = MediaWinRT.Wait(r.OpenReadAsync(), 3000))
            using (var reader = new DataReader(ras))
            {
                ulong size = ras.Size; if (size == 0 || size > 16 * 1024 * 1024) return null;
                uint n = MediaWinRT.Wait(reader.LoadAsync((uint)size), 3000);
                var bytes = new byte[n]; reader.ReadBytes(bytes);
                var ms = new MemoryStream(bytes);
                using (var img = Image.FromStream(ms))
                {
                    // center-crop to square (YouTube thumbnails are 16:9), keep at most 300 px
                    int side = Math.Min(img.Width, img.Height), target = Math.Min(300, side);
                    var src = new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side);
                    var bmp = new Bitmap(target, target, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(img, new Rectangle(0, 0, target, target), src, GraphicsUnit.Pixel);
                    }
                    return bmp;
                }
            }
        }

        /// <summary>Saturation-weighted average color, normalized to a pleasant glow tone.</summary>
        static Color AvgColor(Bitmap b)
        {
            try
            {
                using (var small = new Bitmap(12, 12, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(small)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(b, 0, 0, 12, 12); }
                    double r = 0, gg = 0, bb = 0, wsum = 0;
                    for (int y = 0; y < 12; y++) for (int x = 0; x < 12; x++)
                        {
                            var c = small.GetPixel(x, y);
                            int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
                            double w = 0.08 + (mx - mn) / 255.0 * (mx / 255.0);
                            r += c.R * w; gg += c.G * w; bb += c.B * w; wsum += w;
                        }
                    r /= wsum; gg /= wsum; bb /= wsum;
                    double m = Math.Max(r, Math.Max(gg, bb)); if (m < 1) return Color.Empty;
                    double mn2 = Math.Min(r, Math.Min(gg, bb));
                    if ((m - mn2) / m < 0.28) return Color.Empty; // greyish/muddy art -> widgets fall back to the theme accent
                    double k = 225 / m;
                    return Color.FromArgb(255, (int)Math.Min(255, r * k), (int)Math.Min(255, gg * k), (int)Math.Min(255, bb * k));
                }
            }
            catch { return Color.Empty; }
        }

        /// <summary>UI thread: album art pre-scaled with anti-aliased rounded corners, cached per size. null if no art.</summary>
        public static Bitmap Art(int size, float radius)
        {
            if (size < 4) return null;
            lock (artLock)
            {
                if (cacheVer != artVer) { foreach (var x in artCache.Values) x.Dispose(); artCache.Clear(); cacheVer = artVer; }
                if (artOrig == null) return null;
                long key = size * 1000L + (long)Math.Round(radius * 4);
                Bitmap bmp;
                if (artCache.TryGetValue(key, out bmp)) return bmp;
                if (artCache.Count >= 8) { foreach (var x in artCache.Values) x.Dispose(); artCache.Clear(); }
                using (var scaled = new Bitmap(size, size, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (var ia = new ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); g.DrawImage(artOrig, new Rectangle(0, 0, size, size), 0, 0, artOrig.Width, artOrig.Height, GraphicsUnit.Pixel, ia); }
                    }
                    bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(bmp))
                    using (var tb = new TextureBrush(scaled))
                    using (var path = Ui.Round(new RectangleF(0, 0, size, size), radius))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.FillPath(tb, path);
                    }
                }
                artCache[key] = bmp;
                return bmp;
            }
        }

        // ---------------- demo data (render harness only: NOVABAR_MEDIA_DEMO=1) ----------------
        static void Demo()
        {
            var bmp = new Bitmap(300, 300, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, 300, 300), Color.FromArgb(255, 255, 94, 98), Color.FromArgb(255, 108, 52, 220), 45f)) g.FillRectangle(br, 0, 0, 300, 300);
                using (var br = new SolidBrush(Color.FromArgb(70, 255, 220, 120))) g.FillEllipse(br, 150, 30, 190, 190);
                using (var br = new SolidBrush(Color.FromArgb(60, 20, 10, 60))) g.FillEllipse(br, -60, 170, 220, 220);
            }
            SetArt(bmp, "demo");
            snap = new MediaSnap { Has = true, Playing = true, Title = "Midnight City", Artist = "M83", Album = "Hurry Up, We're Dreaming", App = "Spotify", Pos = 97, Dur = 243, Stamp = DateTime.UtcNow, CanSeek = true, HasArt = true, ArtVer = artVer, ArtColor = artColor };
            mgr = null; Changed();
        }
    }
}
