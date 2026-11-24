// Bar runtime bootstrap: single instance, one bar per monitor, crash safety (taskbar is always restored), message loop.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NovaBar
{
    public static class BarApp
    {
        static int cleaned;
        static readonly List<BarForm> bars = new List<BarForm>();
        internal static BarForm[] Bars { get { return bars.ToArray(); } }
        static BarForm owner;

        /// <summary>Extra["allMonitors"] (default true): one bar per monitor. false = one bar on AppConfig.Monitor.</summary>
        static bool AllMonitors
        {
            get { object v; return !(AppConfig.Current.Extra != null && AppConfig.Current.Extra.TryGetValue("allMonitors", out v) && v != null && !J.AsBool(v)); }
        }

        public static int Run(string[] args)
        {
            string name = "Local\\NovaBar.Bar." + ((uint)Paths.Root.ToLowerInvariant().GetHashCode()).ToString("X8");
            bool created;
            var mutex = new Mutex(true, name, out created);
            if (!created)
            {
                // already running: bring up Settings instead (if this build has it)
                if (Type.GetType("NovaBar.SettingsApp") != null)
                    try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--settings") { UseShellExecute = false }); } catch { }
                return 0;
            }

            BarShell.RestoreTaskbar(); // recover from a previous crash that left the taskbar hidden
            if (!File.Exists(Paths.ConfigFile)) AppConfig.Current.Save();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => BarShell.Log("ThreadException", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { BarShell.Log("UnhandledException", e.ExceptionObject as Exception); Cleanup(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();
            Application.ApplicationExit += (s, e) => Cleanup();

            try
            {
                owner = new BarForm(AllMonitors ? 0 : -1, true);
                bars.Add(owner);
                owner.HandleCreated += (s, e) => owner.BeginInvoke((Action)SyncMonitors);
                Application.Run(owner);
            }
            finally { Cleanup(); GC.KeepAlive(mutex); }
            return 0;
        }

        /// <summary>Create/close secondary bars so there is one per monitor (or none when allMonitors=false).</summary>
        public static void SyncMonitors()
        {
            if (owner == null || owner.IsDisposed) return;
            int want = AllMonitors ? Screen.AllScreens.Length : 1;
            while (bars.Count > want && bars.Count > 1)
            {
                var b = bars[bars.Count - 1]; bars.RemoveAt(bars.Count - 1);
                try { b.Close(); b.Dispose(); } catch { }
            }
            for (int i = bars.Count; i < want; i++)
            {
                var b = new BarForm(i, false);
                bars.Add(b);
                b.Show();
            }
        }

        /// <summary>Apply AppConfig.Current to every bar (after Settings/menu changes or a config reload).</summary>
        public static void ApplyAll(bool fontChanged)
        {
            foreach (var b in bars.ToArray()) if (!b.IsDisposed) { try { b.ApplyConfig(fontChanged, false); } catch (Exception ex) { BarShell.Log("ApplyConfig", ex); } }
            SyncMonitors();
        }

        /// <summary>--exit: politely close running bars (WM_CLOSE → normal cleanup incl. taskbar restore).</summary>
        public static int SignalExit()
        {
            int n = 0, me = Process.GetCurrentProcess().Id;
            Native.EnumWindows((h, l) =>
            {
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid != me && Native.WindowTitle(h) == "NovaBar" && Native.WindowClass(h).StartsWith("WindowsForms10"))
                {
                    try { if (Process.GetProcessById((int)pid).ProcessName.Equals("NovaBar", StringComparison.OrdinalIgnoreCase)) { Native.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero); n++; } } catch { }
                }
                return true;
            }, IntPtr.Zero);
            Console.WriteLine("signalled " + n + " bar window(s)");
            return 0;
        }

        static void Cleanup()
        {
            if (Interlocked.Exchange(ref cleaned, 1) == 1) return;
            foreach (var b in bars.ToArray()) { try { b.ShutdownShell(); } catch { } }
            BarShell.RestoreTaskbar();
        }
    }
}
