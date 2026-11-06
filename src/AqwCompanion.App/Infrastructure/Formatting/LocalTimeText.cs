using System.Globalization;

namespace AqwCompanion.App.Infrastructure.Formatting;

/// <summary>A point in time as the user sees it.</summary>
public static class LocalTimeText
{
    /// <summary>The time in the local zone, as a short date and time in the current culture.</summary>
    public static string Format(DateTimeOffset at) => at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
