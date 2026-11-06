using System.Globalization;

namespace AqwCompanion.Core.Common;

/// <summary>Number checks and parsing shared by the page parsers.</summary>
public static class NumberParsing
{
    /// <summary>True for zero and every positive number.</summary>
    public static bool IsNonNegative(int value) => value >= 0;

    /// <summary>Parses trimmed text as a whole, non-negative int, ignoring the current culture; false for null, junk or a negative number.</summary>
    public static bool TryParseNonNegativeInt(string? text, NumberStyles styles, out int value) =>
        int.TryParse(text?.Trim(), styles, CultureInfo.InvariantCulture, out value) && IsNonNegative(value);
}
