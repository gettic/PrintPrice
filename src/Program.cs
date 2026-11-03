using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PrintPrice
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        /// Test mode start flags (--hidden: off-screen, no taskbar button; --minimized: starts minimized).
        internal static bool Hidden, Minimized;

        static string[] startArgs = new string[0];

        [STAThread]
        static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            // hidden picture mode: PrintPrice.exe --screenshots <folder> renders the windows to PNGs and exits
            // optional "--lang da" / "--lang en" (picture mode only, which never reads the settings file)
            if (args.Length >= 2 && args[0] == "--screenshots")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                L.Danish = args.Length >= 4 && args[2] == "--lang" ? AppSettings.IsDanish(args[3]) : false;
                return Screenshots.Run(args[1]);
            }
            // test mode: --settings "<absolute folder>" [--hidden | --minimized]; any mistake = exit 2, no window, nothing written
            var opt = StartOptions.Parse(args);
            if (opt.Error != null) return Fail(opt.Error);
            if (opt.SettingsFolder != null)
            {
                if (!AppSettings.UseFolder(opt.SettingsFolder)) return Fail("--settings folder refused: " + opt.SettingsFolder);
                try { Directory.CreateDirectory(Path.GetDirectoryName(AppSettings.FilePath)); }
                catch (Exception ex) { return Fail("cannot create the settings folder: " + ex.Message); }
            }
            Hidden = opt.Hidden;
            Minimized = opt.Minimized;
            startArgs = args;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var settings = AppSettings.Load();
            L.Danish = AppSettings.IsDanish(settings.Language);   // set once; a change in Settings needs a restart
            Application.Run(new MainForm(settings, false));
            return 0;
        }

        static int Fail(string message)
        {
            try { Console.Error.WriteLine("PrintPrice: " + message); } catch { }
            return 2;
        }

        /// Like Application.Restart, but with the same arguments quoted correctly (Restart breaks a folder ending in "\").
        internal static void Restart()
        {
            var e = new CancelEventArgs();
            Application.Exit(e);
            if (e.Cancel) return;
            var sb = new StringBuilder();
            foreach (var a in startArgs) { if (sb.Length > 0) sb.Append(' '); sb.Append(Quote(a)); }
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, sb.ToString()) { UseShellExecute = false }); }
            catch { }
        }

        // Windows command-line quoting (CommandLineToArgvW rules)
        static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; continue; }
                sb.Append('\\', c == '"' ? bs * 2 + 1 : bs);
                bs = 0;
                sb.Append(c);
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
