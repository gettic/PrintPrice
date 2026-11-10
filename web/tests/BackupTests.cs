using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using PrintPrice;
using PrintPrice.Web;

// Stage 3b: backup file, restore (History.Combine), the online blob (BackupCrypto with .NET's AES standing in for the
// browser's) against the desktop's Backup.cs and the test vector (plans\_old\history-backup\test vector.md), and the
// web's online texts.
static class BackupTests
{
    static Task<byte[]> NetAes(byte[] key, byte[] iv, byte[] data, bool encrypt)
    {
        using (var a = Aes.Create())
        {
            a.Mode = CipherMode.CBC; a.Padding = PaddingMode.PKCS7; a.Key = key; a.IV = iv;
            using (var t = encrypt ? a.CreateEncryptor() : a.CreateDecryptor())
                return Task.FromResult(t.TransformFinalBlock(data, 0, data.Length));
        }
    }

    static string Hex(byte[] b) { var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }

    public static void Run(Action<string, bool> True, Action<string, double, double> Eq)
    {
        bool wasDanish = L.Danish;
        L.Danish = false;
        const string code = "01234-56789-ABCDE-FGHJF";

        // ---- the blob: test vector ----
        True("storage id (vector)", Backup.StorageId(code) == "5c22a78a2694ed52be5c1efa1565dd0d");
        var iv = new byte[16];
        for (int i = 0; i < 16; i++) iv[i] = (byte)i;
        var sealedBlob = BackupCrypto.Seal(Encoding.ASCII.GetBytes("PrintPrice backup test vector"), code, iv, NetAes).Result;
        True("web Seal = vector bytes", Hex(sealedBlob) == "50504231000102030405060708090a0b0c0d0e0fe7b16e3df4b922480ef68fcee419f9fd65ae5df3cd3a4487289b836f84fa9e4375a63f8abc7eca18d55324858906c9521c6e1edc1309753ef28412b8936718db");
        var vector = Convert.FromBase64String("UFBCMRAREhMUFRYXGBkaGxwdHh91dDP3/rCpAGMKMZKP+W33pKtcLHi+MX5czFGyEmQLlbB2qKl/xtLBE8uIs8Gtf6hjkYX67mJuicMydWhoob9EzyZdDVbGlw7sFKasuVyFImsPtdAngkO+V/ft3GO5o01Go3kxBZfYS4/F+hhalEFWRSR/6xO8+9Apfz2s+XGOhlLNOcdelYMPXTcgnZmyDNYjbVf5Is+TtMI+o6n8wMuI5TKC7F/roCM/j8EmKHcOmQ==");
        var opened = BackupCrypto.Unpack(vector, code, NetAes).Result;
        True("web Unpack of the vector blob", opened.Text == "{\"Format\":\"PrintPriceBackup\",\"Version\":1,\"CreatedAt\":\"2026-11-10 12:00:00\",\"App\":\"test\",\"History\":{\"Version\":1,\"Quotes\":[]},\"Settings\":null}");
        True("typed code with a typo-free variant opens it", BackupCrypto.Unpack(vector, "o1234 56789-abcde-fghjf", NetAes).Result.Text != null);

        // ---- web <-> desktop blobs ----
        var list = HistoryTests.Samples();
        var s = new AppSettings { FriendsMarkupPct = 33, QuoteName = "Gettic test" };
        string json = WebBackup.FileText(list, s, new DateTime(2026, 11, 10, 15, 0, 0));
        var webBlob = BackupCrypto.Pack(json, code, NetAes).Result;
        True("desktop Unpack reads a web blob", Backup.Unpack(webBlob, code) == json);
        var deskBlob = Backup.Pack(json, code);
        True("web Unpack reads a desktop blob", BackupCrypto.Unpack(deskBlob, code, NetAes).Result.Text == json);
        var other = BackupCrypto.Unpack(webBlob, "K7QF2-9XMRT-B3HDW-P5ZN" + Backup.CheckChar("K7QF29XMRTB3HDWP5ZN"), NetAes).Result;
        True("wrong code -> damaged text", other.Text == null && other.Problem == Backup.Damaged());
        True("invalid code", BackupCrypto.Unpack(webBlob, "01234-56789-ABCDE-FGHJG", NetAes).Result.Problem == Backup.InvalidCodeText());
        True("not a blob", BackupCrypto.Unpack(Encoding.ASCII.GetBytes("<html>hello, this is not it at all, really not</html>"), code, NetAes).Result.Text == null);
        var flipped = (byte[])webBlob.Clone();
        flipped[30] ^= 1;
        True("changed byte -> damaged", BackupCrypto.Unpack(flipped, code, NetAes).Result.Problem == Backup.Damaged());
        True("8 sample quotes fit online", OnlineBackup.SizeProblem(webBlob.Length) == null && OnlineBackup.SizeProblem(OnlineBackup.MaxBlob + 1) != null);

        // ---- the Worker address ----
        True("site -> Worker", WebBackup.Url("gettic.github.io", code) == "https://printprice.gtd-relay.workers.dev/b/5c22a78a2694ed52be5c1efa1565dd0d"
             && WebBackup.Url("gettic.github.io", code) == OnlineBackup.Url(OnlineBackup.Server, code));
        True("elsewhere -> local fake", WebBackup.Url("localhost", code) == "_t/b/5c22a78a2694ed52be5c1efa1565dd0d");
        bool threw = false;
        try { WebBackup.Url("localhost", "nope"); } catch (ArgumentException) { threw = true; }
        True("invalid code -> no address", threw);

        // ---- online texts (src\OnlineBackup.cs is linked; the web only changes the 400 text) ----
        var now = new DateTime(2026, 11, 10, 20, 40, 0, DateTimeKind.Utc);
        True("429 with reset = daily limit text", WebBackup.FailureText(new WebBackup.Reply { Status = 429, Reset = "2026-11-11T00:00:00Z" }, false, now)
             == OnlineBackup.DailyLimitText(new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), now));
        True("404 on restore = not found", WebBackup.FailureText(new WebBackup.Reply { Status = 404 }, true, now) == OnlineBackup.ErrorText(404, true));
        True("400 says reload (web)", WebBackup.ErrorText(400, false).Contains("Reload") && WebBackup.ErrorText(500, false) == OnlineBackup.ErrorText(500, false));

