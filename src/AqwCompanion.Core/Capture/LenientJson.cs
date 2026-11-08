using System.Globalization;
using System.Text.Json;

namespace AqwCompanion.Core.Capture;

/// <summary>Readers for the game's loose typing: numbers and flags arrive as JSON numbers, numeric strings, 0/1 or true/false.</summary>
internal static class LenientJson
{
    /// <summary>A whole number from a JSON number or a numeric string; null when missing or not a whole number.</summary>
    public static long? ReadLong(this JsonElement entry, string property)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(property, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var whole))
                {
                    return whole;
                }
                return value.TryGetDouble(out var fraction) && Math.Abs(fraction) < 9e18 ? (long)fraction : null;
            case JsonValueKind.String:
                return long.TryParse(value.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }

    /// <summary>A flag from true/false, a number (non-zero is set) or the text "0", "1", "true", "false"; null when missing or unreadable.</summary>
    public static bool? ReadFlag(this JsonElement entry, string property)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(property, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                return value.TryGetDouble(out var number) ? number != 0 : null;
            case JsonValueKind.String:
                var text = value.GetString()?.Trim();
                if (text is "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return text is "0" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ? false : null;
            default:
                return null;
        }
    }
}
