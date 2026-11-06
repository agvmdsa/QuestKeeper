using System.Text.Json;

namespace AqwCompanion.Core.Common;

/// <summary>Tolerant readers for the properties of a JSON object: a wrong shape gives null instead of an exception.</summary>
public static class JsonElementReader
{
    /// <summary>The cleaned string value, or null when the property is missing, not a string or blank after cleaning.</summary>
    public static string? ReadCleanText(this JsonElement entry, string property, int maxLength)
    {
        if (!entry.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = TextCleaner.Clean(value.GetString(), maxLength);
        return text.Length == 0 ? null : text;
    }

    /// <summary>A whole, non-negative number that fits an int; anything else (missing, text, fraction, too big) is null.</summary>
    public static int? ReadNonNegativeInt32(this JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
        && NumberParsing.IsNonNegative(number)
            ? number
            : null;
}
