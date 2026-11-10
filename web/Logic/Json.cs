using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintPrice
{
    /// Web version of the desktop's Json helper (History.cs, #if !WEB; JavaScriptSerializer there).
    /// Same rules: public fields and properties, exact names (no camelCase), numbers as numbers, non-ASCII as-is,
    /// throws on bad text (History.ParseJson catches). Reading is lenient like JavaScriptSerializer
    /// (any letter case, numbers given as strings). Untyped reads (Dictionary<string, object> / object, e.g.
    /// BambuSlicer.ParseSlotStrings) give JavaScriptSerializer's shapes: Dictionary<string, object>, ArrayList, string,
    /// int / long / decimal, bool, null.
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
            if (typeof(T) == typeof(Dictionary<string, object>) || typeof(T) == typeof(object))
            {
                using (var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true }))
                {
                    object v = Plain(doc.RootElement);
                    if (typeof(T) == typeof(Dictionary<string, object>) && v != null && !(v is Dictionary<string, object>))
                        throw new InvalidOperationException("Not a JSON object.");
                    return (T)v;
                }
            }
            return JsonSerializer.Deserialize<T>(json, Options);
        }

        // a JSON value as JavaScriptSerializer gives it for an untyped target
        static object Plain(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var d = new Dictionary<string, object>();
                    foreach (var p in e.EnumerateObject()) d[p.Name] = Plain(p.Value);
                    return d;
                case JsonValueKind.Array:
                    var a = new ArrayList();
                    foreach (var x in e.EnumerateArray()) a.Add(Plain(x));
                    return a;
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.Number:
                    int i; long l; decimal m;
                    if (e.TryGetInt32(out i)) return i;
                    if (e.TryGetInt64(out l)) return l;
                    if (e.TryGetDecimal(out m)) return m;
                    return e.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }
    }
}
