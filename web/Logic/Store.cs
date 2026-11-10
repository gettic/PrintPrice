using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace PrintPrice.Web
{
    /// Browser storage (localStorage of this site only). Nothing is sent anywhere.
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

        public async Task SetPageLanguage(bool danish, string title)
        {
            try { await js.InvokeVoidAsync("printPrice.page", danish ? "da" : "en", title); }
            catch (Exception) { }
        }
    }
}
