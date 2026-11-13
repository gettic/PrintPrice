using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PrintPrice.Web
{
    /// Backup and restore on the website (stage 3b), without browser calls (web\tests runs it). The file is the desktop's
    /// .printprice-backup (History.ToBackup / ReadBackup / Combine); the online copy uses the same Worker routes and blob as
    /// the desktop's OnlineBackup (src\OnlineBackup.cs is linked; its HTTP part is left out under WEB).
    public static class WebBackup
    {
        public const string App = "web";                       // BackupFile.App of a backup made here
        public const string LastBackupKey = "PrintPrice.lastBackup";   // localStorage: "yyyy-MM-dd HH:mm|file" / "|online"
        public const string SafetyPrefix = "before-restore-";   // IndexedDB: + "yyyyMMdd-HHmmss" = a backup text of what was here
        public const int SafetyKept = 3;
        public const int MaxFileBytes = 64 * 1024 * 1024;
        public const int RemindDays = 30;                       // "Last backup" turns into a warning after this many days

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Quotes(int n)
        {
            return n == 1 ? L.S("1 quote", "1 tilbud") : L.F("{0} quotes", "{0} tilbud", n.ToString("N0", Num.Dk));
        }

        // ---------- the file ----------

        /// The backup text: all quotes and the settings (History.ToBackup, PcOnlyKeys left out).
        public static string FileText(List<QuoteRecord> list, AppSettings s, DateTime now) { return History.ToBackup(list, s, App, now); }

        /// Data URL characters of pictures in a backup written here (newest first). Lower than the desktop's
        /// History.FilePictureBudget (40 MB) so phones' memory copes; reading still takes MaxFileBytes.
        public const long PictureBudget = 20L * 1024 * 1024;

        /// The backup text with this browser's pictures (HistoryStore.BackupPictures).
        public static string FileText(List<QuoteRecord> list, AppSettings s, DateTime now, List<BackupPicture> pictures)
        {
            return History.ToBackup(list, s, App, now, pictures);
        }

        /// The text for an online backup: the newest pictures that keep the blob within OnlineBackup.MaxBlob (Backup.FitForUpload).
        public static string OnlineText(List<QuoteRecord> list, AppSettings s, DateTime now, List<BackupPicture> pictures)
        {
            return Backup.FitForUpload(list, s, App, now, pictures, OnlineBackup.MaxBlob);
        }

        /// How many pictures a backup text holds (0 for none or unreadable).
        public static int PictureCount(string backupText)
        {
            BackupData d;
            return History.ReadBackup(backupText, out d) == null ? d.Pictures.Count : 0;
        }

        /// " With the newest 12 of 40 pictures; a backup file holds them all." / " With 12 pictures." / "" (none).
        public static string PicturesSentText(int sent, int total)
        {
            if (total <= 0) return "";
            if (sent >= total) return " " + (total == 1 ? L.S("With 1 picture.", "Med 1 billede.") : L.F("With {0} pictures.", "Med {0} billeder.", total.ToString("N0", Num.Dk)));
            return " " + L.F("With the newest {0} of {1} pictures (online backups are limited in size; a backup file holds more).",
                             "Med de nyeste {0} af {1} billeder (online-kopier har en størrelsesgrænse; en kopi-fil kan rumme flere).",
                             sent.ToString("N0", Num.Dk), total.ToString("N0", Num.Dk));
        }

        /// A chosen / dropped file's bytes -> the backup (also a desktop history.json). null = OK, else the problem text.
        public static string ReadFile(byte[] bytes, out BackupData data)
        {
            data = null;
            if (bytes == null || bytes.Length == 0) return L.S("This is not a PrintPrice backup.", "Det er ikke en PrintPrice-sikkerhedskopi.");
            if (bytes.Length > MaxFileBytes) return L.S("This file is too big to be a PrintPrice backup.", "Filen er for stor til at være en PrintPrice-sikkerhedskopi.");
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (Exception) { return L.S("This is not a PrintPrice backup.", "Det er ikke en PrintPrice-sikkerhedskopi."); }
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            return ReadText(text, out data);
        }

        /// History.ReadBackup, but "made by a newer version" says to reload the page (the desktop text says "Update the app").
        /// ReadBackup only returns NotBackupText or the newer-version text, so anything else is the newer one.
        public static string ReadText(string text, out BackupData data)
        {
            string problem = History.ReadBackup(text, out data);
            if (problem == null || problem == History.NotBackupText()) return problem;
            return NewerText();
        }

        public static string NewerText()
        {
            return L.S("This backup was made by a newer version of PrintPrice. Reload the page to get the newest website, then try again.",
                       "Sikkerhedskopien er lavet af en nyere udgave af PrintPrice. Genindlæs siden for at få den nyeste hjemmeside, og prøv så igen.");
        }

        /// "Also restore the settings" starts ticked only when this browser has no history yet (BackupForm: Checked = list.Count == 0).
        public static bool SettingsTickedAtStart(int quotesHere) { return quotesHere == 0; }

        /// True for names the Restore tab takes (a dropped one of these opens Backup instead of being read as a sliced file).
        public static bool IsBackupName(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n.EndsWith(History.BackupExtension) || n.EndsWith(".json");
        }

        /// "Backup from 10-11-2026 21:40 (PrintPrice v1.13.0): 214 quotes, with settings." (BackupForm.Summary; App "web" = "(website)").
        public static string Summary(BackupData d)
        {
            DateTime t;
            string when = History.TryTime(d.CreatedAt, out t) ? t.ToString("dd-MM-yyyy HH':'mm", Inv) : "";
            string set = d.Settings != null ? L.S("with settings", "med indstillinger") : L.S("no settings", "uden indstillinger");
            if (when.Length == 0 && string.IsNullOrEmpty(d.App))
                return L.F("A price history file: {0}, {1}.", "En prishistorik-fil: {0}, {1}.", Quotes(d.Quotes.Count), set);
            string app = string.IsNullOrEmpty(d.App) ? "" : d.App == App ? L.S(" (website)", " (hjemmesiden)") : " (PrintPrice v" + d.App + ")";
            int pics = d.Pictures != null ? d.Pictures.Count : 0;
            if (pics > 0)
                set += pics == 1 ? L.S(", 1 picture", ", 1 billede") : L.F(", {0} pictures", ", {0} billeder", pics.ToString("N0", Num.Dk));
            return L.F("Backup from {0}{1}: {2}, {3}.", "Kopi fra {0}{1}: {2}, {3}.", when.Length > 0 ? when : "?", app, Quotes(d.Quotes.Count), set);
        }

        /// A desktop history.json (no backup header: no time, no app) rather than a .printprice-backup.
        public static bool IsPlainHistory(BackupData d) { return d != null && string.IsNullOrEmpty(d.CreatedAt) && string.IsNullOrEmpty(d.App); }

        /// The note under "Also restore the settings" when the file has none, by what the file really is.
        public static string NoSettingsText(BackupData d)
        {
            return IsPlainHistory(d) ? L.S("This file has no settings (a history.json).", "Filen har ingen indstillinger (en history.json).")
                                     : L.S("This backup was made without the settings.", "Kopien blev lavet uden indstillingerne.");
        }

        public static string MergeText(int here)
        {
            return here == 0 ? L.S("Merge: add the backup's quotes", "Flet: tilføj kopiens tilbud")
                             : L.F("Merge: add the backup's quotes to the {0} in this browser", "Flet: læg kopiens tilbud til de {0} i denne browser", here.ToString("N0", Num.Dk));
        }

        public static string ReplaceNote(int here)
        {
            return L.F("Replace takes the {0} in this browser out of the list. A safety copy of them is kept first.",
                       "Erstat fjerner de {0} i denne browser fra listen. En sikkerhedskopi af dem gemmes først.", Quotes(here));
        }

        // ---------- restore ----------

        /// What making a restore real gave: Done = the history was taken over (Problem may still say what failed after
        /// it); not Done = nothing changed, Problem says why. SafetyKept = a safety copy is there to download.
        public sealed class Applied
        {
            public bool Done, SafetyKept;
            public string Problem;
        }

        /// History.Combine (the desktop's merge / replace / settings rules). Never throws: null + problem.
        public static RestoreResult Combine(List<QuoteRecord> current, AppSettings s, BackupData data, bool replace, bool withSettings, out string problem)
        {
            return Combine(current, s, data, replace, withSettings, null, out problem);
        }

        /// Combine with pictures: picturesHere = record ids that have a picture in this browser (Merge keeps those;
        /// null = none here). RestoreResult.Pictures = what to write (HistoryStore.WritePictures).
        public static RestoreResult Combine(List<QuoteRecord> current, AppSettings s, BackupData data, bool replace, bool withSettings,
                                            HashSet<string> picturesHere, out string problem)
        {
            problem = null;
            Func<string, bool> has = null;
            if (picturesHere != null) has = id => picturesHere.Contains(id);
            try { return History.Combine(current, s, data, replace, withSettings && data.Settings != null, has); }
            catch (Exception ex)
            {
                problem = L.S("Backup not restored: ", "Sikkerhedskopien blev ikke gendannet: ") + ex.Message;
                return null;
            }
        }

        /// The done text (BackupForm.ShowDone): main line and the muted "Now N in the history.".
        public static string DoneText(RestoreResult r)
        {
            int here = r.Same + r.Updated;
            string main = L.F("✓ Restored. {0} added", "✓ Gendannet. {0} tilføjet", Quotes(r.Added));
            if (here > 0)
                main += r.Updated > 0
                    ? L.F(", {0} were already here ({1} of them got details from the backup)", ", {0} var her allerede ({1} af dem fik oplysninger fra kopien)",
                          here.ToString("N0", Num.Dk), r.Updated.ToString("N0", Num.Dk))
                    : L.F(", {0} were already here", ", {0} var her allerede", here.ToString("N0", Num.Dk));
            main += ".";
            if (r.SettingsRestored) main += L.S(" Settings restored.", " Indstillinger gendannet.");
            return main;
        }

        public static string NowText(RestoreResult r) { return L.F("Now {0} in the history.", "Nu {0} i historikken.", Quotes(r.Quotes.Count)); }

        public static string ClashText(RestoreResult r)
        {
            int nc = r.InvoiceClashes.Count;
            if (nc == 0) return null;
            return nc == 1
                ? L.F("Invoice code {0} is on two different quotes. Both are kept, because the customer may already have it. Search for it in the history to check them.",
                      "Fakturanummer {0} står på to forskellige tilbud. Begge beholdes, fordi kunden måske allerede har det. Søg efter det i historikken for at tjekke dem.",
                      r.InvoiceClashes[0])
                : L.F("Invoice codes {0} are each on two different quotes. All are kept, because the customers may already have them. Search for them in the history to check them.",
                      "Fakturanumrene {0} står hver på to forskellige tilbud. Alle beholdes, fordi kunderne måske allerede har dem. Søg efter dem i historikken for at tjekke dem.",
                      string.Join(", ", r.InvoiceClashes.ToArray()));
        }

        public static string MovedText(RestoreResult r)
        {
            if (r.PrintersMoved == 0) return null;
            return r.PrintersMoved == 1
                ? L.S("A printer in the backup had the same number as a different printer here. Its quotes are kept under its own name, shown as \"(removed)\", so the totals stay right.",
                      "En printer i kopien havde samme nummer som en anden printer her. Dens tilbud beholdes under dens eget navn, vist som \"(fjernet)\", så tallene passer.")
                : L.F("{0} printers in the backup had the same number as a different printer here. Their quotes are kept under their own names, shown as \"(removed)\", so the totals stay right.",
                      "{0} printere i kopien havde samme nummer som en anden printer her. Deres tilbud beholdes under deres egne navne, vist som \"(fjernet)\", så tallene passer.",
                      r.PrintersMoved.ToString(Num.Dk));
        }

        // ---------- safety copies (IndexedDB) ----------

        public static string SafetyKey(DateTime now) { return SafetyPrefix + now.ToString("yyyyMMdd-HHmmss", Inv); }

        /// Idea 22: the safety copy made before Clear all (its own newest SafetyKept are kept, apart from the restore ones).
        public const string ClearSafetyPrefix = "before-clear-";
        /// What both kinds of safety copy start with (HistoryStore.SaveSafety lists these keys to prune them).
        public const string AnySafetyPrefix = "before-";

        public static string ClearSafetyKey(DateTime now) { return ClearSafetyPrefix + now.ToString("yyyyMMdd-HHmmss", Inv); }

        /// "PrintPrice before-restore 2026-11-10 14.05.09.printprice-backup" (or "before-clear") for a safety copy's key
        /// (seconds: two in one minute differ).
        public static string SafetyFileName(string key)
        {
            string prefix = key != null && key.StartsWith(ClearSafetyPrefix, StringComparison.Ordinal) ? ClearSafetyPrefix : SafetyPrefix;
            DateTime t;
            string stamp = key != null && key.StartsWith(prefix, StringComparison.Ordinal)
                && DateTime.TryParseExact(key.Substring(prefix.Length), "yyyyMMdd-HHmmss", Inv, DateTimeStyles.None, out t)
                ? t.ToString("yyyy-MM-dd HH.mm.ss", Inv) : "copy";
            return "PrintPrice " + prefix.TrimEnd('-') + " " + stamp + History.BackupExtension;
        }

        /// The safety copies to delete so only the newest SafetyKept of each kind (restore / clear) stay (keys sort by time).
        public static List<string> OldSafetyKeys(IEnumerable<string> keys)
        {
            var res = new List<string>();
            foreach (string prefix in new[] { SafetyPrefix, ClearSafetyPrefix })
            {
                var mine = new List<string>();
                if (keys != null) foreach (var k in keys) if (k != null && k.StartsWith(prefix, StringComparison.Ordinal)) mine.Add(k);
                mine.Sort(StringComparer.Ordinal);
                if (mine.Count > SafetyKept) res.AddRange(mine.GetRange(0, mine.Count - SafetyKept));
            }
            return res;
        }

        // ---------- "Last backup" reminder ----------

        public static string LastBackupValue(DateTime now, bool online)
        {
            return now.ToString("yyyy-MM-dd HH':'mm", Inv) + (online ? "|online" : "|file");
        }

        /// "Last backup: never" / "today (file)" / "yesterday (online)" / "12 days ago (file)"; warn = never (with quotes) or
        /// older than RemindDays.
        public static string LastBackupText(string stored, DateTime now, int quotes, out bool warn)
        {
            warn = false;
            DateTime t;
            string s = stored ?? "";
            int bar = s.IndexOf('|');
            string when = bar < 0 ? s : s.Substring(0, bar), kind = bar < 0 ? "" : s.Substring(bar + 1);
            if (!DateTime.TryParseExact(when, "yyyy-MM-dd HH':'mm", Inv, DateTimeStyles.None, out t))
            {
                warn = quotes > 0;
                return L.S("Last backup: never", "Seneste sikkerhedskopi: aldrig");
            }
            int days = (int)(now.Date - t.Date).TotalDays;
            if (days < 0) days = 0;
            warn = days > RemindDays;
            string ago = days == 0 ? L.S("today", "i dag") : days == 1 ? L.S("yesterday", "i går") : L.F("{0} days ago", "for {0} dage siden", days);
            string how = kind == "online" ? L.S(" (online)", " (online)") : kind == "file" ? L.S(" (file)", " (fil)") : "";
            return L.S("Last backup: ", "Seneste sikkerhedskopi: ") + ago + how;
        }

        // ---------- online backup (src\OnlineBackup.cs is linked: its constants, limits and texts are used directly) ----------

        public const int TimeoutMs = 30000;

        /// The Worker's /b/<storage id> on the real site; anywhere else the local test server's fake (_t/b/<id>), so
        /// local runs never touch the live Worker. Throws ArgumentException on an invalid code.
        public static string Url(string host, string code)
        {
            string id = Backup.StorageId(code);
            return (string.Equals(host, LivePower.SiteHost, StringComparison.OrdinalIgnoreCase) ? OnlineBackup.Server + "/b/" : "_t/b/") + id;
        }

        /// One Worker answer (window.printPrice.backupFetch); Status 0 = no answer. Body = base64.
        public sealed class Reply
        {
            public int Status { get; set; }
            public string Body { get; set; }
            public string Left { get; set; }
            public string Reset { get; set; }
            public string RetryAfter { get; set; }
        }

        /// OnlineBackup.ErrorText, except 400: the desktop says "Update PrintPrice", on the web that means reloading the page.
        public static string ErrorText(int status, bool isGet)
        {
            if (status == 400)
                return L.S("The online backup service refused the backup. Reload the page and try again.",
                           "Online-kopi-tjenesten afviste kopien. Genindlæs siden og prøv igen.");
            return OnlineBackup.ErrorText(status, isGet);
        }

        /// A failed call's text: a 429 with X-Uploads-Reset is the daily cap (with the wait), anything else ErrorText.
        public static string FailureText(Reply r, bool isGet, DateTime nowUtc)
        {
            if (r.Status == 429 && r.Reset != null)
                return OnlineBackup.DailyLimitText(OnlineBackup.ParseLimitHeaders("0", r.Reset, r.RetryAfter, nowUtc).ResetUtc, nowUtc);
            return ErrorText(r.Status, isGet);
        }

        /// "✓ Backed up online: 8 quotes and your settings, kept until 15-12-2027." + the uploads left today.
        public static string UploadedText(int quotes, DateTime now, UploadResult info, DateTime nowUtc)
        {
            string main = L.F("✓ Backed up online: {0} and your settings, kept until {1}.", "✓ Kopi gemt online: {0} og dine indstillinger, gemmes til {1}.",
                              Quotes(quotes), now.AddDays(OnlineBackup.KeptDays).ToString("dd-MM-yyyy", Inv));
            if (info.UploadsLeft == 0)
                main += " " + L.F("That was today's last online backup ({0} a day). You can back up again in {1}.",
                                  "Det var dagens sidste online-kopi ({0} om dagen). Du kan tage en kopi igen om {1}.", OnlineBackup.DailyUploads,
                                  info.ResetUtc.HasValue ? OnlineBackup.WaitText(info.ResetUtc.Value - nowUtc) : L.S("a day", "et døgn"));
            else if (info.UploadsLeft == 1) main += " " + L.S("1 online backup left today.", "1 online-kopi tilbage i dag.");
            else if (info.UploadsLeft == 2) main += " " + L.S("2 online backups left today.", "2 online-kopier tilbage i dag.");
            return main;
        }

        /// The code box's state: null = fine (a valid code), else the hint (empty = type it; typo = check it).
        public static string CodeProblem(string typed)
        {
            string c = Backup.Clean(typed ?? "");
            if (c != null && c.Length == 0) return L.S("Type the 20-character code.", "Skriv koden på 20 tegn.");
            if (c == null || (c.Length >= Backup.CodeLength && Backup.Normalize(typed) == null))
                return L.S("This code has a typo: check it letter by letter.", "Koden har en tastefejl: tjek den tegn for tegn.");
            if (c.Length < Backup.CodeLength) return L.F("{0} of 20 characters.", "{0} af 20 tegn.", c.Length);
            return null;
        }
    }
}