        // ---- the file ----
        BackupData data;
        True("backup file read", WebBackup.ReadFile(Encoding.UTF8.GetBytes(json), out data) == null && data.Quotes.Count == 8 && data.Settings != null && data.App == "web");
        var bom = Encoding.UTF8.GetPreamble();
        var withBom = new byte[bom.Length + json.Length * 3];
        int n = Encoding.UTF8.GetBytes(json, 0, json.Length, withBom, bom.Length);
        Array.Copy(bom, withBom, bom.Length);
        Array.Resize(ref withBom, bom.Length + n);
        True("file with a BOM", WebBackup.ReadFile(withBom, out data) == null && data.Quotes.Count == 8);
        True("desktop history.json read", WebBackup.ReadFile(Encoding.UTF8.GetBytes(History.ToJson(list)), out data) == null && data.Settings == null && data.Quotes.Count == 8);
        True("not a backup", WebBackup.ReadFile(Encoding.UTF8.GetBytes("{\"x\":1}"), out data) != null && data == null);
        True("not UTF-8", WebBackup.ReadFile(new byte[] { 0xff, 0xfe, 0x00, 0xd8 }, out data) != null);
        True("settings in the file: no PC-only keys", !json.Contains("SlicerPath") && json.Contains("FriendsMarkupPct=33"));
        True("backup names", WebBackup.IsBackupName("PrintPrice backup 2026-11-10.printprice-backup") && WebBackup.IsBackupName("history.JSON") && !WebBackup.IsBackupName("a.gcode.3mf"));
        WebBackup.ReadFile(Encoding.UTF8.GetBytes(json), out data);
        True("summary: " + WebBackup.Summary(data), WebBackup.Summary(data) == "Backup from 10-11-2026 15:00 (website): 8 quotes, with settings.");

        // ---- the "no settings" note says what the file is ----
        BackupData plain, noSet;
        WebBackup.ReadFile(Encoding.UTF8.GetBytes(History.ToJson(list)), out plain);
        WebBackup.ReadFile(Encoding.UTF8.GetBytes("{\"Format\":\"PrintPriceBackup\",\"Version\":1,\"CreatedAt\":\"2026-11-09 21:40:00\",\"App\":\"1.13.0\",\"History\":{\"Version\":1,\"Quotes\":[]},\"Settings\":null}"), out noSet);
        True("history.json labelled as one", WebBackup.IsPlainHistory(plain) && WebBackup.NoSettingsText(plain).Contains("history.json"));
        True("history-only backup labelled as a backup: " + WebBackup.NoSettingsText(noSet), noSet != null && noSet.Settings == null
             && !WebBackup.IsPlainHistory(noSet) && !WebBackup.NoSettingsText(noSet).Contains("history.json") && !WebBackup.IsPlainHistory(data));

