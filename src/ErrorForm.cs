using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PrintPrice
{
    /// Unexpected errors (issue #2): every error is written to errors.log in the settings folder, then shown in the app's
    /// own dark ErrorForm (Continue / Copy details / Close PrintPrice) instead of .NET's light crash dialog, whose Quit
    /// closed the whole app. Hooked up in Program.Main.
    static class ErrorReport
    {
        public const string LogName = "errors.log";
        public const string IssuesUrl = "https://github.com/gettic/PrintPrice/issues/new";
        const int MaxLogChars = 200 * 1024;

        static bool showing;                                      // an error while the error window is open is only logged
        static string lastKey;                                    // the same error again within 5 s is only logged
        static DateTime lastTime;
        static readonly List<DateTime> recent = new List<DateTime>();   // 3 in 30 s = "keeps happening"
        static readonly object gate = new object();

        /// An error on the UI thread (Application.ThreadException, or caught by a form): log it, then the dark window over
        /// the active window; the app keeps running unless the user picks Close PrintPrice.
        public static void Show(Exception ex)
        {
            Show(ex, false);
        }

        /// fatal = the app ends afterwards (an error on another thread, or before the main window runs): only Copy and Close.
        public static void Show(Exception ex, bool fatal)
        {
            if (ex == null || Screenshots.Active) return;   // picture mode: never a modal window, never the real folder
            string details = Details(ex, fatal);
            Log(details);
            if (Program.Hidden) { HiddenError(ex, details, fatal); return; }   // test run: never a window on screen
            bool repeated;
            lock (gate)
            {
                if (showing) return;
                DateTime now = DateTime.Now;
                string key = Key(ex);
                if (!fatal && key == lastKey && (now - lastTime).TotalSeconds < 5) { lastTime = now; return; }
                lastKey = key;
                lastTime = now;
                recent.Add(now);
                recent.RemoveAll(t => (now - t).TotalSeconds > 30);
                repeated = recent.Count >= 3;
                showing = true;
            }
            try
            {
                if (fatal && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                {
                    // another thread: the window (and the clipboard) need an STA thread of their own
                    var t = new Thread(() => ShowWindow(ex, details, true, repeated));
                    t.SetApartmentState(ApartmentState.STA);
                    t.Start();
                    t.Join();
                }
                else ShowWindow(ex, details, fatal, repeated);
            }
            finally { lock (gate) showing = false; }
        }

        static void ShowWindow(Exception ex, string details, bool fatal, bool repeated)
        {
            DialogResult res;
            try
            {
                using (var f = new ErrorForm(ex, details, fatal, repeated))
                {
                    var owner = fatal ? null : Owner();
                    res = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
                }
            }
            catch (Exception inner)
            {
                Log(Details(inner, fatal));
                try
                {
                    res = MessageBox.Show(Summary(ex) + "\n\n" + MessageText(fatal, repeated),
                                          L.S("Something went wrong", "Noget gik galt"),
                                          fatal ? MessageBoxButtons.OK : MessageBoxButtons.OKCancel, MessageBoxIcon.Error) == DialogResult.Cancel
                          ? DialogResult.Abort : DialogResult.OK;
                }
                catch { res = DialogResult.OK; }
            }
            if (res == DialogResult.Abort && !fatal) Application.Exit();   // MainForm's closing saves run as usual
        }

        // the window the error happened in (not one of ours being torn down), else the main window
        static Form Owner()
        {
            try
            {
                var a = Form.ActiveForm;
                if (a != null && a.Visible && !a.IsDisposed && !(a is ErrorForm)) return a;
                for (int i = Application.OpenForms.Count - 1; i >= 0; i--)
                {
                    var f = Application.OpenForms[i];
                    if (f.Visible && !f.IsDisposed && !(f is ErrorForm)) return f;
                }
            }
            catch { }
            return null;
        }

        /// "--hidden" test run: the error window drawn off-screen into error_window.png next to errors.log, then exit 3.
        static void HiddenError(Exception ex, string details, bool fatal)
        {
            try
            {
                using (var f = new ErrorForm(ex, details, fatal, false))
                    Screenshots.SnapForm(f, AppSettings.Folder, "error_window.png");
            }
            catch (Exception inner) { Log(Details(inner, true)); }
            Environment.Exit(3);
        }

        static string Key(Exception ex)
        {
            string st = ex.StackTrace ?? "";
            int nl = st.IndexOf('\n');
            return ex.GetType().FullName + "|" + ex.Message + "|" + (nl >= 0 ? st.Substring(0, nl) : st);
        }

        /// "InvalidOperationException: message" (first line of the message, cut), for the window.
        public static string Summary(Exception ex)
        {
            string msg = Scrub(ex.Message ?? "").Replace("\r", "");
            int nl = msg.IndexOf('\n');
            if (nl >= 0) msg = msg.Substring(0, nl);
            if (msg.Length > 160) msg = msg.Substring(0, 157) + "…";
            return ex.GetType().Name + ": " + msg;
        }

        /// The window's explanation (also the MessageBox fallback).
        public static string MessageText(bool fatal, bool repeated)
        {
            if (fatal)
                return L.S("PrintPrice has to close. Your saved price history and settings are not affected. Please copy the details and paste them into a new issue on GitHub.",
                           "PrintPrice skal lukke. Din gemte prishistorik og dine indstillinger er ikke berørt. Kopiér gerne detaljerne, og indsæt dem i en ny sag (issue) på GitHub.");
            if (repeated)
                return L.S("Your saved price history and settings are not affected. Closing PrintPrice and starting it again may help. Please copy the details and paste them into a new issue on GitHub.",
                           "Din gemte prishistorik og dine indstillinger er ikke berørt. Det kan hjælpe at lukke PrintPrice og starte det igen. Kopiér gerne detaljerne, og indsæt dem i en ny sag (issue) på GitHub.");
            return L.S("Your saved price history and settings are not affected, and you can keep working. If it happens again, copy the details and paste them into a new issue on GitHub.",
                       "Din gemte prishistorik og dine indstillinger er ikke berørt, og du kan arbejde videre. Hvis det sker igen, så kopiér detaljerne, og indsæt dem i en ny sag (issue) på GitHub.");
        }

        /// The text for Copy details and errors.log: app, Windows, language, screen, window, then the error with its
        /// stack and inner errors. The user's profile folder and name are left out (paths in messages hold them).
        public static string Details(Exception ex, bool fatal)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PrintPrice " + Safe(() => Updater.CurrentText) + (fatal ? " - error (the app closed)" : " - error"));
            sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Windows: " + Safe(() => Environment.OSVersion.VersionString) + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit")
                          + ", " + Safe(() => CultureInfo.CurrentUICulture.Name));
            sb.AppendLine("App language: " + (L.Danish ? "Danish" : "English") + (AppSettings.TestMode ? " (test mode)" : ""));
            sb.AppendLine("Screen: " + Safe(() =>
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Round(g.DpiX) + " dpi (" + Math.Round(g.DpiX / 96f * 100) + " %)";
            }));
            sb.AppendLine("Window: " + Safe(() => { var f = Form.ActiveForm; return f == null ? "(none active)" : f.GetType().Name; }));
            sb.AppendLine();
            sb.AppendLine(Scrub(ex.ToString()));
            return sb.ToString();
        }

        static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch { return "?"; }
        }

        /// The profile folder -> %USERPROFILE%, the Windows user name -> <user>.
        public static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home)) s = Replace(s, home.TrimEnd('\\'), "%USERPROFILE%");
                string user = Environment.UserName;
                if (!string.IsNullOrEmpty(user) && user.Length >= 3) s = Replace(s, user, "<user>");
            }
            catch { }
            return s;
        }

        static string Replace(string s, string find, string with)
        {
            var sb = new StringBuilder();
            int at = 0, i;
            while ((i = s.IndexOf(find, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                sb.Append(s, at, i - at).Append(with);
                at = i + find.Length;
            }
            return sb.Append(s, at, s.Length - at).ToString();
        }

        /// Logs only (no window): a background task's error nobody waited for, or one a form shows in its own box.
        public static void Log(Exception ex)
        {
            if (ex != null && !Screenshots.Active) Log(Details(ex, false));
        }

        /// Appends to <settings folder>\errors.log (kept under ~200 KB: the oldest entries go). Never throws.
        static void Log(string details)
        {
            try
            {
                lock (gate)
                {
                    string dir = AppSettings.Folder;
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, LogName);
                    string entry = "===== " + details.TrimEnd() + Environment.NewLine + Environment.NewLine;
                    if (File.Exists(path) && new FileInfo(path).Length + entry.Length > MaxLogChars)
                    {
                        string old = File.ReadAllText(path, Encoding.UTF8);
                        int keep = old.IndexOf("===== ", Math.Max(0, old.Length - MaxLogChars / 2), StringComparison.Ordinal);
                        File.WriteAllText(path, keep > 0 ? old.Substring(keep) : "", new UTF8Encoding(false));
                    }
                    File.AppendAllText(path, entry, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        /// Picture mode: an error with a real stack (thrown and caught here).
        internal static Exception SampleError()
        {
            try
            {
                try { throw new FormatException("Input string was not in a correct format."); }
                catch (Exception inner) { throw new InvalidOperationException("The price history list could not be shown.", inner); }
            }
            catch (Exception ex) { return ex; }
        }
    }

    /// The dark error window (same look as BackupForm.AskForm): bold line, Error InfoBox (what it means, the error's first
    /// line muted, "Report it on GitHub" link), then Copy details · Close PrintPrice · Continue right-aligned. Continue =
    /// default and Esc; after 3 errors in 30 s (or a fatal one) Close PrintPrice is the default. Close = DialogResult.Abort.
    sealed class ErrorForm : CaptionForm
    {
        const int ContentW = 400;
        readonly Label headline;
        readonly InfoBox info;
        readonly string details;

        public ErrorForm(Exception ex, string details, bool fatal, bool repeated)
        {
            this.details = details ?? "";
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            Text = L.S("Something went wrong", "Noget gik galt");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = fatal;   // fatal: no owner, so it must be findable
            StartPosition = fatal ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            TopMost = fatal;
            if (Program.Hidden || Screenshots.Active) Screenshots.Place(this);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14), Margin = Padding.Empty };
            Controls.Add(root);
            headline = new Label
            {
                Text = repeated && !fatal ? L.S("The same kind of error keeps happening.", "Der bliver ved med at ske fejl.")
                                          : L.S("PrintPrice ran into an unexpected error.", "PrintPrice stødte på en uventet fejl."),
                AutoSize = true, UseMnemonic = false, Font = new Font(Font, FontStyle.Bold),
                MaximumSize = new Size(ContentW, 0), Margin = new Padding(0, 0, 0, 8)
            };
            info = new InfoBox(ContentW, 9.5f) { Margin = new Padding(0, 0, 0, 14) };
            info.Kind = InfoKind.Error;
            info.SetText(ErrorReport.MessageText(fatal, repeated), ErrorReport.Summary(ex));
            info.SetLink(L.S("Report it on GitHub", "Meld det på GitHub"), OpenIssues);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
            string copied = L.S("✓ Copied", "✓ Kopieret");
            var btnCopy = Btn(L.S("Copy details", "Kopiér detaljer"), DialogResult.None);
            btnCopy.Tag = copied;   // FitButtons: wide enough for both texts
            btnCopy.Click += delegate
            {
                try { Clipboard.SetText(this.details); btnCopy.Text = copied; }
                catch (Exception cex) { ErrorReport.Log(cex); }
            };
            var btnClose = Btn(L.S("Close PrintPrice", "Luk PrintPrice"), DialogResult.Abort);
            buttons.Controls.Add(btnCopy);
            buttons.Controls.Add(btnClose);
            Button btnContinue = null;
            if (!fatal)
            {
                btnContinue = Btn(L.S("Continue", "Fortsæt"), DialogResult.OK);
                buttons.Controls.Add(btnContinue);
            }
            btnClose.Margin = btnContinue != null ? new Padding(0, 0, 6, 0) : Padding.Empty;
            root.Controls.Add(headline);
            root.Controls.Add(info);
            root.Controls.Add(buttons);
            var def = btnContinue != null && !repeated ? btnContinue : btnClose;
            AcceptButton = def;
            CancelButton = btnContinue ?? btnClose;
            Theme.Apply(this);
            ResumeLayout(true);
            Load += delegate
            {
                int w = (int)Math.Round(ContentW * DeviceDpi / 96f);
                headline.MaximumSize = new Size(w, 0);
                info.SetWidth(w);
                root.MinimumSize = new Size(w + root.Padding.Horizontal, 0);
                foreach (Control c in buttons.Controls)
                {
                    var b = (Button)c;
                    int tw = TextRenderer.MeasureText(b.Text, b.Font).Width;
                    if (b.Tag is string) tw = Math.Max(tw, TextRenderer.MeasureText((string)b.Tag, b.Font).Width);
                    b.Width = Math.Max(b.Width, tw + (int)Math.Round(24 * DeviceDpi / 96f));
                }
            };
            Shown += delegate { if (!Program.Hidden && !Screenshots.Active) def.Focus(); };
        }

        Button Btn(string text, DialogResult result)
        {
            return new Button { Text = text, Size = new Size(90, 32), Margin = new Padding(0, 0, 6, 0), Padding = Padding.Empty,
                                 UseMnemonic = false, DialogResult = result };
        }

        static void OpenIssues()
        {
            if (Screenshots.Active || Program.Hidden) return;
            try { Process.Start(ErrorReport.IssuesUrl); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override bool ShowWithoutActivation { get { return Screenshots.Active || Program.Hidden; } }
    }
}
