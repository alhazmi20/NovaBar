// WhatsApp — unread count + last sender from Windows notifications.
//
// Primary source: WinRT UserNotificationListener (reads toasts in the Action Center). For an UNPACKAGED desktop app this
// API generally requires package identity: GetAccessStatus/RequestAccessAsync throw or return Denied. We try it once;
// on failure we fall back permanently (for this run) to scanning top-level window titles via EnumWindows for the
// "(3) WhatsApp" pattern used by WhatsApp Web in a browser tab / older desktop builds. The panel shows which source is used.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NovaBar
{
    public class ProdWaMsg { public string From, Text; public DateTime Time; }
    public class ProdWaSnap
    {
        public int Unread; public bool Running; public string Source = ""; public string Note;
        public List<ProdWaMsg> Messages = new List<ProdWaMsg>();
        public string Sig { get { return Unread + "|" + Running + "|" + Source + "|" + string.Join("|", Messages.Select(m => m.From + m.Text + m.Time.Ticks)); } }
        public override bool Equals(object o) { var s = o as ProdWaSnap; return s != null && s.Sig == Sig; }
        public override int GetHashCode() { return Sig.GetHashCode(); }
    }

    public static class ProdWinRT
    {
        /// <summary>Block (thread pool only) on a WinRT async op without AsTask (not available with split winmds).</summary>
        public static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op, int ms)
        {
            using (var ev = new System.Threading.ManualResetEvent(false))
            {
                op.Completed = (o, st) => { try { ev.Set(); } catch { } };
                if (!ev.WaitOne(ms)) { try { op.Cancel(); } catch { } throw new TimeoutException(); }
                if (op.Status != Windows.Foundation.AsyncStatus.Completed) throw new InvalidOperationException("WinRT op " + op.Status);
                return op.GetResults();
            }
        }
    }

    public static class ProdWa
    {
        public const string Key = "prod.whatsapp";
        static bool listenerFailed, requested; static string failReason;
        static readonly Regex titleRx = new Regex(@"^\((\d+)\)\s*WhatsApp|WhatsApp\s*\((\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex countRx = new Regex(@"^(\d+)\s+(new\s+)?messages?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public static void Define() { Hub.Define(Key, 15000, Sample); }

        static object Sample()
        {
            var snap = new ProdWaSnap();
            if (!listenerFailed && TryListener(snap)) return snap;
            Titles(snap);
            snap.Source = "window title";
            snap.Note = "Windows notification access isn't available to this unpackaged app (" + (failReason ?? "denied") + "), so the unread count is read from the WhatsApp window title.";
            return snap;
        }

        static bool TryListener(ProdWaSnap snap)
        {
            try
            {
                var l = Windows.UI.Notifications.Management.UserNotificationListener.Current;
                var st = l.GetAccessStatus();
                if (st == Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Unspecified && !requested)
                {
                    requested = true;
                    st = ProdWinRT.Wait(l.RequestAccessAsync(), 8000);
                }
                if (st != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed) { listenerFailed = true; failReason = st.ToString(); return false; }
                var list = ProdWinRT.Wait(l.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast), 5000);
                foreach (var n in list)
                {
                    string app = ""; try { app = n.AppInfo.DisplayInfo.DisplayName ?? ""; } catch { }
                    if (app.IndexOf("WhatsApp", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var m = new ProdWaMsg { Time = n.CreationTime.LocalDateTime };
                    try
                    {
                        var bind = n.Notification.Visual.GetBinding(Windows.UI.Notifications.KnownNotificationBindings.ToastGeneric);
                        if (bind != null)
                        {
                            var texts = bind.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
                            if (texts.Count > 0) m.From = texts[0];
                            if (texts.Count > 1) m.Text = texts[texts.Count - 1];
                        }
                    }
                    catch { }
                    int extra = 1;
                    if (m.Text != null) { var cm = countRx.Match(m.Text); if (cm.Success) extra = Math.Max(1, int.Parse(cm.Groups[1].Value)); }
                    snap.Unread += extra;
                    snap.Messages.Add(m);
                }
                snap.Messages = snap.Messages.OrderByDescending(x => x.Time).Take(20).ToList();
                snap.Running = WindowRunning();
                snap.Source = "notifications";
                return true;
            }
            catch (Exception ex) { listenerFailed = true; failReason = ex.GetType().Name; return false; }
        }

        static bool WindowRunning() { var s = new ProdWaSnap(); Titles(s); return s.Running; }
        static void Titles(ProdWaSnap snap)
        {
            int best = 0; bool running = false;
            Native.EnumWindows((h, l) =>
            {
                if (!Native.IsWindowVisible(h)) return true;
                int len = Native.GetWindowTextLength(h); if (len < 8 || len > 300) return true;
                var t = Native.WindowTitle(h);
                if (t.IndexOf("WhatsApp", StringComparison.OrdinalIgnoreCase) < 0) return true;
                var m = titleRx.Match(t);
                if (m.Success) { int v; if (int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out v)) best = Math.Max(best, v); running = true; }
                else if (t.Trim().Equals("WhatsApp", StringComparison.OrdinalIgnoreCase) || t.EndsWith("- WhatsApp", StringComparison.OrdinalIgnoreCase)) running = true;
                return true;
            }, IntPtr.Zero);
            snap.Unread = best; snap.Running = running || ProcExists("WhatsApp") || ProcExists("WhatsApp.Root");
        }

        static bool ProcExists(string name)
        {
            var ps = System.Diagnostics.Process.GetProcessesByName(name); bool any = ps.Length > 0;
            foreach (var p in ps) p.Dispose(); return any;
        }
        public static void Open()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("whatsapp:") { UseShellExecute = true }); }
                catch { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://web.whatsapp.com/") { UseShellExecute = true }); } catch { } }
            });
        }
    }

    [Widget("whatsapp", "WhatsApp", "Productivity", "Unread WhatsApp messages and who wrote last, read from Windows' own notifications.", RefName = "WhatsApp", Data = DataKind.Live)]
    public class ProdWhatsAppWidget : Widget
    {
        public override void OnStart() { ProdWa.Define(); Hub.Subscribe(ProdWa.Key, this); }
        public override void OnStop() { Hub.Unsubscribe(ProdWa.Key, this); }
        ProdWaSnap Snap { get { return Hub.Get<ProdWaSnap>(ProdWa.Key) ?? new ProdWaSnap(); } }

        public override int Measure(DrawCtx c) { return c.Si(Snap.Messages.Count > 0 ? 190 : 140); }
        public override void Draw(DrawCtx c)
        {
            var b = c.Bounds; var g = c.G; var s = Snap;
            var ic = new RectangleF(b.X + c.S(8), b.Y + (b.Height - c.S(24)) / 2, c.S(24), c.S(24));
            Ui.FillCircle(g, new PointF(ic.X + ic.Width / 2, ic.Y + ic.Height / 2), c.S(12), Color.FromArgb(c.T.Dark ? 50 : 38, c.T.Good));
            Ui.Icon(g, Icons.Chat, ic, c.T.Good, c.S(13));
            if (s.Unread > 0)
            {
                string n = s.Unread > 99 ? "99+" : s.Unread.ToString();
                float bw = Math.Max(c.S(15), Ui.MeasureW(g, n, c.FS(9)) + c.S(8));
                var br = new RectangleF(ic.Right - bw + c.S(5), ic.Y - c.S(3), bw, c.S(15));
                Ui.FillRound(g, br, br.Height / 2, c.T.Good);
                Ui.Text(g, n, c.FS(9), Color.White, br, 0);
            }
            float x = ic.Right + c.S(8), w = b.Right - c.S(10) - x;
            var last = s.Messages.FirstOrDefault();
            string top = s.Unread > 0 ? s.Unread + " unread" : "WhatsApp";
            string sub = last != null ? (last.From ?? "") + (string.IsNullOrEmpty(last.Text) ? "" : ": " + last.Text) : (s.Unread > 0 ? "new messages" : s.Running ? "No new messages" : "Click to open");
            Ui.Text(g, top, c.FS(12.5f), c.T.Text, new RectangleF(x, b.Y + c.S(3), w, c.S(17)));
            Ui.Text(g, sub, c.F(10), s.Unread > 0 ? c.T.Good : c.T.TextDim, new RectangleF(x, b.Y + c.S(19), w, c.S(13)));
        }
        /// <summary>Left click opens WhatsApp; right click toggles the details card.</summary>
        public override void OnClick(MouseButtons btn)
        {
            if (btn == MouseButtons.Left) { ProdWa.Open(); return; }
            if (btn == MouseButtons.Right || btn == MouseButtons.Middle) { if (Host.IsPanelOpen(this)) Host.ClosePanel(); else Host.OpenPanel(this); }
        }

        public override Size PanelSize(float s) { return new Size((int)(330 * s), (int)(340 * s)); }
        public override void DrawPanel(DrawCtx c)
        {
            var b = c.Bounds; var g = c.G; var s = Snap;
            c.PanelHeader("WhatsApp", s.Unread > 0 ? s.Unread + " unread" : "all caught up", Icons.Chat);
            float x = b.X + c.S(16), w = b.Width - c.S(32), y = b.Y + c.S(48);
            if (s.Messages.Count > 0)
            {
                float rh = c.S(46);
                foreach (var m in s.Messages)
                {
                    if (y + rh > b.Bottom - c.S(56)) break;
                    string from = string.IsNullOrEmpty(m.From) ? "WhatsApp" : m.From;
                    var av = new RectangleF(x, y + c.S(4), c.S(32), c.S(32));
                    Ui.FillCircle(g, new PointF(av.X + c.S(16), av.Y + c.S(16)), c.S(16), Color.FromArgb(c.T.Dark ? 55 : 40, c.T.Good));
                    Ui.Text(g, from.Substring(0, 1).ToUpperInvariant(), c.FS(13), c.T.Good, av, 0);
                    float tx = av.Right + c.S(10);
                    Ui.Text(g, from, c.FS(12.5f), c.T.Text, new RectangleF(tx, y + c.S(4), x + w - tx - c.S(50), c.S(17)));
                    Ui.Text(g, m.Time.Date == DateTime.Today ? Ui.Clock(m.Time) : m.Time.ToString("MMM d", ProdUtil.Inv), c.F(10), c.T.TextFaint, new RectangleF(tx, y + c.S(4), x + w - tx, c.S(17)), 1);
                    Ui.Text(g, m.Text ?? "", c.F(11), c.T.TextDim, new RectangleF(tx, y + c.S(21), x + w - tx, c.S(15)));
                    y += rh;
                }
            }
            else
            {
                var big = new RectangleF(x, y + c.S(6), w, c.S(64));
                Ui.Text(g, s.Unread.ToString(), c.F(40, true), s.Unread > 0 ? c.T.Good : c.T.Text, big, 0);
                Ui.Text(g, s.Unread == 1 ? "unread chat" : "unread chats", c.F(12), c.T.TextDim, new RectangleF(x, big.Bottom, w, c.S(18)), 0);
                y = big.Bottom + c.S(30);
                Ui.Text(g, s.Running ? "WhatsApp is open" : "WhatsApp isn't running", c.FS(11.5f), s.Running ? c.T.Good : c.T.TextFaint, new RectangleF(x, y, w, c.S(16)), 0);
                y += c.S(24);
                if (!string.IsNullOrEmpty(s.Note)) Ui.TextWrap(g, s.Note, c.F(10.5f), c.T.TextFaint, new RectangleF(x, y, w, b.Bottom - c.S(60) - y));
            }
            var ob = new RectangleF(x, b.Bottom - c.S(46), w, c.S(32));
            c.Button(ob, "Open WhatsApp", ProdWa.Open, true, Icons.Chat);
            Ui.Text(g, "source: " + (s.Source.Length > 0 ? s.Source : "…"), c.F(9), c.T.TextFaint, new RectangleF(x, b.Bottom - c.S(14), w, c.S(12)), 0);
        }
    }
}
