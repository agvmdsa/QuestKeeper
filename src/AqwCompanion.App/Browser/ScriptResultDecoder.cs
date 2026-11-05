using System.Text.Json;

namespace AqwCompanion.App.Browser;

/// <summary>
/// Normalises the string a WebView backend returns from a script. Some engines (WebView2) JSON-encode the
/// value, others return it raw; JSON string literals are decoded, anything else is returned unchanged.
/// </summary>
public static class ScriptResultDecoder
{
    public static string DecodeString(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw is "null" or "undefined")
        {
            return string.Empty;
        }

        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(raw) ?? string.Empty;
            }
            catch (JsonException)
            {
                return raw;
            }
        }

        return raw;
    }
}
