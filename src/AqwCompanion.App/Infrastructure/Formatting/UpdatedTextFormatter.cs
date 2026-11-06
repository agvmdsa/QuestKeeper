namespace AqwCompanion.App.Infrastructure.Formatting;

/// <summary>The "Last updated" line shared by the cards: the read time in local time, or nothing without one.</summary>
public static class UpdatedTextFormatter
{
    public static string Format(DateTimeOffset? at) =>
        at is { } value ? "Last updated " + LocalTimeText.Format(value) : string.Empty;
}
