using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace PrintPrice.Web
{
    /// The price history in this browser's IndexedDB (window.printPrice.idb* in index.html): one record "history" = the
    /// desktop's history.json text, rewritten on every change, plus a small picture per quote ("pic:<Id>").
    /// Stays in this browser. Never throws: problems come back as texts.
    public class HistoryStore
    {
        readonly IJSRuntime js;

        public HistoryStore(IJSRuntime js) { this.js = js; }

        /// The JS side's answer ({ ok, value, error }).
        public sealed class Answer
        {
            public bool Ok { get; set; }
            public string Value { get; set; }
            public string Error { get; set; }
        }

        async Task<Answer> Call(string fn, params object[] args)
        {
            try { return await js.InvokeAsync<Answer>(fn, args) ?? new Answer { Error = "no answer" }; }
            catch (Exception e) { return new Answer { Error = e.Message }; }
        }

        /// false after the browser refused IndexedDB (private window etc.): the list lives in memory only.
        public bool Available { get; private set; } = true;

        /// The saved quotes (never null) and a problem text for the page (null = fine).
        public async Task<List<QuoteRecord>> Load(Action<string> problem)
        {
            var a = await Call("printPrice.idbGet", WebHistory.HistoryKey);
            if (!a.Ok)
            {
                Available = false;
                problem(WebHistory.StorageProblem(a.Error));
                return new List<QuoteRecord>();
            }
            string text, badKey;
            var list = WebHistory.ReadStored(a.Value, DateTime.Now, out text, out badKey);
            if (badKey != null) await Call("printPrice.idbPut", badKey, a.Value);   // kept as it was, never overwritten
            if (text != null) problem(text);
            try { await js.InvokeVoidAsync("printPrice.persist"); } catch (Exception) { }
            return list;
        }

        /// Writes the whole list and tells the site's other open tabs. null = OK, else the error text.
        public async Task<string> Save(List<QuoteRecord> list)
        {
            if (!Available) return null;   // already told: memory only
            var a = await Call("printPrice.idbPut", WebHistory.HistoryKey, History.ToJson(list));
            if (!a.Ok) return WebHistory.SaveProblem(a.Error);
            try { await js.InvokeVoidAsync("printPrice.told"); } catch (Exception) { }
            return null;
        }

        /// Keeps the shown model picture (data URL) for a record, made small. Quietly skipped on any problem.
        public async Task SavePicture(string id, string dataUrl)
        {
            if (!Available || string.IsNullOrEmpty(id)) return;
            string small = await Small(dataUrl);
            if (small == null) await Call("printPrice.idbDelete", WebHistory.PictureKey(id));   // a re-save without a picture
            else await Call("printPrice.idbPut", WebHistory.PictureKey(id), small);
        }

        /// The picture as the desktop keeps it (History.EncodePicture's rules, index.html smallPicture): longest side 256 px
        /// (192 px when too big), PNG with transparency else JPEG 85, at most History.MaxPictureBytes; null = none.
        async Task<string> Small(string dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl)) return null;
            string small;
            try
            {
                small = await js.InvokeAsync<string>("printPrice.smallPicture", dataUrl, WebHistory.PictureSize,
                                                     WebHistory.SmallPictureSize, WebHistory.MaxPictureChars);
            }
            catch (Exception) { return null; }
            return small != null && History.PictureFromDataUrl(small) != null ? small : null;
        }

        /// The JS side's answer for several keys ({ ok, values, error }).
        public sealed class ManyAnswer
        {
            public bool Ok { get; set; }
            public string[] Values { get; set; }
            public string Error { get; set; }
        }

        const int Batch = 100;

        /// The record ids that have a picture here (empty when IndexedDB isn't available).
        public async Task<HashSet<string>> PictureIds()
        {
            if (!Available) return new HashSet<string>(StringComparer.Ordinal);
            var a = await Call("printPrice.idbKeys", WebHistory.PicturePrefix);
            return WebHistory.PictureIds(a.Ok && !string.IsNullOrEmpty(a.Value) ? a.Value.Split('\n') : null);
        }

        /// This browser's pictures for a backup: newest records first while their data URLs fit in budget
        /// (History.PicturesForBackup). Old WebP / unfit pictures are made again as PNG / JPEG and written back (once);
        /// a WebP that can't be made again goes in as it is. Read in batches, and only until the budget is full.
        public async Task<List<BackupPicture>> BackupPictures(List<QuoteRecord> list, long budget)
        {
            var urls = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Available || list == null || list.Count == 0) return new List<BackupPicture>();
            var ids = WebHistory.NewestPictureIds(list, await PictureIds());
            long used = 0;
            for (int at = 0; at < ids.Count && used < budget; at += Batch)
            {
                var part = ids.GetRange(at, Math.Min(Batch, ids.Count - at));
                var keys = part.ConvertAll(WebHistory.PictureKey).ToArray();
                ManyAnswer a;
                try { a = await js.InvokeAsync<ManyAnswer>("printPrice.idbGetMany", (object)keys); }
                catch (Exception) { a = null; }
                if (a == null || !a.Ok || a.Values == null) break;
                for (int i = 0; i < part.Count && i < a.Values.Length; i++)
                {
                    string url = a.Values[i];
                    if (string.IsNullOrEmpty(url)) continue;
                    if (WebHistory.NeedsConvert(url))
                    {
                        string again = await Small(url);
                        if (again != null)
                        {
                            await Call("printPrice.idbPut", keys[i], again);
                            url = again;
                        }
                    }
                    if (History.PictureFromDataUrl(url) == null) continue;
                    urls[part[i]] = url;
                    used += url.Length;
                }
            }
            return WebHistory.BackupPictures(list, urls, budget);
        }

        /// Writes a restore's pictures (WebHistory.PictureWrites). Quiet: a missing picture never fails a restore.
        /// Returns how many were written.
        public async Task<int> WritePictures(List<KeyValuePair<string, string>> writes)
        {
            if (!Available || writes == null || writes.Count == 0) return 0;
            int n = 0;
            for (int at = 0; at < writes.Count; at += Batch)
            {
                var part = writes.GetRange(at, Math.Min(Batch, writes.Count - at));
                var keys = part.ConvertAll(w => w.Key).ToArray();
                var values = part.ConvertAll(w => w.Value).ToArray();
                var a = await Call("printPrice.idbPutMany", keys, values);
                if (a.Ok) n += part.Count;
            }
            return n;
        }

        /// The record's kept picture (data URL) or null.
        public async Task<string> Picture(string id)
        {
            if (!Available || string.IsNullOrEmpty(id)) return null;
            var a = await Call("printPrice.idbGet", WebHistory.PictureKey(id));
            return a.Ok ? a.Value : null;
        }

        /// Before a restore: keeps what is here now (a backup text) under WebBackup.SafetyKey, and only the newest
        /// SafetyKept copies. null = OK, else the problem (the restore stops then). Memory-only browsers skip it (null).
        public async Task<string> SaveSafety(string key, string backupText)
        {
            if (!Available) return null;
            var a = await Call("printPrice.idbPut", key, backupText);
            if (!a.Ok) return L.S("Backup not restored, the safety copy failed: ", "Sikkerhedskopien blev ikke gendannet, sikkerhedskopien af det nuværende fejlede: ")
                              + (a.Error ?? "");
            var keys = await Call("printPrice.idbKeys", WebBackup.SafetyPrefix);
            if (keys.Ok && !string.IsNullOrEmpty(keys.Value))
                foreach (var k in WebBackup.OldSafetyKeys(keys.Value.Split('\n')))
                    await Call("printPrice.idbDelete", k);
            return null;
        }

        /// A safety copy's backup text, or null.
        public async Task<string> Safety(string key)
        {
            if (!Available) return null;
            var a = await Call("printPrice.idbGet", key);
            return a.Ok ? a.Value : null;
        }

        /// Removes pictures whose quote is gone (deleted, or trimmed away). The saved history is read again first and its
        /// records are kept too, so a tab with an older list never deletes pictures another tab has just saved or restored.
        public async Task CleanPictures(List<QuoteRecord> list)
        {
            if (!Available) return;
            var a = await Call("printPrice.idbKeys", WebHistory.PicturePrefix);
            if (!a.Ok || string.IsNullOrEmpty(a.Value)) return;
            var h = await Call("printPrice.idbGet", WebHistory.HistoryKey);
            if (!h.Ok) return;
            var keep = new List<QuoteRecord>();
            if (list != null) keep.AddRange(list);
            if (!string.IsNullOrEmpty(h.Value))
            {
                var stored = History.ParseJson(h.Value);
                if (stored == null) return;   // unreadable: never cost pictures
                keep.AddRange(stored);
            }
            foreach (var k in WebHistory.OrphanPictures(a.Value.Split('\n'), keep))
                await Call("printPrice.idbDelete", k);
        }
    }
}
