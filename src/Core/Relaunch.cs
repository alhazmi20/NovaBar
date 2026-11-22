// Restart helpers: the bar and Settings are single-instance, so a restart is a short detached shell chain
// (wait → start) that runs after the current process has exited and released its mutex.
using System;
using System.Diagnostics;

namespace NovaBar
{
    public static class Relaunch
    {
        public static string Exe { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        /// <summary>Run a cmd chain detached, e.g. Chain("\"exe\" --exit", 1, "\"exe\"").</summary>
        static void Chain(string cmd)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                Process.Start(psi);
            }
            catch { }
        }
        static string Q(string s) { return "\"" + s + "\""; }
        static string Wait(int sec) { return "ping -n " + (sec + 1) + " 127.0.0.1 >nul"; }

        /// <summary>From inside the bar: start a fresh bar ~2 s after this one exits (caller then exits).</summary>
        public static void BarFromBar() { Chain(Wait(2) + " & start \"\" " + Q(Exe)); }

        /// <summary>From Settings: close the running bar, start it again, and reopen Settings on the given page.</summary>
        public static void AllFromSettings(string page)
        {
            var exe = Exe;
            Chain(Q(exe) + " --exit & " + Wait(3) + " & start \"\" " + Q(exe) + " & " + Wait(1) + " & start \"\" " + Q(exe) + " --settings " + (page ?? "widgets"));
        }
    }
}
