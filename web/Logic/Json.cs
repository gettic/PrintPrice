using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintPrice
{
    /// Web version of the desktop's Json helper (History.cs, #if !WEB; JavaScriptSerializer there).
    /// Same rules: public fields and properties, exact names (no camelCase), numbers as numbers, non-ASCII as-is,
    /// throws on bad text (History.ParseJson catches). Reading is lenient like JavaScriptSerializer
    /// (any letter case, numbers given as strings).
    public static class Json
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };

        public static string Serialize(object value)
        {
            return value == null ? "null" : JsonSerializer.Serialize(value, value.GetType(), Options);
        }

        /// Throws on text that isn't valid JSON for T (callers catch).
        public static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
    }
}
