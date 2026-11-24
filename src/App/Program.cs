// NovaBar entry point. Modes: bar (default), --render, --list, --settings, --checklist.
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NovaBar
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // --home must be applied before anything touches Paths
            var home = RenderTest.Arg(args, "--home");
            if (!string.IsNullOrEmpty(home)) Environment.SetEnvironmentVariable("NOVABAR_HOME", home);

            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { try { Native.SetProcessDPIAware(); } catch { } }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppConfig.Current = AppConfig.Load();
            Theme.Current = Theme.ForBar(AppConfig.Current);

            try
            {
                if (RenderTest.Flag(args, "--render")) { AttachConsoleOut(); return Exit(RenderTest.Run(args)); }
                if (RenderTest.Flag(args, "--list")) { AttachConsoleOut(); return Exit(RenderTest.List(RenderTest.Arg(args, "--list") ?? "widgets.json")); }
                if (RenderTest.Flag(args, "--exit")) return Exit(BarApp.SignalExit());
                if (RenderTest.Flag(args, "--restore-taskbar")) { BarShell.RestoreTaskbar(); BarShell.ForceShowTaskbar(); return Exit(0); }
                if (RenderTest.Flag(args, "--settings"))
                {
                    string page = null;
                    int i = Array.FindIndex(args, a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase));
                    if (i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--")) page = args[i + 1];
                    return Dispatch("NovaBar.SettingsApp", "Run", page, true) ? 0 : 1;
                }
                if (RenderTest.Flag(args, "--checklist"))
                {
                    AttachConsoleOut();
                    return Exit(Dispatch("NovaBar.Checklist", "Export", RenderTest.Arg(args, "--checklist") ?? "checklist.md") ? 0 : 1);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                if (RenderTest.Flag(args, "--exit")) return Exit(BarApp.SignalExit());
                if (RenderTest.Flag(args, "--restore-taskbar")) { BarShell.RestoreTaskbar(); BarShell.ForceShowTaskbar(); return Exit(0); }
                if (RenderTest.Flag(args, "--settings")) MessageBox.Show(ex.ToString(), "NovaBar");
                return Exit(1);
            }
            return BarApp.Run(args);
        }

        static int Exit(int code) { Console.Out.Flush(); Environment.Exit(code); return code; }

        /// <summary>Invoke a static method on a type that another agent may or may not have written yet.</summary>
        static bool Dispatch(string typeName, string method, string arg, bool gui = false)
        {
            var t = Type.GetType(typeName) ?? Assembly.GetExecutingAssembly().GetType(typeName);
            var mi = t != null ? t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
            if (mi == null)
            {
                Console.Error.WriteLine(typeName + "." + method + " is not part of this build.");
                if (gui) MessageBox.Show(typeName + "." + method + " is not part of this build.", "NovaBar");
                return false;
            }
            try { mi.Invoke(null, new object[] { arg }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            return true;
        }

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        /// <summary>winexe has no console; attach to the parent's so CLI modes can print.</summary>
        static void AttachConsoleOut()
        {
            try
            {
                if (AttachConsole(-1))
                {
                    var o = new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }; Console.SetOut(o);
                    var e = new System.IO.StreamWriter(Console.OpenStandardError()) { AutoFlush = true }; Console.SetError(e);
                }
            }
            catch { }
        }
    }
}
