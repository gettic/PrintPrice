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

        /// Exit code of a --test-* hook run (0 = passed, 4 = a FAIL); Main returns it after Application.Run.
        static int testExitCode;

        /// Ends a --test-* hook run: closes the windows the normal way (Application.Exit) and makes Main return code.
        /// (Environment.Exit straight from the UI thread could let a late repaint hit GDI+ while it shut down: exit 3.)
        internal static void EndTestRun(int code)
        {
            testExitCode = code;
            Application.Exit();
        }

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
            // --test-slice "<spec file>" / --test-repeat (idea 21, test mode + --hidden only): MainForm.TestSliceFlow slices the
            // spec's files with a colour each and saves the job; TestRepeatFlow repeats the newest history record. Both write
            // a txt in the settings folder and exit 0 / 4 (see plans\idea21-multipart\test-hook.md).
            // --test-history "<spec file>" (idea 22, test mode + --hidden only): MainForm.TestHistoryFlow plays the spec in the
            // History window (delete, undo, Clear all, restore...) with the app's own handlers; test_history.txt, exit 0 / 4.
            bool testError = Array.IndexOf(args, "--test-error") >= 0;
            bool testPictures = Array.IndexOf(args, "--test-pictures") >= 0;
            bool testRepeat = Array.IndexOf(args, "--test-repeat") >= 0;
            string testSlice = null, testHistory = null, testSetup = null;
            var rest = new System.Collections.Generic.List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--test-slice")
                {
                    if (i + 1 >= args.Length || testSlice != null) return Fail("--test-slice needs one spec file");
                    testSlice = args[++i];
                    continue;
                }
                if (args[i] == "--test-setup")
                {
                    if (i + 1 >= args.Length || testSetup != null) return Fail("--test-setup needs one spec file");
                    testSetup = args[++i];
                    continue;
                }
                if (args[i] == "--test-history")
                {
                    if (i + 1 >= args.Length || testHistory != null) return Fail("--test-history needs one spec file");
                    testHistory = args[++i];
                    continue;
                }
                if (args[i] != "--test-error" && args[i] != "--test-pictures" && args[i] != "--test-repeat") rest.Add(args[i]);
            }
            var opt = StartOptions.Parse(rest.ToArray());
            if (opt.Error != null) return Fail(opt.Error);
            if (testError && opt.SettingsFolder == null) return Fail("--test-error needs --settings");
            if (testPictures && (opt.SettingsFolder == null || !opt.Hidden)) return Fail("--test-pictures needs --settings and --hidden");
            if ((testSlice != null || testRepeat) && (opt.SettingsFolder == null || !opt.Hidden))
                return Fail("--test-slice / --test-repeat need --settings and --hidden");
            if (testSlice != null && testRepeat) return Fail("--test-slice and --test-repeat can't be used together");
            if (testSlice != null && !File.Exists(testSlice)) return Fail("--test-slice spec file not found: " + testSlice);
            if (testHistory != null)
            {
                if (opt.SettingsFolder == null || !opt.Hidden) return Fail("--test-history needs --settings and --hidden");
                if (testSlice != null || testRepeat || testPictures) return Fail("--test-history can't be used with another --test- hook");
                if (!File.Exists(testHistory)) return Fail("--test-history spec file not found: " + testHistory);
            }
            if (testSetup != null)
            {
                if (opt.SettingsFolder == null || !opt.Hidden) return Fail("--test-setup needs --settings and --hidden");
                if (testSlice != null || testRepeat || testPictures || testHistory != null || testError)
                    return Fail("--test-setup can't be used with another --test- hook");
                if (!File.Exists(testSetup)) return Fail("--test-setup spec file not found: " + testSetup);
            }
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
            // first-time setup (idea 19): a new install (no settings.ini and no history.json) gets the setup window before
            // the main one, so its language choice needs no restart. Never in test mode (an empty test folder is "new" too).
            // Finished or skipped (Set up later / ✕): SetupDone = 1 and saved, so it never asks again.
            // --test-setup "<spec>" (test mode + --hidden only, plans\idea19-setup\followup-plan.md) is the one test run that
            // may open it: SetupTest plays the spec's "setup" block in it and the rest once the main window shows.
            SetupTest setupTest = null;
            if (testSetup != null)
            {
                try { setupTest = new SetupTest(Path.GetFullPath(testSetup)); setupTest.Start(settings); }
                catch (Exception ex) { return Fail("--test-setup spec can't be read: " + ex.Message); }
            }
            bool setupShown = false, setupLater = false;
            if (settings.SetupDone == 0 && (!AppSettings.TestMode || setupTest != null))
            {
                setupShown = true;
                try
                {
                    using (var setup = new SetupForm(settings, false))
                    {
                        setup.ShowDialog();
                        if (setup.Restored) settings = AppSettings.Load();   // a backup with settings was restored and saved
                        else if (setup.Result != null) settings = setup.Result;
                        setupLater = setup.SetUpLater && !setup.Restored;
                    }
                }
                catch (Exception ex) { ErrorReport.Show(ex); }   // the main window still opens, with what we have
                settings.SetupDone = 1;
                settings.Save();
                L.Danish = AppSettings.IsDanish(settings.Language);
            }
            if (setupTest != null) setupTest.AfterStart(setupShown, settings);
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
            if (testSlice != null)
            {
                string spec = Path.GetFullPath(testSlice);
                main.Shown += delegate { main.BeginInvoke((MethodInvoker)delegate { main.TestSliceFlow(spec); }); };
            }
            if (testRepeat)
                main.Shown += delegate { main.BeginInvoke((MethodInvoker)main.TestRepeatFlow); };
            if (setupLater) main.ShowSetupHint();   // "Setup skipped – run it now" once, in the info box
            if (setupTest != null)
                main.Shown += delegate { main.BeginInvoke((MethodInvoker)delegate { main.TestSetupFlow(setupTest); }); };
            if (testHistory != null)
            {
                string spec = Path.GetFullPath(testHistory);
                main.Shown += delegate { main.BeginInvoke((MethodInvoker)delegate { main.TestHistoryFlow(spec); }); };
            }
            Application.Run(main);
            return testExitCode;   // 0 in normal runs
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
