using System.Globalization;

namespace AqwCompanion.App.Infrastructure.Formatting;

/// <summary>Counts written with the right noun form.</summary>
public static class Plural
{
    /// <summary>The count and its noun: "1 item", "5 items", "0 items".</summary>
    public static string Count(int count, string singular, string plural) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? singular : plural)}");
}
