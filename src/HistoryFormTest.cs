using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    /// PASS / FAIL lines of a test hook run (--test-history).
    internal sealed class TestLog
    {
        public readonly List<string> Lines = new List<string>();
        public bool Ok = true;
        public void Add(string line) { Lines.Add(line); }
        public void Check(bool pass, string what)
        {
            Lines.Add((pass ? "PASS " : "FAIL ") + what);
            if (!pass) Ok = false;
        }
    }

    /// --test-history helpers shared by MainForm.TestHistoryFlow and HistoryForm.TestPlay (plans\history-test-hook\plan.md).
    internal static class HistoryTest
    {
        /// Pumps window messages for ms milliseconds (timers such as the 200 ms search delay run).
        public static void Pump(int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            Application.DoEvents();
        }

        /// "cmd rest of line" -> cmd (lower case) + arg (trimmed, "" when none).
        public static void Split(string line, out string cmd, out string arg)
        {
            line = (line ?? "").Trim();
            int sp = line.IndexOf(' ');
            cmd = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
            arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
        }

        /// The words of an argument; a "quoted part" (a path with spaces) is one word, without its quotes.
        public static string[] Words(string arg)
        {
            var words = new List<string>();
            string s = arg ?? "";
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == ' ') { i++; continue; }
                if (s[i] == '"')
                {
                    int end = s.IndexOf('"', i + 1);
                    if (end < 0) end = s.Length;
                    words.Add(s.Substring(i + 1, end - i - 1));
                    i = end + 1;
                    continue;
                }
                int sp = s.IndexOf(' ', i);
                if (sp < 0) sp = s.Length;
                words.Add(s.Substring(i, sp - i));
                i = sp;
            }
            return words.ToArray();
        }

        /// "quote <id>" / "invoice <id>" -> the drawn page's model name (MainForm.TestHistoryFlow), for "expect model".
        public static readonly Dictionary<string, string> PageModels = new Dictionary<string, string>();

        /// "quote|invoice|bill <id…>" -> the drawn page's tower grams (QuoteData.TowerGrams, -1 = none) and whether its
        /// breakdown has the tower row, for "expect tower" (issue #8 step 3).
        public static readonly Dictionary<string, KeyValuePair<double, bool>> PageTowers = new Dictionary<string, KeyValuePair<double, bool>>();

        public static string Command(string line)
        {
            string cmd, arg;
            Split(line, out cmd, out arg);
            return cmd;
        }

        /// history.json read without History.Load (which would change its save block); null = no file / unreadable.
        public static List<QuoteRecord> FileRecords()
        {
            try
            {
                if (!File.Exists(History.FilePath)) return null;
                return History.ParseJson(File.ReadAllText(History.FilePath, System.Text.Encoding.UTF8));
            }
            catch { return null; }
        }

        public static List<string> Pictures()
        {
            var ids = History.PictureIds() ?? new List<string>();
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        public static string FileState()
        {
            var f = FileRecords();
            var p = Pictures();
            return "history.json " + (f == null ? "(none)" : f.Count.ToString()) + ", pictures " + p.Count + " [" + string.Join(" ", p) + "]";
        }

        /// One "expect" line. form = the open History window (null outside it); reopened = MainForm's open quote id
        /// ("none"), null inside the window.
        public static void Expect(string arg, TestLog log, List<QuoteRecord> list, HistoryForm form, Func<string> reopened)
        {
            string what, rest;
            Split(arg, out what, out rest);
            string[] w = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int n;
            switch (what)
            {
                case "quotes":
                    log.Check(int.TryParse(rest, out n) && list.Count == n, "quotes in memory " + list.Count + " (expected " + rest + ")");
                    break;
                case "file":
                {
                    var f = FileRecords();
                    int c = f == null ? -1 : f.Count;
                    log.Check(int.TryParse(rest, out n) && c == n, "history.json has " + c + " (expected " + rest + ")");
                    break;
                }
                case "pictures":
                {
                    int c = Pictures().Count;
                    log.Check(int.TryParse(rest, out n) && c == n, "pictures on disk " + c + " (expected " + rest + ")");
                    break;
                }
                case "picture":
                {
                    bool want = w.Length > 1 && w[1].ToLowerInvariant() == "yes";
                    bool has = w.Length > 0 && History.HasPicture(w[0]);
                    log.Check(w.Length == 2 && has == want, "picture " + (w.Length > 0 ? w[0] : "?") + (has ? " there" : " not there") + " (expected " + rest + ")");
                    break;
                }
                case "clearcopy":
                {
                    string path = History.NewestClearCopy();
                    BackupData data = null;
                    string problem = path == null ? "no copy" : History.ReadBackupFile(path, out data);
                    int q = data != null ? data.Quotes.Count : -1, p = data != null ? data.Pictures.Count : -1;
                    int eq, ep;
                    bool ok = problem == null && w.Length == 2 && int.TryParse(w[0], out eq) && int.TryParse(w[1], out ep) && q == eq && p == ep;
                    log.Check(ok, "clear copy " + (path != null ? Path.GetFileName(path) : "(none)") + ": " + (problem ?? q + " quotes, " + p + " pictures") +
                                  " (expected " + rest + ")");
                    break;
                }
                case "for":
                {
                    // for <id> friends|customer: the record's For (in memory and in history.json)
                    var r = w.Length == 2 ? list.Find(x => x.Id == w[0]) : null;
                    int want = w.Length == 2 && w[1] == "friends" ? History.ForFriends : History.ForCustomer;
                    var f = FileRecords();
                    var fr = f != null && w.Length > 0 ? f.Find(x => x.Id == w[0]) : null;
                    log.Check(r != null && r.For == want && fr != null && fr.For == want,
                              "for " + (w.Length > 0 ? w[0] : "?") + ": memory " + (r != null ? r.For.ToString() : "-") + ", file " +
                              (fr != null ? fr.For.ToString() : "-") + " (expected " + rest + ")");
                    break;
                }
                case "status":
                {
                    // status <id> <notprinted|printing|printed|failed>: the record's print status in memory and in history.json
                    string[] names = { "notprinted", "printing", "printed", "failed" };
                    var r = w.Length == 2 ? list.Find(x => x.Id == w[0]) : null;
                    var f = FileRecords();
                    var fr = f != null && w.Length > 0 ? f.Find(x => x.Id == w[0]) : null;
                    var now = DateTime.Now;
                    string m = r != null ? names[(int)History.PrintStateOf(r, now)] : "-", fs = fr != null ? names[(int)History.PrintStateOf(fr, now)] : "-";
                    log.Check(w.Length == 2 && m == w[1] && fs == w[1], "status " + (w.Length > 0 ? w[0] : "?") + ": memory " + m + ", file " + fs + " (expected " + rest + ")");
                    break;
                }
                case "model":
                {
                    // model <id> "<text>": the model name on the pages drawn by "quote <id>" / "invoice <id>" (each one drawn)
                    int sp = rest.IndexOf(' ');
                    string id = sp < 0 ? rest : rest.Substring(0, sp), want = sp < 0 ? "" : rest.Substring(sp + 1).Trim().Trim('"');
                    var seen = new List<string>();
                    bool ok = true;
                    foreach (var kind in new[] { "quote", "invoice" })
                    {
                        string m;
                        if (!PageModels.TryGetValue(kind + " " + id, out m)) continue;
                        seen.Add(kind + " \"" + m + "\"");
                        ok &= m == want;
                    }
                    log.Check(ok && seen.Count > 0, "model on " + id + "'s pages: " + (seen.Count > 0 ? string.Join(", ", seen) : "none drawn (quote / invoice <id> first)") +
                                                    " (expected \"" + want + "\")");
                    break;
                }
                case "tower":
                {
                    // tower <quote|invoice|bill> <id…> <grams|none>: the page drawn by that step has the tower row with these
                    // grams (QuoteRows.TowerNote, one decimal), or none at all (issue #8 step 3)
                    var tw = Words(rest);
                    if (tw.Length < 3) { log.Check(false, "expect tower needs <quote|invoice|bill> <id…> <grams|none>"); break; }
                    string key = string.Join(" ", tw, 0, tw.Length - 1), want = tw[tw.Length - 1];
                    KeyValuePair<double, bool> got;
                    if (!PageTowers.TryGetValue(key, out got)) { log.Check(false, "tower: no page drawn for \"" + key + "\""); break; }
                    bool ok;
                    if (want == "none") ok = !got.Value;
                    else
                    {
                        double g;
                        ok = double.TryParse(want.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out g) &&
                             got.Value && Math.Abs(got.Key - g) < 0.05;
                    }
                    log.Check(ok, "tower on " + key + ": " + (got.Value ? "row \"" + QuoteRows.TowerNote(got.Key) + "\"" : "no row") +
                                  " (grams " + got.Key.ToString("0.###", Num.Dk) + "; expected " + want + ")");
                    break;
                }
                case "invoice":
                {
                    // invoice <no> <notprinted|printing|printed|failed>: every print with that invoice number has that status,
                    // in memory and in history.json (issue #7, the Invoices window)
                    string[] names = { "notprinted", "printing", "printed", "failed" };
                    var now = DateTime.Now;
                    Func<IEnumerable<QuoteRecord>, string> states = recs => recs == null ? "-" : string.Join(",",
                        recs.Where(x => w.Length > 0 && string.Equals((x.InvoiceNo ?? "").Trim(), w[0], StringComparison.OrdinalIgnoreCase))
                            .Select(x => names[(int)History.PrintStateOf(x, now)]));
                    string m = states(list), fs = states(FileRecords());
                    bool ok = w.Length == 2 && m.Length > 0 && m == fs && m.Split(',').All(s => s == w[1]);
                    log.Check(ok, "invoice " + (w.Length > 0 ? w[0] : "?") + ": memory [" + m + "], file [" + fs + "] (expected all " + (w.Length > 1 ? w[1] : "?") + ")");
                    break;
                }
                case "waiting":
                    // waiting yes|no: a typed search still waiting for its 200 ms
                    if (form == null) { log.Check(false, "expect waiting works only inside open..close"); break; }
                    log.Check(form.TestSearchWaiting == (rest == "yes"), "search waiting " + form.TestSearchWaiting + " (expected " + rest + ")");
                    break;
                case "swallowed":
                    // swallowed yes|no: the last click / Space in the list was swallowed by a waiting search
                    if (form == null) { log.Check(false, "expect swallowed works only inside open..close"); break; }
                    log.Check(form.TestLastSwallowed == (rest == "yes"), "last click swallowed " + form.TestLastSwallowed + " (expected " + rest + ")");
                    break;
                case "searchtext":
                    if (form == null) { log.Check(false, "expect searchtext works only inside open..close"); break; }
                    log.Check(form.TestSearchText == rest.Trim('"'), "search box \"" + form.TestSearchText + "\" (expected " + rest + ")");
                    break;
                case "shown":
                    // rows / cards the list shows now (month and printer filter applied)
                    if (form == null) { log.Check(false, "expect shown works only inside open..close"); break; }
                    log.Check(int.TryParse(rest, out n) && form.TestShownCount == n, "shown " + form.TestShownCount + " (expected " + rest + ")");
                    break;
                case "info":
                    if (form == null) { log.Check(false, "expect info works only inside open..close"); break; }
                    {
                        string want = rest.Trim('"'), text = form.TestInfoText();
                        log.Check(text.Contains(want), "info box \"" + text + "\" contains \"" + want + "\"");
                    }
                    break;
                case "links":
                    if (form == null) { log.Check(false, "expect links works only inside open..close"); break; }
                    {
                        string has = form.TestLinks(), want = string.Join(",", rest.Split(',').Select(s => s.Trim()));
                        log.Check(has == want, "links \"" + has + "\" (expected \"" + want + "\")");
                    }
                    break;
                case "reopened":
                    if (reopened == null) { log.Check(false, "expect reopened works only outside the window"); break; }
                    {
                        string has = reopened();
                        log.Check(has == rest, "reopened quote " + has + " (expected " + rest + ")");
                    }
                    break;
                default:
                    log.Check(false, "unknown expect: " + arg);
                    break;
            }
        }
    }

    // ---------- the History window's side of --test-history ----------
    public partial class HistoryForm
    {
        /// Set by MainForm.TestHistoryFlow before ShowHistory: runs once on this window's Shown (test mode + --hidden only).
        internal static Action<HistoryForm> TestDriver;
        string[] doneLinks;     // the ✓ line's links (ShowDone), for the test log and "undo"
        Action[] doneClicks;

        void HookTestDriver()
        {
            Shown += delegate
            {
                var d = TestDriver;
                if (d == null || !AppSettings.TestMode || !Program.Hidden) return;
                TestDriver = null;
                BeginInvoke((MethodInvoker)delegate { d(this); });
            };
        }

        internal int TestShownCount { get { return shown.Count; } }
        internal bool TestSearchWaiting { get { return searchTimer != null && searchTimer.Enabled; } }
        internal bool TestLastSwallowed { get { return grid.LastClickSwallowed; } }
        internal string TestSearchText { get { return txtSearch.Text; } }
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr TestSendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        /// A real mouse click (WM_LBUTTONDOWN / UP) on a list row's Date cell (list view); false = no such row on screen.
        internal bool TestClickRow(int row)
        {
            if (view != ViewList || row < 0 || row >= grid.Rows.Count) return false;
            var rc = grid.GetCellDisplayRectangle(1, row, false);
            if (rc.Width <= 0) return false;
            var xy = (IntPtr)(((rc.Top + rc.Height / 2) << 16) | ((rc.Left + rc.Width / 2) & 0xFFFF));
            TestSendMessage(grid.Handle, 0x201, (IntPtr)1, xy);
            TestSendMessage(grid.Handle, 0x202, IntPtr.Zero, xy);
            Application.DoEvents();
            return true;
        }

        internal string TestInfoText() { return (info.Text ?? "").Replace("\n", " / "); }

        internal string TestLinks() { return doneShown && doneLinks != null ? string.Join(",", doneLinks) : ""; }

        internal string TestState()
        {
            string undo = undoDelete != null ? "delete of " + undoDelete.Count
                        : undoClearCopy != null ? "clear copy " + Path.GetFileName(undoClearCopy)
                        : undoClearMemory != null ? "clear (memory)" : "none";
            string links = TestLinks();
            return "state: view " + (view == ViewMedium ? "medium" : view == ViewLarge ? "large" : "list") + ", list " + list.Count +
                   ", shown " + shown.Count + ", selected " + SelectedCount() + ", undo " + undo + ", " + HistoryTest.FileState() +
                   "\n  info: " + TestInfoText() + (links.Length > 0 ? " [links: " + links + "]" : "");
        }

        /// Plays the steps between "open" and "close" with the window's own handlers: buttons by PerformClick (only when
        /// they can be clicked), the ✓ line's Undo link by its own Action, questions answered by AskForm.TestAnswers.
        internal async Task TestPlay(List<string> steps, TestLog log, string dir, string specDir)
        {
            var answers = BackupForm.AskForm.TestAnswers;
            foreach (var step in steps)
            {
                log.Add("> " + step);
                string cmd, arg;
                HistoryTest.Split(step, out cmd, out arg);
                try
                {
                    switch (cmd)
                    {
                        case "view":
                        {
                            int v = arg == "list" ? ViewList : arg == "medium" ? ViewMedium : arg == "large" ? ViewLarge : -1;
                            if (v < 0) { log.Check(false, "unknown view: " + arg); break; }
                            SetView(v);
                            log.Check(view == v, "view is " + arg);
                            break;
                        }
                        case "search":
                            // search "<text>": History's search box (filters at once); "search" alone clears it
                            txtSearch.Text = arg.Trim('"');
                            FlushSearch();   // no 200 ms wait in tests
                            Application.DoEvents();
                            log.Add("  search \"" + txtSearch.Text + "\": shown " + TestShownCount);
                            break;
                        // release-test-6: the real delay and input paths
                        case "type":
                            // type "<text>": as typed (focus in the box), the 200 ms search delay NOT skipped
                            ActiveControl = txtSearch;
                            txtSearch.Text = arg.Trim('"');
                            log.Add("  typed \"" + txtSearch.Text + "\": waiting " + TestSearchWaiting + ", shown " + TestShownCount);
                            break;
                        case "wait":
                        {
                            // wait <ms>: messages pumped (timers run) for that long
                            int ms;
                            if (!int.TryParse(arg, out ms)) ms = 300;
                            HistoryTest.Pump(ms);
                            log.Add("  waited " + ms + " ms: waiting " + TestSearchWaiting + ", shown " + TestShownCount);
                            break;
                        }
                        case "click":
                        {
                            // click <row#> (1 = top): a real mouse click (window messages) on that row's Date cell
                            int row;
                            log.Check(int.TryParse(arg, out row) && TestClickRow(row - 1), "clicked row " + arg);
                            log.Add("  click: swallowed " + grid.LastClickSwallowed + ", current " + (CurrentRecord() != null ? CurrentRecord().Id : "none") + ", shown " + TestShownCount);
                            break;
                        }
                        case "esc":
                        {
                            // Esc in the search box through the real key handling (ProcessCmdKey / Cancel)
                            ActiveControl = txtSearch;
                            var m = Message.Create(txtSearch.Handle, 0x100, (IntPtr)Keys.Escape, IntPtr.Zero);
                            bool handled = txtSearch.PreProcessMessage(ref m);
                            Application.DoEvents();
                            log.Add("  esc: handled " + handled + ", search \"" + txtSearch.Text + "\", closed " + (IsDisposed || !Visible || DialogResult == DialogResult.Cancel));
                            break;
                        }
                        case "month":
                        {
                            int index = -1;
                            for (int i = 0; i < cmbMonth.Items.Count; i++)
                            {
                                var m = (MonthItem)cmbMonth.Items[i];
                                if (arg == "all" ? m.Month == DateTime.MinValue : m.Month != DateTime.MinValue && m.Month.ToString("yyyy-MM") == arg) index = i;
                            }
                            log.Check(index >= 0, "month " + arg + " in the list");
                            if (index >= 0) cmbMonth.SelectedIndex = index;
                            break;
                        }
                        case "select":
                        {
                            var keep = new List<QuoteRecord>();
                            bool found = true;
                            if (arg == "all") keep.AddRange(shown);
                            else
                                foreach (var raw in arg.Split(','))
                                {
                                    string t = raw.Trim();
                                    int k;
                                    QuoteRecord r = t.StartsWith("#") && int.TryParse(t.Substring(1), out k) && k >= 1 && k <= shown.Count
                                        ? shown[k - 1] : shown.Find(x => x.Id == t);
                                    if (r == null) { found = false; log.Check(false, "not shown: " + t); }
                                    else keep.Add(r);
                                }
                            Fill(keep, keep.Count > 0 ? keep[0] : null);
                            if (found) log.Check(SelectedCount() == keep.Count, "selected " + SelectedCount() + " (expected " + keep.Count + ")");
                            break;
                        }
                        case "delete":
                            answers.Enqueue(arg == "no" ? DialogResult.No : DialogResult.Yes);
                            TestClick(btnDelete, "Delete", log);
                            break;
                        case "clear":
                            answers.Enqueue(arg == "no" ? DialogResult.No : DialogResult.Yes);
                            TestClick(btnClear, "Clear all", log);
                            break;
                        case "undo":
                        {
                            int i = doneShown && doneLinks != null ? Array.IndexOf(doneLinks, L.S("Undo", "Fortryd")) : -1;
                            log.Check(i >= 0 && doneClicks != null && i < doneClicks.Length, "Undo link shown");
                            if (i >= 0 && doneClicks != null && i < doneClicks.Length) doneClicks[i]();
                            break;
                        }
                        case "backup":
                        {
                            // backup <clearcopy|file> merge|replace [settings] [yes|no]; the file may be quoted (spaces)
                            var w = HistoryTest.Words(arg);
                            string path = w.Length == 0 ? null : w[0] == "clearcopy" ? History.NewestClearCopy()
                                        : Path.IsPathRooted(w[0]) ? w[0] : Path.GetFullPath(Path.Combine(specDir, w[0]));
                            bool replace = w.Contains("replace"), withSettings = w.Contains("settings");
                            if (w.Contains("yes")) answers.Enqueue(DialogResult.Yes);
                            else if (w.Contains("no")) answers.Enqueue(DialogResult.No);
                            log.Add("  backup file: " + (path ?? "(none)"));
                            bool played = false;
                            BackupForm.TestDriver = async bf =>
                            {
                                played = true;
                                try { await bf.TestRestore(path, replace, withSettings, log); }
                                catch (Exception ex) { log.Check(false, "exception in the Backup window: " + ex); }
                                bf.Close();
                            };
                            TestClick(btnBackup, "Backup…", log);
                            BackupForm.TestDriver = null;
                            log.Check(played, "Backup window opened");
                            break;
                        }
                        case "for":
                            // for friends|customer: the row menu's For on the selected rows (invoiced rows keep theirs, A4)
                            if (arg != "friends" && arg != "customer") { log.Check(false, "for needs friends or customer"); break; }
                            SetFor(arg == "friends" ? History.ForFriends : History.ForCustomer);
                            break;
                        case "show":
                        {
                            // show <all|friends|customers|notprinted|printing|printed|failed>: the Show filter (issue #7);
                            // "heading" picks the muted "Print status" line, which must move on to the next real choice
                            string[] names = { "all", "friends", "customers", "heading", "notprinted", "printing", "printed", "failed" };
                            int i = Array.IndexOf(names, arg);
                            if (i < 0) { log.Check(false, "unknown show: " + arg); break; }
                            cmbShow.SelectedIndex = i;
                            int want = i == 3 ? 4 : i;
                            log.Check(cmbShow.SelectedIndex == want && ShowValue == ShowValues[want],
                                      "show " + arg + ": item " + cmbShow.SelectedIndex + " \"" + cmbShow.Text + "\" = Show value " + ShowValue);
                            break;
                        }
                        case "status":
                        {
                            // status <notprinted|printing|printed|failed> [yes|no]: the row menu's Print status on the selected rows
                            var w = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            string[] names = { "notprinted", "printing", "printed", "failed" };
                            int k = w.Length > 0 ? Array.IndexOf(names, w[0]) : -1;
                            if (k < 0) { log.Check(false, "unknown status: " + arg); break; }
                            if (w.Contains("yes")) answers.Enqueue(DialogResult.Yes);
                            else if (w.Contains("no")) answers.Enqueue(DialogResult.No);
                            miStates[k].PerformClick();
                            break;
                        }
                        case "statuscell":
                        {
                            // statuscell <#row>: the Status cell's menu as a click opens it (four items, dot on the row's own)
                            int row;
                            if (!arg.StartsWith("#") || !int.TryParse(arg.Substring(1), out row) || row < 1 || row > grid.Rows.Count)
                            { log.Check(false, "statuscell needs #row"); break; }
                            ShowStatusMenu(row - 1);
                            var r = grid.Rows[row - 1].Tag as QuoteRecord;
                            int dot = -1;
                            for (int j = 0; j < statusMenu.Items.Count; j++) if (((ToolStripMenuItem)statusMenu.Items[j]).Checked) dot = j;
                            log.Check(statusMenu.Items.Count == 4 && r != null && dot == (int)History.PrintStateOf(r, DateTime.Now),
                                      "Status cell menu: " + statusMenu.Items.Count + " items, dot on " + (dot >= 0 ? statusMenu.Items[dot].Text : "none"));
                            Screenshots.Snap(this, dir, "test_history_statusmenu.png");
                            statusMenu.Close();
                            break;
                        }
                        case "snap":
                            Screenshots.Snap(this, dir, "test_history_" + (arg.Length > 0 ? arg : "snap") + ".png");
                            break;
                        case "invoices":
                            // the Invoices window over this window's (real, loaded) list, as Invoices… / Ctrl+I opens it
                            // (shown modeless here so the next steps can drive it; "inv close" / "inv showinhistory" end it)
                            if (testInv != null) { log.Check(false, "the Invoices window is already open"); break; }
                            log.Check(btnInvoices.Visible && btnInvoices.Enabled, "Invoices… button can be clicked");
                            invoicesShowInHistory = null;
                            // "invoices screen=<px>": as on a screen that wide (fit-to-screen, paid-tick); logs the window width
                            // "invoices screen=<w>[x<h>]" (qol 6: the height too: fewer rows, at least 5)
                            int scr = 0, scrH = 0;
                            if (arg.StartsWith("screen="))
                            {
                                var wh = arg.Substring(7).Split('x');
                                int.TryParse(wh[0], out scr);
                                if (wh.Length > 1) int.TryParse(wh[1], out scrH);
                            }
                            InvoicesForm.TestScreenWidth = scr;
                            InvoicesForm.TestScreenHeight = scrH;
                            testInv = NewInvoicesForm();
                            testInv.Show(this);
                            Application.DoEvents();
                            InvoicesForm.TestScreenWidth = 0;
                            InvoicesForm.TestScreenHeight = 0;
                            if (scr > 0) log.Check(testInv.TestWidth <= scr, "Invoices window " + testInv.TestWidth + " px wide fits a " + scr + " px screen");
                            if (scrH > 0) log.Check(testInv.TestHeight <= scrH && testInv.TestRowsShown >= 5,
                                                    "Invoices window " + testInv.TestHeight + " px tall fits a " + scrH + " px screen (" + testInv.TestRowsShown + " rows)");
                            log.Add("  " + testInv.TestStateLine());
                            break;
                        case "inv":
                            TestInvoices(arg, log, answers, dir);
                            break;
                        case "expect":
                            if (HistoryTest.Command(arg) == "inv") { TestExpectInvoices(arg.Substring(3).Trim(), log); break; }
                            if (HistoryTest.Command(arg) == "invselected")
                            {
                                // the prints of the last "inv showinhistory" are exactly the selection in History, and shown
                                var want = testInvShownJobs ?? new List<QuoteRecord>();
                                var have = SelectedRecords();
                                bool same = want.Count > 0 && have.Count == want.Count && want.All(r => have.Contains(r)) && want.All(r => shown.Contains(r));
                                log.Check(same, "History selects the invoice's " + want.Count + " print(s): selected " + have.Count +
                                                " [" + string.Join(" ", have.Select(r => r.Id)) + "]");
                                break;
                            }
                            if (HistoryTest.Command(arg) == "selected")
                            {
                                int n;
                                string rest = arg.Substring(8).Trim();
                                log.Check(int.TryParse(rest, out n) && SelectedCount() == n, "History selected " + SelectedCount() + " (expected " + rest + ")");
                                break;
                            }
                            HistoryTest.Expect(arg, log, list, this, null);
                            break;
                        case "close":
                            if (testInv != null) { testInv.Close(); testInv.Dispose(); testInv = null; }
                            log.Add("  " + TestState());
                            Close();
                            return;
                        default:
                            log.Check(false, "unknown step: " + step);
                            break;
                    }
                }
                catch (Exception ex) { log.Check(false, "exception: " + ex); }
                if (answers.Count > 0) { log.Check(false, "question not asked (" + answers.Count + " answer(s) left)"); answers.Clear(); }
                if (cmd != "expect") log.Add("  " + TestState());
                await Task.Delay(50);   // let queued work run (picture loading, BeginInvokes)
            }
            Close();   // no "close" line: the window closes after the last step
        }

        InvoicesForm testInv;   // the Invoices window opened by the "invoices" step
        List<QuoteRecord> testInvShownJobs;   // the prints of the last "inv showinhistory" (expect invselected)
        static readonly string[] InvStateNames = { "notprinted", "printing", "printed", "failed" };
        static readonly string[] InvFilterNames = { "all", "notprinted", "printing", "printed", "failed", "mixed" };
        static readonly string[] InvPeriodNames = { "all", "month", "30days", "year" };
        static readonly string[] InvPaymentNames = { "all", "unpaid", "overdue", "paid" };

        /// inv search <text> | filter <all|notprinted|printing|printed|failed|mixed> | period <all|month|30days|year> |
        /// select <invoice no.|#row> | set <notprinted|printing|printed|failed> [yes|no] | showinhistory | close | snap [name]
        void TestInvoices(string arg, TestLog log, Queue<DialogResult> answers, string dir)
        {
            if (testInv == null) { log.Check(false, "inv " + arg + ": the Invoices window is not open (step \"invoices\" first)"); return; }
            string cmd, rest;
            HistoryTest.Split(arg, out cmd, out rest);
            switch (cmd)
            {
                case "search":
                    testInv.TestSearch(rest.Trim('"'));
                    break;
                case "filter":
                {
                    int i = Array.IndexOf(InvFilterNames, rest);
                    if (i < 0) { log.Check(false, "unknown inv filter: " + rest); return; }
                    testInv.TestStatusFilter(i);
                    break;
                }
                case "period":
                {
                    int i = Array.IndexOf(InvPeriodNames, rest);
                    if (i < 0) { log.Check(false, "unknown inv period: " + rest); return; }
                    testInv.TestPeriod(i);
                    break;
                }
                case "payment":
                {
                    int i = Array.IndexOf(InvPaymentNames, rest);
                    if (i < 0) { log.Check(false, "unknown inv payment: " + rest); return; }
                    testInv.TestPayment(i);
                    break;
                }
                case "paid":
                    // the Paid cell's click / Space on the selected invoice: paid <-> unpaid, saved at once
                    if (testInv.TestSelected == null) { log.Check(false, "inv paid: no invoice selected"); return; }
                    testInv.TestTogglePaid();
                    break;
                case "undo":
                    // qol 5: the Undo link on the ✓ line after a paid / unpaid tick, clicked on screen (fails when it isn't there)
                    log.Check(testInv.TestUndoPaid(), "Undo link of the last paid / unpaid tick is on screen and clicked");
                    break;
                // release-test-6: real keys / clicks and the real 200 ms search delay
                case "type":
                    testInv.TestType(rest.Trim('"'));
                    log.Add("  typed \"" + rest.Trim('"') + "\": waiting " + testInv.TestSearchWaiting + ", rows " + testInv.TestRowCount);
                    break;
                case "wait":
                {
                    int ms;
                    if (!int.TryParse(rest, out ms)) ms = 300;
                    HistoryTest.Pump(ms);
                    log.Add("  waited " + ms + " ms: waiting " + testInv.TestSearchWaiting + ", rows " + testInv.TestRowCount);
                    break;
                }
                case "esc":
                {
                    bool handled = testInv.TestEscape();
                    Application.DoEvents();
                    log.Add("  esc: handled " + handled + ", closed " + testInv.TestClosedByEscape);
                    break;
                }
                case "space":
                    testInv.TestSpace();
                    log.Add("  space: swallowed " + testInv.TestLastSwallowed);
                    break;
                case "clickpaid":
                    log.Check(testInv.TestClickPaid(rest), "clicked the Paid cell of " + rest);
                    log.Add("  click: swallowed " + testInv.TestLastSwallowed);
                    break;
                case "select":
                {
                    string no = rest;
                    int k;
                    if (rest.StartsWith("#") && int.TryParse(rest.Substring(1), out k))
                        no = k >= 1 && k <= testInv.TestRowCount ? testInv.TestRows[k - 1].No : null;
                    if (no != null) testInv.TestSelect(no);
                    var sel = testInv.TestSelected;
                    log.Check(no != null && sel != null && string.Equals(sel.No, no, StringComparison.OrdinalIgnoreCase),
                              "invoice " + rest + " selected (" + (sel != null ? sel.No + ", " + sel.Jobs.Count + " print(s)" : "none") + ")");
                    break;
                }
                case "set":
                {
                    var w = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int k = w.Length > 0 ? Array.IndexOf(InvStateNames, w[0]) : -1;
                    if (k < 0) { log.Check(false, "unknown inv set: " + rest); return; }
                    if (testInv.TestSelected == null) { log.Check(false, "inv set: no invoice selected"); return; }
                    if (w.Contains("yes")) answers.Enqueue(DialogResult.Yes);
                    else if (w.Contains("no")) answers.Enqueue(DialogResult.No);
                    testInv.SetStatus(testInv.TestSelected, (PrintState)k);
                    break;
                }
                case "showinhistory":
                {
                    var sel = testInv.TestSelected;
                    log.Check(sel != null, "Show in History with an invoice selected");
                    testInvShownJobs = sel != null ? new List<QuoteRecord>(sel.Jobs) : null;
                    testInv.TestShowInHistory();
                    Application.DoEvents();
                    log.Check(testInv.IsDisposed || !testInv.Visible, "the Invoices window closed");
                    if (!testInv.IsDisposed) testInv.Dispose();
                    testInv = null;
                    AfterInvoices();
                    return;
                }
                case "close":
                    testInv.Close();
                    testInv.Dispose();
                    testInv = null;
                    AfterInvoices();
                    return;
                case "snap":
                    Screenshots.Snap(testInv, dir, "test_invoices_" + (rest.Length > 0 ? rest : "snap") + ".png");
                    break;
                default:
                    log.Check(false, "unknown inv step: " + arg);
                    return;
            }
            log.Add("  " + testInv.TestStateLine());
        }

        /// expect inv paid <no|#n> <yes|no> | unpaid "<text>" | rows <n> | sorted | first <no> | state <no> <notprinted|printing|printed|failed|mixed> |
        /// prints <no> <n> | count "<text>" | info "<text>" | has <no> | hasnot <no>
        void TestExpectInvoices(string arg, TestLog log)
        {
            if (testInv == null) { log.Check(false, "expect inv " + arg + ": the Invoices window is not open"); return; }
            string what, rest;
            HistoryTest.Split(arg, out what, out rest);
            var rows = testInv.TestRows;
            var w = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int n;
            // an invoice by its number, or "#row" (1 = the top row shown)
            Func<string, InvoiceRow> find = no =>
                no.StartsWith("#") && int.TryParse(no.Substring(1), out n) ? (n >= 1 && n <= rows.Count ? rows[n - 1] : null)
                : rows.FirstOrDefault(r => string.Equals(r.No, no, StringComparison.OrdinalIgnoreCase));
            string[] names = { "notprinted", "printing", "printed", "failed" };
            switch (what)
            {
                case "selstate":
                case "selsaved":
                {
                    // selstate <state|mixed>: the selected row's status; selsaved <state>: all its prints, memory + history.json
                    var sel = testInv.TestSelected;
                    if (sel == null) { log.Check(false, "expect inv " + what + ": no invoice selected"); break; }
                    if (what == "selstate")
                    {
                        string has = sel.State == null ? "mixed" : names[(int)sel.State.Value];
                        log.Check(has == rest, "selected invoice " + sel.No + " row status " + has + " (expected " + rest + ")");
                        break;
                    }
                    var now = DateTime.Now;
                    var ids = new HashSet<string>(sel.Jobs.Select(j => j.Id));
                    var file = HistoryTest.FileRecords();
                    string m = string.Join(",", list.Where(x => ids.Contains(x.Id)).Select(x => names[(int)History.PrintStateOf(x, now)]));
                    string fs = file == null ? "-" : string.Join(",", file.Where(x => ids.Contains(x.Id)).Select(x => names[(int)History.PrintStateOf(x, now)]));
                    log.Check(m.Length > 0 && m == fs && m.Split(',').All(s => s == rest),
                              "selected invoice " + sel.No + " (" + sel.Jobs.Count + " prints): memory [" + m + "], file [" + fs + "] (expected all " + rest + ")");
                    break;
                }
                case "rows":
                    log.Check(int.TryParse(rest, out n) && rows.Count == n, "Invoices rows " + rows.Count + " (expected " + rest + ")");
                    break;
                case "paid":
                {
                    // paid <no|#n> <yes|no>: the row's tick, every job in memory and in history.json (PaidAt set / "")
                    var r = w.Length == 2 ? find(w[0]) : null;
                    if (r == null) { log.Check(false, "expect inv paid: invoice " + rest + " not listed"); break; }
                    bool want = w[1] == "yes";
                    var ids = new HashSet<string>(r.Jobs.Select(j => j.Id));
                    var file = HistoryTest.FileRecords();
                    // yes = every job paid; no = not all (a partly paid combined invoice is unpaid); memory and file agree per job
                    string ms = string.Join(",", list.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).Select(x => History.IsPaid(x) ? "paid" : "unpaid"));
                    string fls = file == null ? "-" : string.Join(",", file.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).Select(x => History.IsPaid(x) ? "paid" : "unpaid"));
                    bool allPaid = ms.Length > 0 && ms.Split(',').All(s => s == "paid");
                    bool mem = allPaid == want;
                    bool fs = file != null && fls == ms;
                    log.Check(r.Paid == want && mem && fs, "invoice " + r.No + " (" + r.Jobs.Count + " print(s)) paid " + r.Paid + ", memory [" + ms + "], file [" + fls + "] (expected " + w[1] + ")" +
                                                         (r.Paid ? ", tip \"" + (testInv.TestPaidTip(rows.IndexOf(r)) ?? "").Replace("\n", " / ") + "\"" : ""));
                    break;
                }
                case "waiting":
                    log.Check(testInv.TestSearchWaiting == (rest == "yes"), "Invoices search waiting " + testInv.TestSearchWaiting + " (expected " + rest + ")");
                    break;
                case "swallowed":
                    log.Check(testInv.TestLastSwallowed == (rest == "yes"), "Invoices last click / Space swallowed " + testInv.TestLastSwallowed + " (expected " + rest + ")");
                    break;
                case "closed":
                    log.Check(testInv.TestClosedByEscape == (rest == "yes"), "Invoices closed by Esc " + testInv.TestClosedByEscape + " (expected " + rest + ")");
                    break;
                case "search":
                    log.Check(testInv.TestSearchText == rest.Trim('"'), "Invoices search box \"" + testInv.TestSearchText + "\" (expected " + rest + ")");
                    break;
                case "empty":
                {
                    // qol 9: the grey "No invoices match …" line ("" = not shown)
                    string want = rest.Trim('"');
                    log.Check(want.Length == 0 ? testInv.TestEmptyText.Length == 0 : testInv.TestEmptyText.Contains(want),
                              "Invoices empty line \"" + testInv.TestEmptyText + "\" (expected \"" + want + "\")");
                    break;
                }
                case "unpaid":
                {
                    string want = rest.Trim('"');
                    log.Check(testInv.TestUnpaidLine.Contains(want), "Invoices unpaid line \"" + testInv.TestUnpaidLine + "\" contains \"" + want + "\"");
                    break;
                }
                case "sorted":
                {
                    // newest invoice date first, unreadable dates last, then number descending (History.Invoices)
                    int bad = -1;
                    for (int i = 1; i < rows.Count && bad < 0; i++)
                    {
                        var a = rows[i - 1]; var b = rows[i];
                        bool ga = a.BilledAt != DateTime.MinValue, gb = b.BilledAt != DateTime.MinValue;
                        bool ok = ga != gb ? ga : a.BilledAt != b.BilledAt ? a.BilledAt > b.BilledAt : string.CompareOrdinal(a.No, b.No) >= 0;
                        if (!ok) bad = i;
                    }
                    log.Check(bad < 0, "Invoices sorted newest first (" + rows.Count + " rows" +
                                       (bad >= 0 ? "; row " + (bad + 1) + " " + rows[bad].No + " " + rows[bad].BilledAt.ToString("yyyy-MM-dd HH:mm") +
                                                   " after " + rows[bad - 1].No + " " + rows[bad - 1].BilledAt.ToString("yyyy-MM-dd HH:mm") : "") + ")");
                    break;
                }
                case "first":
                    log.Check(rows.Count > 0 && string.Equals(rows[0].No, rest, StringComparison.OrdinalIgnoreCase),
                              "first invoice " + (rows.Count > 0 ? rows[0].No : "none") + " (expected " + rest + ")");
                    break;
                case "has":
                case "hasnot":
                    log.Check((find(rest) != null) == (what == "has"), "invoice " + rest + (find(rest) != null ? " is listed" : " is not listed") + " (expected " + what + ")");
                    break;
                case "state":
                {
                    var r = w.Length == 2 ? find(w[0]) : null;
                    string has = r == null ? "not listed" : r.State == null ? "mixed" : InvStateNames[(int)r.State.Value];
                    log.Check(r != null && has == w[1], "invoice " + (w.Length > 0 ? w[0] : "?") + " row status " + has + " (expected " + rest + ")");
                    break;
                }
                case "prints":
                {
                    var r = w.Length == 2 ? find(w[0]) : null;
                    log.Check(r != null && int.TryParse(w[1], out n) && r.Jobs.Count == n,
                              "invoice " + (w.Length > 0 ? w[0] : "?") + " has " + (r != null ? r.Jobs.Count.ToString() : "-") + " print(s) (expected " + rest + ")");
                    break;
                }
                case "count":
                {
                    string want = rest.Trim('"');
                    log.Check(testInv.TestCount.Contains(want), "Invoices count \"" + testInv.TestCount + "\" contains \"" + want + "\"");
                    break;
                }
                case "info":
                {
                    string want = rest.Trim('"');
                    log.Check(testInv.TestInfoText.Contains(want), "Invoices info \"" + testInv.TestInfoText + "\" contains \"" + want + "\"");
                    break;
                }
                default:
                    log.Check(false, "unknown expect inv: " + arg);
                    break;
            }
        }

        static void TestClick(Button b, string name, TestLog log)
        {
            bool can = b.Visible && b.Enabled;
            log.Check(can, name + " button can be clicked");
            if (can) b.PerformClick();
        }
    }
}
