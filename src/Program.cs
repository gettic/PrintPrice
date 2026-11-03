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
            if (args.Length >= 2 && args[0] == "--screenshots") return Screenshots.Run(args[1]);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
