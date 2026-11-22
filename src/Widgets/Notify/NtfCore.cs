// NovaBar notification system — model, WinRT source, banner suppression and the background service.
//
// Source: WinRT UserNotificationListener. Verified on this PC (Win 11 26200): an UNPACKAGED exe gets
// GetAccessStatus()=Allowed and GetNotificationsAsync works, but the NotificationChanged event throws
// 0x80070490 (needs package identity) → we poll the id set every 2 s on a pool timer (one cheap WinRT call;
// details/logos are fetched only for new ids). See docs/NOTIFY.md.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NovaBar
{
    /// <summary>One notification (immutable after creation except Read).</summary>
    public sealed class NtfItem
    {
        public string Key;          // unique: "w<winId>-<ticks>" for Windows notifications, "d…" for demo/test
        public uint WinId;          // 0 = not a live Windows notification
        public string Aumid = "", App = "", Title = "", Body = "";
        public DateTime Time = DateTime.Now;
        public bool Read;

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object> { { "k", Key }, { "id", (double)WinId }, { "aumid", Aumid }, { "app", App }, { "title", Title }, { "body", Body }, { "t", Time.ToString("o") }, { "read", Read } };
        }
        public static NtfItem FromDict(Dictionary<string, object> d)
        {
            var it = new NtfItem { Key = J.Str(d, "k"), WinId = (uint)J.Num(d, "id", 0), Aumid = J.Str(d, "aumid", ""), App = J.Str(d, "app", ""), Title = J.Str(d, "title", ""), Body = J.Str(d, "body", ""), Read = J.Bool(d, "read", true) };
            DateTime t; if (DateTime.TryParse(J.Str(d, "t", ""), null, System.Globalization.DateTimeStyles.RoundtripKind, out t)) it.Time = t.ToLocalTime();
            return string.IsNullOrEmpty(it.Key) ? null : it;
        }
        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return "now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + "m ago";
            if (d.TotalHours < 24 && t.Date == DateTime.Today) return Ui.Clock(t);
            if (t.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
            return t.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Block (pool threads only) on a WinRT async op. AsTask can't bind against the split System32 winmds.</summary>
    public static class NtfWinRT
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

    static class NtfNative
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern bool PlaySound(string sound, IntPtr mod, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [StructLayout(LayoutKind.Sequential)] public struct ACCENT_POLICY { public int AccentState, AccentFlags, GradientColor, AnimationId; }
        [StructLayout(LayoutKind.Sequential)] public struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr h, ref WINCOMPATTRDATA d);
        public const uint SND_ASYNC = 1, SND_NODEFAULT = 2, SND_ALIAS = 0x10000;
        public const int MA_NOACTIVATE = 3, WM_MOUSEACTIVATE = 0x21;

        public static void Log(string where, Exception ex)
        {
            try
            {
                var f = Path.Combine(Paths.Root, "notify.log");
                if (File.Exists(f) && new FileInfo(f).Length > 128 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("s") + " " + where + " " + (ex != null ? ex.ToString() : "") + Environment.NewLine);
            }
            catch { }
        }

        public static void Broadcast()
        {
            try { IntPtr r; SendMessageTimeout(new IntPtr(0xFFFF), 0x1A /*WM_SETTINGCHANGE*/, IntPtr.Zero, "Notifications", 2 /*SMTO_ABORTIFHUNG*/, 200, out r); } catch { }
        }

        /// <summary>Same backdrop recipe as the bar: glass themes use the accent-policy acrylic (stays on while the window is inactive).</summary>
        public static void Backdrop(IntPtr h, Theme t)
        {
            Native.SetDwm(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, t.Dark ? 1 : 0);
            Native.SetDwm(h, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
            Native.SetDwm(h, Native.DWMWA_BORDER_COLOR, unchecked((int)0xFFFFFFFE));
            if (t.IsGlass)
            {
                Native.ExtendFrame(h, true);
                Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, Native.DWMSBT_NONE);
                Accent(h, 4, t.Dark ? 0x30141418 : 0x30F4F4F6);
            }
            else { Native.SetDwm(h, Native.DWMWA_SYSTEMBACKDROP_TYPE, Native.DWMSBT_NONE); Native.ExtendFrame(h, false); }
        }
        static void Accent(IntPtr h, int state, int color)
        {
            try
            {
                var ap = new ACCENT_POLICY { AccentState = state, AccentFlags = 2, GradientColor = color };
                int size = Marshal.SizeOf(ap); IntPtr p = Marshal.AllocHGlobal(size);
                try { Marshal.StructureToPtr(ap, p, false); var d = new WINCOMPATTRDATA { Attribute = 19, Data = p, SizeOfData = size }; SetWindowCompositionAttribute(h, ref d); }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }
        }

        public static object Extra(string key) { object v; var ex = AppConfig.Current.Extra; return ex != null && ex.TryGetValue(key, out v) ? v : null; }
        public static bool ExtraBool(string key, bool def) { var v = Extra(key); return v == null ? def : J.AsBool(v); }
        public static double ExtraNum(string key, double def) { var v = Extra(key); if (v == null) return def; var n = J.AsNum(v); return n > 0 ? n : def; }

        public static bool HasArg(string a) { try { return Environment.GetCommandLineArgs().Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase)); } catch { return false; } }
        public static string Arg(string a)
        {
            try { var args = Environment.GetCommandLineArgs(); for (int i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], a, StringComparison.OrdinalIgnoreCase)) return args[i + 1]; } catch { }
            return null;
        }
        public static bool RenderMode { get { return HasArg("--render"); } }
        public static bool Env(string name) { var v = Environment.GetEnvironmentVariable(name); return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase); }
    }

    /// <summary>App logos (per AUMID), cached in memory + %APPDATA%\NovaBar\cache\notify-icons. Created on the pool, drawn on the UI thread.</summary>
    public static class NtfIcons
    {
        static readonly Dictionary<string, Bitmap> mem = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static string Dir { get { var p = Path.Combine(Paths.CacheDir, "notify-icons"); Directory.CreateDirectory(p); return p; } }
        static string FileOf(string aumid) { return Path.Combine(Dir, ((uint)aumid.ToLowerInvariant().GetHashCode()).ToString("X8") + ".png"); }

        public static Bitmap Get(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return null;
            lock (mem)
            {
                Bitmap b; if (mem.TryGetValue(aumid, out b)) return b;
                b = null;
                try { var f = FileOf(aumid); if (File.Exists(f)) using (var src = new Bitmap(f)) b = new Bitmap(src); } catch { b = null; }
                mem[aumid] = b; return b;
            }
        }
        public static bool Has(string aumid) { lock (mem) { Bitmap b; return mem.TryGetValue(aumid, out b) && b != null; } }
        public static void Put(string aumid, byte[] png)
        {
            if (png == null || png.Length == 0) return;
            try
            {
                Bitmap b;
                using (var ms = new MemoryStream(png)) using (var src = Image.FromStream(ms))
                {
                    b = new Bitmap(32, 32, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(b)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality; g.DrawImage(src, new Rectangle(0, 0, 32, 32)); }
                }
                try { b.Save(FileOf(aumid), ImageFormat.Png); } catch { }
                lock (mem) { Bitmap old; if (mem.TryGetValue(aumid, out old) && old != null && old != b) { /* keep old alive: may be painting */ } mem[aumid] = b; }
            }
            catch { }
        }
        public static void Clear() { lock (mem) { foreach (var b in mem.Values) if (b != null) b.Dispose(); mem.Clear(); } }

        /// <summary>Draw the app logo, or a coloured letter avatar when there is none.</summary>
        public static void Draw(Graphics g, NtfItem it, RectangleF r, Theme t)
        {
            var b = Get(it.Aumid);
            if (b != null) { g.DrawImage(b, r); return; }
            var name = string.IsNullOrEmpty(it.App) ? "?" : it.App.Substring(0, 1).ToUpperInvariant();
            var col = t.Series[(int)((uint)(it.App ?? "").GetHashCode() % (uint)t.Series.Length)];
            Ui.FillRound(g, r, r.Width * 0.28f, col);
            Ui.Text(g, name, Ui.FontSemi(r.Height * 0.62f), Color.White, r, 0);
        }
    }

    /// <summary>
    /// Reversible per-app banner suppression: HKCU\...\Notifications\Settings\&lt;AUMID&gt; ShowBanner=0 (notifications still
    /// land in the notification list, which is what the listener reads). Originals live in StateStore "notify.banners"
    /// (value or "-" = absent) so they survive a crash and are restored on disable / clean exit.
    /// </summary>
    public static class NtfBanners
    {
        const string Root = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        const string StoreKey = "notify.banners";
        static readonly object L = new object();
        static Dictionary<string, object> store;
        public static bool Active;

        static Dictionary<string, object> Store { get { if (store == null) store = StateStore.Load(StoreKey); return store; } }

        /// <summary>Suppress banners for every app Windows knows plus the given extra AUMIDs.</summary>
        public static void SuppressAll(IEnumerable<string> extra)
        {
            lock (L)
            {
                Active = true;
                var ids = new List<string>();
                try { using (var k = Registry.CurrentUser.OpenSubKey(Root)) if (k != null) ids.AddRange(k.GetSubKeyNames()); } catch { }
                if (extra != null) ids.AddRange(extra);
                bool changed = false;
                foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase)) changed |= SuppressOne(id);
                if (changed) { StateStore.Save(StoreKey, Store); NtfNative.Broadcast(); }
            }
        }
        public static void Suppress(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return;
            lock (L) { if (!Active) return; if (SuppressOne(aumid)) { StateStore.Save(StoreKey, Store); NtfNative.Broadcast(); } }
        }
        static bool SuppressOne(string id)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOf('\\') >= 0) return false;   // AUMIDs with '\\' can't be registry key names
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Root + "\\" + id))
                {
                    if (k == null) return false;
                    var cur = k.GetValue("ShowBanner");
                    bool recorded = false;
                    if (!Store.ContainsKey(id)) { Store[id] = cur == null ? "-" : (object)Convert.ToDouble(cur); recorded = true; }
                    if (!(cur is int) || (int)cur != 0) { k.SetValue("ShowBanner", 0, RegistryValueKind.DWord); return true; }
                    return recorded;
                }
            }
            catch { return false; }
        }

        /// <summary>Put every recorded value back and forget the records.</summary>
        public static void RestoreAll()
        {
            lock (L)
            {
                Active = false;
                if (Store.Count == 0) return;
                foreach (var kv in Store.ToList())
                {
                    try
                    {
                        using (var k = Registry.CurrentUser.OpenSubKey(Root + "\\" + kv.Key, true))
                        {
                            if (k != null)
                            {
                                if (kv.Value is string) { if (k.GetValue("ShowBanner") != null) k.DeleteValue("ShowBanner", false); }
                                else k.SetValue("ShowBanner", (int)J.AsNum(kv.Value), RegistryValueKind.DWord);
                            }
                        }
                    }
                    catch { }
                }
                Store.Clear(); StateStore.Save(StoreKey, Store);
                NtfNative.Broadcast();
            }
        }
        public static int Count { get { lock (L) return Store.Count; } }
    }

    /// <summary>The notification service: one per process, refcounted (bell widgets + the "notify.replace" setting).</summary>
    public static class NtfService
    {
        public const string HistKey = "notify.history";
        const int MaxHistory = 100, PollMs = 2000;

        static readonly object L = new object();
        static volatile NtfItem[] items = new NtfItem[0];
        static volatile int unread;
        static int refs; static bool replaceRef, booted, started, demo;
        static System.Threading.Timer poll; static int busy;
        static readonly HashSet<uint> known = new HashSet<uint>();
        static bool primed;
        static readonly HashSet<Widget> subs = new HashSet<Widget>();
        static Control ui;                 // UI-thread marshaler (bar process)
        static FileSystemWatcher testWatch;
        static int pollCount;

        // status for the panel
        public static volatile string Status = "";        // "" = ok, else a human explanation
        public static volatile bool WindowsToastsOff;      // HKCU PushNotifications ToastEnabled = 0
        public static bool Demo { get { return demo; } }
        public static NtfItem[] Items { get { return items; } }
        public static int Unread { get { return unread; } }
        public static bool Dnd { get { return NtfNative.ExtraBool("notify.dnd", false); } }
        public static bool ReplaceOn { get { return NtfNative.ExtraBool("notify.replace", true); } }
        /// <summary>True when NovaBar shows its own banners (and suppresses Windows').</summary>
        public static bool Popups { get { return started && replaceRef && !NtfNative.RenderMode; } }

        // ---------------- lifecycle ----------------
        /// <summary>Host-agnostic auto start (called from the widget static ctor). Bar process only; idempotent.
        /// The host may also call it explicitly from the UI thread after the bar is created.</summary>
        public static void Boot()
        {
            if (booted) return;
            if (NtfNative.HasArg("--settings") || NtfNative.HasArg("--render") || NtfNative.HasArg("--list") || NtfNative.HasArg("--checklist")) return;
            booted = true;
            EventHandler idle = null;
            idle = (s, e) =>
            {
                Application.Idle -= idle;
                try { EnsureUi(); ApplyConfig(); } catch (Exception ex) { NtfNative.Log("boot", ex); }
            };
            Application.Idle += idle;   // runs on the thread that loaded the registry (the bar UI thread) once its loop is idle
            AppConfig.Changed += () => Post(ApplyConfig);
            Application.ApplicationExit += (s, e) => Shutdown();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Shutdown();
        }

        static void EnsureUi()
        {
            if (ui != null) return;
            ui = new Control(); ui.CreateControl(); var h = ui.Handle;
        }
        public static void Post(Action a)
        {
            var u = ui;
            if (u == null || !u.IsHandleCreated) { try { a(); } catch (Exception ex) { NtfNative.Log("post", ex); } return; }
            try { u.BeginInvoke((Action)(() => { try { a(); } catch (Exception ex) { NtfNative.Log("post", ex); } })); } catch { }
        }

        static void ApplyConfig()
        {
            bool want = ReplaceOn;
            if (want && !replaceRef) { replaceRef = true; AddRef(); }
            else if (!want && replaceRef) { replaceRef = false; Release(); }
            UpdateBanners();
        }

        public static void AddRef()
        {
            lock (L) { refs++; if (started) { UpdateBannersLater(); return; } started = true; }
            if (!NtfNative.RenderMode) EnsureUi();
            demo = NtfNative.RenderMode ? Environment.GetEnvironmentVariable("NOVABAR_NOTIFY_DEMO") != "0" : NtfNative.Env("NOVABAR_NOTIFY_DEMO");
            if (demo) { LoadDemo(); return; }
            LoadHistory();
            poll = new System.Threading.Timer(_ => Poll(), null, 300, PollMs);
            StartTestWatch();
            UpdateBannersLater();
        }
        public static void Release()
        {
            lock (L) { if (refs > 0) refs--; if (refs > 0 || !started) { UpdateBannersLater(); return; } started = false; }
            if (poll != null) { poll.Dispose(); poll = null; }
            if (testWatch != null) { try { testWatch.EnableRaisingEvents = false; testWatch.Dispose(); } catch { } testWatch = null; }
            if (NtfBanners.Active) ThreadPool.QueueUserWorkItem(_ => NtfBanners.RestoreAll());
            Post(NtfToasts.CloseAll);
            lock (L) { known.Clear(); primed = false; }
        }
        static void UpdateBannersLater() { Post(UpdateBanners); }
        static void UpdateBanners()
        {
            if (NtfNative.RenderMode || demo) return;
            bool want = Popups && NtfNative.ExtraBool("notify.suppressBanners", true);
            if (want) { if (NtfBanners.Active) return; }
            else if (!NtfBanners.Active && NtfBanners.Count == 0) return;   // records left by a crash are restored here
            string[] extra; lock (L) extra = items.Select(i => i.Aumid).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToArray();
            ThreadPool.QueueUserWorkItem(_ => { try { if (want) NtfBanners.SuppressAll(extra); else NtfBanners.RestoreAll(); } catch (Exception ex) { NtfNative.Log("banners", ex); } });
        }
        /// <summary>Clean exit: restore Windows banners (synchronously, registry only).</summary>
        public static void Shutdown()
        {
            try { if (poll != null) { poll.Dispose(); poll = null; } } catch { }
            try { if (NtfBanners.Active || (!demo && !NtfNative.RenderMode && NtfBanners.Count > 0)) NtfBanners.RestoreAll(); } catch { }
        }

        // ---------------- subscribers (bell tiles) ----------------
        public static void Subscribe(Widget w) { lock (subs) subs.Add(w); }
        public static void Unsubscribe(Widget w) { lock (subs) subs.Remove(w); }
        static void Changed()
        {
            Widget[] ws; lock (subs) ws = subs.ToArray();
            foreach (var w in ws) try { w.Invalidate(); } catch { }
        }

        // ---------------- history ----------------
        static void LoadHistory()
        {
            var d = StateStore.Load(HistKey);
            var list = new List<NtfItem>();
            var arr = J.Arr(d, "items");
            if (arr != null) foreach (var o in arr) { var od = o as Dictionary<string, object>; if (od == null) continue; var it = NtfItem.FromDict(od); if (it != null) list.Add(it); }
            items = list.OrderByDescending(i => i.Time).Take(MaxHistory).ToArray();
            unread = (int)J.Num(d, "unread", 0);
        }
        static void SaveHistory()
        {
            if (demo) return;
            var snap = items; int u = unread;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var d = new Dictionary<string, object>();
                d["items"] = snap.Select(i => (object)i.ToDict()).ToArray(); d["unread"] = (double)u;
                lock (L) StateStore.Save(HistKey, d);
            });
        }

        public static void MarkAllRead()
        {
            if (unread == 0 && items.All(i => i.Read)) return;
            foreach (var i in items) i.Read = true;
            unread = 0; SaveHistory(); Changed();
        }

        public static void Dismiss(NtfItem it, bool removeFromWindows = true)
        {
            if (it == null) return;
            lock (L) items = items.Where(i => i.Key != it.Key).ToArray();
            if (!it.Read && unread > 0) unread--;
            if (removeFromWindows && it.WinId != 0 && !demo) { var id = it.WinId; ThreadPool.QueueUserWorkItem(_ => RemoveWin(id)); }
            SaveHistory(); Changed();
            Post(() => NtfToasts.Close(it.Key));
        }
        public static void DismissApp(string app)
        {
            var list = items.Where(i => i.App == app).ToList();
            foreach (var i in list) Dismiss(i);
        }
        public static void ClearAll()
        {
            var list = items;
            lock (L) items = new NtfItem[0];
            unread = 0;
            if (!demo) ThreadPool.QueueUserWorkItem(_ => { foreach (var i in list) if (i.WinId != 0) RemoveWin(i.WinId); });
            SaveHistory(); Changed();
            Post(NtfToasts.CloseAll);
        }
        static void RemoveWin(uint id)
        {
            try { UserNotificationListener.Current.RemoveNotification(id); } catch { }
            lock (L) known.Remove(id);
        }

        /// <summary>Launch the source app (shell:AppsFolder\AUMID) and dismiss the notification.</summary>
        public static void Activate(NtfItem it)
        {
            if (it == null) return;
            var aumid = it.Aumid;
            if (!string.IsNullOrEmpty(aumid) && !demo)
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + aumid) { UseShellExecute = true }); }
                    catch (Exception ex) { NtfNative.Log("activate", ex); }
                });
            Dismiss(it);
        }

        public static void SetDnd(bool on)
        {
            AppConfig.Current.Extra["notify.dnd"] = on;
            // prefer the host's self-save (suppresses its own file-watcher reload); fall back to a plain save
            bool saved = false;
            try
            {
                var t = typeof(NtfService).Assembly.GetType("NovaBar.BarForm");
                var mi = t != null ? t.GetMethod("SaveConfig", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) : null;
                if (mi != null && mi.GetParameters().Length == 0) { mi.Invoke(null, null); saved = true; }
            }
            catch { }
            if (!saved && !NtfNative.RenderMode) AppConfig.Current.Save();
            if (on) Post(NtfToasts.CloseAll);
            Changed();
        }

        // ---------------- incoming ----------------
        static void Add(NtfItem it, bool popup)
        {
            lock (L)
            {
                if (items.Any(i => i.Key == it.Key)) return;
                var l = new List<NtfItem>(items.Length + 1) { it }; l.AddRange(items);
                items = l.OrderByDescending(i => i.Time).Take(MaxHistory).ToArray();
            }
            if (!it.Read) unread++;
            SaveHistory(); Changed();
            if (popup) Post(() => Popup(it));
        }

        static void Popup(NtfItem it)
        {
            if (!Popups || Dnd || Busy()) return;   // DND / fullscreen: queued silently in the center (unread badge)
            NtfToasts.Show(it);
            if (NtfNative.ExtraBool("notify.sound", true)) try { NtfNative.PlaySound("Notification.Default", IntPtr.Zero, NtfNative.SND_ALIAS | NtfNative.SND_ASYNC | NtfNative.SND_NODEFAULT); } catch { }
        }
        /// <summary>Fullscreen game/video/presentation/store app (SHQueryUserNotificationState 2/3/4/7) or Windows quiet time (6).</summary>
        static bool Busy()
        {
            try { int st; if (Native.SHQueryUserNotificationState(out st) == 0) return st == 2 || st == 3 || st == 4 || st == 6 || st == 7; } catch { }
            return false;
        }

        /// <summary>A sample notification (Settings "Send test notification", via notify-test.flag).</summary>
        public static void Test()
        {
            var it = new NtfItem { Key = "d" + Guid.NewGuid().ToString("N"), App = "NovaBar", Title = "Test notification", Body = "This is how notifications look in NovaBar. Click to dismiss, hover to keep it on screen.", Time = DateTime.Now };
            Add(it, false);
            Post(() => { if (started) { NtfToasts.Show(it); if (NtfNative.ExtraBool("notify.sound", true)) try { NtfNative.PlaySound("Notification.Default", IntPtr.Zero, NtfNative.SND_ALIAS | NtfNative.SND_ASYNC | NtfNative.SND_NODEFAULT); } catch { } } });
        }
        static void StartTestWatch()
        {
            try
            {
                var flag = Path.Combine(Paths.Root, "notify-test.flag");
                Action fire = () => { try { if (File.Exists(flag)) { File.Delete(flag); Test(); } } catch { } };
                testWatch = new FileSystemWatcher(Paths.Root, "notify-test.flag");
                testWatch.Created += (s, e) => { Thread.Sleep(50); fire(); };
                testWatch.Changed += (s, e) => { Thread.Sleep(50); fire(); };
                testWatch.EnableRaisingEvents = true;
                fire();
            }
            catch (Exception ex) { NtfNative.Log("testwatch", ex); }
        }

        // ---------------- polling (pool thread) ----------------
        static void Poll()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                pollCount++;
                if (pollCount % 15 == 1)
                {
                    try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications")) { var v = k != null ? k.GetValue("ToastEnabled") : null; WindowsToastsOff = v is int && (int)v == 0; } } catch { }
                }
                var l = UserNotificationListener.Current;
                var st = l.GetAccessStatus();
                if (st == UserNotificationListenerAccessStatus.Unspecified) st = NtfWinRT.Wait(l.RequestAccessAsync(), 8000);
                if (st != UserNotificationListenerAccessStatus.Allowed)
                {
                    SetStatus("Windows denied notification access (" + st + "). Allow it in Settings › Privacy › Notifications.");
                    if (poll != null) poll.Change(30000, 30000);
                    return;
                }
                var list = NtfWinRT.Wait(l.GetNotificationsAsync(NotificationKinds.Toast), 5000);
                SetStatus("");
                bool first = !primed;
                var now = new HashSet<uint>();
                foreach (var n in list)
                {
                    uint id; try { id = n.Id; } catch { continue; }
                    now.Add(id);
                    lock (L) { if (known.Contains(id)) continue; }
                    var it = Read(n, id);
                    lock (L) known.Add(id);
                    if (it == null) continue;
                    // already in history (restart) → skip; otherwise new
                    if (items.Any(i => i.Key == it.Key)) continue;
                    bool fresh = !first && (DateTime.Now - it.Time).TotalMinutes < 2;
                    if (first) it.Read = true; // already there at startup: silent, not counted
                    Add(it, fresh);
                    if (NtfBanners.Active && !string.IsNullOrEmpty(it.Aumid)) NtfBanners.Suppress(it.Aumid);
                }
                lock (L) { known.IntersectWith(now); primed = true; }
                if (poll != null && pollCount > 1) poll.Change(PollMs, PollMs);
            }
            catch (Exception ex)
            {
                SetStatus("Windows notifications aren't available (" + ex.GetType().Name + ").");
                if (pollCount < 3) NtfNative.Log("poll", ex);
                try { if (poll != null) poll.Change(30000, 30000); } catch { }
            }
            finally { Interlocked.Exchange(ref busy, 0); }
        }
        static void SetStatus(string s) { if (Status != s) { Status = s; Changed(); } }

        static NtfItem Read(UserNotification n, uint id)
        {
            var it = new NtfItem { WinId = id };
            try { it.Time = n.CreationTime.LocalDateTime; } catch { }
            it.Key = "w" + id + "-" + it.Time.Ticks;
            try { it.Aumid = n.AppInfo.AppUserModelId ?? ""; } catch { }
            try { it.App = n.AppInfo.DisplayInfo.DisplayName ?? ""; } catch { }
            if (string.IsNullOrEmpty(it.App)) it.App = PrettyAumid(it.Aumid);
            try
            {
                var bind = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (bind != null)
                {
                    var texts = bind.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (texts.Count > 0) it.Title = texts[0].Trim();
                    if (texts.Count > 1) it.Body = string.Join("\n", texts.Skip(1).Select(x => x.Trim()));
                }
            }
            catch { }
            if (string.IsNullOrEmpty(it.Title) && string.IsNullOrEmpty(it.Body)) it.Title = it.App;
            if (!string.IsNullOrEmpty(it.Aumid) && !NtfIcons.Has(it.Aumid) && NtfIcons.Get(it.Aumid) == null)
            {
                try
                {
                    var rf = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32));
                    using (var ras = NtfWinRT.Wait(rf.OpenReadAsync(), 3000))
                    using (var rd = new DataReader(ras))
                    {
                        ulong size = ras.Size; if (size > 0 && size < 4 * 1024 * 1024)
                        {
                            uint got = NtfWinRT.Wait(rd.LoadAsync((uint)size), 3000);
                            var bytes = new byte[got]; rd.ReadBytes(bytes);
                            NtfIcons.Put(it.Aumid, bytes);
                        }
                    }
                }
                catch { }
            }
            return it;
        }
        static string PrettyAumid(string a)
        {
            if (string.IsNullOrEmpty(a)) return "App";
            var s = a; int i = s.LastIndexOf('!'); if (i > 0) s = s.Substring(0, i);
            i = s.LastIndexOf('\\'); if (i >= 0) s = s.Substring(i + 1);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            i = s.IndexOf('_'); if (i > 0) s = s.Substring(0, i);
            var parts = s.Split('.'); s = parts[parts.Length - 1];
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : "App";
        }

        // ---------------- demo ----------------
        static void LoadDemo()
        {
            var n = DateTime.Now;
            bool empty = Environment.GetEnvironmentVariable("NOVABAR_NOTIFY_DEMO") == "empty";   // render the empty state
            items = empty ? new NtfItem[0] : NtfDemo.Samples(n);
            unread = empty ? 0 : 3;
        }
    }

    public static class NtfDemo
    {
        public static NtfItem[] Samples(DateTime n)
        {
            return new[]
            {
                new NtfItem { Key = "d1", Aumid = "demo.teams", App = "Microsoft Teams", Title = "Sara Ahmed", Body = "Can you take a quick look at the Q4 deck before 3pm? I left comments on slides 4–7.", Time = n.AddMinutes(-1) },
                new NtfItem { Key = "d2", Aumid = "demo.outlook", App = "Outlook", Title = "Design review moved to Thursday", Body = "Omar Haddad · The weekly design review is now Thursday 10:00–11:00 in Room 4B.", Time = n.AddMinutes(-7) },
                new NtfItem { Key = "d3", Aumid = "demo.whatsapp", App = "WhatsApp", Title = "Family", Body = "Mom: Dinner at 8 tonight, don't be late!", Time = n.AddMinutes(-12) },
                new NtfItem { Key = "d4", Aumid = "demo.teams", App = "Microsoft Teams", Title = "Khalid replied in #launch", Body = "Build 1.4.2 is green, shipping to the beta ring now.", Time = n.AddMinutes(-26), Read = true },
                new NtfItem { Key = "d5", Aumid = "demo.defender", App = "Windows Security", Title = "Virus & threat protection", Body = "Quick scan finished. No current threats.", Time = n.AddHours(-3), Read = true },
                new NtfItem { Key = "d6", Aumid = "demo.store", App = "Microsoft Store", Title = "Updates installed", Body = "3 apps were updated: Photos, Calculator, Clipchamp.", Time = n.AddHours(-20), Read = true },
            };
        }
    }
}
