using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PrintPrice
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // hidden picture mode: PrintPrice.exe --screenshots <folder> renders the windows to PNGs and exits
            // optional "--lang da" / "--lang en" (picture mode only, which never reads the settings file)
            if (args.Length >= 2 && args[0] == "--screenshots")
            {
                L.Danish = args.Length >= 4 && args[2] == "--lang" ? AppSettings.IsDanish(args[3]) : false;
                return Screenshots.Run(args[1]);
            }
            var settings = AppSettings.Load();
            L.Danish = AppSettings.IsDanish(settings.Language);   // set once; a change in Settings needs a restart
            Application.Run(new MainForm(settings, false));
            return 0;
        }
    }
}
