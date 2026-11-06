namespace AqwCompanion.Core.Common;

/// <summary>Cleans text read from a page so it is safe to keep: no control characters, trimmed and capped.</summary>
public static class TextCleaner
{
    /// <summary>
    /// The text without control characters, trimmed and capped to <paramref name="maxLength"/>; empty when nothing visible is left.
    /// The cap never leaves half of a surrogate pair behind, which is not valid UTF-16 and cannot be stored as JSON.
    /// </summary>
    public static string Clean(string? value, int maxLength)
    {
        var text = new string((value ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (text.Length <= maxLength)
        {
            return text;
        }

        if (maxLength <= 0)
        {
            return string.Empty;
        }

        var cut = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..cut];
    }

    /// <summary>The trimmed text, or null when it is null or only white space.</summary>
    public static string? NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
