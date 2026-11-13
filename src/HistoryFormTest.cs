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
        /// "cmd rest of line" -> cmd (lower case) + arg (trimmed, "" when none).
        public static void Split(string line, out string cmd, out string arg)
        {
            line = (line ?? "").Trim();
            int sp = line.IndexOf(' ');
            cmd = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
            arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
        }

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
                            // backup <clearcopy|file> merge|replace [settings] [yes|no]
                            var w = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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
                        case "snap":
                            Screenshots.Snap(this, dir, "test_history_" + (arg.Length > 0 ? arg : "snap") + ".png");
                            break;
                        case "expect":
                            HistoryTest.Expect(arg, log, list, this, null);
                            break;
                        case "close":
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

        static void TestClick(Button b, string name, TestLog log)
        {
            bool can = b.Visible && b.Enabled;
            log.Check(can, name + " button can be clicked");
            if (can) b.PerformClick();
        }
    }
}
