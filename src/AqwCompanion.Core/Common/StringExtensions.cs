namespace AqwCompanion.Core.Common;

/// <summary>Small string helpers that name a common comparison.</summary>
public static class StringExtensions
{
    /// <summary>Ordinal comparison ignoring case; two nulls are equal.</summary>
    public static bool EqualsIgnoreCase(this string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
