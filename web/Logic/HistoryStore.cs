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
            string small = null;
            if (!string.IsNullOrEmpty(dataUrl))
            {
                try { small = await js.InvokeAsync<string>("printPrice.smallPicture", dataUrl, WebHistory.PictureSize); }
                catch (Exception) { small = null; }
            }
            if (small == null) await Call("printPrice.idbDelete", WebHistory.PictureKey(id));   // a re-save without a picture
            else await Call("printPrice.idbPut", WebHistory.PictureKey(id), small);
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

        /// Removes pictures whose quote is gone (deleted, or trimmed away).
        public async Task CleanPictures(List<QuoteRecord> list)
        {
            if (!Available) return;
            var a = await Call("printPrice.idbKeys", WebHistory.PicturePrefix);
            if (!a.Ok || string.IsNullOrEmpty(a.Value)) return;
            foreach (var k in WebHistory.OrphanPictures(a.Value.Split('\n'), list))
                await Call("printPrice.idbDelete", k);
        }
    }
}
