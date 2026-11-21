using System;
using System.Collections.Generic;

namespace PrintPrice.Web
{
    /// Combined bill (several History jobs on one quote / invoice) on the website, step 1: one page (the desktop's
    /// MainForm combined-bill section + QuotePage.DrawGroup / DrawCard / ChooseGroupLevel). The bill itself is the shared
    /// History.cs (BillProblem, InvoiceGroup, OldestFirst, GroupBill, GroupBreakdown, group MakeInvoice) and QuoteRows.cs
    /// (BuildGroup, JobOre, GroupText, GroupSummary, JobSub, GroupFileName). A bill too long for one A4 page is one taller
    /// page here, printed smaller onto A4 (step 2: pages as the desktop's PDF).
    public static partial class WebQuote
    {
        /// One job's card (= QuoteJob): Picture = a data URL or null (a drawn cube).
        public sealed class Card
        {
            public string Name = "", Detail = "", Sub = "", Amount = "", Weight = "", Picture;
            public long Ore;
            public int Hours, Minutes;
            public double TowerGrams = -1;   // qol 2: this job's prime tower for its own note (-1 = none / setting off)
        }

        /// A combined bill (2+ cards).
        public static bool IsGroup(Sheet d) { return d != null && d.Jobs != null && d.Jobs.Count >= 2; }

        // ---------- opening: which jobs go on the bill (MainForm.NewRecordsQuoteForm / NewGroupQuoteForm) ----------

        /// What History's Quote… / Invoice… on records opens: Problem (a refusal for History's info box), Single (the one-job
        /// window) or Jobs (the combined window, oldest first); Notes under the help (Warn = in the warning colour).
        public sealed class BillOpen
        {
            public string Problem;
            public QuoteRecord Single;
            public List<QuoteRecord> Jobs;
            public List<string> Notes = new List<string>();
            public bool Warn;
            public string StartName = "";   // the name box (made invoice: its BilledTo, else the most used name)
            public bool Made;               // an invoice already made for all of them (name and price locked)
        }

