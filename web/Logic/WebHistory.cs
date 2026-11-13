using System;
using System.Collections.Generic;
using System.Globalization;

namespace PrintPrice.Web
{
    /// The website's price history, without browser calls (web\tests runs it): stored text, the list's texts, filters
    /// and month cards (HistoryForm's rules), saving a quote (MainForm.SaveToHistory) and Open again / Repeat print values
    /// (MainForm.FillFromRecord). The records are the desktop's QuoteRecord and history.json text (History.ToJson / ParseJson).
    public static class WebHistory
    {
        public const string HistoryKey = "history";      // IndexedDB key: the history.json text
        public const string PicturePrefix = "pic:";      // + record Id: the small model picture (data URL; in backups as BackupFile Pictures)
        public const string BadPrefix = "history.bad-";  // + time: an unreadable history kept as it was
        public const int PictureSize = History.PictureSize;            // px, longest side of a kept picture (= the desktop)
        public const int SmallPictureSize = History.SmallPictureSize;  // second try when PictureSize is too big
        /// Most base64 characters after the comma of a kept picture's data URL (= History.MaxPictureBytes).
        public const int MaxPictureChars = (History.MaxPictureBytes + 2) / 3 * 4;
        public const int ShowStep = 200;                 // rows shown at once ("Show more" adds as many again)
        public const int MaxNote = 200;                  // as HistoryForm's Note column

        public const int ShowAll = History.ShowAll, ShowFriends = History.ShowFriends, ShowCustomers = History.ShowCustomers,
                         ShowFailed = History.ShowFailed, ShowNotPrinted = History.ShowNotPrinted;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");

        // ---------- stored text ----------

        /// The stored history.json text -> the quotes (never null). Nothing stored = a new, empty history. Unreadable text
        /// or a newer version: empty list, problem = the text for the page, badKey = where to keep the old text (as the
        /// desktop renames the file to history.bad-*.json); else badKey = null.
        public static List<QuoteRecord> ReadStored(string text, DateTime now, out string problem, out string badKey)
        {
            problem = null;
            badKey = null;
            if (string.IsNullOrEmpty(text)) return new List<QuoteRecord>();
            var list = History.ParseJson(text);
            if (list != null) return list;
            badKey = BadPrefix + now.ToString("yyyyMMdd-HHmmss", Inv);
            problem = L.F("The saved price history couldn't be read, so it was kept aside ({0}) and a new one started.",
                          "Den gemte prishistorik kunne ikke læses, så den blev lagt til side ({0}), og en ny blev startet.", badKey);
            return new List<QuoteRecord>();
        }

        public static string PictureKey(string id) { return PicturePrefix + (id ?? ""); }