        // ---- restore = History.Combine ----
        string problem;
        var mine = HistoryTests.Samples();
        var same = WebBackup.Combine(mine, new AppSettings(), data, false, false, out problem);
        True("merge with itself: all the same", same != null && same.Same == 8 && same.Added == 0 && same.Quotes.Count == 8 && !same.SettingsRestored);
        var half = HistoryTests.Samples().GetRange(0, 3);
        var merged = WebBackup.Combine(half, new AppSettings(), data, false, true, out problem);
        True("merge into 3: 5 added, settings restored", merged.Added == 5 && merged.Same == 3 && merged.Quotes.Count == 8 && merged.SettingsRestored
             && merged.Settings.FriendsMarkupPct == 33 && merged.Settings.QuoteName == "Gettic test");
        True("done text: " + WebBackup.DoneText(merged), WebBackup.DoneText(merged) == "✓ Restored. 5 quotes added, 3 were already here. Settings restored."
             && WebBackup.NowText(merged) == "Now 8 quotes in the history.");
        var replaced = WebBackup.Combine(new List<QuoteRecord> { new QuoteRecord { Id = "x", SavedAt = "2026-11-10 10:00:00" } }, new AppSettings(), data, true, false, out problem);
        True("replace: only the backup's", replaced.Quotes.Count == 8 && !replaced.Quotes.Exists(r => r.Id == "x"));
        True("no clash, none moved", WebBackup.ClashText(merged) == null && WebBackup.MovedText(merged) == null);
        BackupData histOnly;
        WebBackup.ReadFile(Encoding.UTF8.GetBytes(History.ToJson(list)), out histOnly);
        True("history.json: settings never restored", !WebBackup.Combine(half, new AppSettings(), histOnly, false, true, out problem).SettingsRestored);

        // ---- safety copies and the reminder ----
        var t0 = new DateTime(2026, 11, 10, 14, 5, 9);
        True("safety key / file name", WebBackup.SafetyKey(t0) == "before-restore-20261110-140509"
             && WebBackup.SafetyFileName("before-restore-20261110-140509") == "PrintPrice before-restore 2026-11-10 14.05.09.printprice-backup");
        True("two safety copies in one minute: different names", WebBackup.SafetyFileName(WebBackup.SafetyKey(t0)) != WebBackup.SafetyFileName(WebBackup.SafetyKey(t0.AddSeconds(20))));
        True("settings ticked only with an empty history", WebBackup.SettingsTickedAtStart(0) && !WebBackup.SettingsTickedAtStart(3));
        var keep = new AppSettings { FriendsMarkupPct = 11 };
        var notTicked = WebBackup.Combine(HistoryTests.Samples().GetRange(0, 3), keep, data, false, WebBackup.SettingsTickedAtStart(3), out problem);
        True("merge with the default tick keeps this browser's settings", !notTicked.SettingsRestored && notTicked.Settings.FriendsMarkupPct == 11);
        string newerFile = json.Replace("\"Version\":1,\"CreatedAt\"", "\"Version\":99,\"CreatedAt\"");
        BackupData nd;
        True("newer backup says reload", newerFile != json && WebBackup.ReadFile(Encoding.UTF8.GetBytes(newerFile), out nd) == WebBackup.NewerText() && nd == null
             && WebBackup.ReadText("{\"Version\":99,\"Quotes\":[]}", out nd) == WebBackup.NewerText() && WebBackup.ReadText("{\"x\":1}", out nd) == History.NotBackupText());
        var old = WebBackup.OldSafetyKeys(new[] { "before-restore-20261110-140509", "history", "before-restore-20261101-090000", "before-restore-20261105-090000", "before-restore-20261109-090000", "pic:a" });
        True("keep the newest 3 safety copies", old.Count == 1 && old[0] == "before-restore-20261101-090000");
        bool warn;
        True("never (with quotes) warns", WebBackup.LastBackupText("", t0, 3, out warn) == "Last backup: never" && warn);
        WebBackup.LastBackupText(null, t0, 0, out warn);
        True("never without quotes: no warning", !warn);
        True("today (file)", WebBackup.LastBackupText(WebBackup.LastBackupValue(t0, false), t0, 3, out warn) == "Last backup: today (file)" && !warn);
        True("12 days ago (online)", WebBackup.LastBackupText(WebBackup.LastBackupValue(t0.AddDays(-12), true), t0, 3, out warn) == "Last backup: 12 days ago (online)" && !warn);
        WebBackup.LastBackupText(WebBackup.LastBackupValue(t0.AddDays(-31), false), t0, 3, out warn);
        True("31 days warns", warn);
        True("code hints", WebBackup.CodeProblem("") != null && WebBackup.CodeProblem("01234-5") == "6 of 20 characters." && WebBackup.CodeProblem(code) == null
             && WebBackup.CodeProblem("01234-56789-ABCDE-FGHJG").Contains("typo") && WebBackup.CodeProblem("U") .Contains("typo"));
        var up = WebBackup.UploadedText(8, t0, new UploadResult { UploadsLeft = 2 }, t0);
        True("uploaded text: " + up, up == "✓ Backed up online: 8 quotes and your settings, kept until 15-12-2027. 2 online backups left today.");

        L.Danish = true;
        True("DA last backup", WebBackup.LastBackupText(WebBackup.LastBackupValue(t0.AddDays(-1), false), t0, 3, out warn) == "Seneste sikkerhedskopi: i går (fil)");
        True("DA summary", WebBackup.Summary(data).StartsWith("Kopi fra 10-11-2026 15:00 (hjemmesiden): 8 tilbud"));
        L.Danish = wasDanish;
    }
}
