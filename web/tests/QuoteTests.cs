using System;
using System.Collections.Generic;
using PrintPrice;
using PrintPrice.Web;

// Stage 4: the quote / invoice page. The 4-colour Benchy = the desktop's picture-mode job (MainForm.SetSampleMultiJob,
// default settings): its numbers must be the ones on the desktop's quote_light.png / invoice_light.png.
static class QuoteTests
{
    static WebQuote.Job Benchy()
    {
        string[] colours = { "#118002", "#FFFF0A", "#074080", "#ED1C24" };
        string[] profiles = { "Bambu PLA Basic", "Bambu PLA Matte", "Bambu PLA Silk+", "Bambu PETG Translucent" };
        double[] grams = { 33.04, 55.99, 17.28, 29.54 };
        var uses = new List<FilamentUse>();
        var lines = new List<FilamentLine>();
        for (int i = 0; i < 4; i++)
        {
            uses.Add(new FilamentUse { Slot = i + 1, Colour = colours[i], Type = i == 3 ? "PETG" : "PLA", Profile = profiles[i] + " @BBL A1", Grams = grams[i] });
            lines.Add(new FilamentLine { Grams = grams[i], PricePerKg = 200 });
        }
        return new WebQuote.Job
        {
            Input = new QuoteInput { Hours = 10, Minutes = 43, FilamentGrams = 135.85, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 3.65, Filaments = lines },
            Model = "Four-Color Multicolor Test Benchy",
            Source = "https://www.printables.com/model/1648230-four-color-multicolor-test-benchy",
            Colours = uses, PurgeGrams = 124.0, FilamentType = "PLA"
        };
    }

    static string Texts(WebQuote.Layout lay)
    {
        var all = new List<string>();
        foreach (var i in lay.Items) if (i.Text.Length > 0 && i.Kind != "picture") all.Add(i.Text);
        return string.Join("|", all.ToArray());
    }

