using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace PrintPrice.Web
{
    /// Browser storage (localStorage of this site only) and the browser calls (files, downloads, AES, the Worker fetches).
    public class Store
    {
        public const string SettingsKey = "PrintPrice.settings";   // the desktop settings.ini text
        public const string PrefsKey = "PrintPrice.web";            // web-only values (WebCalc.Prefs)

        readonly IJSRuntime js;

        public Store(IJSRuntime js) { this.js = js; }

        public async Task<string> Get(string key)
        {
            try { return await js.InvokeAsync<string>("printPrice.get", key); }
            catch (Exception) { return null; }   // storage blocked (private mode etc.): defaults
        }

        public async Task Set(string key, string value)
        {
            try { await js.InvokeVoidAsync("printPrice.set", key, value); }
            catch (Exception) { /* not saved; the page still works */ }
        }

        public async Task Remove(string key)
        {
            try { await js.InvokeVoidAsync("printPrice.remove", key); }
            catch (Exception) { }
        }

        /// The browser's language ("da-DK", "en-US", ...).
        public async Task<string> BrowserLanguage()
        {
            try { return await js.InvokeAsync<string>("printPrice.lang"); }
            catch (Exception) { return ""; }
        }

        /// The address's #part ("#settings", "#printer" open Settings on that tab).
        public async Task<string> Hash()
        {
            try { return await js.InvokeAsync<string>("printPrice.hash"); }
            catch (Exception) { return ""; }
        }

        /// Starts listening for a chosen file (the hidden file input) and files dropped anywhere on the page;
        /// the page's [JSInvokable] FileChosen(id, name, size, count) is called for each.
        public async Task HookFiles<T>(DotNetObjectReference<T> page, Microsoft.AspNetCore.Components.ElementReference input) where T : class
        {
            try { await js.InvokeVoidAsync("printPrice.hookFiles", page, input); }
            catch (Exception) { }
        }

        /// The live electricity price for an area (DK1/DK2): the Worker on the real site, a test answer elsewhere.
        /// Never throws; status 0 = no answer.
        public async Task<LivePower.Answer> FetchPower(string area)
        {
            try
            {
                string host = await js.InvokeAsync<string>("printPrice.host");
                return await js.InvokeAsync<LivePower.Answer>("printPrice.fetchPower", LivePower.Url(host, area), LivePower.TimeoutMs);
            }
            catch (Exception) { return new LivePower.Answer(); }
        }

        /// Opens the browser's file picker (the hidden file input).
        public async Task PickFile()
        {
            try { await js.InvokeVoidAsync("printPrice.pick"); }
            catch (Exception) { }
        }

        /// The chosen file, read by ranges (File.slice) - never read whole, never sent anywhere.
        public IRangeSource File(int id, long length) { return new BrowserFile(js, id, length); }

        sealed class BrowserFile : IRangeSource
        {
            readonly IJSRuntime js;
            readonly int id;
            readonly long length;
            public BrowserFile(IJSRuntime js, int id, long length) { this.js = js; this.id = id; this.length = length; }
            public long Length { get { return length; } }
            public async Task<byte[]> Read(long offset, int count)
            {
                if (offset < 0 || offset >= length || count <= 0) return new byte[0];
                return await js.InvokeAsync<byte[]>("printPrice.readRange", id, offset, (int)Math.Min(count, length - offset)) ?? new byte[0];
            }
        }

        /// Listens for Back / Forward (hashchange -> HashChanged(hash)) and for other open tabs of the site saving the
        /// history (BroadcastChannel -> HistoryChangedElsewhere()).
        public async Task HookPage<T>(DotNetObjectReference<T> page) where T : class
        {
            try { await js.InvokeVoidAsync("printPrice.hookPage", page); }
            catch (Exception) { }
        }

        /// Sets the address's #part ("" = the calculator, "#history"), so Back works.
        public async Task Go(string hash)
        {
            try { await js.InvokeVoidAsync("printPrice.go", hash); }
            catch (Exception) { }
        }

        // ---------- stage 3b: backup ----------

        /// Offers text as a download (a backup file, a safety copy). false = the browser refused.
        public async Task<bool> Download(string name, string text)
        {
            try { await js.InvokeVoidAsync("printPrice.download", name, text); return true; }
            catch (Exception) { return false; }
        }

        /// Restore > Choose file…: the backup file input; its file comes back through FileChosen like a dropped one.
        public async Task PickBackup()
        {
            try { await js.InvokeVoidAsync("printPrice.hookBackupInput"); await js.InvokeVoidAsync("printPrice.pickBackup"); }
            catch (Exception) { }
        }

        public async Task HookBackupInput()
        {
            try { await js.InvokeVoidAsync("printPrice.hookBackupInput"); }
            catch (Exception) { }
        }

        /// The browser's AES-256-CBC (crypto.subtle) for BackupCrypto.
        public async Task<byte[]> Aes(byte[] key, byte[] iv, byte[] data, bool encrypt)
        {
            return await js.InvokeAsync<byte[]>("printPrice.aes", key, iv, data, encrypt);
        }

        /// One call to the online backup service (or the local test server's fake). Never throws; Status 0 = no answer.
        public async Task<WebBackup.Reply> BackupFetch(string method, string url, byte[] body)
        {
            try
            {
                return await js.InvokeAsync<WebBackup.Reply>("printPrice.backupFetch", method, url,
                    body == null ? null : Convert.ToBase64String(body), WebBackup.TimeoutMs) ?? new WebBackup.Reply();
            }
            catch (Exception) { return new WebBackup.Reply(); }
        }

        public async Task<string> Host()
        {
            try { return await js.InvokeAsync<string>("printPrice.host"); }
            catch (Exception) { return ""; }
        }

        public async Task ToTop()
        {
            try { await js.InvokeVoidAsync("printPrice.toTop"); }
            catch (Exception) { }
        }

        public async Task SetPageLanguage(bool danish, string title)
        {
            try { await js.InvokeVoidAsync("printPrice.page", danish ? "da" : "en", title); }
            catch (Exception) { }
        }
    }
}