        public static BillOpen OpenRecords(List<QuoteRecord> history, IList<QuoteRecord> records, bool invoice, AppSettings s)
        {
            var o = new BillOpen();
            var sel = new List<QuoteRecord>();
            if (records != null) foreach (var r in records) if (r != null && !sel.Contains(r)) sel.Add(r);
            if (sel.Count == 0) return o;
            List<QuoteRecord> jobs;
            if (sel.Count == 1)
            {
                var r = sel[0];
                var group = invoice && InvoiceMade(r) ? History.InvoiceGroup(history, r.InvoiceNo) : null;
                if (group == null || group.Count < 2) { o.Single = r; return o; }
                jobs = group;
                o.Notes.Add(group.Count == 2
                    ? L.F("\"{0}\" is on combined invoice {1} with 1 other print: the whole invoice is shown.",
                          "\"{0}\" er på samlet faktura {1} med 1 andet print: hele fakturaen vises.", RecordModelName(r), r.InvoiceNo.Trim())
                    : L.F("\"{0}\" is on combined invoice {1} with {2} other prints: the whole invoice is shown.",
                          "\"{0}\" er på samlet faktura {1} med {2} andre print: hele fakturaen vises.", RecordModelName(r), r.InvoiceNo.Trim(), group.Count - 1));
            }
            else
            {
                var check = History.BillProblem(sel);
                if (invoice && check.Clash != null)
                {
                    o.Problem = L.F("\"{0}\" is already on invoice {1}. Pick only unbilled prints, or only that invoice's prints.",
                                    "\"{0}\" er allerede på faktura {1}. Vælg kun print uden faktura, eller kun den fakturas print.",
                                    RecordModelName(check.Clash), check.ClashCode);
                    return o;
                }
                if (check.Jobs.Count == 0)
                {
                    o.Problem = L.S("Failed prints can't go on a bill, and all the selected ones failed.",
                                    "Fejlede print kan ikke faktureres, og alle de valgte er fejlet.");
                    return o;
                }
                jobs = History.OldestFirst(check.Jobs);
                if (check.FailedLeftOut > 0) { o.Warn = true; o.Notes.Add(FailedLeftOutText(check.FailedLeftOut)); }
                if (invoice && check.Code.Length > 0)
                {
                    var group = History.InvoiceGroup(history, check.Code);
                    if (group.Count > jobs.Count)
                        o.Notes.Add(L.F("Invoice {0} has {1} prints: the whole invoice is shown.", "Faktura {0} har {1} print: hele fakturaen vises.",
                                        check.Code, group.Count));
                    if (group.Count >= jobs.Count) jobs = group;
                }
                if (jobs.Count == 1) { o.Single = jobs[0]; return o; }
            }
            try { History.GroupBill(jobs, s, false); History.GroupBill(jobs, s, true); }
            catch (Exception ex)
            {
                // a record with an absurd number (e.g. a hand-edited history): the bill can't be worked out
                if (!(ex is OverflowException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                o.Problem = L.S("These prints can't go on one bill: one of them has a price or amount too large to work with. Check their time and grams.",
                                "Disse print kan ikke komme på én regning: et af dem har en pris eller mængde, der er for stor. Tjek deres tid og gram.");
                o.Notes.Clear();
                return o;
            }
            o.Jobs = jobs;
            o.Made = invoice && AllMade(jobs);
            var names = JobNames(jobs);
            o.StartName = o.Made ? (History.InvoiceInfo(jobs[0], s).BilledTo ?? "") : names.Count > 0 ? names[0] : "";
            if (!o.Made && names.Count > 1)
            {
                o.Warn = true;
                string list = string.Join(", ", names.GetRange(0, Math.Min(3, names.Count)).ToArray()) + (names.Count > 3 ? ", …" : "");
                o.Notes.Add(L.F("Different names on these prints ({0}): the name you type goes on all of them.",
                                "Forskellige navne på disse print ({0}): navnet, du skriver, kommer på dem alle.", list));
            }
            return o;
        }

        public static string FailedLeftOutText(int n)
        {
            return n == 1 ? L.S("1 failed print is left off the bill.", "1 fejlet print er udeladt af regningen.")
                          : L.F("{0} failed prints are left off the bill.", "{0} fejlede print er udeladt af regningen.", n);
        }

        /// The jobs' names (trimmed, not empty), the most used first; ties keep the first-seen order (MainForm.JobNames).
        public static List<string> JobNames(IList<QuoteRecord> jobs)
        {
            var count = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            var order = new List<string>();
            foreach (var r in jobs)
            {
                string c = (r.Customer ?? "").Trim();
                if (c.Length == 0) continue;
                int k;
                if (count.TryGetValue(c, out k)) count[c] = k + 1;
                else { count[c] = 1; order.Add(c); }
            }
            var res = new List<string>(order);
            res.Sort((a, b) => { int c = count[b].CompareTo(count[a]); return c != 0 ? c : order.IndexOf(a).CompareTo(order.IndexOf(b)); });
            return res;
        }

        public static bool AllMade(IList<QuoteRecord> jobs)
        {
            if (jobs == null || jobs.Count == 0) return false;
            foreach (var r in jobs) if (!InvoiceMade(r)) return false;
            return true;
        }

        /// A combined bill's price: friends only when every job is at the friends price (MainForm.BilledFriends(jobs)).
        public static bool AllFriends(IList<QuoteRecord> jobs)
        {
            if (jobs == null || jobs.Count == 0) return false;
            foreach (var r in jobs) if (r.For != History.ForFriends) return false;
            return true;
        }

        /// The combined window's help (MainForm.NewGroupQuoteForm, with the web's Print / PDF).
        public static string GroupHelpText(int n, bool invoice, bool made, string code)
        {
            string print = L.S(" Print / PDF opens the browser's print window: choose \"Save as PDF\" for a file.",
                               " Print / PDF åbner browserens printvindue: vælg \"Gem som PDF\" for en fil.");
            if (made) return L.F("Invoice {0} ({1} prints) is already made and stays as it is.", "Faktura {0} ({1} print) er allerede lavet og forbliver uændret.", code, n) + print;
            if (invoice && n == 2)   // qol 11: "both", not "all 2"
                return L.S("Making the invoice (Print / PDF or Copy text) gives both prints one number and marks them as printed in the history.",
                           "Når fakturaen laves (Print / PDF eller Kopiér tekst), får begge print ét nummer og markeres som printet i historikken.") + print;
            if (invoice)
                return L.F("Making the invoice (Print / PDF or Copy text) gives all {0} prints one number and marks them as printed in the history.",
                           "Når fakturaen laves (Print / PDF eller Kopiér tekst), får alle {0} print ét nummer og markeres som printet i historikken.", n) + print;
            return L.F("Print / PDF or Copy text puts the name and price on these {0} quotes in the history.",
                       "Print / PDF eller Kopiér tekst sætter navn og pris på disse {0} tilbud i historikken.", n) + print;
        }

        // ---------- the page's data (MainForm.BuildGroupQuote) ----------

        /// The combined page: a card per job (its price with its share of any top-up, QuoteRows.JobOre, so the cards add up
        /// to the total), the printer / plate when shared, the whole bill's breakdown, the total. Quote: number / date /
        /// valid-until from when; invoice: the first job's code, date and snapshot once made (else "(new)" and now).
        /// pictures: record Id -> data URL (missing = a cube).
        public static Sheet FromRecords(IList<QuoteRecord> jobs, AppSettings s, bool friends, string customer, bool invoice,
                                        DateTime when, DateTime now, IDictionary<string, string> pictures)
        {
            var bill = History.GroupBill(jobs, s, friends);
            var d = new Sheet
            {
                Name = (s.QuoteName ?? "").Trim(),
                Customer = (customer ?? "").Trim(),
                Number = History.QuoteNumber(when), Date = when, ValidUntil = s.QuoteValidUntil(when),
                Summary = QuoteRows.GroupSummary(jobs),
                GroupFriends = friends,
                GroupTotal = bill.Total,
                Jobs = new List<Card>()
            };
            string printer = null;
            bool samePrinter = true;
            long[] jobOre = QuoteRows.JobOre(bill);
            for (int i = 0; i < jobs.Count; i++)
            {
                var r = jobs[i];
                var md = new Sheet();
                FillRecordMaterial(md, r, s);
                long ore = i < jobOre.Length ? jobOre[i] : 0;
                string pic = null;
                if (pictures != null && r.Id != null) pictures.TryGetValue(r.Id, out pic);
                d.Jobs.Add(new Card
                {
                    Name = RecordModelName(r),
                    Detail = QuoteRows.JobDetail(r, md.Material),
                    Sub = QuoteRows.JobSub(r),
                    Amount = Num.Kr(ore / 100.0),
                    Ore = ore,
                    Picture = pic,
                    Hours = r.Hours, Minutes = r.Minutes,
                    Weight = Num.Fmt(r.Grams) + " g",
                    TowerGrams = RecordTower(s, r)
                });
                string pt = PrinterText(s, r);
                if (printer == null) printer = pt;
                else if (pt != printer) samePrinter = false;
            }
            d.Printer = samePrinter ? printer ?? "" : "";
            var gb = History.GroupBreakdown(bill, s);
            d.Breakdown = new QuoteBreakdown { Friends = friends, Lines = gb.Lines, TotalOre = gb.TotalOre, Price = gb.Total };
            d.GroupSameRates = gb.SameRates;
            d.TowerGrams = s.QuoteShowTower == 1 ? History.TowerGramsSum(jobs) : -1;   // issue #8 step 3: the jobs' towers
            foreach (var m in gb.Materials)
            {
                bool unnamed = string.IsNullOrEmpty(m.Name) && string.IsNullOrEmpty(m.Type);
                d.MaterialNames.Add(unnamed ? DefaultMaterial(s) : WebHistory.MaterialName(new FilamentUse { Profile = m.Name ?? "", Type = m.Type ?? "", Colour = m.Colour ?? "" }));
                d.MaterialColours.Add(ColourCss(m.Colour));
            }
            NameByColour(d.MaterialNames, d.MaterialColours);
            d.MaterialJob = MaterialOwners(jobs, gb.Materials);
            int minutes = 0;
            string bed = null;
            bool sameBed = true;
            foreach (var r in jobs)
            {
                minutes += Math.Max(0, r.Hours) * 60 + Math.Max(0, r.Minutes);
                string bt = PlateName(s, r.BedType ?? "");   // the plate the job's own page shows
                if (bed == null) bed = bt; else if (!string.Equals(bed, bt, StringComparison.OrdinalIgnoreCase)) sameBed = false;
            }
            d.Hours = minutes / 60;
            d.Minutes = minutes % 60;
            d.PlateName = sameBed && !string.IsNullOrEmpty(bed) ? bed : "";
            if (bill.TopUp > 0)
            {
                d.TopUpLabel = QuoteRows.MinimumLabel(bill.Minimum > 0 ? bill.Minimum : bill.Total);
                d.TopUpAmount = Num.Kr(bill.TopUp);
            }
            if (invoice)
            {
                var first = jobs[0];
                DateTime billed = DateTime.MinValue;
                bool made = AllMade(jobs) && History.TryTime(first.BilledAt, out billed);
                if (!made) billed = now;
                d.Invoice = true;
                d.InvoiceNo = made ? first.InvoiceNo.Trim() : L.S("(new)", "(ny)");
                d.Date = billed;
                var inv = History.InvoiceInfo(first, s, billed);
                d.Due = inv.Due;
                d.PayMobilePay = (inv.MobilePay ?? "").Trim();
                d.PayBank = (inv.Bank ?? "").Trim();
                d.Name = (inv.FromName ?? "").Trim();
                d.PaidAt = made ? PaidAll(jobs) : DateTime.MinValue;   // paid-tick
            }
            return d;
        }

        /// Paid-tick: the latest paid date when every job is marked paid (= InvoiceRow.Paid / PaidAt), else MinValue.
        public static DateTime PaidAll(IList<QuoteRecord> jobs)
        {
            DateTime latest = DateTime.MinValue, p;
            if (jobs == null || jobs.Count == 0) return latest;
            foreach (var j in jobs)
            {
                if (!History.PaidOn(j, out p)) return DateTime.MinValue;
                if (p > latest) latest = p;
            }
            return latest;
        }

        /// qol 2: which job each of the bill's materials (History.GroupBreakdown's Materials, keyed as it keys them: the
        /// filament's name, else the record's MaterialName, + upper-case colour) comes from; -1 = more than one job (or none).
        public static List<int> MaterialOwners(IList<QuoteRecord> jobs, IList<HistoryFilament> materials)
        {
            var owners = new List<int>();
            if (materials == null) return owners;
            var keys = new List<HashSet<string>>();
            foreach (var r in jobs)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string own = (r.MaterialName ?? "").Trim();
                if (r.Filaments == null || r.Filaments.Count == 0) set.Add(own + "\n");
                else
                    foreach (var f in r.Filaments)
                    {
                        if (f == null) { set.Add(own + "\n"); continue; }
                        string name = !string.IsNullOrWhiteSpace(f.Name) ? f.Name.Trim() : own;
                        set.Add(name + "\n" + (f.Colour ?? "").Trim().ToUpperInvariant());
                    }
                keys.Add(set);
            }
            foreach (var m in materials)
            {
                string k = (m.Name ?? "") + "\n" + (m.Colour ?? "");
                int owner = -1, hits = 0;
                for (int j = 0; j < keys.Count; j++) if (keys[j].Contains(k)) { owner = j; hits++; }
                owners.Add(hits == 1 ? owner : -1);
            }
            return owners;
        }

        /// The combined bill's breakdown rows (QuoteRows.BuildGroup). qol 2: when every material line belongs to one job and
        /// each job's lines are together, the lines are grouped under the job's name (a "label" row in marks) and each job's
        /// own tower note ("note" in marks) follows its lines; otherwise one list with the summed tower note
        /// (QuoteRows.AddTowerNote) as before. marks: row -> "label" / "note" (may be null).
        internal static List<QuoteRow> GroupRows(Sheet d, bool shortRows, IDictionary<QuoteRow, string> marks)
        {
            var rows = QuoteRows.BuildGroup(d.Breakdown, d.MaterialNames, d.Hours, d.Minutes, d.PlateName, d.GroupSameRates,
                                            shortRows ? 4 : MaxGroupColours, shortRows);
            int firstRule = rows.FindIndex(r => r.Kind == QuoteRowKind.Rule);
            if (firstRule < 0) firstRule = rows.Count;
            var subs = new List<QuoteRow>();
            for (int i = 1; i < firstRule; i++) subs.Add(rows[i]);
            bool group = d.MaterialJob != null && d.Jobs != null && marks != null && firstRule > 1 && rows[0].Kind == QuoteRowKind.Head
                         && subs.Count == d.MaterialJob.Count;
            var owners = new List<int>();
            if (group)
                foreach (var r in subs)
                {
                    if (r.Kind != QuoteRowKind.Sub || r.ColourIndex < 0 || r.ColourIndex >= d.MaterialJob.Count || d.MaterialJob[r.ColourIndex] < 0
                        || d.MaterialJob[r.ColourIndex] >= d.Jobs.Count) { group = false; break; }
                    int o = d.MaterialJob[r.ColourIndex];
                    if (owners.Count > 0 && owners[owners.Count - 1] != o && owners.Contains(o)) { group = false; break; }   // a job's lines apart
                    owners.Add(o);
                }
            if (group)
            {
                int distinct = 0;
                for (int i = 0; i < owners.Count; i++) if (i == 0 || owners[i] != owners[i - 1]) distinct++;
                group = distinct >= 2;
            }
            if (!group) return QuoteRows.AddTowerNote(rows, d.TowerGrams);
            var res = new List<QuoteRow> { rows[0] };
            bool towers = d.TowerGrams > 0.005;   // the setting is on and a job has a tower
            for (int i = 0; i < subs.Count; i++)
            {
                int o = owners[i];
                if (i == 0 || owners[i - 1] != o)
                {
                    var label = new QuoteRow { Kind = QuoteRowKind.Note, Label = d.Jobs[o].Name };
                    marks[label] = "label";
                    res.Add(label);
                }
                res.Add(subs[i]);
                bool last = i == subs.Count - 1 || owners[i + 1] != o;
                if (last && towers && d.Jobs[o].TowerGrams > 0.005)
                {
                    var note = new QuoteRow { Kind = QuoteRowKind.Note, Label = QuoteRows.TowerNote(d.Jobs[o].TowerGrams) };
                    marks[note] = "note";
                    res.Add(note);
                }
            }
            for (int i = firstRule; i < rows.Count; i++) res.Add(rows[i]);
            return res;
        }

        /// The shared printer / plate beside "Price breakdown" ("Bambu Lab A1, 0.4 mm nozzle  ·  Smooth PEI build plate").
        internal static string GroupSetupText(Sheet d)
        {
            var setup = new List<string>();
            if (!string.IsNullOrEmpty(d.Printer)) setup.Add(d.Printer);
            if (!string.IsNullOrEmpty(d.PlateName)) setup.Add(L.F("{0} build plate", "Byggeplade {0}", d.PlateName));
            return string.Join("  ·  ", setup.ToArray());
        }

        /// The total box's small line with the job prices: "50,00 + 50,00 · please pay by …" / "… · Paid … – thank you".
        internal static string GroupTotalNoteWithSums(Sheet d)
        {
            string small = TotalNote(d);
            var sums = new List<string>();
            foreach (var j in d.Jobs) sums.Add(Num.N2(j.Ore / 100.0));
            // "· please pay by …" lower-case; the paid line keeps its capital ("· Paid 12 November 2026 – thank you", qol 8)
            bool lower = small.Length > 0 && d.PaidAt == DateTime.MinValue;
            return string.Join(" + ", sums.ToArray()) + " · " + (lower ? char.ToLower(small[0]) + small.Substring(1) : small);
        }

        // ---------- exports (MainForm.ExportGroup without the file part) ----------

        /// One export's changes to the jobs (the caller saves, then the settings for a new number): every job gets the name
        /// (not on a made invoice) and For (not on an invoiced job, A4); an invoice is made for all at once (History.MakeInvoice:
        /// one code, Printed, the minimum's shares). Returns null, or the refusal (nothing changed). friends = the price used
        /// (a made invoice: its billed one).
        public static string ApplyGroupExport(List<QuoteRecord> list, IList<QuoteRecord> jobs, bool invoice, bool history,
                                              ref bool friends, string customer, DateTime now, AppSettings s)
        {
            customer = (customer ?? "").Trim();
            bool made = invoice && AllMade(jobs);
            if (made) friends = AllFriends(jobs);
            int forWho = friends ? History.ForFriends : History.ForCustomer;
            if (!history)
            {
                if (invoice && customer.Length == 0) return NeedName;
                if (invoice && !made)
                {
                    var check = History.BillProblem(jobs);
                    if (!check.Ok)
                        return check.Clash != null
                            ? L.F("\"{0}\" is already on invoice {1}: no invoice was made.", "\"{0}\" er allerede på faktura {1}: der blev ikke lavet en faktura.",
                                  RecordModelName(check.Clash), check.ClashCode)
                            : L.S("No prints to invoice.", "Ingen print at fakturere.");
                }
            }
            foreach (var r in jobs) { if (!made) r.Customer = customer; if (!InvoiceMade(r)) r.For = forWho; }
            if (invoice && !history) History.MakeInvoice(list, new List<QuoteRecord>(jobs), now, s, friends);
            return null;
        }

        /// Copy text of a combined bill (QuoteRows.GroupText with the page's number / date and the first job's invoice info).
        public static string GroupText(IList<QuoteRecord> jobs, Sheet d, bool friends, AppSettings s)
        {
            var bill = History.GroupBill(jobs, s, friends);
            return QuoteRows.GroupText(bill, d.Invoice, d.Invoice ? d.InvoiceNo : d.Number, d.Customer, d.Date,
                                       d.Invoice ? History.InvoiceInfo(jobs[0], s, d.Date) : null);
        }

        /// The suggested PDF name: an invoice "2026-48213 - Name" (made), a quote "2026-11-19 3 prints - Name".
        public static string GroupFileName(Sheet d)
        {
            if (d.Invoice && IsInvoiceCode(d.InvoiceNo)) return QuoteRows.InvoiceFileName(d.InvoiceNo, d.Customer);
            return QuoteRows.GroupFileName(d.Date, d.Jobs.Count, d.Customer);
        }

        public static string GroupSavedText(int n) { return L.F("✓ Saved to history ({0}).", "✓ Gemt i historikken ({0}).", QuoteRows.PrintsText(n)); }

        /// The status after Print / PDF or Copy text of a combined bill.
        public static string GroupDoneText(bool text, bool invoice, string code, int n, bool saved)
        {
            string head = text ? (invoice ? L.S("✓ Invoice text copied", "✓ Fakturaen er kopieret som tekst") : L.S("✓ Quote text copied", "✓ Tilbuddet er kopieret som tekst"))
                               : L.S("✓ Print window opened", "✓ Printvinduet er åbnet");
            if (invoice && !saved) return text ? L.S("Not copied: the invoice couldn't be saved.", "Ikke kopieret: fakturaen kunne ikke gemmes.")
                                               : L.S("Not printed: the invoice couldn't be saved.", "Ikke printet: fakturaen kunne ikke gemmes.");
            if (!saved) return head + ".";
            return head + (invoice ? L.F(". Invoice no. {0} is in the history ({1}).", ". Faktura nr. {0} er gemt i historikken ({1}).", code, QuoteRows.PrintsText(n))
                                   : L.F(" and saved to history ({0}).", " og gemt i historikken ({0}).", QuoteRows.PrintsText(n)));
        }

        // ---------- the page (QuotePage.DrawGroup / DrawCard / ChooseGroupLevel, step for step in the 1000-unit page) ----------

        const double GL0 = 40, GR0 = 960, PageLimit = A4H - 80, CardGap = 24, RowGap = 24, ContinueTop = 60, NoteH = 46;
        const int CardCols = 3, MaxGroupColours = 6;

        static int Cols(Sheet d)
        {
            if (d.GroupLevel >= 2 && d.Jobs.Count == 4) return 4;   // 4 prints: always one row of 4
            return Math.Max(1, Math.Min(CardCols, d.Jobs.Count));
        }

        static void Tx(List<Item> it, string text, double size, bool bold, string role, double x, double y, double w, string align)
        {
            if (string.IsNullOrEmpty(text)) return;
            it.Add(new Item { Text = text, Size = size, Bold = bold, Role = role, X = x, Y = y, W = w, Align = align });
        }

        static void Ln(List<Item> it, double ax, double bx, double ly, string role, double width)
        {
            it.Add(new Item { Kind = "line", X = ax, Y = ly - width / 2, W = bx - ax, H = width, Role = role });
        }

        /// Estimated text width (Segoe UI's average letter; the browser cuts what doesn't fit with …).
        static double TextW(string text, double size, bool bold) { return (text ?? "").Length * size * (bold ? 0.56 : 0.5); }

        /// The combined bill's pages (QuotePage.RenderPages): the layout step chosen as ChooseGroupLevel (the first whose plan
        /// is ONE page; exactly 4 prints always 4 in a row), then whole rows of cards per A4 page, the header and summary only
        /// on page 1, later pages with a top line saying whose bill they are, the breakdown / total / payment on the last
        /// page ("Breakdown and total: next page →" under the cards before it), "Page 1 of 2" in the footer.
        /// head = page 1's layout with the header already in it.
        static List<Layout> GroupPages(Sheet d, Layout head)
        {
            int[] order = d.Jobs.Count == 4 ? new[] { 2, 3 } : new[] { 0, 1 };
            List<int[]> plan = null;
            foreach (int level in order)
            {
                d.GroupLevel = level;
                plan = PagePlan(d);
                if (plan.Count == 1) break;
            }
            if (plan.Count > 1) { d.GroupLevel = order[0]; plan = PagePlan(d); }
            var pages = new List<Layout>();
            for (int i = 0; i < plan.Count; i++)
            {
                var lay = i == 0 ? head : new Layout();
                double end = GroupBody(d, lay.Items, plan[i][0], plan[i][1], i == 0, plan[i][2] == 1, NoteText(plan, i));
                double pageH = Math.Max(A4H, end + 90);   // only a breakdown taller than a page grows (never in practice)
                Ln(lay.Items, GL0, GR0, pageH - 62, "line", 1);
                Tx(lay.Items, Footer, 14, false, "muted", GL0, pageH - 44, GR0 - GL0, "c");
                if (plan.Count > 1) Tx(lay.Items, L.F("Page {0} of {1}", "Side {0} af {1}", i + 1, plan.Count), 14, false, "muted", GR0 - 200, pageH - 44, 200, "r");
                lay.Height = pageH;
                pages.Add(lay);
            }
            return pages;
        }

        /// QuotePage.NoteText: under the cards of a page without the breakdown part; null = none.
        static string NoteText(List<int[]> plan, int i)
        {
            if (plan[i][2] == 1 || plan[i][1] <= plan[i][0]) return null;
            int tailPage = plan.Count;
            return tailPage == i + 2
                ? L.S("Breakdown and total: next page →", "Prisoversigt og total: næste side →")
                : L.F("Breakdown and total: page {0} →", "Prisoversigt og total: side {0} →", tailPage);
        }

        /// QuotePage.PagePlan: pages as {from job, to job (exclusive), 1 = the breakdown / total part is on it}. Whole rows,
        /// as many per page as fit (at least one; room kept for the note); the breakdown part after the last row when it
        /// fits there, else on a page of its own. Always the last page.
        static List<int[]> PagePlan(Sheet d)
        {
            int n = d.Jobs.Count, cols = Cols(d);
            var scratch = new List<Item>();
            double headEnd = GroupBody(d, scratch, 0, 0, true, false, null);
            double tailH = GroupBody(d, scratch, 0, 0, false, true, null) - ContinueTop;
            var rowH = new List<double>();
            for (int a = 0; a < n; a += cols) rowH.Add(GroupBody(d, scratch, a, Math.Min(n, a + cols), false, false, null) - ContinueTop);
            var plan = new List<int[]>();
            int pageFrom = 0, rowsOnPage = 0;
            double top = headEnd;
            for (int k = 0; k < rowH.Count; k++)
            {
                if (rowsOnPage > 0 && top + rowH[k] - RowGap + NoteH > PageLimit)
                {
                    plan.Add(new[] { pageFrom, k * cols, 0 });
                    pageFrom = k * cols; rowsOnPage = 0; top = ContinueTop;
                }
                top += rowH[k];
                rowsOnPage++;
            }
            if (top + tailH <= PageLimit) plan.Add(new[] { pageFrom, n, 1 });
            else
            {
                plan.Add(new[] { pageFrom, n, 0 });
                plan.Add(new[] { n, n, 1 });
            }
            return plan;
        }

        /// Tests (= QuotePage.PageLayout): the pages as "from-to" job ranges, "+note" / "+total" ("0-9+note | 9-12+total").
        public static string PageLayout(Sheet d)
        {
            if (!IsGroup(d)) return "single";
            DrawPages(d, true);   // sets GroupLevel
            var plan = PagePlan(d);
            var parts = new List<string>();
            for (int i = 0; i < plan.Count; i++)
                parts.Add(plan[i][0] + "-" + plan[i][1] + (NoteText(plan, i) != null ? "+note" : "") + (plan[i][2] == 1 ? "+total" : ""));
            return string.Join(" | ", parts.ToArray());
        }
        /// How many A4 pages a sheet has (1 for a single job).
        public static int PageCount(Sheet d) { return IsGroup(d) ? DrawPages(d, true).Count : 1; }

        /// One page of a combined bill (QuotePage.DrawGroup): page 1 = summary rows, else the "whose bill" top line; the
        /// cards of jobs from..to (whole rows); the note; tail = the whole bill's breakdown, the total box and payment /
        /// valid-until. Returns where the content ends.
        static double GroupBody(Sheet d, List<Item> it, int from, int to, bool head, bool tail, string note)
        {
            double y;
            if (head)
            {
                // summary rows, two to a line: Bill to (invoice) and Prints; the shared printer / plate go beside "Price breakdown"
                var rows = new List<string[]>();
                if (d.Invoice) rows.Add(new[] { L.S("Bill to", "Kunde"), d.Customer ?? "" });
                rows.Add(new[] { L.S("Prints", "Print"), d.Summary ?? "" });
                double vx = L.Danish ? 140 : 130, mid = 500;
                y = 214;
                for (int k = 0; k < rows.Count; k += 2)
                {
                    Ln(it, GL0, GR0, y, "line", 1);
                    bool pair = k + 1 < rows.Count;
                    Tx(it, rows[k][0], 18, false, "muted", GL0 + 3, y + 10, 0, "l");
                    Tx(it, rows[k][1], 18, true, "ink", GL0 + vx, y + 10, (pair ? mid - 10 : GR0) - GL0 - vx, "l");
                    if (pair)
                    {
                        Tx(it, rows[k + 1][0], 18, false, "muted", mid + 3, y + 10, 0, "l");
                        Tx(it, rows[k + 1][1], 18, true, "ink", mid + vx, y + 10, GR0 - mid - vx, "l");
                    }
                    y += 46;
                }
                Ln(it, GL0, GR0, y, "line", 1);
                y += 26;
            }
            else
            {
                // a later page: which bill it belongs to, muted 14 at the top, a rule under it
                var bits = new List<string>();
                if (d.Invoice) bits.Add(L.F("Invoice no. {0}", "Faktura nr. {0}", d.InvoiceNo));
                else
                {
                    bits.Add(L.S("3D print quote", "Tilbud på 3D-print"));
                    if (!string.IsNullOrEmpty(d.Number)) bits.Add(L.F("Quote no. {0}", "Tilbud nr. {0}", d.Number));
                }
                if (!string.IsNullOrEmpty(d.Customer)) bits.Add(d.Customer);
                bits.Add(QuoteRows.LongDate(d.Date));
                Tx(it, string.Join("  ·  ", bits.ToArray()), 14, false, "muted", GL0 + 3, 20, GR0 - GL0 - 3, "l");
                Ln(it, GL0, GR0, 44, "line", 1);
                y = ContinueTop;
            }
            // the cards (every card of a row as tall as the tallest)
            int cols = Cols(d);
            double cardW = (GR0 - GL0 - CardGap * (cols - 1)) / cols;
            bool anyPicture = false;
            foreach (var j in d.Jobs) if (j.Picture != null) anyPicture = true;
            for (int a = from; a < to && a < d.Jobs.Count; a += cols)
            {
                int b = Math.Min(Math.Min(to, d.Jobs.Count), a + cols);
                double rowH = 0;
                for (int i = a; i < b; i++) rowH = Math.Max(rowH, DrawCard(null, d.Jobs[i], 0, y, cardW, 0));
                for (int i = a; i < b; i++) DrawCard(it, d.Jobs[i], GL0 + (i - a) * (cardW + CardGap), y, cardW, rowH);
                y += rowH + RowGap;
            }
            // the breakdown is on a later page: say so under the cards (bold 20, larger than the body text)
            if (!string.IsNullOrEmpty(note))
            {
                Tx(it, note, 20, true, "ink", GL0 + 3, y, GR0 - GL0 - 3, "l");
                y += NoteH;
            }
            if (!tail) return y;

            // the whole bill's breakdown (minimum and payback folded into Work & handling by QuoteRows.BuildGroup)
            string setupText = GroupSetupText(d);
            if (d.Breakdown != null && d.Breakdown.Lines != null && d.Breakdown.Lines.Count > 0)
            {
                string heading = L.S("Price breakdown", "Prisoversigt");
                Tx(it, heading, 24, true, "ink", GL0, y, 0, "l");
                if (setupText.Length > 0)
                {
                    double sx = GL0 + TextW(heading, 24, true) + 30;
                    if (TextW(setupText, 16, false) <= GR0 - sx) Tx(it, setupText, 16, false, "muted", sx, y + 7, GR0 - sx, "r");
                    else
                    {
                        Tx(it, setupText, 16, false, "muted", GL0 + 3, y + 38, GR0 - GL0 - 3, "l");
                        y += 28;
                    }
                }
                y += 40;
                Ln(it, GL0, GR0, y, "line", 2);
                y += 12;
                bool shortRows = d.GroupLevel == 1 || d.GroupLevel == 3;
                var marks = new Dictionary<QuoteRow, string>();
                y = DrawRows(it, d, GroupRows(d, shortRows, marks), y, true, marks);
                y += 4;
                Ln(it, GL0, GR0, y, "line", 2);
                y += 20;
            }
            else if (setupText.Length > 0)
            {
                Tx(it, setupText, 16, false, "muted", GL0 + 3, y, GR0 - GL0 - 3, "l");
                y += 32;
            }

            // total box ("Total · 3 prints"; the job prices "a + b + c · …" on its small line when they fit)
            it.Add(new Item { Kind = "round", X = GL0, Y = y, W = GR0 - GL0, H = 110, R = 14, Role = "totalback", Role2 = "totalline" });
            it.Add(new Item { Kind = "rect", X = GL0, Y = y + 12, W = 8, H = 86, Role = "stripe" });
            Tx(it, L.S("Total", "I alt") + " · " + QuoteRows.PrintsText(d.Jobs.Count), 26, true, "ink", 80, y + 22, 340, "l");
            string small = TotalNote(d);
            string total = Num.Kr(d.GroupTotal);
            double smallW = 900 - 80 - 30 - TextW(total, 56, true);
            string withSums = GroupTotalNoteWithSums(d);
            if (TextW(withSums, 16, false) <= smallW) small = withSums;
            Tx(it, small, 16, false, "muted", 80, y + 62, 0, "l");
            Tx(it, total, 56, true, "total", 420, y + 14, 510, "r");
            y += 140;

            if (d.Invoice)
            {
                var pay = PayLines(d);
                if (pay.Count > 0)
                {
                    Tx(it, L.S("Payment", "Betaling"), 19, true, "ink", GL0 + 3, y, 0, "l");
                    y += 32;
                    foreach (var line in pay) { Tx(it, line, 18, false, "ink", GL0 + 3, y, GR0 - GL0 - 3, "l"); y += 28; }
                    Tx(it, PayNote(d), 16, false, "muted", GL0 + 3, y + 2, 0, "l");
                    y += 32;
                }
            }
            else
            {
                Tx(it, ValidLine(d), 18, false, "ink", GL0 + 3, y, 0, "l");
                if (anyPicture)
                {
                    Tx(it, L.S("Colours and finish may differ slightly from the pictures.", "Farver og overflade kan afvige lidt fra billederne."),
                       16, false, "muted", GL0 + 3, y + 30, 0, "l");
                    y += 60;
                }
                else y += 30;
            }
            return y;
        }

        /// One job's card (QuotePage.DrawCard): rounded 1.5 px frame, picture well (or a cube), name bold 18 (2 lines) and
        /// faint 12 lines "Quote no. …" / date, Print time and Weight rows, the "Print price" box at the row's bottom.
        /// it = null only measures; h = the row's height (0 = measure). Returns the height it needs.
        static double DrawCard(List<Item> it, Card job, double x, double y, double w, double h)
        {
            const double Pad = 12, BoxH = 50, NameLH = 18 * 1.33;
            bool narrow = w < 250;   // 4 in a row: smaller picture, short price label
            double Pic = narrow ? 56 : 76;
            double top = y, R = x + w;
            if (it != null && h > 0) it.Add(new Item { Kind = "frame", X = x, Y = top, W = w, H = h, R = 10, Role = "line" });
            if (it != null)
            {
                it.Add(new Item { Kind = "round", X = x + Pad, Y = top + Pad, W = Pic, H = Pic, R = 10, Role = "picback", Role2 = "line" });
                if (job.Picture != null) it.Add(new Item { Kind = "picture", X = x + Pad, Y = top + Pad, W = Pic, H = Pic, Text = job.Picture });
                else it.Add(new Item { Kind = "cube", X = x + Pad, Y = top + Pad, W = Pic, H = Pic, Role = "faint" });
            }
            double tx = x + Pad + Pic + 12, tw = R - Pad - tx;
            double ny = top + Pad - 3;
            double factsTop = Math.Max(top + Pad + Pic, ny + NameLH * 2 + 4 + 2 * 17) + 10;
            if (!string.IsNullOrEmpty(job.Name))
            {
                int lines = TextW(job.Name, 18, true) > tw ? 2 : 1;
                if (it != null) it.Add(new Item { Kind = "wrap", Text = job.Name, Size = 18, Bold = true, Role = "ink", X = tx, Y = ny, W = tw, H = lines * NameLH + 2 });
                ny += lines * NameLH + 4;
            }
            foreach (var part in (job.Sub ?? "").Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (it != null) Tx(it, part, 12, false, "faint", tx, ny, tw, "l");
                ny += 17;
            }
            y = Math.Max(factsTop, ny + 10);
            var facts = new[]
            {
                new[] { L.S("Print time", "Printtid"), QuoteRows.Time(job.Hours, job.Minutes) },
                new[] { L.S("Weight", "Vægt"), job.Weight },
            };
            double valueX = x + Pad + (L.Danish ? 82 : 78);
            foreach (var r in facts)
            {
                if (it != null)
                {
                    Ln(it, x + 10, R - 10, y, "line", 1);
                    Tx(it, r[0], 14, false, "muted", x + Pad + 2, y + 6, 0, "l");
                    Tx(it, r[1], 14, true, "ink", valueX, y + 6, R - Pad - valueX, "l");
                }
                y += 28;
            }
            if (it != null) Ln(it, x + 10, R - 10, y, "line", 1);
            y += 10;
            double boxY = h > 0 ? Math.Max(y, top + h - Pad - BoxH) : y;
            if (it != null)
            {
                it.Add(new Item { Kind = "round", X = x + 10, Y = boxY, W = w - 20, H = BoxH, R = 8, Role = "totalback", Role2 = "totalline" });
                it.Add(new Item { Kind = "rect", X = x + 10, Y = boxY + 8, W = 5, H = BoxH - 16, Role = "stripe" });
                double priceW = TextW(job.Amount, 22, true);
                Tx(it, narrow ? L.S("Price", "Pris") : L.S("Print price", "Printpris"), 16, true, "ink", x + 26, boxY + 13, Math.Max(10, R - 24 - priceW - 8 - (x + 26)), "l");
                Tx(it, job.Amount, 22, true, "total", R - 24 - priceW - 40, boxY + 8, priceW + 40, "r");
            }
            return boxY + BoxH + Pad - top;
        }

        /// The breakdown rows (QuoteRows.Build / BuildGroup) from y (QuotePage.DrawRows): heads bold 19, sub-lines 17 with a
        /// colour dot, notes faint 15, rules 1 px. compact (combined bill): tighter steps. Returns y under the last row.
        static double DrawRows(List<Item> it, Sheet d, List<QuoteRow> rows, double y, bool compact)
        {
            return DrawRows(it, d, rows, y, compact, null);
        }

        /// As above; marks (GroupRows, may be null): "label" = a job's name over its material lines (muted bold 14),
        /// "note" = that job's tower note, in line with the material names. qol 2: material lines without a colour start
        /// where the dotted ones' names start when any material has a dot (one indent).
        static double DrawRows(List<Item> it, Sheet d, List<QuoteRow> rows, double y, bool compact, IDictionary<QuoteRow, string> marks)
        {
            double subEnd = compact ? 6 : 10;
            QuoteRowKind prev = QuoteRowKind.Rule;
            bool anyDot = false;
            foreach (var c in d.MaterialColours) if (!string.IsNullOrEmpty(c)) anyDot = true;
            bool inMaterial = true;   // before the first rule
            foreach (var r in rows)
            {
                string mark = null;
                if (marks != null) marks.TryGetValue(r, out mark);
                if (r.Kind == QuoteRowKind.Rule) inMaterial = false;
                if (mark == "label")
                {
                    if (prev == QuoteRowKind.Sub) y += compact ? 2 : 4;
                    Tx(it, r.Label, 14, true, "muted", 70, y + 2, GR0 - 70, "l");
                    y += compact ? 22 : 26;
                    prev = QuoteRowKind.Note;
                    continue;
                }
                if (mark == "note")
                {
                    Tx(it, r.Label, 15, false, "faint", anyDot ? 94 : 70, y, GR0 - 94, "l");
                    y += compact ? 26 : 30;
                    prev = QuoteRowKind.Note;
                    continue;
                }
                switch (r.Kind)
                {
                    case QuoteRowKind.Rule:
                        if (prev == QuoteRowKind.Sub) y += subEnd;
                        Ln(it, GL0, GR0, y, "line", 1);
                        y += compact ? 12 : 14;
                        break;
                    case QuoteRowKind.Head:
                        Tx(it, r.Label, 19, true, "ink", GL0 + 3, y, 420, "l");
                        Tx(it, r.Detail, 16, false, "muted", 470, y + 2, 240, "l");
                        Tx(it, r.Amount, 19, true, "ink", 700, y, GR0 - 700, "r");
                        y += compact ? (r.Alone ? 38 : 32) : (r.Alone ? 44 : 36);
                        break;
                    case QuoteRowKind.Sub:
                        double lx = 70;
                        string dot = r.ColourIndex >= 0 && r.ColourIndex < d.MaterialColours.Count ? d.MaterialColours[r.ColourIndex] : "";
                        if (dot.Length > 0)
                        {
                            it.Add(new Item { Kind = "dot", X = 70, Y = y + 5, W = 15, H = 15, Colour = dot, Role = "dotring" });
                            lx = 94;
                        }
                        else if (inMaterial && anyDot && r.ColourIndex >= 0) lx = 94;   // qol 2: one indent for all material lines
                        Tx(it, r.Label, 17, false, "ink", lx, y, 465 - lx, "l");
                        Tx(it, r.Detail, 16, false, "muted", 470, y + 1, 240, "l");
                        Tx(it, r.Amount, 17, false, "ink", 700, y, GR0 - 30 - 700, "r");
                        y += compact ? 27 : 32;
                        break;
                    default:
                        Tx(it, r.Label, 15, false, "faint", 70, y, GR0 - 70, "l");
                        y += 34;
                        break;
                }
                prev = r.Kind;
            }
            if (prev == QuoteRowKind.Sub) y += subEnd;
            return y;
        }
    }
}