        /// Picture keys whose record is gone (deleted, or dropped by History.Trim).
        public static List<string> OrphanPictures(IEnumerable<string> keys, List<QuoteRecord> list)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in list) ids.Add(r.Id ?? "");
            var res = new List<string>();
            if (keys == null) return res;
            foreach (var k in keys)
                if (k != null && k.StartsWith(PicturePrefix, StringComparison.Ordinal) && !ids.Contains(k.Substring(PicturePrefix.Length)))
                    res.Add(k);
            return res;
        }

        // ---------- pictures in backups (issue #4) ----------

        /// The record Id of a "pic:<Id>" key, or null for other keys.
        public static string PictureId(string key)
        {
            return key != null && key.StartsWith(PicturePrefix, StringComparison.Ordinal) && key.Length > PicturePrefix.Length
                ? key.Substring(PicturePrefix.Length) : null;
        }

        /// The record ids that have a picture, from IndexedDB's "pic:" keys.
        public static HashSet<string> PictureIds(IEnumerable<string> keys)
        {
            var res = new HashSet<string>(StringComparer.Ordinal);
            if (keys != null) foreach (var k in keys) { string id = PictureId(k); if (id != null) res.Add(id); }
            return res;
        }

        /// A kept picture that should be made again before it goes into a backup: WebP (the desktop can't show it; made
        /// by the website before v1.17) or anything History.PictureFromDataUrl refuses (other type, too big).
        public static bool NeedsConvert(string dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl)) return false;
            if (dataUrl.StartsWith("data:image/webp", StringComparison.OrdinalIgnoreCase)) return true;
            return History.PictureFromDataUrl(dataUrl) == null;
        }

        /// The ids of list's records that may have a picture, in History.PicturesForBackup's order (newest SavedAt first,
        /// unreadable times last, list order on ties), each once; only ids in have (null = all).
        public static List<string> NewestPictureIds(List<QuoteRecord> list, HashSet<string> have)
        {
            var order = new List<QuoteRecord>();
            if (list != null) foreach (var r in list) if (r != null && History.SafePictureId(r.Id) && (have == null || have.Contains(r.Id))) order.Add(r);
            var pos = new Dictionary<QuoteRecord, int>();
            for (int i = 0; i < order.Count; i++) pos[order[i]] = i;
            order.Sort(delegate (QuoteRecord a, QuoteRecord b)
            {
                DateTime ta, tb;
                if (!History.TryTime(a.SavedAt, out ta)) ta = DateTime.MinValue;
                if (!History.TryTime(b.SavedAt, out tb)) tb = DateTime.MinValue;
                int c = tb.CompareTo(ta);
                return c != 0 ? c : pos[a].CompareTo(pos[b]);
            });
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var res = new List<string>();
            foreach (var r in order) if (seen.Add(r.Id)) res.Add(r.Id);
            return res;
        }

        /// The backup's pictures from the loaded data URLs (id -> data URL): History.PicturesForBackup within budget.
        public static List<BackupPicture> BackupPictures(List<QuoteRecord> list, Dictionary<string, string> urls, long budget)
        {
            return History.PicturesForBackup(list, id =>
            {
                string u;
                return urls != null && urls.TryGetValue(id, out u) ? History.PictureFromDataUrl(u) : null;
            }, budget);
        }

        /// A restore's pictures as IndexedDB writes ("pic:<Id>" -> data URL); invalid ones are left out.
        public static List<KeyValuePair<string, string>> PictureWrites(RestoreResult r)
        {
            var res = new List<KeyValuePair<string, string>>();
            if (r == null || r.Pictures == null) return res;
            foreach (var p in r.Pictures)
            {
                if (!History.SafePictureId(p.Key)) continue;
                string url = History.PictureDataUrl(p.Value);
                if (url != null) res.Add(new KeyValuePair<string, string>(PictureKey(p.Key), url));
            }
            return res;
        }

        public static string StorageProblem(string error)
        {
            return L.S("This browser won't let the site keep a price history (a private window, or storage turned off), so saved quotes last only until the page is closed.",
                       "Denne browser lader ikke siden gemme en prishistorik (et privat vindue, eller lager slået fra), så gemte tilbud holder kun, til siden lukkes.")
                   + (string.IsNullOrEmpty(error) ? "" : " (" + error + ")");
        }

        public static string SaveProblem(string error)
        {
            return L.S("The price history couldn't be saved in this browser", "Prishistorikken kunne ikke gemmes i denne browser")
                   + (string.IsNullOrEmpty(error) ? "." : ": " + (error == "QuotaExceededError"
                        ? L.S("the browser's storage is full.", "browserens lager er fuldt.") : error + "."));
        }

        // ---------- the list's texts (HistoryForm) ----------

        /// The model's name, else the file name of the link/file, else "(no name)".
        public static string ModelText(QuoteRecord r) { return History.ModelText(r); }

        /// "30-10-2026 10:05".
        public static string DateText(QuoteRecord r)
        {
            DateTime t;
            return History.TryTime(r.SavedAt, out t) ? t.ToString(DateTimeFormat, Inv) : r.SavedAt;
        }

        // ':' kept literal: .NET's ICU da-DK writes "10.05" for HH:mm, the desktop (.NET Framework) "10:05"
        const string DateTimeFormat = "dd-MM-yyyy HH':'mm";

        /// Phone cards: "30 Oct 10:05" / "30. okt. 10:05".
        public static string ShortDate(QuoteRecord r)
        {
            DateTime t;
            if (!History.TryTime(r.SavedAt, out t)) return r.SavedAt;
            return L.Danish ? t.ToString("d. MMM HH':'mm", Num.Dk) : t.ToString("d MMM HH':'mm", En);
        }

        public static string TimeText(QuoteRecord r) { return r.Hours + L.S(" h ", " t ") + r.Minutes + " m"; }

        public static string GramsText(QuoteRecord r) { return Num.Fmt(r.Grams); }

        public static string PriceText(QuoteRecord r) { return Amount(History.PriceFor(r)); }

        /// A price without " kr." ("72,72"): Num.Kr's text, so every price on the site is rounded by the one function.
        public static string Amount(double v)
        {
            string kr = Num.Kr(v);
            return kr.EndsWith(" kr.", StringComparison.Ordinal) ? kr.Substring(0, kr.Length - 4) : kr;
        }

        public static bool IsFriends(QuoteRecord r) { return r.For == History.ForFriends; }

        public static string ForText(int forWho)
        {
            return forWho == History.ForFriends ? L.S("Friends", "Venner") : L.S("Customer", "Kunde");
        }

        /// "October 2026" / "Oktober 2026".
        public static string MonthText(DateTime m)
        {
            string s = L.Danish ? m.ToString("MMMM yyyy", Num.Dk) : m.ToString("MMMM yyyy", En);
            return s.Length > 0 ? char.ToUpper(s[0], Num.Dk) + s.Substring(1) : s;
        }

        public static string CountText(int shown, int total)
        {
            return shown == total ? L.F(total == 1 ? "{0} quote" : "{0} quotes", "{0} tilbud", total)
                                  : L.F("{0} of {1} quotes", "{0} af {1} tilbud", shown, total);
        }

        // ---------- several printers ----------

        /// Printer names by id (Settings' printers, then removed ones from the records: History.PrinterTotals, all time).
        /// printerMode = 2+ printers: the Printer column, filter and Profit tooltip show.
        public static Dictionary<string, string> PrinterNames(List<QuoteRecord> list, AppSettings s, out List<PrinterTotals> all)
        {
            all = History.PrinterTotals(list, s, 0, 0);
            var names = new Dictionary<string, string>();
            foreach (var t in all) names[t.Id] = AppSettings.CleanPrinterName(t.Name);
            return names;
        }

        public static string PrinterText(QuoteRecord r, Dictionary<string, string> names)
        {
            string name;
            if (names != null && names.TryGetValue(History.PrinterOf(r), out name)) return name;
            return AppSettings.CleanPrinterName(r.PrinterName);
        }

        public static string PrinterChoiceText(PrinterTotals t)
        {
            return AppSettings.CleanPrinterName(t.Name) + (t.Removed ? L.S(" (removed)", " (fjernet)") : "");
        }

        // ---------- filter (HistoryForm.Matches) ----------

        public sealed class Filter
        {
            public DateTime Month = DateTime.MinValue;   // 1st of the month; MinValue = all months
            public int Show;                             // ShowAll ... ShowNotPrinted
            public string PrinterId;                     // null = all printers
            public string Query = "";
        }

        /// printerNames: null with one printer (the printer's name isn't searched then).
        public static bool Matches(QuoteRecord r, Filter f, Dictionary<string, string> printerNames)
        {
            return History.Matches(r, f.Month, f.PrinterId, f.Show, f.Query,
                                   printerNames != null ? (Func<QuoteRecord, string>)(x => PrinterText(x, printerNames)) : null);
        }

        public static List<QuoteRecord> Apply(List<QuoteRecord> list, Filter f, Dictionary<string, string> printerNames)
        {
            return list.FindAll(r => Matches(r, f, printerNames));
        }

        /// The newest month with quotes (HistoryForm opens on it), else all months.
        public static DateTime StartMonth(List<QuoteRecord> list)
        {
            var months = History.Months(list);
            return months.Count > 0 ? months[0] : DateTime.MinValue;
        }

        // ---------- month cards (HistoryForm.UpdateCards) ----------

        public sealed class Card
        {
            public string Caption = "", Value = "", Sub = "";
            public string ShortCaption = "", ShortValue = "", ShortSub = "";   // phone cards
            public string Css = "";                                            // "red", "green", "error" or ""
            public string Tip;                                                 // Profit by printer (null = none)
        }

        /// The month's totals (all months = the sum of every month; one printer = MonthSummary with its id).
        public static MonthTotals Totals(List<QuoteRecord> list, DateTime month, string printerId)
        {
            if (month != DateTime.MinValue) return History.MonthSummary(list, month.Year, month.Month, printerId);
            if (printerId != null) return History.MonthSummary(list, 0, 0, printerId);
            return History.AllMonthsTotals(list);
        }

        /// The six cards: Prints, Print time, Customers, Friends & family, Costs, Profit (texts as HistoryForm).
        public static List<Card> Cards(MonthTotals t)
        {
            Func<int, string> prints = n => n == 1 ? L.S("1 print", "1 print") : L.F("{0} prints", "{0} print", n);
            Func<double, string> plain = Amount;
            int minutes = (int)Math.Round(t.PrintHours * 60);
            string time = L.F("{0} h {1:00} m", "{0} t {1:00} m", minutes / 60, minutes % 60);
            string grams = t.Grams >= 1000 ? (t.Grams / 1000).ToString("0.00", Num.Dk) + " kg" : Math.Round(t.Grams).ToString("0", Num.Dk) + " g";
            bool noIncome = Math.Abs(t.Income) < 1e-9;
            string pct = Math.Round(t.ProfitPct).ToString("0", Num.Dk) + " %";
            return new List<Card>
            {
                new Card { Caption = L.S("Prints", "Print"), Value = t.Prints.ToString(Num.Dk),
                           Sub = t.FailedPrints > 0 ? L.F("+ {0} failed", t.FailedPrints == 1 ? "+ {0} fejlet" : "+ {0} fejlede", t.FailedPrints) : L.S("no failed prints", "ingen fejlprint"),
                           ShortSub = t.FailedPrints > 0 ? L.F("+ {0} failed", t.FailedPrints == 1 ? "+ {0} fejlet" : "+ {0} fejlede", t.FailedPrints) : L.S("none failed", "ingen fejl") },
                new Card { Caption = L.S("Print time", "Printtid"), Value = time, Sub = grams + L.S(" filament", " filament"), ShortSub = grams },
                new Card { Caption = L.S("Customers", "Kunder"), Value = Num.Kr(t.CustomerIncome), ShortValue = plain(t.CustomerIncome),
                           Sub = prints(t.CustomerCount), Css = "red" },
                new Card { Caption = L.S("Friends & family", "Venner og familie"), ShortCaption = L.S("Friends", "Venner"),
                           Value = Num.Kr(t.FriendsIncome), ShortValue = plain(t.FriendsIncome), Sub = prints(t.FriendsCount), Css = "green" },
                new Card { Caption = L.S("Costs", "Omkostninger"), Value = Num.Kr(t.Costs + t.Lost), ShortValue = plain(t.Costs + t.Lost),
                           Sub = t.Lost > 0 ? L.F("incl. {0} failed", "inkl. {0} fejlprint", Num.Kr(t.Lost)) : L.S("material, power, wear", "materiale, strøm, slid"),
                           ShortSub = t.Lost > 0 ? L.F("{0} failed", "{0} fejlprint", plain(t.Lost)) : L.S("no failed", "ingen fejl") },
                new Card { Caption = L.S("Profit", "Overskud"), Value = Num.Kr(t.Profit), ShortValue = plain(t.Profit),
                           Sub = noIncome ? L.S("no income yet", "ingen indtægt endnu") : L.F("{0} of income", "{0} af indtægten", pct),
                           ShortSub = noIncome ? "–" : pct, Css = t.Profit < 0 ? "error" : "green" }
            };
        }

        /// The Profit card's tooltip with 2+ printers and All printers: each printer's profit for the month (all months =
        /// all time); null otherwise.
        public static string ProfitTip(List<QuoteRecord> list, AppSettings s, DateTime month, bool printerMode, string printerId)
        {
            if (!printerMode || printerId != null) return null;
            var lines = new List<string> { L.S("Profit by printer", "Overskud pr. printer") };
            foreach (var t in History.PrinterTotals(list, s, month == DateTime.MinValue ? 0 : month.Year, month.Month))
                lines.Add(PrinterChoiceText(t) + ":  " + Num.Kr(t.Profit));
            return string.Join("\n", lines);
        }

        // ---------- saving the quote on screen (MainForm.SaveToHistory) ----------

        /// The record for the quote on screen. materials = the colour lines' Colour / Type / Name (Name = the filament
        /// profile) in the same order as input's lines (null = leave them empty); purge = the file's purge grams (2+ colours).
        /// reopened = a quote opened again keeps its For; repeatOf = a repeat print: same For, RepeatOf = its Id.
        public static QuoteRecord MakeRecord(QuoteInput input, QuoteResult result, AppSettings s, string model, DateTime now,
                                             IList<HistoryFilament> materials, double purge, QuoteRecord reopened, QuoteRecord repeatOf)
        {
            var r = History.FromQuote(input, result, s, model ?? "", "", now);
            if (r.Filaments != null && materials != null && r.Filaments.Count == materials.Count)
            {
                for (int k = 0; k < materials.Count; k++)
                {
                    var m = materials[k];
                    if (m == null) continue;
                    r.Filaments[k].Colour = m.Colour ?? "";
                    r.Filaments[k].Type = m.Type ?? "";
                    r.Filaments[k].Name = BambuSlicer.FilamentBaseName(m.Name ?? ""); // no "@BBL A1" (old records kept as they are)
                }
                if (materials.Count >= 2) r.PurgeGrams = purge;
            }
            if (reopened != null) r.For = reopened.For;
            if (repeatOf != null) { r.For = repeatOf.For; r.RepeatOf = repeatOf.Id ?? ""; }
            return r;
        }

        /// Adds it (the same quote again within 10 minutes replaces the newest one and keeps its name). Returns the record
        /// now in the list (its Id leaves the quote out of the payback debt from now on).
        public static QuoteRecord Save(List<QuoteRecord> list, QuoteRecord r)
        {
            if (list.Count > 0 && History.SameQuote(list[0], r)) r.Customer = list[0].Customer ?? "";
            return History.Add(list, r);
        }

        /// Deletes a record; true when it was there.
        public static bool Delete(List<QuoteRecord> list, string id) { return History.Remove(list, id); }

        // ---------- delete several / Clear all / Undo / views (idea 22, issue #5) ----------

        public const int ViewList = AppSettings.HistoryViewList, ViewMedium = AppSettings.HistoryViewMedium, ViewLarge = AppSettings.HistoryViewLarge;

        /// Settings' HistoryView as a known view (anything else = the list).
        public static int View(AppSettings s)
        {
            int v = s == null ? ViewList : (int)s.HistoryView;
            return v == ViewMedium || v == ViewLarge ? v : ViewList;
        }

        /// Takes these records out of the list (History.RemoveMany by Id; old records without an Id by reference) and
        /// returns them with their places, for History.PutBack (Undo). Never null.
        public static RemovedRecords RemoveRecords(List<QuoteRecord> list, IEnumerable<QuoteRecord> records)
        {
            var ids = new List<string>();
            var loose = new HashSet<QuoteRecord>();
            if (list == null || records == null) return new RemovedRecords();
            foreach (var r in records)
            {
                if (r == null) continue;
                if (!string.IsNullOrEmpty(r.Id)) ids.Add(r.Id); else loose.Add(r);
            }
            var looseAt = new List<KeyValuePair<int, QuoteRecord>>();
            if (loose.Count > 0)
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && loose.Contains(list[i])) looseAt.Add(new KeyValuePair<int, QuoteRecord>(i, list[i]));
            var res = History.RemoveMany(list, ids);
            if (looseAt.Count == 0) return res;
            foreach (var p in looseAt) list.Remove(p.Value);
            // one list of (old index, record), lowest index first
            var all = new List<KeyValuePair<int, QuoteRecord>>(looseAt);
            for (int k = 0; k < res.Records.Count; k++) all.Add(new KeyValuePair<int, QuoteRecord>(res.Indexes[k], res.Records[k]));
            all.Sort((a, b) => a.Key.CompareTo(b.Key));
            var merged = new RemovedRecords();
            foreach (var p in all) { merged.Records.Add(p.Value); merged.Indexes.Add(p.Key); }
            return merged;
        }

        /// The ticked records that are still among rows (the filter's list): a record from a reloaded list (another tab
        /// saved) keeps its tick by Id. A filter change drops ticks of rows no longer shown, so a delete only takes
        /// what the user can see.
        public static HashSet<QuoteRecord> KeepTicks(ICollection<QuoteRecord> ticked, List<QuoteRecord> rows)
        {
            var res = new HashSet<QuoteRecord>();
            if (ticked == null || ticked.Count == 0 || rows == null) return res;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in ticked) if (t != null && !string.IsNullOrEmpty(t.Id)) ids.Add(t.Id);
            foreach (var r in rows)
                if (r != null && (ticked.Contains(r) || (!string.IsNullOrEmpty(r.Id) && ids.Contains(r.Id)))) res.Add(r);
            return res;
        }

        /// The picture keys to delete once an Undo is no longer possible: removed records not back in the list.
        public static List<string> DroppedPictureKeys(List<QuoteRecord> list, RemovedRecords removed)
        {
            var res = new List<string>();
            if (removed == null) return res;
            var have = new HashSet<string>(StringComparer.Ordinal);
            if (list != null) foreach (var r in list) if (r != null && !string.IsNullOrEmpty(r.Id)) have.Add(r.Id);
            foreach (var id in removed.Ids()) if (History.SafePictureId(id) && !have.Contains(id)) res.Add(PictureKey(id));
            return res;
        }

        /// The total list price of some records (History.PriceFor each).
        public static double Total(IEnumerable<QuoteRecord> records)
        {
            double sum = 0;
            if (records != null) foreach (var r in records) if (r != null) sum += History.PriceFor(r);
            return sum;
        }

        /// "3 ticked" / "3 markeret".
        public static string TickedText(int n) { return L.F("{0} ticked", n == 1 ? "{0} markeret" : "{0} markerede", n); }

        /// "Delete 3 ticked…" / "Slet 3 markerede…", "Slet 1 markeret…".
        public static string DeleteTickedText(int n) { return L.F("Delete {0} ticked…", n == 1 ? "Slet {0} markeret…" : "Slet {0} markerede…", n); }

        /// "Benchy", "Benchy and Plant pot", "Benchy, Plant pot and Dragon", "Benchy, Plant pot, Dragon and 5 more".
        public static string NamesText(IList<QuoteRecord> records)
        {
            var names = new List<string>();
            if (records != null) foreach (var r in records) if (r != null) names.Add(History.ModelText(r));
            if (names.Count == 0) return "";
            string and = L.S(" and ", " og ");
            if (names.Count == 1) return names[0];
            if (names.Count <= 3) return string.Join(", ", names.GetRange(0, names.Count - 1)) + and + names[names.Count - 1];
            return string.Join(", ", names.GetRange(0, 3)) + and + L.F("{0} more", "{0} mere", names.Count - 3);
        }

        /// The confirm dialog's title: "Delete 3 quotes?" / "Clear all 48 quotes?".
        public static string ConfirmTitle(int n, bool clear)
        {
            if (clear) return n == 1 ? L.S("Clear all: 1 quote?", "Ryd alt: 1 tilbud?") : L.F("Clear all {0} quotes?", "Ryd alle {0} tilbud?", n);
            return n == 1 ? L.S("Delete 1 quote?", "Slet 1 tilbud?") : L.F("Delete {0} quotes?", "Slet {0} tilbud?", n);
        }

        /// The confirm button: "Delete 3" / "Clear all 48".
        public static string ConfirmButton(int n, bool clear)
        {
            return clear ? L.F("Clear all {0}", "Ryd alle {0}", n) : L.F("Delete {0}", "Slet {0}", n);
        }

        /// The dialog's lines (plain, then the warning ones) from History.SummarizeDelete / SummarizeClear.
        public static List<string> ConfirmLines(DeleteSummary s, IList<QuoteRecord> records, bool clear, out List<string> warnings)
        {
            var lines = new List<string>();
            warnings = new List<string>();
            if (s == null) return lines;
            if (clear)
                lines.Add(L.S("Every saved quote and picture in this browser is deleted, not only the ones the filter shows. Settings stay.",
                              "Alle gemte tilbud og billeder i denne browser slettes, ikke kun dem filteret viser. Indstillingerne bliver."));
            else
                lines.Add(L.F("{0}, with their pictures.", "{0} med deres billeder.", NamesText(records)));
            if (s.Invoices > 0)
                lines.Add(s.Invoices == 1 ? L.S("1 has an invoice; its number is never used again.", "1 har en faktura; dens nummer bruges aldrig igen.")
                                          : L.F("{0} have an invoice; their numbers are never used again.", "{0} har en faktura; deres numre bruges aldrig igen.", s.Invoices));
            if (s.FailedOpen > 0)
                warnings.Add(s.FailedOpen == 1
                    ? L.F("1 failed print is not paid back yet ({0}): deleting it drops that debt.", "1 fejlet print er ikke betalt tilbage endnu ({0}): sletter du det, forsvinder den gæld.", Num.Kr(s.FailedOpenAmount))
                    : L.F("{0} failed prints are not paid back yet ({1}): deleting them drops that debt.", "{0} fejlede print er ikke betalt tilbage endnu ({1}): sletter du dem, forsvinder den gæld.", s.FailedOpen, Num.Kr(s.FailedOpenAmount)));
            if (!clear && s.DebtAfter > s.DebtBefore + 0.005)
                warnings.Add(L.F("Some of these quotes paid back failed prints: the failed-print debt goes up from {0} to {1}.",
                                 "Nogle af disse tilbud betalte fejlede print tilbage: gælden for fejlede print stiger fra {0} til {1}.", Num.Kr(s.DebtBefore), Num.Kr(s.DebtAfter)));
            return lines;
        }

        /// The done bar after a delete / Clear all.
        public static string DeletedText(int n, bool clear)
        {
            if (clear) return n == 1 ? L.S("1 quote cleared.", "1 tilbud ryddet.") : L.F("{0} quotes cleared.", "{0} tilbud ryddet.", n);
            return n == 1 ? L.S("1 quote deleted.", "1 tilbud slettet.") : L.F("{0} quotes deleted.", "{0} tilbud slettet.", n);
        }

        /// The done bar after Undo.
        public static string UndoneText(int n)
        {
            return n == 1 ? L.S("1 quote is back.", "1 tilbud er tilbage.") : L.F("{0} quotes are back.", "{0} tilbud er tilbage.", n);
        }

        // ---------- Open again / Repeat print (MainForm.FillFromRecord) ----------

        /// A colour row to show again: the material (Profile = saved name) and its kr/kg box text.
        public sealed class FillRow
        {
            public FilamentUse Use;
            public string Name = "", PerKg = "";
        }

        /// The boxes from a saved record. Open again (repeat false): the saved kr/kg and electricity price. Repeat print:
        /// today's kr/kg per material (settings.PriceFor; no material = the last kr/kg) and Elec = null (today's stays).
        public sealed class Fill
        {
            public string Hours = "", Minutes = "", Grams = "", PerKg = "";
            public string Elec;                     // null = keep today's box
            public List<FillRow> Rows = new List<FillRow>();   // 2+ colours
            public FilamentUse Single;              // one colour with a saved material (null: none / old record)
            public string MaterialName;             // one colour: its material's name (kr/kg remembered under it); null = none
            public double PurgeGrams = -1;
        }

        public static Fill FillFrom(QuoteRecord r, bool repeat, AppSettings s)
        {
            var f = new Fill
            {
                Hours = r.Hours.ToString(Inv), Minutes = r.Minutes.ToString(Inv), Grams = Num.Fmt(r.Grams),
                Elec = repeat ? null : r.ElectricityDkkPerKwh.ToString("0.00##", Num.Dk)
            };
            var one = History.SingleLine(r);
            if (r.Filaments != null && r.Filaments.Count >= 2)
            {
                f.PurgeGrams = r.PurgeGrams;
                for (int k = 0; k < r.Filaments.Count; k++)
                {
                    var h = r.Filaments[k];
                    var use = new FilamentUse { Slot = k + 1, Colour = h.Colour ?? "", Type = h.Type ?? "", Profile = h.Name ?? "", Grams = h.Grams, ModelGrams = -1 };
                    string name = MaterialName(use);
                    f.Rows.Add(new FillRow { Use = use, Name = name, PerKg = Num.Fmt(repeat ? s.PriceFor(name) : h.PricePerKg) });
                }
                return f;
            }
            if (one != null && ((one.Name ?? "").Length > 0 || (one.Type ?? "").Length > 0 || (one.Colour ?? "").Length > 0))
            {
                f.Single = new FilamentUse { Slot = 1, Colour = one.Colour ?? "", Type = one.Type ?? "", Profile = one.Name ?? "",
                                             Grams = Math.Max(one.Grams, 0.01), ModelGrams = -1 };
                if ((one.Name ?? "").Length > 0) f.MaterialName = MaterialName(f.Single);
            }
            double perKg = one != null ? one.PricePerKg : r.FilamentDkkPerKg;
            if (repeat) perKg = f.MaterialName != null ? s.PriceFor(f.MaterialName) : s.LastFilamentPrice;
            f.PerKg = Num.Fmt(perKg);
            return f;
        }

        /// A repeat print's dropped file keeps the saved material per colour (name and type) when the colour count is the
        /// same, so the kr/kg is today's price of the same material (MainForm.KeepSavedMaterials).
        public static void KeepSavedMaterials(SliceResult res, QuoteRecord r)
        {
            if (res == null || r == null || r.Filaments == null || res.Filaments.Count != r.Filaments.Count) return;
            for (int k = 0; k < res.Filaments.Count; k++)
            {
                var saved = r.Filaments[k];
                if (!string.IsNullOrEmpty(saved.Name)) res.Filaments[k].Profile = saved.Name;
                if (!string.IsNullOrEmpty(saved.Type)) res.Filaments[k].Type = saved.Type;
            }
        }

        /// The one-colour line's Colour / Type / Name for saving: the file's colour and profile (no profile: the material
        /// name the page used, e.g. Settings' filament), type from the file, else the page's filament type.
        public static HistoryFilament SingleMaterial(FilamentUse single, string materialName, string filamentType)
        {
            var h = new HistoryFilament();
            if (single != null)
            {
                h.Colour = single.Colour ?? "";
                h.Type = single.Type ?? "";
                h.Name = single.Profile ?? "";
            }
            if (h.Name.Length == 0 && h.Type.Length == 0 && !string.IsNullOrEmpty(materialName)) h.Name = materialName;
            if (h.Type.Length == 0) h.Type = filamentType ?? "";
            return h;
        }

        /// The one-colour line of the job on screen: a job typed in by hand (no file, no reopened material) is saved with
        /// Settings' filament profile as its name, as the desktop does (MainForm.SetColours: Profile = FilamentPreset), so
        /// the quote shows the same material when reopened.
        public static HistoryFilament JobMaterial(FilamentUse single, string materialName, AppSettings s, string filamentType)
        {
            return SingleMaterial(single, string.IsNullOrEmpty(materialName) ? s.FilamentPreset : materialName, filamentType);
        }

        /// A colour's material name: the profile's base name, else its type, else "Colour N" (Calculator's rows).
        public static string MaterialName(FilamentUse f)
        {
            if (!string.IsNullOrEmpty(f.Profile)) return BambuSlicer.FilamentBaseName(f.Profile);
            if (!string.IsNullOrEmpty(f.Type)) return f.Type;
            return L.F("Colour {0}", "Farve {0}", f.Slot);
        }

        /// "\"model name\"", else the file name, else "a quote".
        public static string RecordName(QuoteRecord r)
        {
            if (!string.IsNullOrEmpty(r.Model)) return "\"" + r.Model + "\"";
            string s = (r.Source ?? "").Trim().Trim('"');
            if (s.Length > 0) return ModelText(r);
            return L.S("a quote", "et tilbud");
        }

        /// Open again: "↻ Opened from history: …, saved …." + muted "Saved price a / b kr. — now c / d kr. (today's settings)".
        public static void ReopenLines(QuoteRecord r, QuoteResult now, out string main, out string muted)
        {
            DateTime t;
            string when = History.TryTime(r.SavedAt, out t) ? t.ToString(DateTimeFormat, Inv) : r.SavedAt;
            main = L.F("↻ Opened from history: {0}, saved {1}.", "↻ Åbnet fra historikken: {0}, gemt {1}.", RecordName(r), when);
            Func<double, string> n = Amount;
            string nowText = now != null ? n(now.FriendsPrice) + " / " + n(now.OthersPrice) + " kr." : "–";
            muted = L.F("Saved price {0} / {1} kr. — now {2} (today's settings)", "Gemt pris {0} / {1} kr. — nu {2} (dagens indstillinger)",
                        n(r.FriendsPrice), n(r.OthersPrice), nowText);
        }

        /// Repeat print: "↻ Repeat print of … (first quoted …) with today's prices." + muted "Was a → now b (+c), … price".
        public static void RepeatLines(QuoteRecord r, QuoteResult now, out string main, out string muted)
        {
            DateTime t;
            string when = History.TryTime(r.SavedAt, out t) ? t.ToString("dd-MM-yyyy", Num.Dk) : r.SavedAt;
            main = L.F("↻ Repeat print of {0} (first quoted {1}) with today's prices.", "↻ Print igen af {0} (første tilbud {1}) med dagens priser.",
                       RecordName(r), when);
            bool friends = r.For == History.ForFriends;
            double was = History.PriceFor(r);
            if (now == null) { muted = L.F("Was {0}", "Før {0}", Num.Kr(was)); return; }
            double price = friends ? now.FriendsPrice : now.OthersPrice;
            double diff = Math.Round(price - was, 2);
            string change = diff > 0 ? "+" + Num.Kr(diff) : diff < 0 ? "−" + Num.Kr(-diff) : L.S("same price", "samme pris");
            muted = L.F("Was {0} → now {1} ({2}), {3} price", "Før {0} → nu {1} ({2}), {3}", Num.Kr(was), Num.Kr(price), change,
                        friends ? L.S("friends & family", "venne- og familiepris") : L.S("customer", "kundepris"));
        }

        /// The web can't slice: a repeat keeps the saved time and grams until the sliced file is dropped again.
        public static string RepeatHint
        {
            get { return L.S("Time and grams are as saved: drop the sliced file again to update them.",
                             "Tid og gram er som gemt: slip den slicede fil igen for at opdatere dem."); }
        }

        /// The electricity line after Open again / Repeat print (MainForm.FillFromRecord).
        public static string ElecLine(QuoteRecord r, bool repeat)
        {
            string saved = r.ElectricityDkkPerKwh.ToString("0.00", Num.Dk);
            return repeat
                ? L.F("Today's electricity price; the saved quote used {0} kr/kWh. Click Update for the live price.",
                      "Dagens elpris; det gemte tilbud brugte {0} kr/kWh. Klik Opdatér for live-prisen.", saved)
                : L.F("Electricity price {0} kr/kWh from the saved quote. Click Update for today's.",
                      "Elpris {0} kr/kWh fra det gemte tilbud. Klik Opdatér for dagens.", saved);
        }

        /// The payback row under Base cost (MainForm.UpdatePaybackRow): caption and amount; null caption = no row.
        public static string PaybackCaption(QuoteResult r)
        {
            if (r == null || r.PaybackCost <= 0) return null;
            return r.PaybackBothPrices ? L.S("Failed prints payback", "Fejlprint tjenes ind")
                                       : L.S("Failed prints payback – customers only", "Fejlprint tjenes ind – kun kunder");
        }

        public static string PaybackAmount(QuoteResult r)
        {
            return L.F("{0}  ({1} left)", "{0}  ({1} tilbage)", Num.Kr(r.PaybackCost), Num.Kr(r.PaybackLeft));
        }
    }
}