    public static void Run(Action<string, bool> True, Action<string, double, double> Eq)
    {
        bool wasDanish = L.Danish;
        L.Danish = false;
        var s = new AppSettings { QuoteName = AppSettings.LegacyQuoteName };   // quote_light.png's "Gettic" (new installs: empty)
        WebCalc.ApplyAutoWear(s, "PLA");
        var when = new DateTime(2026, 11, 4, 17, 45, 0);

        // ---- the job's quote = quote_light.png ----
        var d = WebQuote.FromJob(Benchy(), s, false, when, History.QuoteNumber(when), "");
        Eq("Benchy quote total 72,72", 72.72, d.Breakdown.Total);
        True("details", d.Weight == "135,85 g" && d.Printer == "Bambu Lab A1, 0.4 mm nozzle" && d.Material == "PLA + PETG, 4 colours"
             && d.Link == "printables.com/model/1648230" && d.Number == "261104-1745" && d.Name == "Gettic");
        True("colour names and dots", string.Join(",", d.MaterialNames.ToArray()) == "Bambu PLA Basic,Bambu PLA Matte,Bambu PLA Silk+,Bambu PETG Translucent"
             && d.MaterialColours[0] == "#118002" && d.MaterialColours[1] == "#ffff0a");
        var lay = WebQuote.Draw(d, true);
        string t = Texts(lay);
        string[] want = {
            "3D print quote", "Gettic · 3D printing", "Quote no. 261104-1745", "4 November 2026", "Four-Color Multicolor Test Benchy",
            "printables.com/model/1648230", "Print time|10 h 43 m|Weight|135,85 g|Printer|Bambu Lab A1, 0.4 mm nozzle|Material|PLA + PETG, 4 colours",
            "Price breakdown|Material|27,18 kr.|Bambu PLA Basic|33,04 g  ×  200 kr/kg|6,61|Bambu PLA Matte|55,99 g  ×  200 kr/kg|11,20",
            "Bambu PLA Silk+|17,28 g  ×  200 kr/kg|3,46|Bambu PETG Translucent|29,54 g  ×  200 kr/kg|5,91|incl. 124 g filament used for colour changes",
            "Electricity|1,07 kWh  ×  3,65 kr/kWh|3,91 kr.|Machine wear|10 h 43 m  ×  1,71 kr/h|18,32 kr.",
            "Printer|0,70 kr/h|7,49|Spare parts & upkeep|0,40 kr/h|4,28|Nozzle / hotend|0,28 kr/h|2,98|Build plate (Smooth PEI)|0,33 kr/h|3,57",
            "Work & handling|23,31 kr.|Total|Shipping not included|72,72 kr.|Valid until 18 November 2026|Made with Gettic's 3D Print Price Calculator" };
        foreach (var w in want) True("quote page has \"" + w + "\"", t.Contains(w));
        Eq("quote page = one A4 (1414)", WebQuote.A4H, lay.Height);
        True("no picture: no picture note", !t.Contains("may differ"));
        d.Picture = "data:image/png;base64,AA==";
        True("with a picture: note + picture box", Texts(WebQuote.Draw(d, true)).Contains("Colours and finish may differ slightly from the picture.")
             && WebQuote.Draw(d, true).Items.Exists(i => i.Kind == "picture"));
        d.Picture = null;
        var ff = WebQuote.FromJob(Benchy(), s, true, when, "", "Mette Hansen");
        True("friends 57,18 + For line", Math.Abs(ff.Breakdown.Total - 57.18) < 1e-9 && Texts(WebQuote.Draw(ff, true)).Contains("For: Mette Hansen")
             && WebQuote.ForSize(ff, true) == 20);
        True("file name", WebQuote.FileName(ff) == "2026-11-04 Four-Color Multicolor Test Benchy - Mette Hansen");
        ff.Customer = "A very long company name for the header ApS & Partners International";
        True("long For line gets smaller (15-19)", WebQuote.ForSize(ff, true) < 20 && WebQuote.ForSize(ff, true) >= 15);

        // ---- the invoice = invoice_light.png (sample number, payment ways; desktop RenderSampleInvoice) ----
        var inv = WebQuote.FromJob(Benchy(), s, false, when, History.QuoteNumber(when), "Lene Hansen");
        inv.Invoice = true; inv.InvoiceNo = "2026-K7Q4"; inv.Due = s.InvoiceDue(when);
        inv.PayMobilePay = "12 34 56 78"; inv.PayBank = "reg. 1234, account 0012345678";
        var il = WebQuote.Draw(inv, true);
        string it = Texts(il);
        foreach (var w in new[] { "Invoice|Gettic · 3D printing|Invoice no. 2026-K7Q4|Invoice date 4 November 2026|Due 18 November 2026",
                                  "Bill to|Lene Hansen", "Please pay by 18 November 2026", "Payment|MobilePay: 12 34 56 78|Bank: reg. 1234, account 0012345678",
                                  "Please write invoice no. 2026-K7Q4 with the payment." })
            True("invoice page has \"" + w + "\"", it.Contains(w));
        True("invoice: no Printer row, no validity", !it.Contains("Printer|Bambu") && !it.Contains("Valid until") && !it.Contains("For: "));
        True("invoice taller than A4, as the desktop's (its rows end at 1412, footer line at 1440): " + il.Height, Math.Abs(il.Height - 1502) < 1);

        // ---- a saved quote (History's Quote… / Invoice…) ----
        var list = new List<QuoteRecord>();
        var job = Benchy();
        var res = Pricing.Calculate(job.Input, s);
        var rec = History.FromQuote(job.Input, res, s, "Four-Color Multicolor Test Benchy", job.Source, when);
        for (int k = 0; k < 4; k++) { rec.Filaments[k].Colour = job.Colours[k].Colour; rec.Filaments[k].Type = job.Colours[k].Type; rec.Filaments[k].Name = job.Colours[k].Profile; }
        rec.PurgeGrams = 124;
        var saved = WebQuote.SaveExport(list, rec, false, " Mette Hansen ");
        True("export saves For + name", list.Count == 1 && saved.For == History.ForCustomer && saved.Customer == "Mette Hansen");
        var rq = WebQuote.FromRecord(saved, s, false, saved.Customer, false, when.AddDays(3), null);
        Eq("record quote = saved price 72,72", 72.72, rq.Breakdown.Total);
        True("record quote number/date from SavedAt", rq.Number == "261104-1745" && rq.Date == when && rq.Material == "PLA + PETG, 4 colours");
        saved.OthersPrice = 80;   // the inputs no longer give the saved price: the page still shows it (N4)
        Eq("record quote follows the saved price (MatchSavedTotal)", 80, WebQuote.FromRecord(saved, s, false, "", false, when, null).Breakdown.Total);
        saved.OthersPrice = res.OthersPrice;
        var ri = WebQuote.FromRecord(saved, s, false, "Mette Hansen", true, when.AddDays(1), null);
        True("invoice not made yet: (new), today, today's settings", ri.Invoice && ri.InvoiceNo == "(new)" && ri.Date == when.AddDays(1)
             && ri.Due == s.InvoiceDue(when.AddDays(1)) && WebQuote.PayLines(ri).Count == 0);
        s.PayMobilePay = "12 34 56 78";
        WebQuote.ApplyRecordExport(list, saved, true, "Lene Hansen", true, when.AddDays(1), s);
        True("invoice made: code, Printed, For, name, used code kept", System.Text.RegularExpressions.Regex.IsMatch(saved.InvoiceNo ?? "", @"^2026-[0-9A-Z]{4}$") && saved.Printed && saved.For == History.ForFriends
             && saved.Customer == "Lene Hansen" && s.UsedInvoiceNos.Contains(saved.InvoiceNo));
        string no = saved.InvoiceNo;
        s.PayMobilePay = "99 99 99 99";   // changed later: the invoice keeps what it said (its snapshot)
        var made = WebQuote.FromRecord(saved, s, true, saved.Customer, true, when.AddDays(9), null);
        True("made invoice keeps its number, date and snapshot", made.InvoiceNo == no && made.Date.Date == when.AddDays(1).Date
             && made.PayMobilePay == "12 34 56 78" && Math.Abs(made.Breakdown.Total - saved.FriendsPrice) < 0.005);
        WebQuote.ApplyRecordExport(list, saved, true, "Lene Hansen", true, when.AddDays(20), s);
        True("making it again keeps the number", saved.InvoiceNo == no);
        s.PayMobilePay = "";

        // ---- helpers ----
        True("Printables id", WebQuote.ParsePrintablesId("https://www.printables.com/model/1648230-four-color") == "1648230"
             && WebQuote.ParsePrintablesId("https://www.printables.com/da/model/3161-3d-benchy") == "3161"
             && WebQuote.ParsePrintablesId("https://example.com/model/12") == null);
        True("short links: web only", WebQuote.ShortLink("https://www.thingiverse.com/thing:763622") == "thingiverse.com/thing:763622"
             && WebQuote.ShortLink("Benchy.gcode.3mf") == "" && WebQuote.ShortLink(null) == "");
        True("colour dots", WebQuote.ColourCss("#FFFF0AFF") == "#ffff0a" && WebQuote.ColourCss("bad") == "" && WebQuote.ColourCss("") == "");
        var one = WebQuote.FromJob(new WebQuote.Job { Input = new QuoteInput { Hours = 1, Minutes = 5, FilamentGrams = 12, FilamentDkkPerKg = 180, ElectricityDkkPerKwh = 2 } },
                                   s, false, when, "", "");
        True("typed job: Settings' filament, 3D print, no link: " + one.Material, one.Material == BambuSlicer.FilamentBaseName(s.FilamentPreset) && one.Model == "3D print" && one.Link == ""
             && Texts(WebQuote.Draw(one, true)).Contains("Material|12 g  ×  180 kr/kg"));

        // ---- tester findings (stage 4) ----
        // 1. every price through Num.Kr: the History list's price = the quote's (25,005 -> 25,01 as on the desktop)
        True("Amount = Num.Kr without kr.", WebHistory.Amount(72.72) == "72,72" && WebHistory.Amount(1234.5) == "1.234,50");
        var half = new QuoteRecord { For = History.ForFriends, FriendsPrice = 25.005, OthersPrice = 30 };
        True("25,005: list " + WebHistory.PriceText(half) + " = Num.Kr " + Num.Kr(25.005) + " = 25,01",
             WebHistory.PriceText(half) + " kr." == Num.Kr(25.005) && WebHistory.PriceText(half) == "25,01");
        // 2. a job typed in by hand is saved with Settings' filament, so the reopened quote shows the same material
        var typed = new QuoteInput { Hours = 3, Minutes = 0, FilamentGrams = 85, FilamentDkkPerKg = 200, ElectricityDkkPerKwh = 2 };
        var typedJob = WebQuote.FromJob(new WebQuote.Job { Input = typed }, s, true, when, "", "");
        var typedRec = WebHistory.MakeRecord(typed, Pricing.Calculate(typed, s), s, "", when, new List<HistoryFilament> { WebHistory.JobMaterial(null, null, s, "PLA") }, -1, null, null);
        True("typed job saved with Settings' filament name: " + typedRec.Filaments[0].Name, typedRec.Filaments[0].Name == s.FilamentPreset);
        True("reopened typed quote shows the same material: " + WebQuote.FromRecord(typedRec, s, true, "", false, when, null).Material,
             WebQuote.FromRecord(typedRec, s, true, "", false, when, null).Material == typedJob.Material
             && WebHistory.FillFrom(typedRec, false, s).MaterialName == typedJob.Material);
        True("a file's material still wins", WebHistory.JobMaterial(null, "Bambu PETG HF", s, "PETG").Name == "Bambu PETG HF");
        // 3. #quote/<id> / #invoice/<id> addresses
        bool isInv;
        True("address ids", WebQuote.AddressId("#quote/abc", out isInv) == "abc" && !isInv && WebQuote.AddressId("#invoice/x1", out isInv) == "x1" && isInv
             && WebQuote.AddressId("#quote", out isInv) == null && WebQuote.AddressId("#history", out isInv) == null && WebQuote.NotFound.StartsWith("Quote not found"));

        // ---- Danish ----
        L.Danish = true;
        var da = WebQuote.FromJob(Benchy(), s, false, when, History.QuoteNumber(when), "Mette");
        string dt = Texts(WebQuote.Draw(da, true));
        foreach (var w in new[] { "Tilbud på 3D-print", "Til: Mette", "Tilbud nr. 261104-1745", "4. november 2026", "Printtid|10 t 43 m",
                                  "Bambu Lab A1, 0,4 mm dyse", "PLA + PETG, 4 farver", "Prisoversigt", "Strøm", "Maskinslid", "Byggeplade (Glat PEI)",
                                  "Arbejde & håndtering", "I alt|Fragt er ikke inkluderet|72,72 kr.", "Gyldigt til 18. november 2026" })
            True("Danish page has \"" + w + "\"", dt.Contains(w));
        L.Danish = wasDanish;
    }
}
