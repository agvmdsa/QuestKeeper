using System.Globalization;

namespace AqwCompanion.App.Account;

/// <summary>The "Last updated" line shared by the cards: the read time in local time, or nothing without one.</summary>
public static class UpdatedTextFormatter
{
    public static string Format(DateTimeOffset? at) =>
        at is { } value ? "Last updated " + value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty;
}
