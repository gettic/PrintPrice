using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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
            Net.EnsureTls();
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
            // test mode: --settings "<absolute folder>" [--hidden | --minimized] [--test-error]; any mistake = exit 2, no window,
            // nothing written. --test-error (test mode only) throws a made-up error once the main window shows, to check
            // the error window / errors.log (with --hidden: error_window.png next to errors.log, then exit 3).
            // --test-pictures (test mode + --hidden only): MainForm.TestPictureFlow saves a job with a picture, reopens it,
            // draws its quote / invoice, backs up and restores, writes test_pictures.txt + PNGs in the folder, exits 0 / 4.
            bool testError = Array.IndexOf(args, "--test-error") >= 0;
            bool testPictures = Array.IndexOf(args, "--test-pictures") >= 0;
            var opt = StartOptions.Parse(Array.FindAll(args, a => a != "--test-error" && a != "--test-pictures"));
            if (opt.Error != null) return Fail(opt.Error);
            if (testError && opt.SettingsFolder == null) return Fail("--test-error needs --settings");
            if (testPictures && (opt.SettingsFolder == null || !opt.Hidden)) return Fail("--test-pictures needs --settings and --hidden");
            if (opt.SettingsFolder != null)
            {
                if (!AppSettings.UseFolder(opt.SettingsFolder)) return Fail("--settings folder refused: " + opt.SettingsFolder);
                try { Directory.CreateDirectory(Path.GetDirectoryName(AppSettings.FilePath)); }
                catch (Exception ex) { return Fail("cannot create the settings folder: " + ex.Message); }
            }
            Hidden = opt.Hidden;
            Minimized = opt.Minimized;
            startArgs = args;

            // unexpected errors (issue #2): the app's own dark window + errors.log instead of .NET's dialog, whose Quit
            // closed the app. UI-thread errors (also async void handlers) keep the app open; other threads end it.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ErrorReport.Show(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ErrorReport.Show(e.ExceptionObject as Exception, true);
            TaskScheduler.UnobservedTaskException += (s, e) => { ErrorReport.Log(e.Exception); e.SetObserved(); };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var settings = AppSettings.Load();
            L.Danish = AppSettings.IsDanish(settings.Language);   // set once; a change in Settings needs a restart
            MainForm main;
            try { main = new MainForm(settings, false); }
            catch (Exception ex) { ErrorReport.Show(ex, true); return 1; }   // no main window: tell, then end
            if (testError)
                main.Shown += delegate
                {
                    // (WinForms passes only the innermost exception of a BeginInvoke'd call on to ThreadException)
                    main.BeginInvoke((MethodInvoker)delegate
                    {
                        throw new InvalidOperationException("Test error from --test-error: nothing is wrong.");
                    });
                };
            if (testPictures)
                main.Shown += delegate { main.BeginInvoke((MethodInvoker)main.TestPictureFlow); };
            Application.Run(main);
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
