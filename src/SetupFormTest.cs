using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintPrice
{
    /// --test-setup "<spec>" (idea 19; test mode + --hidden only; plans\idea19-setup\followup-plan.md). Program hands the
    /// spec to this class: the "setup … end" block (or "nosetup") is played inside the first-start setup window by
    /// SetupForm.TestPlay, the lines after "main" by PlayMain once the main window shows ("again … end" = Settings > App >
    /// Run setup again…). Writes <folder>\test_setup.txt; exit 0 = no FAIL, 4 = a FAIL (also after 120 s).
    internal sealed class SetupTest
    {
        /// True only in a --test-setup run: lets SetupForm use TestFetch (the stand-in for the live price server).
        internal static bool Active;

        readonly List<string> lines;
        readonly string dir;
        public readonly TestLog Log = new TestLog();
        readonly List<string> firstBlock;   // "setup" block (null = none)
        readonly bool expectNoWindow;       // "nosetup"
        readonly int mainAt;                // index after "main" (lines.Count = none)
        bool windowPlayed;
        System.Windows.Forms.Timer guard;

        public SetupTest(string specFile)
        {
            dir = AppSettings.Folder;
            lines = File.ReadAllLines(specFile).Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("//")).ToList();
            int i = 0;
            if (i < lines.Count && HistoryTest.Command(lines[i]) == "setup")
            {
                firstBlock = new List<string>();
                while (++i < lines.Count && HistoryTest.Command(lines[i]) != "end") firstBlock.Add(lines[i]);
                i++;
            }
            else if (i < lines.Count && HistoryTest.Command(lines[i]) == "nosetup") { expectNoWindow = true; i++; }
            while (i < lines.Count && HistoryTest.Command(lines[i]) != "main") i++;
            mainAt = Math.Min(lines.Count, i + 1);
        }

        /// The fixed price every test-mode run uses instead of Energi Data Service (MainForm and SetupForm).
        internal static PowerQuote StandInPower(string area, double spot)
        {
            return new PowerQuote { Area = area == "DK2" ? "DK2" : "DK1", SlotStartUtc = DateTime.UtcNow, SpotDkkPerKwh = spot };
        }

        /// The line every --test-* hook log starts with, so a reader sees no live server was asked.
        internal const string StandInNote = "live price: test stand-in (1,20 kr/kWh spot), never Energi Data Service";

        void Write() { try { File.WriteAllLines(Path.Combine(dir, "test_setup.txt"), Log.Lines); } catch { } }

        /// Before the setup window could open: the 120 s guard, the question queue, the live-price stand-in (1,20 kr/kWh
        /// spot, never the real server).
        public void Start(AppSettings settings)
        {
            Active = true;
            SetupForm.TestFetch = a => StandInPower(a, 1.20);
            BackupForm.AskForm.TestAnswers = new Queue<DialogResult>();
            BackupForm.AskForm.TestLog = Log;
            guard = new System.Windows.Forms.Timer { Interval = 120000 };
            guard.Tick += delegate { Log.Check(false, "timeout: the run took over 120 s"); Write(); Environment.Exit(4); };
            guard.Start();
            Log.Add(StandInNote);
            Log.Add("start: settings.ini " + (File.Exists(AppSettings.FilePath) ? "there" : "none") + ", history.json " +
                    (File.Exists(History.FilePath) ? "there" : "none") + ", SetupDone " + settings.SetupDone);
            if (firstBlock != null)
                SetupForm.TestDriver = async f =>
                {
                    windowPlayed = true;
                    try { await f.TestPlay(firstBlock, this); }
                    catch (Exception ex) { Log.Check(false, "exception in the setup window: " + ex); f.Close(); }
                };
        }

        /// After the start (setup window shown or not), before the main window.
        public void AfterStart(bool shown, AppSettings settings)
        {
            SetupForm.TestDriver = null;
            if (firstBlock != null) Log.Check(shown && windowPlayed, "setup window opened at the first start");
            if (expectNoWindow) Log.Check(!shown, "no setup window at the start");
            Log.Add("after the start: SetupDone " + settings.SetupDone + ", settings.ini " + (File.Exists(AppSettings.FilePath) ? "there" : "none"));
        }

        /// The lines after "main", once the main window shows.
        public async void PlayMain(MainForm main)
        {
            try
            {
                Log.Add("main: " + MainState(main));
                for (int i = mainAt; i < lines.Count; i++)
                {
                    string cmd, arg;
                    HistoryTest.Split(lines[i], out cmd, out arg);
                    if (cmd == "again")
                    {
                        var block = new List<string>();
                        while (++i < lines.Count && HistoryTest.Command(lines[i]) != "end") block.Add(lines[i]);
                        Log.Add("> again");
                        bool played = false, finished = false;
                        SetupForm.TestDriver = async f =>
                        {
                            played = true;
                            try { await f.TestPlay(block, this); }
                            catch (Exception ex) { Log.Check(false, "exception in the setup window: " + ex); f.Close(); }
                            finished = true;
                        };
                        main.TestSetupAgain();   // Settings > App > Run setup again… (the real button), then RunSetupAgain
                        for (int w = 0; w < 200 && !finished; w++) await Task.Delay(50);
                        await Task.Delay(300);   // RunSetupAgain takes the answers over after the window closes
                        SetupForm.TestDriver = null;
                        Log.Check(played, "setup window opened from Settings");
                        Log.Add("  " + MainState(main));
                        continue;
                    }
                    Log.Add("> " + lines[i]);
                    switch (cmd)
                    {
                        case "expect": ExpectMain(arg, main); break;
                        case "snap": Screenshots.Snap(main, dir, "test_setup_" + (arg.Length > 0 ? arg : "main") + ".png"); break;
                        default: Log.Check(false, "unknown step: " + lines[i]); break;
                    }
                }
            }
            catch (Exception ex) { Log.Check(false, "exception: " + ex); }
            if (guard != null) guard.Stop();
            Write();
            Program.EndTestRun(Log.Ok ? 0 : 4);
        }

        static string Printers(AppSettings s)
        {
            return string.Join(",", s.Printers.Select(p => p.Model + (p.Id == s.ActivePrinterId ? "*" : "")).ToArray());
        }

        static string MainState(MainForm main)
        {
            var s = main.TestSettings;
            return "SetupDone " + s.SetupDone + ", language \"" + s.Language + "\", printers " + Printers(s) + ", filament " +
                   Num.Fmt(s.PriceFor(s.FilamentPreset)) + ", power " + (s.AutoPowerPrice != 0 ? "live " + s.PriceArea : "fixed " + Num.Fmt(s.LastElectricityPrice)) +
                   ", markups " + Num.Fmt(s.FriendsMarkupPct) + "/" + Num.Fmt(s.OthersMarkupPct) + ", minimum " + Num.Fmt(s.MinimumPrice) +
                   ", name \"" + s.QuoteName + "\", hint " + (main.TestHintShown ? "yes" : "no") + ", " + HistoryTest.FileState() +
                   "\n  power line: " + main.TestPowerLine;
        }

        void ExpectMain(string arg, MainForm main)
        {
            string what, rest;
            HistoryTest.Split(arg, out what, out rest);
            switch (what)
            {
                case "ini":
                {
                    // a line of settings.ini on disk, e.g. "SetupDone=1", "Language=" (empty value)
                    string[] file = File.Exists(AppSettings.FilePath) ? File.ReadAllLines(AppSettings.FilePath) : new string[0];
                    int eq = rest.IndexOf('=');
                    string key = eq < 0 ? rest : rest.Substring(0, eq);
                    string line = file.FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal));
                    Log.Check(line != null && line == rest, "settings.ini " + rest + " (is: " + (line ?? "no such line") + ")");
                    break;
                }
                case "printers":
                {
                    string got = Printers(main.TestSettings);
                    Log.Check(got == rest, "printers " + rest + " (is: " + got + ")");
                    break;
                }
                case "history":
                {
                    var f = HistoryTest.FileRecords();
                    int n = f == null ? 0 : f.Count;
                    Log.Check(n.ToString() == rest, "history.json has " + rest + " quotes (is: " + n + ")");
                    break;
                }
                case "hint":
                    Log.Check(main.TestHintShown == (rest == "yes"), "setup hint in the main window: " + rest);
                    break;
                case "lang":
                    Log.Check(L.Danish == (rest == "da"), "the app runs in " + rest);
                    break;
                default:
                    Log.Check(false, "unknown expect: " + arg);
                    break;
            }
        }
    }

    // ---------- the setup window's side of --test-setup ----------
    sealed partial class SetupForm
    {
        /// Set by SetupTest before the window opens: runs once on Shown (test mode + --hidden only).
        internal static Action<SetupForm> TestDriver;
        /// --test-setup's stand-in for PowerPrice.GetCurrent (used only while SetupTest.Active).
        internal static Func<string, PowerQuote> TestFetch;
        static readonly string[] RowNames = { "language", "printers", "studio", "filament", "electricity", "markups", "minimum", "name" };

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

        string Tiles()
        {
            return string.Join(",", models.Select(m => m + (again && haveModels.Contains(m) ? "#" : "") + (!again && models[0] == m ? "*" : "")).ToArray());
        }

        TextBox BoxNamed(string name)
        {
            switch (name)
            {
                case "filament": return txtFilament;
                case "elec": return txtElec;
                case "friends": return txtFriends;
                case "customers": return txtOthers;
                case "minimum": return txtMin;
                case "name": return txtName;
                default: return null;
            }
        }

        internal string TestState()
        {
            var red = new List<string>();
            foreach (var n in new[] { "filament", "elec", "friends", "customers", "minimum" })
                if (BoxNamed(n).BackColor == Theme.InvalidBack) red.Add(n);
            return "step " + (step + 1) + " \"" + lblTitle.Text + "\", lang " + lang + (L.Danish ? " (window da)" : " (window en)") +
                   ", tiles " + Tiles() + ", filament " + filamentText + ", power " + (live ? "live " + area : "fixed " + elecText) +
                   ", markups " + friendsText + "/" + othersText + ", minimum " + minText + ", name \"" + nameText + "\"" +
                   (red.Count > 0 ? ", red " + string.Join(",", red.ToArray()) : "") + ", live \"" + (liveLine ?? "") + "\", example " +
                   example.Prices + ", studio " + (BambuSlicer.FindExe(slicerPath) != null ? "found" : "missing");
        }

        static string Unquote(string s)
        {
            s = (s ?? "").Trim();
            return s.Length >= 2 && s.StartsWith("\"") && s.EndsWith("\"") ? s.Substring(1, s.Length - 2) : s;
        }

        /// Plays a block with the window's own handlers. A step that closes the window (finish, later, esc, a restore with
        /// settings) ends the block; lines after it are a FAIL.
        internal async Task TestPlay(List<string> steps, SetupTest t)
        {
            var log = t.Log;
            log.Add("  window: " + TestState());
            for (int k = 0; k < steps.Count; k++)
            {
                string line = steps[k];
                log.Add("> " + line);
                string cmd, arg;
                HistoryTest.Split(line, out cmd, out arg);
                bool closes = false;
                switch (cmd)
                {
                    case "next": NextStep(); closes = finishing; break;
                    case "finish":
                        log.Check(step == StepSummary, "on the summary");
                        if (step == StepSummary) { NextStep(); closes = true; }
                        break;
                    case "skip":
                        log.Check(btnSkip.Visible, "Skip shown");
                        if (btnSkip.Visible) SkipStep();
                        break;
                    case "back":
                        log.Check(btnBack.Enabled, "Back can be clicked");
                        if (btnBack.Enabled) ShowStep(step - 1);
                        break;
                    case "later":
                        log.Check(lnkLater.Visible, "Set up later shown");
                        Close();   // the link's handler
                        closes = true;
                        break;
                    case "esc":
                    {
                        var m = new Message();
                        ProcessCmdKey(ref m, Keys.Escape);
                        closes = true;
                        break;
                    }
                    case "lang":
                        (arg == "da" ? choiceDa : choiceEn).Checked = true;   // the round choice (rebuilds the window on a first run)
                        await Task.Delay(150);
                        break;
                    case "tick":
                    case "untick":
                    {
                        var tile = tiles.FirstOrDefault(x => x.Model == arg);
                        log.Check(tile != null, "tile " + arg);
                        if (tile != null) InvokeOnClick(tile, EventArgs.Empty);
                        break;
                    }
                    case "filament": case "friends": case "customers": case "minimum": case "name": case "elec":
                        BoxNamed(cmd).Text = Unquote(arg);   // as typed (TextChanged)
                        break;
                    case "power":
                    {
                        var w = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (w.Length > 0 && w[0] == "live")
                        {
                            choiceLive.Checked = true;
                            if (w.Length > 1) cmbArea.SelectedIndex = w[1] == "DK2" ? 1 : 0;
                        }
                        else
                        {
                            choiceFixed.Checked = true;
                            if (w.Length > 1) txtElec.Text = w[1];
                        }
                        break;
                    }
                    case "live":
                    {
                        double spot;
                        if (arg == "fail") TestFetch = a => { throw new System.Net.WebException("offline (test stand-in)"); };
                        else if (Num.TryParse(arg, out spot)) TestFetch = a => new PowerQuote { Area = a, SlotStartUtc = DateTime.UtcNow, SpotDkkPerKwh = spot };
                        else { log.Check(false, "live fail | <spot price>"); break; }
                        liveFor = null;   // fetch again with the new stand-in
                        FetchLive();
                        break;
                    }
                    case "wait":
                    {
                        string busy = L.S("Getting the price now…", "Henter prisen nu…");
                        for (int w = 0; w < 100 && liveLine == busy; w++) await Task.Delay(100);
                        log.Check(liveLine != busy, "live price answered");
                        break;
                    }
                    case "change":
                    {
                        int i = Array.IndexOf(RowNames, arg);
                        log.Check(i >= 0 && step == StepSummary, "Change link " + arg + " on the summary");
                        if (i >= 0 && step == StepSummary) InvokeOnClick(sumChange[i], EventArgs.Empty);
                        break;
                    }
                    case "restore":
                    {
                        // restore sample [settings] [merge|replace]: a sample backup in the test folder, then the real
                        // "Restore a backup…" button and Backup window (BackupForm.TestRestore)
                        var w = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        bool withSettings = w.Contains("settings"), replace = w.Contains("replace");
                        string path = Path.Combine(AppSettings.Folder, "sample" + History.BackupExtension);
                        var bs = new AppSettings { Language = "da", FriendsMarkupPct = 33 };
                        string err = History.WriteBackupFile(path, HistoryForm.SampleQuotes(), bs, AppInfo.Name, DateTime.Now);
                        log.Check(err == null, "sample backup written" + (err != null ? ": " + err : ""));
                        log.Check(btnRestore.Visible, "Restore a backup… shown");
                        bool played = false;
                        BackupForm.TestDriver = async bf =>
                        {
                            played = true;
                            try { await bf.TestRestore(path, replace, withSettings, log); }
                            catch (Exception ex) { log.Check(false, "exception in the Backup window: " + ex); }
                            bf.Close();
                        };
                        if (btnRestore.Visible) InvokeOnClick(btnRestore, EventArgs.Empty);
                        BackupForm.TestDriver = null;
                        log.Check(played, "Backup window opened");
                        log.Add("  restore info: " + infoRestore.Text.Replace("\n", " / "));
                        closes = Restored;
                        break;
                    }
                    case "snap":
                        Screenshots.Snap(this, AppSettings.Folder, "test_setup_" + (arg.Length > 0 ? arg : "snap") + ".png");
                        break;
                    case "expect":
                        TestExpect(arg, log);
                        break;
                    default:
                        log.Check(false, "unknown step: " + line);
                        break;
                }
                if (closes)
                {
                    log.Add("  window closed: " + (Restored ? "restored" : finishing ? "finished" : again ? "cancel" : "set up later"));
                    if (k < steps.Count - 1) log.Check(false, (steps.Count - 1 - k) + " step(s) after the window closed");
                    return;   // nothing awaited after this: the window's own loop ends now
                }
                if (cmd != "expect") log.Add("  " + TestState());
                await Task.Delay(50);
            }
            log.Check(false, "the block ended with the window still open");
            Close();
        }

        void TestExpect(string arg, TestLog log)
        {
            string what, rest;
            HistoryTest.Split(arg, out what, out rest);
            switch (what)
            {
                case "step":
                    log.Check((step + 1).ToString() == rest, "step " + rest + " (is " + (step + 1) + ")");
                    break;
                case "title":
                    log.Check(lblTitle.Text == Unquote(rest), "title " + rest + " (is \"" + lblTitle.Text + "\")");
                    break;
                case "tiles":
                    log.Check(Tiles() == rest, "tiles " + rest + " (is " + Tiles() + ")");
                    break;
                case "red":
                {
                    var b = BoxNamed(rest);
                    log.Check(b != null && b.BackColor == Theme.InvalidBack, rest + " box is red");
                    break;
                }
                case "example":
                {
                    // expect example 50,30 64,95
                    var w = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string want = w.Length == 2 ? w[0] + " kr. | " + w[1] + " kr." : rest;
                    log.Check(example.Prices == want, "example " + want + " (is " + example.Prices + ")");
                    break;
                }
                case "live":
                    log.Check((liveLine ?? "").Contains(Unquote(rest)), "live line has " + rest + " (is \"" + liveLine + "\")");
                    break;
                case "row":
                {
                    // expect row markups "Friends & family 25 % · customers 75 %"
                    string name, text;
                    HistoryTest.Split(rest, out name, out text);
                    int i = Array.IndexOf(RowNames, name);
                    if (i < 0 || step != StepSummary) { log.Check(false, "row " + name + " on the summary"); break; }
                    string full = tip.GetToolTip(sumValue[i]);
                    if (string.IsNullOrEmpty(full)) full = sumValue[i].Text;
                    log.Check(full == Unquote(text), "row " + name + " " + text + " (is \"" + full + "\")");
                    break;
                }
                case "lang":
                    log.Check(lang == rest && (again || L.Danish == (rest == "da")), "language " + rest);
                    break;
                default:
                    log.Check(false, "unknown expect: " + arg);
                    break;
            }
        }
    }
}
